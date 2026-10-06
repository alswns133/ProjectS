using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.NPCs;
using ProjectS.UI;

namespace ProjectS.Managers
{
    /// <summary>
    /// 상점의 브레인. NPC 허브에서 상점을 고르면 팝업을 열고, 구매·판매를 실행한다.
    /// 소유·재화 변경은 InventoryManager에 위임하고(FireGoldChanged·저장 자동), 이 매니저는
    /// "무엇을 파는 상점인가 + 거래 판정"만 맡는다. 팝업(ShopPopup)은 순수 View로 이 값을 읽어 그린다.
    /// </summary>
    public class ShopManager : MonoBehaviour
    {

        /// <summary>싱글톤 인스턴스. 중복 생성분은 Awake에서 제거된다.</summary>
        public static ShopManager Instance { get; private set; }
        

        /// <summary>
        /// 지금 열려 있는 상점 정의(팝업이 읽는다). 닫히면 null.
        /// </summary>
        public ShopTable CurrentShop { get; private set; }

        private NpcInteractionController activeNpc;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            NpcInteractionController.HubFeatureSelected += OnFeature;
        }

        private void OnDisable()
        {
            NpcInteractionController.HubFeatureSelected -= OnFeature;
        }


        private void OnFeature(NpcInteractionController npc, NpcHubFeature feature)
        {
            if (feature != NpcHubFeature.Shop) return;

            // 상점 ID는 NPC 허브 설정(hubFeatures의 shopId)에서 온다 — NPC마다 다른 상점(잡화/장비)을 연다.
            int shopId = npc.GetShopIdForFeature(feature);

            ShopTable shop = JsonManager.Instance != null ?
                JsonManager.Instance.Get<ShopTable>(shopId) : null;
            if (shop == null) return;   // 정의 없음/로딩 전 — 조용히 무시
            
            CurrentShop = shop;
            UIManager.Instance?.ShowPopup<ShopPopup>(); // 팝업 OnShow가 CurrentShop을 읽어 그린다
            activeNpc = npc;
            npc.HideHubForExternal(); // 상점 팝업이 NPC 허브를 가리므로, 허브를 숨기고 상호작용 잠금


        }

        /// <summary>상점 팝업이 닫힐 때 호출. 상점을 연 NPC의 허브(인사말)로 돌려보내 상호작용 잠금을 푼다.</summary>
        public void OnShopClosed()
        {
            activeNpc?.BackToGreeting(); // 상점 닫으면 허브로 돌아가고 상호작용 잠금 해제
            activeNpc = null;
        }

        // ---------- 거래 ----------

        /// <summary>상점 항목을 구매한다. 골드가 모자라거나 가방에 자리가 없으면 false.</summary>
        public bool Buy(ShopItemEntry entry, int count = 1)
        {
            if (entry == null || count <= 0) return false;

            InventoryManager inventory = InventoryManager.Instance;
            int total = entry.BuyPrice * count;

            if (inventory == null || ! inventory.CanAfford(total, 0, 0)) return false;

            // 가방 여유 확인: AddItem은 꽉 차면 초과분을 버리므로(돈은 이미 나감), 넣을 자리부터 확인한다.
            if (!inventory.CanAddItem(entry.ItemId, count)) return false;

            // 구매 실패(잔액/자리 부족)는 위 return 경로에서 별도 '거부' 음을 낼 수도 있다.
            SoundManager.Instance?.PlaySFX(SoundID.SFX_Trade);
            inventory.Spend(total, 0, 0); // 차감 + FireGoldChanged + 저장
            inventory.AddItem(entry.ItemId, count);
            return true;
        }

        /// <summary>소비품·재료 스택을 판매한다. 실제 판매는 InventoryManager가 처리한다.</summary>
        /// <param name="stack">판매할 스택.</param>
        /// <param name="count">판매 개수.</param>
        /// <returns>판매에 성공하면 true.</returns>
        public bool SellStack(ProjectS.Items.ItemStack stack, int count = 1)
        {
            return InventoryManager.Instance != null
                && InventoryManager.Instance.SellStack(stack, count);
        }

        /// <summary>장비 한 개를 판매한다. 실제 판매는 InventoryManager가 처리한다.</summary>
        /// <returns>판매에 성공하면 true.</returns>
        public bool SellEquipment(ProjectS.Enhance.EquipmentInstance eq)
        {
            return InventoryManager.Instance != null
                && InventoryManager.Instance.SellEquipment(eq);
        }

        /// <summary>
        /// 판매 UI의 [판매하기] — 판매 슬롯에 올린 항목 전체를 한 번에 판다.
        /// 판매 UI는 예약만 하고 인벤을 건드리지 않으므로, 실제 제거·골드 지급은 전부 여기서 일어난다.
        /// </summary>
        /// <param name="entries">판매 예정 항목들(빈칸 제외).</param>
        /// <returns>하나라도 팔렸으면 true.</returns>
        public bool SellAll(System.Collections.Generic.IReadOnlyList<ProjectS.Items.ShopSellEntry> entries)
        {
            // 검증·제거·골드·저장은 소유자인 InventoryManager가 한 번에 한다(저장 1회). 이 매니저는 상점 쪽 진입점만 맡는다.
            return InventoryManager.Instance != null
                && InventoryManager.Instance.SellBatch(entries);
        }
    }
}
