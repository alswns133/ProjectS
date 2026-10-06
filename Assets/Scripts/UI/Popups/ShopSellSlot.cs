using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 판매 UI 격자의 한 칸. 판매 예정 항목(<see cref="ShopSellEntry"/>) 하나를 보여 주고, 입력을 호스트(ShopSellView)에 전달한다.
    /// <list type="bullet">
    /// <item>인벤 슬롯을 좌클릭 드래그해 놓으면 → 호스트에 "올려 달라" 요청(<see cref="OnDrop"/>)</item>
    /// <item>우클릭 → 슬롯에서 빼기</item>
    /// <item>좌클릭 드래그로 인벤토리 위에 놓으면 → 슬롯에서 빼기(<see cref="OnEndDrag"/>)</item>
    /// <item>마우스를 올리면 → 아이템 툴팁(인벤 슬롯과 같은 <see cref="ItemTooltip"/>)</item>
    /// </list>
    /// 등급 표시는 같은 오브젝트의 <see cref="ItemSlotGradeView"/>(선택)에 맡긴다. 이 칸은 InventoryItemSlot이 아니라
    /// 등급 뷰가 스스로 따라오지 못하므로, 내용이 바뀔 때마다 <see cref="ItemSlotGradeView.SetItem"/>을 직접 부른다.
    /// 실제 추가/제거·합계 계산은 호스트가 하고, 이 칸은 표시와 입력 전달만 한다(InventoryItemSlot과 같은 결).
    /// 인벤→판매 드롭 판정을 여기 두는 이유: InventoryItemSlot(기반층)은 화면층(상점)을 알 수 없으므로,
    /// 화면층인 이 칸이 드롭을 받아 소스 슬롯을 읽는다(EquipSlotView의 드래그 해제와 같은 방향).
    /// </summary>
    public class ShopSellSlot : MonoBehaviour,
        IDropHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image icon;
        [Tooltip("판매 개수 표시(장비·1개면 숨김).")]
        [SerializeField] private TMP_Text countText;

        private ShopSellView host;
        private GameObject dragGhost;
        private ItemSlotGradeView gradeView;   // 등급 표현(선택). 프리팹에 안 붙어 있으면 null이고 등급 표시는 생략된다.

        /// <summary>이 칸에 올라간 항목. 비어 있으면 null.</summary>
        public ShopSellEntry Entry { get; private set; }

        /// <summary>비어 있는 칸인지.</summary>
        public bool IsEmpty => Entry == null;

        /// <summary>호스트 연결(ShopSellView가 슬롯 생성 시 1회).</summary>
        /// <remarks>
        /// 등급 뷰 캐싱을 Awake가 아니라 여기서 하는 이유: 호스트가 Instantiate 직후 Init → Set(null)을 부르는데,
        /// 부모가 꺼진 상태에서 생성되면 Awake가 Set보다 늦게 돌아 첫 표시에서 등급 뷰를 못 찾는다.
        /// Init은 생성 시 정확히 1회라 GetComponent 캐싱 규칙(1회)도 지킨다.
        /// </remarks>
        public void Init(ShopSellView owner)
        {
            host = owner;
            gradeView = GetComponent<ItemSlotGradeView>();
        }

        /// <summary>항목을 표시한다. null이면 빈칸.</summary>
        public void Set(ShopSellEntry entry)
        {
            Entry = entry;

            if (countText != null)
            {
                bool showCount = entry != null && entry.Count > 1;
                countText.text = showCount ? entry.Count.ToString() : string.Empty;
                countText.gameObject.SetActive(showCount);
            }

            ItemData item = ItemOf(entry);
            if (gradeView != null) gradeView.SetItem(item);   // 빈칸이면 null → 등급 표시 끔

            // 마우스를 올린 채 내용이 바뀌면(우클릭으로 빼기·당겨 정렬) 옛 아이템 툴팁이 남으므로 닫는다.
            // 주인 지정 Hide라 다른 슬롯이 띄운 툴팁은 건드리지 않는다.
            ItemTooltip.Instance?.Hide(this);

            LoadIcon(item);
        }

        // 항목의 아이템 정의(장비/스택 공통). 빈칸이면 null.
        private static ItemData ItemOf(ShopSellEntry entry)
            => entry?.Equipment?.Item ?? entry?.Stack?.Item;

        // 아이콘을 비동기로 로드한다. 대기 중 칸이 비거나 다른 항목으로 바뀌면 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(ItemData item)
        {
            // 로드 전엔 비운다 — 스프라이트 없는 Image는 흰 사각형으로 그려져 팝인이 보인다.
            if (icon != null) { icon.sprite = null; icon.enabled = false; }
            if (item == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);

            if (this == null || icon == null) return;
            if (!ReferenceEquals(ItemOf(Entry), item)) return;   // 그사이 칸 내용이 바뀜

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        /// <summary>마우스를 올리면 이 칸의 아이템 정보를 커서 지점에 툴팁으로 띄운다(빈칸이면 무시).</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (IsEmpty || ItemTooltip.Instance == null) return;

            // 인벤 슬롯과 같은 툴팁을 쓴다 — 장비는 실제 인스턴스(+N·옵션·착용품 비교), 스택은 아이템 정보.
            // 주인(this)을 넘겨, 이 칸이 꺼지거나 내용이 바뀔 때 자기 툴팁만 닫을 수 있게 한다.
            if (Entry.Equipment != null) ItemTooltip.Instance.ShowEquipment(Entry.Equipment, eventData.position, this);
            else if (Entry.Stack != null) ItemTooltip.Instance.ShowStack(Entry.Stack, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 이 칸이 띄운 툴팁을 닫는다.</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide(this);

        /// <summary>인벤 슬롯을 좌클릭 드래그해 이 칸에 놓으면 호스트에 추가를 요청한다.</summary>
        public void OnDrop(PointerEventData eventData)
        {
            // 우클릭 드래그에도 OnDrop이 오므로 좌클릭만 받는다(InventoryItemSlot.OnDrop과 같은 가드).
            if (eventData.button != PointerEventData.InputButton.Left) return;

            InventoryItemSlot source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;
            if (source == null) return;   // 판매 슬롯끼리 드래그 등은 무시

            host?.TryAddFromInventory(source);
        }

        /// <summary>우클릭하면 이 칸을 비운다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsEmpty || eventData.button != PointerEventData.InputButton.Right) return;
            host?.Remove(this);
        }

        /// <summary>좌클릭 드래그 시작: 고스트 아이콘을 띄운다.</summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (IsEmpty || eventData.button != PointerEventData.InputButton.Left) return;
            if (icon == null || icon.sprite == null) return;

            // 드래그 중엔 다른 슬롯 hover 툴팁이 뜨지 않게 막는다(인벤 슬롯 드래그와 같은 규칙).
            ItemTooltip.Instance?.Hide();
            ItemTooltip.DragSuppressed = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 고스트는 최상단 캔버스에 얹는다 — 상점 캔버스에 얹으면 인벤 창(다른 캔버스) 위로 끌 때 뒤로 숨는다.
            // raycastTarget을 꺼야 놓는 지점의 인벤 슬롯이 레이캐스트에 잡힌다.
            dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(InventoryItemSlot.TopmostCanvas(canvas).transform, false);
            dragGhost.transform.SetAsLastSibling();

            Image ghostImage = dragGhost.GetComponent<Image>();
            ghostImage.sprite = icon.sprite;
            ghostImage.raycastTarget = false;
            ghostImage.color = new Color(1f, 1f, 1f, 0.7f);

            ((RectTransform)dragGhost.transform).sizeDelta = icon.rectTransform.sizeDelta;
            dragGhost.transform.position = eventData.position;
        }

        /// <summary>드래그 중 고스트를 커서에 붙인다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (dragGhost != null) dragGhost.transform.position = eventData.position;
        }

        /// <summary>
        /// 드래그 종료: 놓은 곳이 인벤토리 슬롯이면 이 칸을 비운다(인벤으로 되돌리기).
        /// 예약 모델이라 인벤 데이터는 원래 그대로 있으므로, 판매 목록에서만 빼면 된다.
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragGhost != null) Destroy(dragGhost);
            dragGhost = null;
            ItemTooltip.DragSuppressed = false;

            if (IsEmpty || eventData.button != PointerEventData.InputButton.Left) return;

            // 인벤 창 위 아무 데나(슬롯이든 배경이든) 놓으면 되돌린 것으로 본다. 슬롯 칸에 정확히 놓아야만
            // 빠지게 하면 칸 사이 틈에 놓았을 때 아무 반응이 없어 "안 되는 것"처럼 보인다.
            GameObject target = eventData.pointerCurrentRaycast.gameObject;
            if (target != null && target.GetComponentInParent<InventoryPopup>() != null)
                host?.Remove(this);
        }

        // 드래그 도중 판매 화면이 꺼지면(모드 전환·창 닫기) OnEndDrag가 안 올 수 있으니 고스트를 여기서도 치운다.
        private void OnDisable()
        {
            // 판매 화면이 꺼지면(모드 전환·창 닫기) 마우스가 안 움직여 PointerExit가 안 와도 내 툴팁을 닫는다.
            ItemTooltip.Instance?.Hide(this);

            // 내 드래그가 진행 중일 때만 정리한다 — 42칸이 함께 꺼지므로, 무조건 풀면 다른 슬롯의 드래그 억제까지 풀린다.
            if (dragGhost == null) return;

            Destroy(dragGhost);
            dragGhost = null;
            ItemTooltip.DragSuppressed = false;
        }
    }
}
