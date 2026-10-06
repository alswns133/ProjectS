using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 상점 그리드의 카드 한 장. 아이템 하나(이름·정보·아이콘)와 가격을 표시하고, 클릭하면 호스트(ShopPopup)에
    /// 자기를 알려 선택 상태로 만든다. 구입/판매 어느 쪽이든 같은 카드를 쓰고, 실제로 무엇을 거래할지는
    /// <see cref="Payload"/>(구입=ShopItemEntry, 판매=ItemStack 또는 EquipmentInstance)에 담아 호스트가 해석한다.
    /// 아이콘은 <see cref="ItemIconLoader"/>로 어드레서블에서 비동기 로드한다(InventoryItemSlot과 같은 방식).
    /// 클릭을 받으려면 이 오브젝트에 raycastTarget인 Graphic(배경/아이콘)이 있어야 한다.
    ///
    /// 수량(ItemCounter)은 카드가 스스로 관리한다 — 호스트는 Bind에서 상한(maxCount)만 알려주고,
    /// 거래 시점에 <see cref="Count"/>를 읽어 간다. 상한이 1이면 카운터는 통째로 숨는다(장비 등).
    /// 수량은 화살표(±1) 외에 입력칸(countInput)에 숫자를 직접 쳐서 정할 수도 있다(1 ~ MaxCount로 잘림).
    /// </summary>
    public class ShopItemCard : MonoBehaviour, IPointerClickHandler
    {
        [Tooltip("카드 자체 아이콘. 아이템 슬롯(itemSlot)이 있으면 슬롯이 아이콘을 그리므로 쓰지 않는다.")]
        [SerializeField] private Image icon;

        [Tooltip("카드 안의 아이템 슬롯(InventoryItemSlot 프리팹). 아이콘·+N/수량·등급 표시 전용이며 조작은 전부 막힌다. " +
                 "비워두면 자식에서 자동으로 찾는다.")]
        [SerializeField] private InventoryItemSlot itemSlot;

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private TMP_Text priceText;

        [Tooltip("선택 시 켜는 하이라이트(테두리 등). 없어도 동작한다.")]
        [SerializeField] private GameObject selectedHighlight;

        [Header("수량 카운터(선택 — 비워두면 항상 1개 거래)")]
        [Tooltip("카운터 묶음(ItemCounter). 수량 조절이 불가능한 항목에선 통째로 끈다.")]
        [SerializeField] private GameObject counterRoot;
        [Tooltip("현재 수량 표시(ItemCounter/Num).")]
        [SerializeField] private TMP_Text countText;
        [Tooltip("수량 직접 입력칸(ItemCounter/NumInput). 넣으면 숫자를 타이핑해 수량을 정할 수 있다. " +
                 "비워두면 화살표로만 조절한다. countText와 함께 쓰면 둘 다 갱신된다.")]
        [SerializeField] private TMP_InputField countInput;
        [Tooltip("수량 +1 (ItemCounter/UpArrow).")]
        [SerializeField] private Button increaseButton;
        [Tooltip("수량 -1 (ItemCounter/DownArrow).")]
        [SerializeField] private Button decreaseButton;

        [Tooltip("카드별 [구매] 버튼. 누르면 이 카드의 현재 수량(Count)으로 구매를 요청한다.")]
        [SerializeField] private Button buyButton;

        [Tooltip("켜면 가격 칸에 단가 대신 '단가 × 수량' 합계를 표시한다.")]
        [SerializeField] private bool showTotalPrice = true;

        // 늦게 온 아이콘을 버리기 위한 현재 아이템(그리드 재사용 중 다른 아이템으로 재바인딩 대비).
        private ItemData currentItem;
        private Action<ShopItemCard> onClick;
        private Action<ShopItemCard> onBuy;
        private int unitPrice;

        // 프리팹에 지정된 이름 색. 등급 행을 못 찾을 때(테이블 로딩 전 등) 되돌릴 기본값으로, 처음 칠하기 직전에 한 번만 저장한다.
        // Awake가 아니라 첫 Bind에서 잡는 이유: 비활성 부모 아래 생성되면 Awake보다 Bind가 먼저 불려 등급색을 기본값으로 잡아 버린다.
        private Color? defaultNameColor;

        /// <summary>이 카드가 거래하는 대상. 구입=<see cref="ShopItemEntry"/>, 판매=ItemStack 또는 EquipmentInstance.</summary>
        public object Payload { get; private set; }

        /// <summary>지금 선택된 거래 수량(1 이상). 호스트가 구입/판매 확정 시 읽는다.</summary>
        public int Count { get; private set; } = 1;

        /// <summary>이 카드에서 올릴 수 있는 최대 수량(호스트가 Bind로 지정: 소지금·스택 한도·보유량).</summary>
        public int MaxCount { get; private set; } = 1;

        // 리스너는 여기서 한 번만 건다. 카드는 풀에서 재사용되며 Bind가 여러 번 불리므로,
        // Bind에서 걸면 클릭 한 번에 수량이 여러 칸씩 뛴다.
        private void Awake()
        {
            if (itemSlot == null) itemSlot = GetComponentInChildren<InventoryItemSlot>(true);
            if (itemSlot != null) MakeSlotDisplayOnly();

            if (increaseButton != null) increaseButton.onClick.AddListener(() => Step(1));
            if (decreaseButton != null) decreaseButton.onClick.AddListener(() => Step(-1));
            if (buyButton != null) buyButton.onClick.AddListener(() => onBuy?.Invoke(this));

            if (countInput != null)
            {
                // 숫자만 받는다. 음수 부호는 IntegerNumber가 허용하지만 SetCount의 Clamp가 1로 되돌린다.
                countInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                countInput.onSelect.AddListener(OnCountInputSelected);
                countInput.onValueChanged.AddListener(OnCountInputChanged);
                countInput.onEndEdit.AddListener(OnCountInputEndEdit);
            }
        }

        /// <summary>카드에 아이템·가격·거래 대상을 채우고 클릭 콜백을 건다(호스트가 카드 재사용마다 호출).</summary>
        /// <param name="item">표시할 아이템 정의(이름·정보·아이콘)</param>
        /// <param name="price">아이템 1개 가격(구입가 또는 판매가)</param>
        /// <param name="payload">거래 대상(구입=ShopItemEntry, 판매=ItemStack/EquipmentInstance)</param>
        /// <param name="clickHandler">카드 클릭 시 호출할 콜백</param>
        /// <param name="maxCount">올릴 수 있는 최대 수량. 1이면 카운터를 숨긴다(장비처럼 낱개 거래).</param>
        public void Bind(ItemData item, int price, object payload, Action<ShopItemCard> clickHandler, int maxCount = 1)
        {
            currentItem = item;
            Payload = payload;
            onClick = clickHandler;
            unitPrice = price;
            MaxCount = Mathf.Max(1, maxCount);
            Count = 1;   // 재사용된 카드에 옛 수량이 남지 않게 항상 1로 되돌린다

            if (nameText != null)
            {
                nameText.text = item != null ? item.Name : string.Empty;

                // 카드는 풀에서 재사용되므로 매 Bind마다 색을 다시 칠한다(안 하면 이전 카드의 등급색이 남는다).
                defaultNameColor ??= nameText.color;
                nameText.color = ItemGradeColors.ColorOf(item, defaultNameColor.Value);
            }
            if (infoText != null) infoText.text = item != null ? item.Description : string.Empty;

            RefreshCounter();
            SetSelected(false);

            if (itemSlot != null) FillSlot(item, payload);
            else LoadIcon(item);
        }

        // 카드 안 슬롯을 표시 전용으로 만든다. 슬롯의 클릭(우클릭 메뉴·더블클릭 강화 선택)·드래그·드롭·툴팁은
        // 전부 포인터 이벤트로 시작하므로, 레이캐스트를 끊으면 슬롯 코드를 건드리지 않고 한 번에 막힌다.
        // 끊긴 클릭은 뒤의 카드 본체로 떨어져, 아이콘 위를 눌러도 카드가 선택된다.
        // 프리팹 설정이 아니라 코드로 거는 이유는, 슬롯 프리팹을 인벤과 공유하므로 카드 쪽에서 빠뜨릴 수 없게 하기 위함이다.
        private void MakeSlotDisplayOnly()
        {
            if (!itemSlot.TryGetComponent(out CanvasGroup group))
                group = itemSlot.gameObject.AddComponent<CanvasGroup>();

            group.blocksRaycasts = false;
        }

        // 슬롯에 거래 대상을 그린다. 판매는 실제 인벤 내용물(스택 수량·장비 +N)을 그대로, 구입은 아이템 정의만 보여준다.
        private void FillSlot(ItemData item, object payload)
        {
            // 카드 자체 아이콘은 슬롯과 겹치지 않게 끈다. 슬롯을 채우기 "전에" 꺼야 한다 — icon이 슬롯의
            // 아이콘 Image를 가리키도록 연결돼 있어도, 이어지는 슬롯의 아이콘 로드가 다시 켜 준다.
            if (icon != null) { icon.sprite = null; icon.enabled = false; }

            switch (payload)
            {
                case EquipmentInstance equipment:
                    itemSlot.SetEquipment(equipment);
                    break;
                case ItemStack stack:
                    itemSlot.SetStack(stack);
                    break;
                default:
                    // 구입 목록은 인스턴스가 없으므로 표시용 1개짜리 스택을 만들어 넘긴다(수량 1은 라벨이 숨는다).
                    if (item != null) itemSlot.SetStack(new ItemStack(item, null));
                    else itemSlot.SetEmpty();
                    break;
            }
        }

        /// <summary>
        /// 카드의 [구매] 버튼 콜백을 건다(호스트가 Bind 직후 호출). 리스너는 Awake에서 한 번만 걸고
        /// 여기선 대상 콜백만 바꾼다 — 풀 재사용 중 Bind마다 AddListener하면 한 번 눌러 여러 번 사진다.
        /// </summary>
        /// <param name="handler">구매 요청 콜백. null이면 버튼이 아무것도 하지 않는다.</param>
        public void SetBuyHandler(Action<ShopItemCard> handler) => onBuy = handler;

        /// <summary>선택 하이라이트를 켜고 끈다(호스트가 선택 변경 시 호출).</summary>
        /// <param name="on">선택 상태면 true</param>
        public void SetSelected(bool on)
        {
            if (selectedHighlight != null) selectedHighlight.SetActive(on);
        }

        /// <summary>수량을 지정 값으로 맞춘다(1 ~ <see cref="MaxCount"/>로 잘린다).</summary>
        /// <param name="value">원하는 수량</param>
        public void SetCount(int value)
        {
            Count = Mathf.Clamp(value, 1, MaxCount);
            RefreshCounter();
        }

        /// <summary>카드를 클릭하면 호스트에 자기를 알린다(선택 교체는 호스트가 처리).</summary>
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(this);

        // 화살표 한 번 = ±1. 수량을 만지면 그 카드가 선택되게 호스트에도 알린다
        // (화살표는 자기 Button이 클릭을 먹어 카드 본체의 OnPointerClick까지 올라오지 않는다).
        private void Step(int delta)
        {
            onClick?.Invoke(this);
            SetCount(Count + delta);
        }

        // 입력칸을 누르면 카드도 선택되게 한다(입력칸이 클릭을 먹어 카드 본체까지 올라오지 않는 건 화살표와 같다).
        private void OnCountInputSelected(string _) => onClick?.Invoke(this);

        // 타이핑 중 실시간 반영. 거래 버튼은 Count를 바로 읽으므로 입력 확정(엔터/포커스 해제)을 기다리지 않는다.
        // 빈칸(전부 지우고 다시 쓰는 중)은 Count를 건드리지 않고 그대로 둔다 — 여기서 1로 채우면
        // "1을 지우고 5를 치면 15"가 되는 식으로 입력이 꼬인다. 빈칸 복구는 OnCountInputEndEdit이 맡는다.
        private void OnCountInputChanged(string text)
        {
            if (!int.TryParse(text, out int value)) return;

            int clamped = Mathf.Clamp(value, 1, MaxCount);
            Count = clamped;

            // 상한 초과·0 입력은 즉시 잘라 보여준다(얼마까지 살 수 있는지 입력하면서 바로 알 수 있게).
            // 범위 안이면 텍스트를 덮어쓰지 않는다 — 캐럿 위치가 튀지 않게.
            if (clamped != value) RefreshCounter();
            else RefreshCounterExceptInput();
        }

        // 입력 확정(엔터·포커스 해제) 시 빈칸이나 잘못된 값을 현재 Count로 되돌린다.
        private void OnCountInputEndEdit(string text)
        {
            SetCount(int.TryParse(text, out int value) ? value : Count);
        }

        // 수량 표시·가격·화살표 활성 상태를 현재 Count/MaxCount에 맞춘다.
        private void RefreshCounter()
        {
            // SetTextWithoutNotify: 코드가 넣은 값으로 onValueChanged가 다시 돌지 않게 한다.
            if (countInput != null) countInput.SetTextWithoutNotify(Count.ToString());
            RefreshCounterExceptInput();
        }

        // 입력칸 텍스트만 빼고 갱신한다. 타이핑 중엔 입력칸을 덮어쓰면 캐럿이 튀므로 이쪽을 쓴다.
        private void RefreshCounterExceptInput()
        {
            // 상한이 1이면 조절할 여지가 없으므로 카운터를 통째로 숨긴다(장비, 소지금 부족 등).
            if (counterRoot != null) counterRoot.SetActive(MaxCount > 1);

            if (countText != null) countText.text = Count.ToString();
            if (priceText != null) priceText.text = (showTotalPrice ? unitPrice * Count : unitPrice).ToString();

            // 끝에 닿으면 눌러도 변화가 없으므로 버튼을 꺼서 한계를 눈에 보이게 한다.
            if (increaseButton != null) increaseButton.interactable = Count < MaxCount;
            if (decreaseButton != null) decreaseButton.interactable = Count > 1;
        }

        // 아이콘을 비동기 로드한다. 대기 중 카드가 다른 아이템으로 재바인딩되면 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(ItemData item)
        {
            // 로드 전엔 아이콘을 비운다 — 스프라이트 없는 Image의 흰 사각형 팝인을 막는다(완료 시 다시 켠다).
            if (icon != null) { icon.sprite = null; icon.enabled = false; }
            if (item == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);

            if (this == null || icon == null) return;
            if (!ReferenceEquals(currentItem, item)) return;   // 카드가 다른 아이템으로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
