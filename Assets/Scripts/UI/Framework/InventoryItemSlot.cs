using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 인벤토리 그리드 한 칸. 장비(<see cref="EquipmentInstance"/>) 또는 스택형 아이템(<see cref="ItemStack"/>)
    /// 하나를 표시하거나 빈칸으로 둔다. 표시 포맷은 NpcRewardSlot과 같은 결(아이콘 + 수량 라벨)이다.
    /// 아이콘은 <see cref="ItemIconLoader"/>로 어드레서블에서 비동기 로드한다.
    ///
    /// 상호작용: <b>좌클릭 드래그</b>로 아이템을 집어 옮기고(고스트 아이콘이 커서를 따라감, 드롭 판정은
    /// 드롭 대상 = HUD 포션 슬롯 등이 처리), <b>우클릭</b>으로 컨텍스트 메뉴 콜백을 부른다(등록/사용).
    /// 의존 방향(화면→Framework)을 지키려 슬롯은 메뉴를 직접 열지 않고 콜백만 노출한다(호스트 패널이 연다).
    /// 포인터/드래그 이벤트를 받으려면 이 오브젝트에 raycastTarget인 Graphic(배경/아이콘)이 있어야 한다.
    /// </summary>
    public class InventoryItemSlot : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text countText;   // 스택 수량 또는 강화 +N (없으면 숨김)

        private EquipmentInstance equipment;
        private ItemStack stack;
        private Action<InventoryItemSlot> onLeftClick;
        private Action<InventoryItemSlot, PointerEventData> onRightClick;
        private Action<InventoryItemSlot, InventoryItemSlot> onDropReceived;   // (출발 슬롯, 도착 슬롯=this)

        private GameObject dragGhost;

        /// <summary>이 슬롯이 표시 중인 장비(스택/빈칸이면 null).</summary>
        public EquipmentInstance Equipment => equipment;

        /// <summary>이 슬롯이 표시 중인 스택형 아이템(장비/빈칸이면 null).</summary>
        public ItemStack Stack => stack;

        /// <summary>표시 중인 내용이 없는 빈칸인지.</summary>
        public bool IsEmpty => equipment == null && stack == null;

        // 로드 대기 중 슬롯이 재사용되면 늦게 온 아이콘을 무시하기 위한 현재 아이템.
        private ItemData CurrentItem => equipment?.Item ?? stack?.Item;

        /// <summary>좌클릭(집기 아님, 짧은 클릭) 콜백. 지금은 미사용이며 장비 상세/선택 등 후속용으로 남긴다.</summary>
        /// <param name="handler">클릭 콜백</param>
        public void SetClickHandler(Action<InventoryItemSlot> handler) => onLeftClick = handler;

        /// <summary>우클릭 콜백(호스트 패널이 컨텍스트 메뉴를 연다). 커서 위치를 위해 이벤트를 함께 넘긴다.</summary>
        /// <param name="handler">우클릭 콜백</param>
        public void SetRightClickHandler(Action<InventoryItemSlot, PointerEventData> handler) => onRightClick = handler;

        /// <summary>
        /// 이 슬롯에 다른 인벤 슬롯이 드롭됐을 때의 콜백(출발 슬롯, 도착 슬롯=this)을 지정한다.
        /// 호스트 패널이 두 슬롯의 격자 인덱스를 구해 이동을 처리한다(슬롯은 매니저를 직접 부르지 않는다).
        /// </summary>
        /// <param name="handler">드롭 콜백</param>
        public void SetDropHandler(Action<InventoryItemSlot, InventoryItemSlot> handler) => onDropReceived = handler;

        /// <summary>슬롯을 장비로 채운다. 강화 단계가 1 이상이면 +N을 함께 표시한다.</summary>
        /// <param name="equip">표시할 장비 인스턴스</param>
        public void SetEquipment(EquipmentInstance equip)
        {
            equipment = equip;
            stack = null;
            string label = equip != null && equip.EnhanceStep > 0 ? $"+{equip.EnhanceStep}" : null;
            Apply(equip?.Item, label);
        }

        /// <summary>슬롯을 스택형 아이템으로 채운다. 수량이 2 이상이면 수량을 표시한다.</summary>
        /// <param name="itemStack">표시할 스택</param>
        public void SetStack(ItemStack itemStack)
        {
            stack = itemStack;
            equipment = null;
            string label = itemStack != null && itemStack.Count > 1 ? itemStack.Count.ToString() : null;
            Apply(itemStack?.Item, label);
        }

        /// <summary>슬롯을 빈칸으로 만든다. 아이콘/수량을 지운다.</summary>
        public void SetEmpty()
        {
            equipment = null;
            stack = null;
            Apply(null, null);
        }

        // 공통 표시 반영. item이 null이면 빈칸, 아니면 아이콘을 어드레서블에서 로드해 채운다.
        private void Apply(ItemData item, string label)
        {
            if (countText != null)
            {
                countText.text = label ?? string.Empty;
                countText.gameObject.SetActive(!string.IsNullOrEmpty(label));
            }

            if (icon != null && item == null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (item != null) LoadIcon(item);
        }

        // 아이콘을 비동기로 로드한다. 대기 중 슬롯 내용이 바뀌면(그리드 재사용) 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(ItemData item)
        {
            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);

            if (this == null || icon == null) return;
            if (!ReferenceEquals(CurrentItem, item)) return;   // 슬롯이 다른 아이템으로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>좌클릭은 좌클릭 콜백, 우클릭은 우클릭 콜백을 부른다(빈칸이면 무시).</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsEmpty) return;

            if (eventData.button == PointerEventData.InputButton.Right)
                onRightClick?.Invoke(this, eventData);
            else if (eventData.button == PointerEventData.InputButton.Left)
                onLeftClick?.Invoke(this);
        }

        // ---- 좌클릭 드래그(고스트) : InventorySlotView와 동일 패턴 ----
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (IsEmpty || icon == null) return;

            // 드래그 시작 시 남아 있던 툴팁을 치운다.
            ItemTooltip.Instance?.Hide();

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 고스트: 드래그 동안 커서를 따라다니는 반투명 아이콘. 드롭 레이캐스트를 막지 않게 raycastTarget off.
            // 소스 슬롯의 캔버스가 아니라 "최상단 캔버스"에 얹는다 — 창마다 캔버스가 달라(sortingOrder),
            // 소스 캔버스에 얹으면 더 높은 창(장비창 등) 뒤로 고스트가 숨는다.
            dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(TopmostCanvas(canvas).transform, false);
            dragGhost.transform.SetAsLastSibling();

            Image ghostImg = dragGhost.GetComponent<Image>();
            ghostImg.sprite = icon.sprite;
            ghostImg.raycastTarget = false;
            ghostImg.color = new Color(1f, 1f, 1f, 0.7f);

            ((RectTransform)dragGhost.transform).sizeDelta = icon.rectTransform.sizeDelta;
            dragGhost.transform.position = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragGhost != null) dragGhost.transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragGhost != null) Destroy(dragGhost);
            dragGhost = null;
        }

        // 활성 캔버스 중 sortingOrder가 가장 높은 루트 캔버스를 찾는다(없으면 fallback).
        // 드래그 고스트를 여기 얹어 어떤 창보다도 위에 그려지게 한다. 드래그 시작 1회라 스캔 비용은 무시할 만하다.
        private static Canvas TopmostCanvas(Canvas fallback)
        {
            Canvas top = fallback;
            int bestOrder = fallback != null ? fallback.sortingOrder : int.MinValue;

            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isActiveAndEnabled) continue;

                Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
                if (root.sortingOrder > bestOrder)
                {
                    bestOrder = root.sortingOrder;
                    top = root;
                }
            }

            return top;
        }

        /// <summary>다른 인벤 슬롯을 이 슬롯(빈칸 포함)에 드롭하면 이동/교환을 호스트에 알린다.</summary>
        public void OnDrop(PointerEventData eventData)
        {
            InventoryItemSlot source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;

            if (source == null || source == this) return;   // 자기 자신·비인벤 드래그는 무시
            onDropReceived?.Invoke(source, this);
        }

        /// <summary>마우스를 올리면 이 아이템 정보를 커서 지점에 툴팁으로 띄운다(빈칸이면 무시).</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsEmpty || ItemTooltip.Instance == null) return;

            if (equipment != null) ItemTooltip.Instance.ShowEquipment(equipment, eventData.position);
            else if (stack != null) ItemTooltip.Instance.ShowStack(stack, eventData.position);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide();
    }
}
