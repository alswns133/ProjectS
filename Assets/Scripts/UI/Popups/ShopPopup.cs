using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 상점 창(순수 View). 구입/판매 탭을 전환하며 카드 그리드를 그린다 — 구입 탭은 ShopManager.CurrentShop의
    /// 판매목록, 판매 탭은 보유 아이템(스택 + 장비)을 <see cref="ShopItemCard"/>로 나열한다. 카드 하나를 선택한 뒤
    /// 하단 구입/판매 버튼으로 거래를 확정한다. 수량은 카드의 ItemCounter로 고르며(구입=스택 한도·소지금,
    /// 판매=보유량이 상한), 확정 시 <see cref="ShopItemCard.Count"/>를 읽어 넘긴다. 장비는 인스턴스 단위라 낱개다.
    ///
    /// 하단 버튼은 <b>탭 겸 확정</b>이다 — 지금 탭이 아니면 그 탭으로 전환하고, 이미 그 탭이면 선택한 카드를 거래한다
    /// (스크린샷처럼 하단에 구입/판매 두 버튼만 있는 구성에 맞춘 것). 별도 탭 버튼을 두고 싶으면 그 버튼에서
    /// <see cref="SetTab"/>을 부르고 구입/판매 버튼은 확정만 하도록 갈라도 된다.
    ///
    /// 소유·재화 변경은 InventoryManager/ShopManager가 맡고, 이 창은 표시와 입력 전달만 한다(InventoryPopup과 같은 결).
    /// </summary>
    public class ShopPopup : BasePopup
    {
        private enum Tab { Buy, Sell }

        [Header("그리드")]
        [SerializeField] private Transform cardRoot;
        [SerializeField] private ShopItemCard cardPrefab;
        [SerializeField] private ScrollRect scrollRect;

        [Header("탭 선택 표시(선택 — 구입/판매 버튼 이미지)")]
        [SerializeField] private Image buyTabImage;
        [SerializeField] private Image sellTabImage;
        [SerializeField] private Color tabNormalColor = new Color(0.4f, 0.42f, 0.5f, 1f);
        [SerializeField] private Color tabSelectedColor = Color.white;

        [Header("버튼")]
        [Tooltip("구입: 구입 탭이 아니면 전환, 이미 구입 탭이면 선택 카드 구매.")]
        [SerializeField] private Button buyButton;
        [Tooltip("판매: 판매 탭이 아니면 전환, 이미 판매 탭이면 선택 카드 판매.")]
        [SerializeField] private Button sellButton;
        [SerializeField] private Button closeButton;

        [Header("기타")]
        [SerializeField] private TMP_Text goldText;

        // 카드 풀. 필요한 만큼 만들어 재사용하고, 남는 카드는 숨긴다(탭/갱신마다 파괴·재생성하지 않음).
        private readonly List<ShopItemCard> cards = new();
        private Tab currentTab = Tab.Buy;
        private ShopItemCard selectedCard;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만(OnInit은 SetActive 이전 1회 — BasePopup.Show 참고).
            // 인스펙터에선 Button 참조만 연결하면 되고 onClick은 코드가 건다.
            if (buyButton != null) buyButton.onClick.AddListener(OnBuyButton);
            if (sellButton != null) sellButton.onClick.AddListener(OnSellButton);
            if (closeButton != null) closeButton.onClick.AddListener(() => RequestClose());
        }

        protected override void OnShow()
        {
            PlayerEvents.OnGoldChanged += SetGold;
            InventoryEvents.OnInventoryChanged += Rebuild;   // 판매 후 보유목록 갱신
            PlayerEvents.FireStatsRefreshRequested();        // 골드 스냅샷(인벤과 동일 경로)

            currentTab = Tab.Buy;
            UpdateTabVisual();
            Rebuild();
            ResetScroll();
        }

        protected override void OnHide()
        {
            PlayerEvents.OnGoldChanged -= SetGold;
            InventoryEvents.OnInventoryChanged -= Rebuild;
            ShopManager.Instance?.OnShopClosed();
            ClearSelection();
        }

        // 구입 버튼: 구입 탭이 아니면 구입 탭으로 전환, 이미 구입 탭이면 선택한 카드를 산다.
        private void OnBuyButton()
        {
            if (currentTab != Tab.Buy) { SetTab(Tab.Buy); return; }

            if (selectedCard?.Payload is ShopItemEntry entry)
                ShopManager.Instance?.Buy(entry, selectedCard.Count);   // 실패(골드 부족·자리 없음)는 조용히 무시 — 피드백은 TODO
        }

        // 판매 버튼: 판매 탭이 아니면 판매 탭으로 전환, 이미 판매 탭이면 선택한 것을 판다.
        private void OnSellButton()
        {
            if (currentTab != Tab.Sell) { SetTab(Tab.Sell); return; }
            if (selectedCard == null) return;

            if (selectedCard.Payload is ItemStack stack)
                ShopManager.Instance?.SellStack(stack, selectedCard.Count);
            else if (selectedCard.Payload is EquipmentInstance eq)
                ShopManager.Instance?.SellEquipment(eq);
            // 판매 성공 시 InventoryEvents.OnInventoryChanged → Rebuild로 목록이 갱신된다.
        }

        private void SetTab(Tab tab)
        {
            currentTab = tab;
            UpdateTabVisual();
            Rebuild();
            ResetScroll();
        }

        private void UpdateTabVisual()
        {
            if (buyTabImage != null) buyTabImage.color = currentTab == Tab.Buy ? tabSelectedColor : tabNormalColor;
            if (sellTabImage != null) sellTabImage.color = currentTab == Tab.Sell ? tabSelectedColor : tabNormalColor;
        }

        // 현재 탭의 소스를 카드로 그린다. 카드는 풀에서 재사용되고 Bind가 선택·수량을 초기화하므로,
        // 거래 직후 재빌드에서도 같은 대상을 계속 고르고 있도록 선택을 payload 기준으로 되살린다
        // (안 하면 5개 사고 나서 5개 더 사려면 카드를 다시 클릭해야 한다).
        private void Rebuild()
        {
            if (!IsVisible) return;
            if (cardRoot == null || cardPrefab == null) return;

            object keep = selectedCard != null ? selectedCard.Payload : null;
            ClearSelection();

            int used = currentTab == Tab.Buy ? BuildBuyCards() : BuildSellCards();

            // 남는 카드는 숨긴다(풀 재사용).
            for (int i = used; i < cards.Count; i++)
                cards[i].gameObject.SetActive(false);

            RestoreSelection(keep, used);
        }

        // 재빌드 전에 고르고 있던 대상이 목록에 그대로 있으면 그 카드를 다시 선택한다.
        // 다 팔아 없어졌으면(스택 소진 등) 선택 없음으로 남는다.
        private void RestoreSelection(object payload, int used)
        {
            if (payload == null) return;

            for (int i = 0; i < used; i++)
            {
                if (!ReferenceEquals(cards[i].Payload, payload)) continue;

                selectedCard = cards[i];
                selectedCard.SetSelected(true);
                return;
            }
        }

        // 구입 탭: 현재 상점의 판매목록. 정의가 사라진 아이템(ItemData 없음)은 건너뛴다.
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

                GetCard(i).Bind(item, entry.BuyPrice, entry, OnCardClicked, BuyableCount(item, entry.BuyPrice));
                i++;
            }
            return i;
        }

        // 구입 탭에서 한 번에 살 수 있는 최대 개수. 스택 한도와 지금 소지금 중 작은 쪽으로 자른다
        // (ShopManager.Buy도 골드를 다시 검사하지만, 카운터가 살 수 없는 수량까지 올라가면 UI가 거짓말을 한다).
        // 살 돈이 아예 없으면 1을 돌려 카운터를 숨긴다 — 구매 자체는 Buy에서 실패로 막힌다.
        private static int BuyableCount(ItemData item, int price)
        {
            int limit = Mathf.Max(1, item.MaxStack);
            InventoryManager inv = InventoryManager.Instance;

            if (inv == null || price <= 0) return limit;
            return Mathf.Clamp(inv.Gold / price, 1, limit);
        }

        // 판매 탭: 보유 스택(소비품 + 재료) + 보유 장비. 판매가 = ItemData.SellPrice.
        private int BuildSellCards()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return 0;

            int i = 0;
            foreach (ItemStack stack in inv.StackItems)
            {
                if (stack?.Item == null) continue;
                GetCard(i).Bind(stack.Item, stack.Item.SellPrice, stack, OnCardClicked, stack.Count);
                i++;
            }
            foreach (EquipmentInstance eq in inv.OwnedEquipment)
            {
                if (eq?.Item == null) continue;
                GetCard(i).Bind(eq.Item, eq.Item.SellPrice, eq, OnCardClicked);   // 장비는 인스턴스마다 +N이 달라 낱개 거래(카운터 숨김)
                i++;
            }
            return i;
        }

        // 카드 클릭 → 선택 교체(이전 선택 해제).
        private void OnCardClicked(ShopItemCard card)
        {
            if (selectedCard == card) return;

            selectedCard?.SetSelected(false);
            selectedCard = card;
            selectedCard.SetSelected(true);
        }

        private void ClearSelection()
        {
            selectedCard?.SetSelected(false);
            selectedCard = null;
        }

        // 인덱스 위치의 카드를 얻는다(모자라면 생성). 활성화해 돌려준다(풀 재사용).
        private ShopItemCard GetCard(int index)
        {
            while (cards.Count <= index)
                cards.Add(Instantiate(cardPrefab, cardRoot));

            cards[index].gameObject.SetActive(true);
            return cards[index];
        }

        private void SetGold(int gold)
        {
            if (goldText != null) goldText.text = gold.ToString();
        }

        // 탭 전환·재오픈처럼 목록이 통째로 바뀔 때 스크롤을 맨 위로 되돌린다.
        // ForceUpdateCanvases로 Size Fitter가 새 콘텐츠 높이를 먼저 반영하게 한 뒤 위치를 잡아야 정확하다.
        private void ResetScroll()
        {
            if (scrollRect == null) return;
            scrollRect.StopMovement();  // 관성 제거(안 하면 리셋 후 다시 흘러

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f; // 1=맨 위
        }
    }
}
