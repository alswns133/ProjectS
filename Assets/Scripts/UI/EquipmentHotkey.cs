using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// P키로 장비창(<see cref="EquipmentPopup"/>)을 열고 닫는다. 인벤토리 핫키와 같은 방침 —
    /// 다른 창과 공존하는 팝업이라 "떠 있는지"로 토글하고, NPC 상호작용 중엔 열지 않으며(이미 열렸으면 닫힘),
    /// 상호작용이 시작되면 닫는다. 씬을 넘어 유지되는 오브젝트(예: UIManager)에 붙인다.
    /// (P는 원래 타임스케일 디버그 키였으나 그건 , . / m 으로 옮겼다 — DebugTimeScaleKey.)
    /// </summary>
    public class EquipmentHotkey : MonoBehaviour
    {
        private void OnEnable() => NpcInteractionController.ActiveChanged += OnNpcInteractionChanged;
        private void OnDisable() => NpcInteractionController.ActiveChanged -= OnNpcInteractionChanged;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.pKey.wasPressedThisFrame) Toggle();
        }

        private void Toggle()
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<EquipmentPopup>())
            {
                ui.ClosePopup<EquipmentPopup>();
                return;
            }

            // 대화·허브 중엔 열지 않는다. 단, 강화창이 떠 있으면 허용한다(강화 중 장비창 확인·조작 위해).
            if (NpcInteractionController.Active != null && !ui.IsPopupOpen<EnhancePopup>()) return;
            ui.ShowPopup<EquipmentPopup>();
        }

        private void OnNpcInteractionChanged(NpcInteractionController active)
        {
            if (active != null) UIManager.Instance?.ClosePopup<EquipmentPopup>();
        }
    }
}
