using UnityEngine;
using ProjectS.NPCs;
using ProjectS.UI;

namespace ProjectS.Managers
{
    /// <summary>
    /// 강화의 허브 진입점. NPC 허브에서 강화를 고르면 강화창(<see cref="EnhancePopup"/>)을 연다.
    /// 판정·차감은 EnhanceService/InventoryManager가, 표시·입력은 EnhancePresenter가 맡고,
    /// 이 매니저는 "허브에서 강화창을 열고, 닫히면 NPC 허브로 돌려보내는" 연결만 담당한다(ShopManager와 같은 결).
    /// 상점의 CurrentShop 같은 보유 상태가 없어 더 얇다 — 강화창은 대상 장비를 인벤에서 드래그로 고르기 때문이다.
    /// </summary>
    /// <remarks>
    /// 팝업의 Presenter는 팝업이 활성일 때만 이벤트를 구독하므로(닫히면 비활성), 허브 선택 이벤트를
    /// 상시 받으려면 이렇게 항상 켜진 매니저가 필요하다. NPC가 있는 씬(마을 등)에 ShopManager와 함께 배치한다.
    /// </remarks>
    public class EnhanceManager : MonoBehaviour
    {
        public static EnhanceManager Instance { get; private set; }

        // 강화창을 연 NPC. 닫을 때 이 NPC의 허브로 되돌려 상호작용 잠금을 푼다.
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

        // 허브에서 강화를 골랐을 때만 반응한다(상점 등 다른 기능은 각자 매니저가 처리).
        private void OnFeature(NpcInteractionController npc, NpcHubFeature feature)
        {
            if (feature != NpcHubFeature.Enhance) return;

            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            ui.ShowPopup<EnhancePopup>();
            // 강화 대상은 인벤 슬롯 드래그/더블클릭으로 고르므로, 강화창과 함께 인벤토리도 연다
            // (허브 진입 시 인벤이 닫혀 있어, 이걸 안 열면 대상을 고를 수단이 없다).
            ui.ShowPopup<InventoryPopup>();
            activeNpc = npc;
            npc.HideHubForExternal();   // 강화창이 NPC 허브를 가리므로 허브를 숨기고 상호작용 잠금
        }

        /// <summary>
        /// 강화창이 닫힐 때 <see cref="EnhancePopup"/>.OnHide가 호출한다. 함께 열었던 인벤/장비창을 정리하고,
        /// 강화창을 연 NPC가 있으면 그 허브로 되돌아가 상호작용 잠금을 푼다(NPC 없이 열린 경우엔 허브 복귀만 건너뛴다).
        /// </summary>
        public void OnEnhanceClosed()
        {
            UIManager ui = UIManager.Instance;
            if (ui != null)
            {
                // 강화 흐름에서 띄운 창들을 함께 닫아 허브로 깔끔히 돌아간다(안 열려 있으면 no-op).
                ui.ClosePopup<InventoryPopup>();
                ui.ClosePopup<EquipmentPopup>();
            }

            activeNpc?.BackToGreeting();
            activeNpc = null;
        }
    }
}
