using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 화면 공통 UI 알림(토스트 등)을 시스템 간에 알리는 static 이벤트 허브.
    /// PlayerEvents/InventoryEvents/EnhanceEvents와 같은 패턴이다. 어디서든 짧은 안내 메시지를
    /// 띄우고 싶을 때 <see cref="FireToast"/>를 부르고, HUD의 토스트 뷰가 이를 구독해 표시한다.
    /// </summary>
    public static class UIEvents
    {
        /// <summary>짧게 떴다 사라지는 안내 메시지 요청. 인자는 표시할 문구다.</summary>
        public static event Action<string> OnToast;

        /// <summary>
        /// 토스트 메시지를 요청한다(예: "강화 재료가 부족합니다."). 구독하는 토스트 뷰가 없으면 조용히 무시된다.
        /// </summary>
        /// <param name="message">표시할 문구</param>
        public static void FireToast(string message)
            => OnToast?.Invoke(message);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다
        /// (이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnToast = null;
        }
    }
}
