using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ProjectS.Enhance;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 강화창 코어 슬롯에 붙여 인벤토리 슬롯의 드롭을 받는 대상. 드롭된 장비를 강화 대상으로 알린다.
    /// 드래그 페이로드는 eventData.pointerDrag(드래그한 실제 인벤 슬롯 <see cref="InventoryItemSlot"/>)에서 읽는다.
    /// 드롭을 받으려면 이 오브젝트에 raycastTarget인 Graphic(코어 슬롯 Image)이 있어야 한다.
    /// (2026-07-23 TH / 2026-08-19 실제 인벤 슬롯 InventoryItemSlot과 연결)
    /// </summary>
    public class CoreSlotDropTarget : MonoBehaviour, IDropHandler
    {
        /// <summary>코어 슬롯에 장비를 드롭했을 때 발행. 강화 Presenter가 대상으로 잡는다.</summary>
        public static event Action<EquipmentInstance> OnDropped;

        public void OnDrop(PointerEventData eventData)
        {
            // 좌클릭 드래그의 드롭만 받는다(우클릭 드래그는 컨텍스트 메뉴용 — InventoryItemSlot과 동일 규약).
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // 실제 인벤토리 그리드가 InventoryItemSlot으로 장비를 그리므로, 그 슬롯의 Equipment를 대상으로 넘긴다.
            var slot = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;

            if (slot != null && slot.Equipment != null) OnDropped?.Invoke(slot.Equipment);
        }

        /// <summary>static 이벤트 구독 초기화(도메인 리로드를 꺼도 이전 세션 구독자가 남지 않게).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnDropped = null;
    }
}
