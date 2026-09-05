using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;

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
    /// </summary>
    public class ShopItemCard : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image icon;
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
        [Tooltip("수량 +1 (ItemCounter/UpArrow).")]
        [SerializeField] private Button increaseButton;
        [Tooltip("수량 -1 (ItemCounter/DownArrow).")]
        [SerializeField] private Button decreaseButton;

        [Tooltip("켜면 가격 칸에 단가 대신 '단가 × 수량' 합계를 표시한다.")]
        [SerializeField] private bool showTotalPrice = true;

        // 늦게 온 아이콘을 버리기 위한 현재 아이템(그리드 재사용 중 다른 아이템으로 재바인딩 대비).
        private ItemData currentItem;
        private Action<ShopItemCard> onClick;
        private int unitPrice;

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
            if (increaseButton != null) increaseButton.onClick.AddListener(() => Step(1));
            if (decreaseButton != null) decreaseButton.onClick.AddListener(() => Step(-1));
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

            if (nameText != null) nameText.text = item != null ? item.Name : string.Empty;
            if (infoText != null) infoText.text = item != null ? item.Description : string.Empty;

            RefreshCounter();
            SetSelected(false);
            LoadIcon(item);
        }

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

        // 수량 표시·가격·화살표 활성 상태를 현재 Count/MaxCount에 맞춘다.
        private void RefreshCounter()
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
