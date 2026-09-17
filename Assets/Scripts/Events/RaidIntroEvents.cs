using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 레이드 등장 연출 <b>전 대기 화면</b>(파티원 준비 현황) 알림 허브.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 흐름: 서버가 인스턴스를 만들며 대기 시작을 알리고(<see cref="FireWaitStarted"/>), 파티원이 하나씩
    /// 준비될 때마다 현황을 뿌리고(<see cref="FireReadyCountChanged"/>), 전원 준비(또는 시간 초과)로
    /// 연출이 시작되면 대기를 끝낸다(<see cref="FireWaitEnded"/>). 싱글은 <c>BossIntroDirector</c>가 같은 이벤트를 1/1로 발행한다.
    /// </para>
    /// <para>
    /// 네트워크 코드(<c>PartyManager</c>)와 화면(<c>RaidWaitPresenter</c>)이 서로를 모르게 하기 위한 분리다
    /// (<c>ChatEvents</c>·<c>PartyEvents</c>와 같은 방침).
    /// </para>
    /// </remarks>
    public static class RaidIntroEvents
    {
        /// <summary>
        /// 대기 시작. 인자는 화면을 실제로 띄우기까지의 지연(초)이다.
        /// </summary>
        /// <remarks>
        /// 파티는 0 — 로딩 화면이 닫히기 <b>전에</b> 깔아 둬야 로딩→씬 사이의 붕 뜬 프레임이 안 보인다.
        /// 싱글은 보통 곧바로 준비되므로 짧은 지연을 줘, 바로 통과할 때 검은 화면이 번쩍이지 않게 한다.
        /// </remarks>
        public static event Action<float> OnWaitStarted;

        /// <summary>준비 현황 변경. (준비된 파티원 수, 이 인스턴스에서 접속 중인 파티원 수).</summary>
        public static event Action<int, int> OnReadyCountChanged;

        /// <summary>대기 종료(연출 시작 또는 대기 취소). 화면을 내린다.</summary>
        public static event Action OnWaitEnded;

        /// <summary>대기 시작을 알린다.</summary>
        /// <param name="showDelay">화면을 띄우기까지 기다릴 시간(초). 그 전에 대기가 끝나면 아예 띄우지 않는다.</param>
        public static void FireWaitStarted(float showDelay) => OnWaitStarted?.Invoke(showDelay);

        /// <summary>준비 현황을 알린다.</summary>
        /// <param name="ready">준비된 파티원 수.</param>
        /// <param name="total">이 인스턴스에서 접속 중인 파티원 수.</param>
        public static void FireReadyCountChanged(int ready, int total) => OnReadyCountChanged?.Invoke(ready, total);

        /// <summary>대기 종료를 알린다.</summary>
        public static void FireWaitEnded() => OnWaitEnded?.Invoke();

        // 플레이 모드 리로드 후 이전 구독자가 남지 않게 초기화한다(static 이벤트 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            OnWaitStarted = null;
            OnReadyCountChanged = null;
            OnWaitEnded = null;
        }
    }
}
