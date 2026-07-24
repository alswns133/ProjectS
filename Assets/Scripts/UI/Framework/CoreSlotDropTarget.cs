using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ProjectS.Enhance;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 강화창 코어 슬롯에 붙여 인벤토리 슬롯의 드롭을 받는 대상. 드롭된 장비를 강화 대상으로 알린다.
    /// 드래그 페이로드는 eventData.pointerDrag(드래그한 InventorySlotView)에서 읽는다.
    /// 드롭을 받으려면 이 오브젝트에 raycastTarget인 Graphic(코어 슬롯 Image)이 있어야 한다.
    /// (2026-07-23 TH)
    /// </summary>
    public class CoreSlotDropTarget : MonoBehaviour, IDropHandler
    {
        /// <summary>코어 슬롯에 장비를 드롭했을 때 발행. 강화 Presenter가 대상으로 잡는다.</summary>
        public static event Action<EquipmentInstance> OnDropped;

        public void OnDrop(PointerEventData eventData)
        {
            var slot = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventorySlotView>()
                : null;

            if (slot != null && slot.Instance != null) OnDropped?.Invoke(slot.Instance);
        }

        /// <summary>static 이벤트 구독 초기화(도메인 리로드를 꺼도 이전 세션 구독자가 남지 않게).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnDropped = null;
    }
}
