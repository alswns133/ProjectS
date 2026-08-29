using ProjectS.Managers;
using ProjectS.NPCs;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 팝업 하나를 여닫는 공용 토글. 키보드 핫키(I/P/K)와 HUD 메뉴 아이콘 버튼(<see cref="HudMenuButton"/>)이
    /// <b>같은 규칙</b>으로 열고 닫도록 한곳에 모은 것이다. 핫키마다 복붙돼 있던 토글 로직을 여기로 합쳐,
    /// 아이콘과 단축키가 어긋나지 않게 한다.
    /// </summary>
    public static class PopupToggle
    {
        /// <summary>여닫을 수 있는 팝업 창의 종류. 인스펙터에서 고른 값을 실제 팝업 타입으로 매핑한다.</summary>
        public enum PopupKind
        {
            Inventory,  // 인벤토리(I)
            Equipment,  // 장비창(P)
            Skill,      // 스킬창(K)
        }

        /// <summary>
        /// enum으로 지정한 팝업을 토글한다. enum → 실제 팝업 타입 매핑을 이 한곳에 모아,
        /// 키보드 핫키(<see cref="PopupHotkey"/>)와 아이콘 버튼(<see cref="HudMenuButton"/>)이 같은 표를 공유한다.
        /// (제네릭 <see cref="Toggle{T}"/>는 이 매핑이 부르는 실제 구현이다.)
        /// </summary>
        public static void Toggle(PopupKind kind)
        {
            switch (kind)
            {
                // 인벤/장비는 강화 대상 선택 때문에 강화창이 떠 있으면 열기를 허용, 스킬은 아님.
                case PopupKind.Inventory: Toggle<InventoryPopup>(allowDuringEnhance: true); break;
                case PopupKind.Equipment: Toggle<EquipmentPopup>(allowDuringEnhance: true); break;
                case PopupKind.Skill:     Toggle<SkillPopup>(allowDuringEnhance: false); break;
            }
        }

        /// <summary>
        /// enum으로 지정한 팝업이 열려 있으면 닫는다(토글이 아니라 무조건 닫기).
        /// NPC 상호작용이 시작될 때 열려 있던 창을 정리하는 용도.
        /// </summary>
        public static void Close(PopupKind kind)
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            switch (kind)
            {
                case PopupKind.Inventory: ui.ClosePopup<InventoryPopup>(); break;
                case PopupKind.Equipment: ui.ClosePopup<EquipmentPopup>(); break;
                case PopupKind.Skill:     ui.ClosePopup<SkillPopup>(); break;
            }
        }

        /// <summary>
        /// T 팝업을 토글한다. 이미 떠 있으면 닫고, 아니면 연다.
        /// NPC 대화·허브 중에는 새로 열지 않는다(닫기는 허용) — 상호작용 UI 위에 창이 겹치는 것을 막기 위함.
        /// </summary>
        /// <typeparam name="T">여닫을 BasePopup 파생 팝업.</typeparam>
        /// <param name="allowDuringEnhance">
        /// true면 강화창이 떠 있는 동안엔 NPC 상호작용 중이라도 여는 것을 허용한다.
        /// 강화 대상을 인벤/장비에서 드래그로 골라야 하는 인벤토리·장비창에만 준다(스킬창은 false).
        /// </param>
        public static void Toggle<T>(bool allowDuringEnhance = false) where T : BasePopup
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<T>())
            {
                ui.ClosePopup<T>();
                return;
            }

            // 대화·허브 중엔 열지 않는다. 단 강화창이 떠 있고 허용 대상이면 연다.
            if (NpcInteractionController.Active != null && !(allowDuringEnhance && ui.IsPopupOpen<EnhancePopup>()))
                return;

            ui.ShowPopup<T>();
        }
    }
}
