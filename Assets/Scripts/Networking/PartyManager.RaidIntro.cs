using System.Collections;
using Mirror;
using ProjectS.Events;
using ProjectS.Scenes;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// <see cref="PartyManager"/>의 레이드 등장 연출 통신부. 서버 판단은 <see cref="RaidIntroSession"/>이 하고,
    /// 여기는 클라↔서버 메시지(준비 보고·대기 현황·시작/종료 지시)만 나른다.
    /// </summary>
    /// <remarks>
    /// 별도 NetworkBehaviour가 아니라 PartyManager의 partial로 둔 이유: 커넥션 소유 오브젝트(playerPrefab)에 이미 붙어 있어
    /// Command 권한과 파티원별 TargetRpc 경로가 갖춰져 있고, 새 컴포넌트를 프리팹에 추가하는 수작업이 필요 없기 때문이다.
    /// </remarks>
    public partial class PartyManager
    {
        /// <summary>
        /// 이 클라의 화면이 연출을 볼 준비가 됐다고 서버에 알린다(로딩 끝 + 내 캐릭터·보스 도착).
        /// <c>BossIntroDirector</c>가 한 번 부른다.
        /// </summary>
        public void ReportRaidIntroReady() => CmdRaidIntroReady();

        [Command]
        private void CmdRaidIntroReady()
        {
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) return;

            if (RaidIntroSession.TryGet(me.PartyId, out RaidIntroSession session))
                session.MarkReady(netId);
        }

        /// <summary>대기 화면을 띄우라는 지시(서버 → 파티원). 로딩 화면보다 먼저 깔려야 해 지연 없이 띄운다.</summary>
        /// <param name="target">받을 파티원의 커넥션.</param>
        [TargetRpc]
        public void TargetRaidWaitStarted(NetworkConnectionToClient target) => RaidIntroEvents.FireWaitStarted(0f);

        /// <summary>대기 현황(준비 수 / 접속 중인 파티원 수) 갱신 지시.</summary>
        /// <param name="target">받을 파티원의 커넥션.</param>
        /// <param name="ready">준비된 파티원 수.</param>
        /// <param name="total">접속 중인 파티원 수.</param>
        [TargetRpc]
        public void TargetRaidReadyCount(NetworkConnectionToClient target, int ready, int total)
            => RaidIntroEvents.FireReadyCountChanged(ready, total);

        /// <summary>
        /// 연출 시작 지시. <paramref name="startTime"/>은 서버 시계(<see cref="NetworkTime.time"/>) 기준 시작 시각이라,
        /// 늦게 받은 클라도 그만큼 건너뛰어 모두가 같은 순간에 끝난다.
        /// </summary>
        /// <param name="target">받을 파티원의 커넥션.</param>
        /// <param name="startTime">서버 기준 연출 시작 시각.</param>
        [TargetRpc]
        public void TargetRaidIntroStart(NetworkConnectionToClient target, double startTime)
            => StartCoroutine(StartRaidIntroWhenDirectorFound(startTime));

        /// <summary>
        /// 연출 종료 지시(서버의 끝 시각 도달). 이 클라가 연출 끝을 놓쳤더라도 여기서 입력·동기화·화면을 되돌린다.
        /// </summary>
        /// <param name="target">받을 파티원의 커넥션.</param>
        [TargetRpc]
        public void TargetRaidIntroEnd(NetworkConnectionToClient target)
        {
            RaidIntroEvents.FireWaitEnded();

            BossIntroDirector intro = BossIntroDirector.Find(default, BossIntroDirector.DirectorRole.Intro);
            if (intro != null) intro.ForceEnd();
        }

        // 지시 도착 시점에 이 클라의 디렉터가 아직 없을 수 있다(씬 도착 직후 등). 나타나면 서버 시각에 맞춰 재생한다.
        private IEnumerator StartRaidIntroWhenDirectorFound(double startTime)
        {
            const float timeout = 10f;
            float deadline = Time.time + timeout;

            while (Time.time < deadline)
            {
                BossIntroDirector intro = BossIntroDirector.Find(default, BossIntroDirector.DirectorRole.Intro);
                if (intro != null)
                {
                    intro.PlaySynced(null, startTime, true);
                    yield break;
                }

                yield return null;
            }

            RaidIntroEvents.FireWaitEnded();
            Debug.LogWarning($"[RaidIntro] 연출 시작 지시를 받았지만 {timeout:0}초 안에 BossIntroDirector를 찾지 못했습니다.", this);
        }
    }
}
