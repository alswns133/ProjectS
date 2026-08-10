using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 장비창의 착용 부위 슬롯 하나(무기·헬멧·상의·하의·신발 중 하나). 인벤에서 드래그한 장비를 드롭하면 착용하고
    /// (<see cref="InventoryManager.Equip"/>), 슬롯을 클릭하거나 <b>인벤토리로 드래그해 놓으면</b> 해제한다
    /// (<see cref="InventoryManager.Unequip"/>). 표시(아이콘·강화)는 <see cref="InventoryManager.GetEquipped"/>를
    /// 읽어 <see cref="Refresh"/>가 갱신한다.
    ///
    /// 드래그-해제는 이 화면층 슬롯이 스스로 처리한다 — 드롭 대상인 <see cref="InventoryItemSlot"/>은 기반층
    /// (ProjectS.UI.Framework)이라 화면층 타입을 참조할 수 없어(의존 방향: 화면→기반층 단방향), 대신 여기서
    /// 드래그를 발행하고 끝난 지점이 인벤 슬롯 위인지 판정한다. (인벤 슬롯의 OnDrop은 소스가 InventoryItemSlot이
    /// 아니면 무시하므로 장비→인벤 드롭이 엉뚱한 이동을 일으키지 않는다.)
    /// </summary>
    public class EquipSlotView : MonoBehaviour,
        IDropHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("이 슬롯이 담당하는 착용 부위")]
        [SerializeField] private EquipSlot slot;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text enhanceText;   // +N (없으면 숨김)

        private int currentItemId;   // 아이콘 async 로드 stale 판정용
        private GameObject dragGhost;   // 드래그 동안 커서를 따라다니는 반투명 아이콘

        /// <summary>이 슬롯의 부위.</summary>
        public EquipSlot Slot => slot;

        /// <summary>현재 착용 상태를 읽어 아이콘·강화 표시를 갱신한다(장비창이 호출).</summary>
        public void Refresh()
        {
            EquipmentInstance eq = InventoryManager.Instance != null ? InventoryManager.Instance.GetEquipped(slot) : null;

            if (eq?.Item == null)
            {
                currentItemId = 0;
                if (icon != null) { icon.sprite = null; icon.enabled = false; }
                if (enhanceText != null) enhanceText.text = string.Empty;
                return;
            }

            currentItemId = eq.Item.Index;
            if (enhanceText != null) enhanceText.text = eq.EnhanceStep > 0 ? $"+{eq.EnhanceStep}" : string.Empty;
            LoadIcon(eq.Item);
        }

        /// <summary>인벤에서 드래그한 장비를 이 부위에 착용한다(부위·직업이 안 맞으면 Equip이 거부).</summary>
        public void OnDrop(PointerEventData eventData)
        {
            InventoryItemSlot source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;

            EquipmentInstance eq = source?.Equipment;
            if (eq != null) InventoryManager.Instance?.Equip(eq);
        }

        /// <summary>슬롯을 클릭하면 착용 장비를 해제해 가방으로 되돌린다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            InventoryManager.Instance?.Unequip(slot);
        }

        /// <summary>마우스를 올리면 착용 장비 정보를 커서 지점에 툴팁으로 띄운다(빈 슬롯이면 무시). 인벤 슬롯과 같은 위젯을 공유한다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (ItemTooltip.Instance == null) return;

            EquipmentInstance eq = InventoryManager.Instance != null ? InventoryManager.Instance.GetEquipped(slot) : null;
            if (eq?.Item != null) ItemTooltip.Instance.ShowEquipment(eq, eventData.position);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide();

        // ---- 좌클릭 드래그(고스트) → 인벤토리에 놓으면 해제 : InventoryItemSlot과 동일 패턴 ----

        /// <summary>착용 중인 장비를 좌클릭으로 집는다(우클릭·빈 슬롯은 무시). 고스트가 커서를 따라간다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            // 좌클릭 드래그만 허용. 우클릭 드래그는 집기가 아니다(요구사항: 우클릭 드래그 불가).
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (icon == null || icon.sprite == null) return;

            // 착용 중인 게 있어야 집는다.
            EquipmentInstance eq = InventoryManager.Instance != null ? InventoryManager.Instance.GetEquipped(slot) : null;
            if (eq?.Item == null) return;

            ItemTooltip.Instance?.Hide();

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 고스트: 드롭 레이캐스트를 막지 않게 raycastTarget off. 창마다 캔버스 sortingOrder가 달라
            // 최상단 캔버스에 얹어야 인벤창 위로 보인다(소스 캔버스에 얹으면 더 높은 창 뒤로 숨음).
            dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(TopmostCanvas(canvas).transform, false);
            dragGhost.transform.SetAsLastSibling();

            Image ghostImg = dragGhost.GetComponent<Image>();
            ghostImg.sprite = icon.sprite;
            ghostImg.raycastTarget = false;
            ghostImg.color = new Color(1f, 1f, 1f, 0.7f);

            // 실제 렌더 크기(rect.size)로 잡는다. 장비창 아이콘이 stretch 앵커면 sizeDelta가 (0,0)이라
            // 고스트가 0 크기로 안 보인다. 그래도 0이면(레이아웃 전) 기본값으로 폴백한다.
            Vector2 ghostSize = icon.rectTransform.rect.size;
            if (ghostSize.x < 1f || ghostSize.y < 1f) ghostSize = new Vector2(80f, 80f);
            ((RectTransform)dragGhost.transform).sizeDelta = ghostSize;

            dragGhost.transform.position = eventData.position;
        }

        /// <summary>드래그 중 고스트를 커서에 붙인다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (dragGhost != null) dragGhost.transform.position = eventData.position;
        }

        /// <summary>인벤토리 슬롯 위에서 놓으면 해제해 가방으로 되돌린다(그 외 위치면 착용 유지).</summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragGhost != null) Destroy(dragGhost);
            dragGhost = null;

            if (eventData.button != PointerEventData.InputButton.Left) return;

            // 놓은 지점 아래에 인벤 슬롯이 있으면 그 슬롯 자리로 해제한다(비어 있으면 그 셀, 아니면 첫 빈 셀).
            // Unequip이 가방 빈칸 여부를 스스로 검사한다.
            GameObject dropTarget = eventData.pointerCurrentRaycast.gameObject;
            InventoryItemSlot targetSlot = dropTarget != null ? dropTarget.GetComponentInParent<InventoryItemSlot>() : null;
            if (targetSlot != null)
                InventoryManager.Instance?.Unequip(slot, targetSlot.GridIndex);
        }

        // 활성 캔버스 중 sortingOrder가 가장 높은 루트 캔버스(없으면 fallback). 고스트를 여기 얹어 어떤 창보다 위에 그린다.
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

        private async void LoadIcon(ItemData item)
        {
            if (icon == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);
            if (this == null || icon == null) return;
            if (currentItemId != item.Index) return;   // 그새 다른 장비로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
