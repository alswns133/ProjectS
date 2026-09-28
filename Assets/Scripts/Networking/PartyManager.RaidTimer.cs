using Mirror;
using ProjectS.Events;

namespace ProjectS.Networking
{
    /// <summary>
    /// <see cref="PartyManager"/>의 <b>레이드 제한 시간</b> 통신부. 서버 판단은 <c>RaidTimeLimit</c>이 하고,
    /// 여기는 상태(남은 시간·끝 시각·흐르는 중인지)를 파티원 화면에 나르기만 한다.
    /// </summary>
    /// <remarks>
    /// <c>PartyManager.RaidIntro</c>·<c>PartyManager.RaidFail</c>과 같은 이유로 partial로 둔다 —
    /// 파티원별 TargetRpc 경로가 이미 있고, 프리팹에 컴포넌트를 추가하는 수작업이 없다.
    /// </remarks>
    public partial class PartyManager
    {
        /// <summary>
        /// 제한 시간 상태를 알린다. 시작·일시정지·재개·정지 때만 오고, 그 사이는 화면이 끝 시각으로 스스로 줄여 그린다.
        /// </summary>
        /// <param name="target">받을 파티원.</param>
        /// <param name="remaining">멈춰 있을 때 보여 줄 남은 시간(초).</param>
        /// <param name="endTime">흐르는 중일 때의 끝 시각(서버 시계 <see cref="NetworkTime.time"/> 기준).</param>
        /// <param name="running">시간이 흐르는 중이면 true.</param>
        [TargetRpc]
        public void TargetRaidTimer(NetworkConnectionToClient target, float remaining, double endTime, bool running)
            => RaidTimerEvents.FireChanged(remaining, endTime, running);
    }
}
