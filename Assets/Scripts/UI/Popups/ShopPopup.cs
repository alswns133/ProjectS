using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 상점 창(순수 View). 한 창 안에서 <b>구매 화면</b>과 <b>판매 화면</b>을 하단 버튼 하나로 전환한다.
    /// <list type="bullet">
    /// <item>구매 화면: 상단 카테고리 바(<see cref="ShopCategoryBar"/>)로 거른 판매목록 카드 그리드. 카드마다 [구매] 버튼.</item>
    /// <item>판매 화면: <see cref="ShopSellView"/>(판매 슬롯 격자 + 예상 골드 + [판매하기]).</item>
    /// </list>
    /// 하단 모드 버튼은 구매 화면에선 "판매", 판매 화면에선 "상점"으로 텍스트·아이콘을 바꿔 단다(버튼 하나 재사용).
    /// ? 버튼은 아이템 등급 안내를 토글한다. 상점을 열면 인벤토리도 같이 연다 — 판매는 인벤에서 아이템을 끌어오기 때문.
    ///
    /// 소유·재화 변경은 InventoryManager/ShopManager가 맡고, 이 창은 표시와 입력 전달만 한다.
    /// </summary>
    public class ShopPopup : BasePopup
    {
        private enum Mode { Buy, Sell }

        [Header("구매 화면")]
        [Tooltip("구매 화면 루트(카테고리 바 + 카드 그리드). 판매 화면일 때 끈다.")]
        [SerializeField] private GameObject buyRoot;
        [SerializeField] private ShopCategoryBar categoryBar;
        [SerializeField] private Transform cardRoot;
        [SerializeField] private ShopItemCard cardPrefab;
        [SerializeField] private ScrollRect scrollRect;

        [Header("판매 화면")]
        [Tooltip("판매 화면. 이 오브젝트를 켜고 끄는 것으로 화면을 전환한다(켜질 때 ShopSellView.Active 등록).")]
        [SerializeField] private ShopSellView sellView;

        [Header("모드 전환 버튼 (하단 버튼 하나)")]
        [SerializeField] private Button modeButton;
        [SerializeField] private TMP_Text modeButtonText;
        [SerializeField] private Image modeButtonIcon;
        [Tooltip("구매 화면일 때 버튼 표시(→ 판매 화면으로 감).")]
        [SerializeField] private string toSellLabel = "판매";
        [SerializeField] private Sprite toSellIcon;
        [Tooltip("판매 화면일 때 버튼 표시(→ 구매 화면으로 감).")]
        [SerializeField] private string toBuyLabel = "상점";
        [SerializeField] private Sprite toBuyIcon;

        [Header("도움말 (? 버튼)")]
        [SerializeField] private Button helpButton;
        [Tooltip("아이템 등급 안내 패널. 시작은 꺼 둔다.")]
        [SerializeField] private GameObject helpPanel;

        [Header("기타")]
        [SerializeField] private Button closeButton;

        [Header("문구")]
        [SerializeField] private string notEnoughGoldMessage = "골드가 부족합니다.";
        [SerializeField] private string bagFullMessage = "인벤토리 공간이 부족합니다.";

        // 카드 풀. 필요한 만큼 만들어 재사용하고, 남는 카드는 숨긴다(카테고리/갱신마다 파괴·재생성하지 않음).
        private readonly List<ShopItemCard> cards = new();
        private Mode currentMode = Mode.Buy;

        // 이 상점이 인벤을 대신 열었는지. 닫을 때 원래 열려 있던 인벤까지 닫지 않기 위해 기억한다.
        private bool openedInventory;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만(OnInit은 SetActive 이전 1회 — BasePopup.Show 참고).
            if (modeButton != null) modeButton.onClick.AddListener(ToggleMode);
            if (helpButton != null) helpButton.onClick.AddListener(ToggleHelp);
            if (closeButton != null) closeButton.onClick.AddListener(() => RequestClose());
            if (categoryBar != null) categoryBar.OnCategorySelected += _ => RebuildBuy();
        }

        protected override void OnShow()
        {
            InventoryEvents.OnInventoryChanged += RebuildBuy;   // 소지금·보유 변화로 카드 수량 상한이 바뀐다
            PlayerEvents.OnGoldChanged += OnGoldChanged;

            OpenInventoryAlongside();

            if (helpPanel != null) helpPanel.SetActive(false);

            // 카테고리(상점 종류별 탭 정리 + 전체 선택)를 먼저 정하고, SetMode가 그 카테고리로 목록을 그린다.
            // 순서가 반대면 첫 목록이 지난번 상점의 카테고리로 한 번 그려진다.
            ShopTable shop = ShopManager.Instance != null ? ShopManager.Instance.CurrentShop : null;
            if (categoryBar != null && shop != null) categoryBar.Setup(shop.ShopType);

            SetMode(Mode.Buy);
            ResetScroll();
        }

        protected override void OnHide()
        {
            InventoryEvents.OnInventoryChanged -= RebuildBuy;
            PlayerEvents.OnGoldChanged -= OnGoldChanged;

            // 판매 화면을 끄면 ShopSellView.OnDisable이 예약 목록·수량 팝업을 정리한다.
            if (sellView != null) sellView.gameObject.SetActive(false);

            CloseInventoryAlongside();
            ShopManager.Instance?.OnShopClosed();
        }

        // ---------- 모드 전환 ----------

        private void ToggleMode() => SetMode(currentMode == Mode.Buy ? Mode.Sell : Mode.Buy);

        // 화면 루트를 켜고 끄고, 하단 버튼을 "반대편으로 가는" 표시로 바꿔 단다.
        private void SetMode(Mode mode)
        {
            currentMode = mode;

            if (buyRoot != null) buyRoot.SetActive(mode == Mode.Buy);
            if (sellView != null) sellView.gameObject.SetActive(mode == Mode.Sell);

            // 버튼은 "지금 화면"이 아니라 "누르면 갈 화면"을 보여준다: 구매 화면에선 [판매], 판매 화면에선 [상점].
            bool toSell = mode == Mode.Buy;
            if (modeButtonText != null) modeButtonText.text = toSell ? toSellLabel : toBuyLabel;

            Sprite icon = toSell ? toSellIcon : toBuyIcon;
            if (modeButtonIcon != null && icon != null) modeButtonIcon.sprite = icon;   // 스프라이트 미지정이면 프리팹 아이콘 유지

            // 구매 화면으로 돌아올 땐 보던 카테고리·스크롤을 그대로 둔다(판매하고 와서 다시 찾지 않게).
            // 판매 화면에 있던 동안 소지금이 바뀌어 카드 수량 상한이 달라졌을 수 있으니 목록만 다시 그린다.
            if (mode == Mode.Buy) RebuildBuy();
        }

        // ---------- 도움말 ----------

        // ? 버튼: 등급 안내를 켜고/끈다(같은 버튼으로 토글).
        private void ToggleHelp()
        {
            if (helpPanel != null) helpPanel.SetActive(!helpPanel.activeSelf);
        }

        // ---------- 인벤토리 동시 표시 ----------

        // 판매는 인벤에서 아이템을 끌어/우클릭해 오므로 상점과 인벤을 같이 띄운다.
        private void OpenInventoryAlongside()
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            openedInventory = !ui.IsPopupOpen<InventoryPopup>();
            if (openedInventory) ui.ShowPopup<InventoryPopup>();
        }

        private void CloseInventoryAlongside()
        {
            // 상점이 대신 연 인벤만 같이 닫는다. 유저가 원래 열어 둔 인벤은 그대로 둔다(유저가 연 창을 상점이 닫지 않게).
            // 인벤을 먼저(ESC 등으로) 닫았으면 ClosePopup은 아무것도 하지 않는다.
            if (openedInventory) UIManager.Instance?.ClosePopup<InventoryPopup>();
            openedInventory = false;
        }

        // ---------- 구매 화면 ----------

        // 현재 카테고리에 맞는 판매목록을 카드로 그린다.
        private void RebuildBuy()
        {
            if (!IsVisible || currentMode != Mode.Buy) return;
            if (cardRoot == null || cardPrefab == null) return;

            int used = BuildBuyCards();

            // 남는 카드는 숨긴다(풀 재사용).
            for (int i = used; i < cards.Count; i++)
                cards[i].gameObject.SetActive(false);
        }

        // 구매 목록: 현재 상점 판매목록 중 선택 카테고리에 속하는 것만. 정의가 사라진 아이템은 건너뛴다.
        private int BuildBuyCards()
        {
            ShopTable shop = ShopManager.Instance != null ? ShopManager.Instance.CurrentShop : null;
            JsonManager json = JsonManager.Instance;
            if (shop == null || json == null) return 0;

            int i = 0;
            foreach (ShopItemEntry entry in shop.Items)
            {
                ItemData item = json.Get<ItemData>(entry.ItemId);
                if (item == null) continue;

                // 카테고리 필터. 장비 부위·무기 종류는 EquipmentData에 있으므로 장비일 때만 함께 넘긴다.
                if (categoryBar != null)
                {
                    bool isEquipment = item.Category == ItemCategory.Weapon || item.Category == ItemCategory.Armor;
                    EquipmentData equipment = isEquipment ? json.Get<EquipmentData>(entry.ItemId) : null;
                    if (!ShopCategoryBar.Matches(categoryBar.Current, item, equipment, categoryBar.ShopType)) continue;
                }

                ShopItemCard card = GetCard(i);
                card.Bind(item, entry.BuyPrice, entry, null, BuyableCount(item, entry.BuyPrice));
                card.SetBuyHandler(OnCardBuy);
                i++;
            }
            return i;
        }

        // 카드의 [구매] 버튼.
        private void OnCardBuy(ShopItemCard card)
        {
            if (card?.Payload is not ShopItemEntry entry) return;

            ShopManager shop = ShopManager.Instance;
            if (shop == null || shop.Buy(entry, card.Count)) return;

            // 실패 사유 안내. Buy는 bool만 돌려주므로 같은 조건을 여기서 다시 봐 문구를 고른다(검사 순서도 Buy와 같게).
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            if (!inv.CanAfford(entry.BuyPrice * card.Count, 0, 0)) UIEvents.FireToast(notEnoughGoldMessage);
            else if (!inv.CanAddItem(entry.ItemId, card.Count)) UIEvents.FireToast(bagFullMessage);
        }

        // 한 번에 살 수 있는 최대 개수. 스택 한도와 지금 소지금 중 작은 쪽으로 자른다
        // (ShopManager.Buy도 골드를 다시 검사하지만, 카운터가 살 수 없는 수량까지 올라가면 UI가 거짓말을 한다).
        // 살 돈이 아예 없으면 1을 돌려 카운터를 숨긴다 — 구매 자체는 Buy에서 실패로 막힌다.
        private static int BuyableCount(ItemData item, int price)
        {
            int limit = Mathf.Max(1, item.MaxStack);
            InventoryManager inv = InventoryManager.Instance;

            if (inv == null || price <= 0) return limit;
            return Mathf.Clamp(inv.Gold / price, 1, limit);
        }

        // 골드가 바뀌면 카드 수량 상한(BuyableCount)이 달라지므로 다시 그린다.
        private void OnGoldChanged(int _) => RebuildBuy();

        // 인덱스 위치의 카드를 얻는다(모자라면 생성). 활성화해 돌려준다(풀 재사용).
        private ShopItemCard GetCard(int index)
        {
            while (cards.Count <= index)
                cards.Add(Instantiate(cardPrefab, cardRoot));

            cards[index].gameObject.SetActive(true);
            return cards[index];
        }

        // 카테고리 전환·재오픈처럼 목록이 통째로 바뀔 때 스크롤을 맨 위로 되돌린다.
        // ForceUpdateCanvases로 Size Fitter가 새 콘텐츠 높이를 먼저 반영하게 한 뒤 위치를 잡아야 정확하다.
        private void ResetScroll()
        {
            if (scrollRect == null) return;
            scrollRect.StopMovement();  // 관성 제거(안 하면 리셋 후 다시 흘러감)

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f; // 1=맨 위
        }
    }
}
