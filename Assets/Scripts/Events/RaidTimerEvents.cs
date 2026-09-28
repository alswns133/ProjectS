using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 레이드 <b>플레이 제한 시간</b> 표시 알림 허브. 타이머를 쥔 쪽(서버·싱글의 <c>RaidTimeLimit</c>)이 상태가 바뀔 때만
    /// 발행하고, 화면(<c>BossHpPresenter</c> → <c>BossHpView</c>)은 받은 끝 시각으로 매 프레임 스스로 줄여 그린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 매초 남은 시간을 보내지 않고 "끝 시각"을 보내나.</b> 남은 시간은 매 프레임 바뀌어 보내려면 계속 통신해야
    /// 하지만, 끝 시각은 시작·일시정지·재개 때만 바뀐다. 끝 시각은 <c>RaidTimeLimit.Now</c>(네트워크 중이면 서버 시계
    /// <c>NetworkTime.time</c>) 기준이라, 파티원 화면마다 받은 시점이 달라도 같은 순간에 0이 된다
    /// (등장 연출을 시작 시각으로 맞추는 것과 같은 방식).
    /// </para>
    /// <para>
    /// 싱글도 같은 이벤트를 탄다 — 파티는 서버가 TargetRpc로 각자에게 전하고(<c>PartyManager.TargetRaidTimer</c>),
    /// 싱글은 타이머가 직접 발행한다. 그래서 화면은 한 벌이다(<see cref="RaidFailEvents"/>와 같은 방침).
    /// </para>
    /// </remarks>
    public static class RaidTimerEvents
    {
        /// <summary>
        /// 제한 시간 상태 변경. (남은 시간 초, 끝 시각, 흐르는 중인지).
        /// 흐르는 중이면 끝 시각 − 지금으로 그리고, 멈춰 있으면(연출 대기·페이즈 전환·종료) 남은 시간을 그대로 그린다.
        /// </summary>
        public static event Action<float, double, bool> OnChanged;

        /// <summary>상태 변경을 알린다.</summary>
        /// <param name="remaining">멈춰 있을 때 보여 줄 남은 시간(초).</param>
        /// <param name="endTime">흐르는 중일 때의 끝 시각(<c>RaidTimeLimit.Now</c> 기준).</param>
        /// <param name="running">시간이 흐르는 중이면 true.</param>
        public static void FireChanged(float remaining, double endTime, bool running)
            => OnChanged?.Invoke(remaining, endTime, running);

        // 플레이 모드 리로드 후 이전 구독자가 남지 않게 초기화한다(static 이벤트 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnChanged = null;   // ★ 새 이벤트는 여기에도 반드시 추가
        }
    }
}
