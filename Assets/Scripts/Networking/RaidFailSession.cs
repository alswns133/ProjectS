using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 파티 레이드 인스턴스 하나의 <b>전멸 판정과 재시도 투표를 서버가 쥐는</b> 세션.
    /// 파티원의 다운(사망 + 부활 기회 0) 상태를 모아 전원이 다운되면 실패를 확정하고, 재시도 투표를 진행한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 서버가 쥐나.</b> "전원이 쓰러졌는가"는 한 사람의 화면만 봐서는 알 수 없다. 각자 자기 상태만
    /// 보고하고(<c>PartyManager.CmdRaidDown</c>), 합산과 판정은 서버가 한다. 투표도 같은 이유로 서버 집계다 —
    /// 클라가 각자 세면 이탈·지연으로 서로 다른 결론에 도달한다.
    /// </para>
    /// <para>
    /// <b>성립 조건은 전원 동의</b>(2026-09-22 사용자 확정). 한 명이라도 거절하거나 제한 시간이 지나면
    /// 무산되고 전원이 마을로 돌아간다. 레이드는 인원이 갖춰져야 의미가 있어, 일부만 재도전하는 경우를 두지 않는다.
    /// </para>
    /// <para>
    /// <b>수명.</b> <see cref="RaidIntroSession"/>과 같이 인스턴스 씬 안의 빈 오브젝트에 붙어, 인스턴스가
    /// 내려가면 함께 사라진다. 그래서 재시도 실행(인스턴스 재로드)은 이 세션이 아니라 마을 씬에 남는
    /// <see cref="PartyManager"/>가 맡는다 — 자기가 탄 가지를 자르면 절차가 중간에 끊긴다.
    /// </para>
    /// </remarks>
    public class RaidFailSession : MonoBehaviour
    {
        /// <summary>재시도 투표 제한 시간(초). 넘으면 무산되어 전원 마을 복귀.</summary>
        public const float VoteSeconds = 30f;

        private static readonly Dictionary<uint, RaidFailSession> sessions = new();

        private enum Phase
        {
            Fighting,   // 교전 중(아직 전멸하지 않음)
            Voting,     // 실패 확정 → 재시도 투표 중
            Resolved    // 재시도 확정 또는 무산 — 더 이상 아무것도 하지 않는다
        }

        private Phase phase = Phase.Fighting;

        private uint partyId;
        private Scene instance;

        // 이 인스턴스에 있는 파티원(플레이어 오브젝트 netId). 끊기거나 나간 사람은 매 프레임 분모에서 뺀다
        // — 안 빼면 이미 없는 사람의 다운/동의를 영원히 기다린다.
        private readonly HashSet<uint> expected = new();
        private readonly HashSet<uint> down = new();
        private readonly HashSet<uint> agreed = new();

        private double voteDeadline;

        // 같은 값을 매 프레임 다시 보내지 않기 위한 마지막 전송값.
        private int sentAgreed = -1;
        private int sentTotal = -1;

        /// <summary>
        /// 인스턴스의 실패 판정 세션을 연다. 입장 처리에서 <see cref="RaidIntroSession.Open"/>과 함께 부른다.
        /// </summary>
        /// <param name="party">파티 ID.</param>
        /// <param name="instanceScene">이 파티의 인스턴스 씬.</param>
        /// <param name="members">인스턴스로 들어온 파티원.</param>
        /// <returns>열린 세션. 서버가 아니면 null.</returns>
        public static RaidFailSession Open(uint party, Scene instanceScene, IEnumerable<PlayerPresence> members)
        {
            // ([Server] 속성은 NetworkBehaviour에서만 보장되므로 MonoBehaviour인 여기선 직접 막는다.)
            if (!NetworkServer.active) return null;

            if (sessions.TryGetValue(party, out RaidFailSession old) && old != null)
                Destroy(old.gameObject);

            GameObject host = new GameObject($"RaidFailSession{party}");
            SceneManager.MoveGameObjectToScene(host, instanceScene);

            RaidFailSession session = host.AddComponent<RaidFailSession>();
            session.partyId = party;
            session.instance = instanceScene;

            foreach (PlayerPresence member in members)
                if (member != null) session.expected.Add(member.netId);

            sessions[party] = session;

            Debug.Log($"[진단][RaidFail] 세션 시작 — 파티 {party}, 파티원 {session.expected.Count}명", host);
            return session;
        }

        /// <summary>파티의 진행 중인 세션을 찾는다.</summary>
        /// <param name="party">파티 ID.</param>
        /// <param name="session">찾은 세션.</param>
        /// <returns>있으면 true.</returns>
        public static bool TryGet(uint party, out RaidFailSession session)
            => sessions.TryGetValue(party, out session) && session != null;

        /// <summary>
        /// 파티원 한 명의 다운 상태를 기록한다. 다운은 "사망 + 부활 기회 0"이며, 부활하면 해제된다.
        /// </summary>
        /// <param name="memberNetId">보고한 파티원의 플레이어 오브젝트 netId.</param>
        /// <param name="isDown">다운이면 true, 복귀면 false.</param>
        public void MarkDown(uint memberNetId, bool isDown)
        {
            if (!NetworkServer.active || phase != Phase.Fighting || !expected.Contains(memberNetId)) return;

            bool changed = isDown ? down.Add(memberNetId) : down.Remove(memberNetId);
            if (!changed) return;

            Debug.Log($"[진단][RaidFail] 다운 {(isDown ? "보고" : "해제")} — netId={memberNetId} ({down.Count}/{expected.Count})", this);
        }

        /// <summary>재시도 투표를 기록한다. 거절이 하나라도 들어오면 그 자리에서 무산된다.</summary>
        /// <param name="memberNetId">투표한 파티원의 플레이어 오브젝트 netId.</param>
        /// <param name="agree">재시도에 동의하면 true.</param>
        public void Vote(uint memberNetId, bool agree)
        {
            if (!NetworkServer.active || phase != Phase.Voting || !expected.Contains(memberNetId)) return;

            if (!agree)
            {
                Cancel("파티원이 재시도를 거절했습니다.");
                return;
            }

            agreed.Add(memberNetId);
        }

        private void Update()
        {
            if (!NetworkServer.active || phase == Phase.Resolved) return;

            // 접속이 끊겼거나 인스턴스에서 나간(본체가 마을로 돌아간) 파티원은 분모·분자에서 뺀다.
            expected.RemoveWhere(id => !NetworkServer.spawned.TryGetValue(id, out NetworkIdentity member)
                                       || member == null
                                       || member.gameObject.scene != instance);
            down.IntersectWith(expected);
            agreed.IntersectWith(expected);

            if (expected.Count == 0)
            {
                // 전원 이탈 — 판정할 사람이 없다. 인스턴스도 곧 내려가므로 조용히 끝낸다.
                phase = Phase.Resolved;
                Destroy(gameObject);
                return;
            }

            if (phase == Phase.Fighting)
            {
                if (down.Count >= expected.Count) Fail();
                return;
            }

            BroadcastVoteIfChanged();

            if (agreed.Count >= expected.Count) Retry();
            else if (NetworkTime.time >= voteDeadline) Cancel("시간이 지나 재시도가 취소됐습니다.");
        }

        // 파티 전멸 → 실패 확정. 투표를 열고 전원에게 알린다.
        private void Fail()
        {
            phase = Phase.Voting;
            voteDeadline = NetworkTime.time + VoteSeconds;

            foreach (uint id in expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidFailed(conn, expected.Count, VoteSeconds);

            Debug.Log($"[진단][RaidFail] 레이드 실패 — 파티 {partyId}, {expected.Count}명 전멸. 재시도 투표 시작({VoteSeconds:0}초)", this);
        }

        // 전원 동의 → 재시도. 실행은 인스턴스와 함께 사라지지 않는 PartyManager에 넘긴다.
        private void Retry()
        {
            phase = Phase.Resolved;

            PartyManager runner = null;

            foreach (uint id in expected)
            {
                if (!TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn)) continue;

                pm.TargetRaidRetryStarting(conn);
                runner ??= pm;   // 절차를 돌릴 주체는 아무 파티원이나 좋다(파티 전체를 옮기므로)
            }

            Debug.Log($"[진단][RaidFail] 재시도 확정 — 파티 {partyId}", this);

            if (runner != null) runner.ServerRestartRaidInstance(instance);
            else Debug.LogWarning("[RaidFail] 재시도를 실행할 파티원을 찾지 못했습니다.", this);

            Destroy(gameObject);
        }

        // 거절·시간 초과 → 무산. 각자 마을로 돌아가는 것은 클라의 실패 화면이 처리한다
        // (그 과정에서 RequestLeaveInstance가 오고, 마지막 사람이 나가면 인스턴스가 내려간다).
        private void Cancel(string reason)
        {
            phase = Phase.Resolved;

            foreach (uint id in expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidRetryCancelled(conn, reason);

            Debug.Log($"[진단][RaidFail] 재시도 무산({reason}) — 파티 {partyId}", this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (sessions.TryGetValue(partyId, out RaidFailSession current) && current == this)
                sessions.Remove(partyId);
        }

        private void BroadcastVoteIfChanged()
        {
            if (agreed.Count == sentAgreed && expected.Count == sentTotal) return;

            sentAgreed = agreed.Count;
            sentTotal = expected.Count;

            foreach (uint id in expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidRetryVote(conn, sentAgreed, sentTotal);
        }

        // ★ TargetRpc는 "불린 오브젝트"의 대상 클라 복제본에서 실행된다. 반드시 그 파티원 자신의 PartyManager를 찾아 부른다.
        private static bool TryGetMember(uint netId, out PartyManager partyManager, out NetworkConnectionToClient conn)
        {
            partyManager = null;
            conn = null;

            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null) return false;
            if (!identity.TryGetComponent(out partyManager)) return false;

            conn = identity.connectionToClient;
            return conn != null;
        }
    }
}
