using System;
using UnityEngine;
using UnityEngine.EventSystems;
using ProjectS.Enhance;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 강화창 코어 슬롯에 붙여 인벤토리 슬롯의 드롭을 받는 대상. 드롭된 장비를 강화 대상으로 알린다.
    /// 드래그 페이로드는 eventData.pointerDrag(드래그한 실제 인벤 슬롯 <see cref="InventoryItemSlot"/>)에서 읽는다.
    /// 드롭을 받으려면(그리고 hover 툴팁을 띄우려면) 이 오브젝트에 raycastTarget인 Graphic(코어 슬롯 배경 Image)이
    /// 있어야 하고, 그 Image는 빈 슬롯일 때도 꺼지면 안 된다(아이콘 Image와 별개여야 하는 이유 — 아이콘은 빈칸에서 꺼진다).
    /// 코어에 올라간 장비 위에 마우스를 올리면 인벤 슬롯과 같은 아이템 툴팁을 띄운다
    /// (강화창이 <see cref="SetEquipment"/>로 현재 대상을 알려준다).
    /// (2026-07-23 TH / 2026-08-19 실제 인벤 슬롯 InventoryItemSlot과 연결 / 2026-08-25 hover 툴팁)
    /// </summary>
    public class CoreSlotDropTarget : MonoBehaviour,
        IDropHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>코어 슬롯에 장비를 드롭했을 때 발행. 강화 Presenter가 대상으로 잡는다.</summary>
        public static event Action<EquipmentInstance> OnDropped;

        /// <summary>코어 슬롯을 우클릭해 올려둔 장비를 내렸을 때 발행. 강화 Presenter가 대상을 비운다(빈 슬롯 복귀).</summary>
        public static event Action OnCleared;

        // 지금 코어에 올라가 있는 장비. 강화창이 대상 갱신/초기화 때 넣어준다(더블클릭·드래그 어느 경로로 골랐든).
        // 툴팁은 이 인스턴스를 인벤 슬롯 툴팁과 같은 형식으로 띄운다(롤 주스탯·옵션·+N 포함).
        private EquipmentInstance current;

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

        /// <summary>코어 슬롯을 우클릭하면 올려둔 장비를 내린다(강화 대상 해제). 빈 슬롯이면 무시한다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            if (current == null) return;

            // hover 툴팁이 이 슬롯 것으로 떠 있으면 함께 닫는다(대상이 사라지므로).
            ItemTooltip.Instance?.Hide(this);
            OnCleared?.Invoke();
        }

        /// <summary>
        /// 코어에 현재 올라간 장비를 알린다(강화창의 대상 갱신/초기화에서 호출). hover 툴팁이 이 값을 쓴다.
        /// 아이콘/이름 표시는 강화창(<see cref="EnhancePopup"/>)이 따로 처리하므로, 여기선 툴팁 대상만 들고 있는다.
        /// </summary>
        /// <param name="equip">코어에 올라간 장비(없으면 null → 빈 슬롯, 툴팁 없음)</param>
        public void SetEquipment(EquipmentInstance equip)
        {
            current = equip;

            // 대상이 비워졌는데 이 슬롯이 띄운 툴팁이 아직 떠 있으면 닫는다(빈 슬롯 위에 옛 정보가 남지 않게).
            if (current == null) ItemTooltip.Instance?.Hide(this);
        }

        /// <summary>마우스를 올리면 코어에 올라간 장비를 인벤 슬롯과 같은 툴팁으로 띄운다(빈 슬롯이면 무시).</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (current?.Item == null || ItemTooltip.Instance == null) return;

            // owner로 this를 넘겨, 강화창이 닫힐 때(OnDisable)만 이 툴팁이 닫히게 한다(다른 창 툴팁은 안 건드림).
            ItemTooltip.Instance.ShowEquipment(current, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다(InventoryItemSlot과 동일).</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide();

        /// <summary>강화창이 닫혀 슬롯이 비활성되면 이 슬롯이 띄운 툴팁을 닫는다(마우스가 안 움직여 Exit가 안 와도).</summary>
        private void OnDisable() => ItemTooltip.Instance?.Hide(this);

        /// <summary>static 이벤트 구독 초기화(도메인 리로드를 꺼도 이전 세션 구독자가 남지 않게).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnDropped = null;
            OnCleared = null;
        }
    }
}
