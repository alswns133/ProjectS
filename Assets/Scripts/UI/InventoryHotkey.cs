using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// I키로 인벤토리 창(<see cref="InventoryPopup"/>)을 열고 닫는다. 이미 열려 있으면 닫고, 아니면 연다.
    /// 인벤토리는 다른 창과 공존하는 팝업이라 "최상단인지"가 아니라 "떠 있는지"로 토글을 판정한다.
    /// UI 열기는 게임플레이 입력이 아니므로 PlayerInputHandler에 넣지 않고(입력 경계 규칙) 이 전용 컴포넌트가
    /// 담당한다. 씬을 넘어 유지되는 오브젝트(예: UIManager)에 붙인다.
    /// </summary>
    public class InventoryHotkey : MonoBehaviour
    {
        // NPC 상호작용이 시작되면(대화·허브·보상) 열려 있던 인벤토리를 닫는다.
        private void OnEnable() => NpcInteractionController.ActiveChanged += OnNpcInteractionChanged;
        private void OnDisable() => NpcInteractionController.ActiveChanged -= OnNpcInteractionChanged;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.iKey.wasPressedThisFrame) Toggle();
        }

        // 상호작용 시작(active != null) 시 인벤토리가 떠 있으면 닫는다. 종료(null)엔 아무것도 하지 않는다.
        private void OnNpcInteractionChanged(NpcInteractionController active)
        {
            if (active != null) UIManager.Instance?.ClosePopup<InventoryPopup>();
        }

        // 인벤토리가 떠 있으면 닫고, 아니면 새로 띄운다.
        // 단, NPC 상호작용(대화·허브·보상) 중에는 열지 않는다(이미 열려 있으면 닫는 건 허용).
        private void Toggle()
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<InventoryPopup>())
            {
                ui.ClosePopup<InventoryPopup>();
                return;
            }

            // NPC와 대화/허브 중이면 열기를 막는다. 단, 강화창이 떠 있으면 허용한다
            // (강화 대상을 인벤에서 드래그로 골라야 하므로 — EnhanceManager가 강화와 함께 인벤을 연다).
            if (NpcInteractionController.Active != null && !ui.IsPopupOpen<EnhancePopup>()) return;

            ui.ShowPopup<InventoryPopup>();
        }
    }
}
