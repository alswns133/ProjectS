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
        /// 상점 판매 슬롯의 예약(올린 아이템·개수)이 바뀜. 인벤토리 창이 받아 예약된 아이템을 회색/남은 수량으로 다시 그린다.
        /// InventoryEvents.OnInventoryChanged를 쓰지 않는 이유: 예약은 인벤 데이터 변화가 아니라서, 그 이벤트로 쏘면
        /// 판매 화면 자신의 보유량 검증(OnInventoryChanged 구독)이 다시 돌아 서로를 부르는 고리가 생긴다.
        /// </summary>
        public static event Action OnSellReservationChanged;

        /// <summary>판매 예약 변경을 알린다(ShopSellView가 추가·제거·비우기 후 호출).</summary>
        public static void FireSellReservationChanged()
            => OnSellReservationChanged?.Invoke();

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다
        /// (이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnToast = null;
            OnSellReservationChanged = null;
        }
    }
}
