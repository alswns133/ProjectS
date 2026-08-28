using UnityEngine;
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

            // TODO shopId 해석: 지금은 임시 상점 하나 → 1 고정.
            //      NPC마다 다른 상점을 열려면 NpcInteractionController에 shopId를 노출해 npc.ShopId를 쓴다.

            int shopId = npc.GetShopIdForFeature(feature);

            ShopTable shop = JsonManager.Instance != null ?
                JsonManager.Instance.Get<ShopTable>(shopId) : null;
            if (shop == null) return;   // 정의 없음/로딩 전 — 조용히 무시
            
            CurrentShop = shop;
            UIManager.Instance?.ShowPopup<ShopPopup>(); // 팝업 OnShow가 CurrentShop을 읽어 그린다
            activeNpc = npc;
            npc.HideHubForExternal(); // 상점 팝업이 NPC 허브를 가리므로, 허브를 숨기고 상호작용 잠금


        }

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


            // TODO 가방 여유 확인: AddItem은 꽉 차면 초과분을 버린다(돈은 나감). 넣을 자리부터 확인할 것.
            //      (장비=빈 셀 count개 / 스택=기존 스택 여유 + 빈 셀 계산)

            // TODO(sound): 상점 구매음(코인/거래 성사) — SoundManager.Instance.PlaySFX(<구매 SFX>);
            //   구매 실패(잔액/자리 부족)는 위 return 경로에서 별도 '거부' 음을 낼 수도 있다.
            inventory.Spend(total, 0, 0); // 차감 + FireGoldChanged + 저장
            inventory.AddItem(entry.ItemId, count);
            return true;
        }

        public bool SellStack(ProjectS.Items.ItemStack stack, int count = 1)
        {
            // TODO InventoryManager에 판매 메서드가 아직 없다(아래 2번). 그걸 호출한다.

            return InventoryManager.Instance != null
                && InventoryManager.Instance.SellStack(stack, count);
        }

        public bool SellEquipment(ProjectS.Enhance.EquipmentInstance eq)
        {
            return InventoryManager.Instance != null
                && InventoryManager.Instance.SellEquipment(eq);
        }
    }
}
