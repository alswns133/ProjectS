using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// K키로 스킬창(<see cref="SkillPopup"/>)을 열고 닫는다. 이미 열려 있으면 닫고, 아니면 연다.
    /// 스킬창은 인벤·강화창과 공존하는 팝업이라 "최상단인지"가 아니라 "떠 있는지"로 토글을 판정한다.
    /// UI 열기는 게임플레이 입력이 아니므로 PlayerInputHandler에 넣지 않고(입력 경계 규칙) 이 전용 컴포넌트가
    /// 담당한다. 씬을 넘어 유지되는 오브젝트(예: UIManager)에 붙인다.
    /// </summary>
    public class SkillHotkey : MonoBehaviour
    {
        // NPC 상호작용이 시작되면(대화·허브·보상) 열려 있던 스킬창을 닫는다(InventoryHotkey와 동일).
        private void OnEnable() => NpcInteractionController.ActiveChanged += OnNpcInteractionChanged;
        private void OnDisable() => NpcInteractionController.ActiveChanged -= OnNpcInteractionChanged;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.kKey.wasPressedThisFrame) Toggle();
        }

        private void OnNpcInteractionChanged(NpcInteractionController active)
        {
            if (active != null) UIManager.Instance?.ClosePopup<SkillPopup>();
        }

        // 스킬창이 떠 있으면 닫고, 아니면 새로 띄운다. NPC 상호작용 중에는 열지 않는다(이미 열려 있으면 닫기는 허용).
        private void Toggle()
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<SkillPopup>())
            {
                ui.ClosePopup<SkillPopup>();
                return;
            }

            if (NpcInteractionController.Active != null) return;

            ui.ShowPopup<SkillPopup>();
        }
    }
}
