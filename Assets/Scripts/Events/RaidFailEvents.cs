using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 레이드 <b>실패(파티 전멸)와 재시도 투표</b> 알림 허브.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 흐름: 파티원이 모두 "사망 + 부활 기회 0"이 되면 서버가 실패를 확정하고(<see cref="FireFailed"/>),
    /// 전원에게 재시도 투표를 띄운다. 동의가 들어올 때마다 현황을 뿌리고(<see cref="FireRetryVoteChanged"/>),
    /// 전원이 동의하면 재시도가 시작되며(<see cref="FireRetryStarting"/>), 한 명이라도 거절하거나 시간이
    /// 지나면 무산되어 전원이 마을로 돌아간다(<see cref="FireRetryCancelled"/>).
    /// </para>
    /// <para>
    /// 싱글 플레이(파티 인스턴스 밖)도 같은 이벤트를 1/1로 발행해 화면이 한 벌로 동작한다
    /// (<c>RaidIntroEvents</c>가 대기 화면을 싱글에서도 1/1로 굴리는 것과 같은 방침).
    /// </para>
    /// <para>
    /// 네트워크 코드(<c>PartyManager</c>·<c>RaidFailSession</c>)와 화면(<c>RaidFailPopup</c>)이 서로를 모르게
    /// 하기 위한 분리다.
    /// </para>
    /// </remarks>
    public static class RaidFailEvents
    {
        /// <summary>
        /// 레이드 실패 확정(파티 전멸). 인자는 (투표 총원, 투표 제한 시간 초). 제한 시간이 0 이하면 무제한.
        /// </summary>
        public static event Action<int, float> OnFailed;

        /// <summary>재시도 동의 현황 변경. (동의한 수, 총원).</summary>
        public static event Action<int, int> OnRetryVoteChanged;

        /// <summary>재시도 확정. 화면을 덮고 레이드 재진입을 기다린다.</summary>
        public static event Action OnRetryStarting;

        /// <summary>재시도 무산(거절·시간 초과). 인자는 사용자에게 보여 줄 사유.</summary>
        public static event Action<string> OnRetryCancelled;

        /// <summary>실패를 알린다.</summary>
        /// <param name="total">투표에 참여할 인원(이 인스턴스에 남아 있는 파티원 수. 싱글은 1).</param>
        /// <param name="voteSeconds">투표 제한 시간(초). 0 이하면 제한 없음.</param>
        public static void FireFailed(int total, float voteSeconds) => OnFailed?.Invoke(total, voteSeconds);

        /// <summary>동의 현황을 알린다.</summary>
        /// <param name="agreed">재시도에 동의한 수.</param>
        /// <param name="total">총원.</param>
        public static void FireRetryVoteChanged(int agreed, int total) => OnRetryVoteChanged?.Invoke(agreed, total);

        /// <summary>재시도 시작을 알린다.</summary>
        public static void FireRetryStarting() => OnRetryStarting?.Invoke();

        /// <summary>재시도 무산을 알린다.</summary>
        /// <param name="reason">사용자에게 보여 줄 사유.</param>
        public static void FireRetryCancelled(string reason) => OnRetryCancelled?.Invoke(reason);

        // 플레이 모드 리로드 후 이전 구독자가 남지 않게 초기화한다(static 이벤트 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnFailed = null;   // ★ 새 이벤트는 여기에도 반드시 추가
            OnRetryVoteChanged = null;
            OnRetryStarting = null;
            OnRetryCancelled = null;
        }
    }
}
