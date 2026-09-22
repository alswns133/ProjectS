using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectS.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// <see cref="PartyManager"/>의 <b>레이드 실패·재시도</b> 부분. 클라의 보고/투표를 서버 세션
    /// (<see cref="RaidFailSession"/>)으로 넘기고, 결과를 파티원별 TargetRpc로 돌려준다.
    /// </summary>
    /// <remarks>
    /// <see cref="PartyManager.RaidIntro"/>와 같은 이유로 새 NetworkBehaviour를 만들지 않고 여기에 붙인다 —
    /// Command 권한과 파티원별 TargetRpc 경로가 이미 갖춰져 있고, 프리팹에 컴포넌트를 추가하는 수작업이 없다.
    /// </remarks>
    public partial class PartyManager
    {
        // 인스턴스 언로드를 기다리는 상한(초). 넘으면 그냥 진행한다 — 영원히 멈춰 있는 것보다 낫다.
        private const float InstanceUnloadTimeout = 5f;

        // ── 클라 → 서버 ─────────────────────────────────────────────

        /// <summary>
        /// "더 이상 일어설 수 없음"(사망 + 부활 기회 0)을 서버에 보고한다. 부활하면 false로 되돌린다.
        /// <c>RaidFailFlow</c>가 부른다.
        /// </summary>
        /// <param name="down">다운이면 true.</param>
        public void ReportRaidDown(bool down) => CmdRaidDown(down);

        [Command]
        private void CmdRaidDown(bool down)
        {
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) return;
            if (!RaidFailSession.TryGet(me.PartyId, out RaidFailSession session)) return;

            session.MarkDown(netId, down);
        }

        /// <summary>재시도 투표를 보낸다. <c>RaidFailFlow</c>가 부른다.</summary>
        /// <param name="agree">재시도에 동의하면 true.</param>
        public void VoteRaidRetry(bool agree) => CmdVoteRaidRetry(agree);

        [Command]
        private void CmdVoteRaidRetry(bool agree)
        {
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) return;
            if (!RaidFailSession.TryGet(me.PartyId, out RaidFailSession session)) return;

            session.Vote(netId, agree);
        }

        // ── 서버 → 클라(각자에게) ────────────────────────────────────

        /// <summary>레이드 실패(파티 전멸)를 알리고 재시도 투표를 띄운다.</summary>
        /// <param name="target">받을 파티원.</param>
        /// <param name="total">투표 총원.</param>
        /// <param name="voteSeconds">투표 제한 시간(초).</param>
        [TargetRpc]
        public void TargetRaidFailed(NetworkConnectionToClient target, int total, float voteSeconds)
            => RaidFailEvents.FireFailed(total, voteSeconds);

        /// <summary>재시도 동의 현황을 알린다.</summary>
        /// <param name="target">받을 파티원.</param>
        /// <param name="agreed">동의한 수.</param>
        /// <param name="total">총원.</param>
        [TargetRpc]
        public void TargetRaidRetryVote(NetworkConnectionToClient target, int agreed, int total)
            => RaidFailEvents.FireRetryVoteChanged(agreed, total);

        /// <summary>재시도 확정을 알린다. 클라는 화면을 덮고 서버의 씬 재로드 지시를 기다린다.</summary>
        /// <param name="target">받을 파티원.</param>
        [TargetRpc]
        public void TargetRaidRetryStarting(NetworkConnectionToClient target)
            => RaidFailEvents.FireRetryStarting();

        /// <summary>재시도 무산을 알린다(거절·시간 초과).</summary>
        /// <param name="target">받을 파티원.</param>
        /// <param name="reason">사용자에게 보여 줄 사유.</param>
        [TargetRpc]
        public void TargetRaidRetryCancelled(NetworkConnectionToClient target, string reason)
            => RaidFailEvents.FireRetryCancelled(reason);

        // ── 재시도 실행(서버) ───────────────────────────────────────

        /// <summary>
        /// 파티 레이드 인스턴스를 처음부터 다시 연다. 전원 동의로 재시도가 확정되면 세션이 부른다.
        /// </summary>
        /// <remarks>
        /// <b>왜 "리셋"이 아니라 "다시 열기"인가</b>(2026-09-22 사용자 확정): 보스 HP·페이즈·그로기만
        /// 되돌리면 열린 문·부서진 오브젝트·남은 잡몹 같은 상태가 남는다. 인스턴스를 통째로 다시 열면
        /// 입장과 똑같은 경로를 타므로 되돌릴 것을 빠뜨릴 수 없다.
        ///
        /// <para><b>★ 한계 — 호스트.</b> 호스트는 서버 씬을 공유하므로 입장 때도 클라 씬 로드 지시를 받지 않는다
        /// (그 특례는 <c>ServerLoadInstanceAndMove</c>에 이미 있다). 따라서 호스트 화면의 재진입 처리는
        /// 파티 동시입장 Stage 2(클라 씬 로드/언로드 사이클)와 함께 마무리해야 한다.</para>
        /// </remarks>
        /// <param name="oldInstance">내리고 다시 열 현재 인스턴스 씬.</param>
        [Server]
        public void ServerRestartRaidInstance(Scene oldInstance)
        {
            StartCoroutine(ServerRestartRaidRoutine(oldInstance));
        }

        [Server]
        private IEnumerator ServerRestartRaidRoutine(Scene oldInstance)
        {
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) yield break;

            uint pid = me.PartyId;

            // ① 파티원 전원을 인스턴스에서 빼낸다(아바타 파괴 + 본체를 마을로 복귀).
            //    마지막 사람이 빠지는 순간 ServerUnloadInstanceIfEmpty가 남은 네트워크 오브젝트를 정리하고 씬을 내린다.
            foreach (NetworkConnectionToClient conn in new List<NetworkConnectionToClient>(NetworkServer.connections.Values))
            {
                if (conn == null || conn.identity == null) continue;
                if (!conn.identity.TryGetComponent(out PlayerPresence member) || member.PartyId != pid) continue;

                ServerLeaveInstance(conn);
            }

            // ② 언로드가 끝나기를 기다린다. 기다리지 않고 새 씬을 열면, "마지막에 로드된 씬"으로 인스턴스를
            //    잡는 현재 방식(Stage 1 스톱갭)이 내려가는 중인 옛 씬을 집을 수 있다.
            float until = Time.time + InstanceUnloadTimeout;
            while (oldInstance.IsValid() && oldInstance.isLoaded && Time.time < until) yield return null;

            if (oldInstance.IsValid() && oldInstance.isLoaded)
                Debug.LogWarning($"[RaidFail] 옛 인스턴스가 {InstanceUnloadTimeout:0}초 안에 내려가지 않았습니다 — 그대로 재입장을 진행합니다.", this);

            // ③ 입장 경로를 그대로 다시 탄다. 인스턴스 로드·파티원 이동·등장 연출 세션·클라 씬 로드 지시가
            //    전부 재현되므로, 재시도만을 위한 별도 절차를 만들지 않는다.
            ServerEnterDungeonForMyParty();
        }
    }
}
