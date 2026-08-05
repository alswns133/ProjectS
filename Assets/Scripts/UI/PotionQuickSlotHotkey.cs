using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// Q/E 키로 HUD 포션 퀵슬롯을 사용한다(Q=슬롯0, E=슬롯1). 등록이 없거나 재고·쿨다운이면 조용히 무시된다
    /// (판정은 <see cref="InventoryManager.UseQuickSlot"/>). 소비품 사용은 게임플레이지만, UI 열기처럼 별도
    /// 컴포넌트로 두어 PlayerInputHandler의 InputAction 배선을 늘리지 않는다. persistent 오브젝트에 붙인다.
    /// </summary>
    public class PotionQuickSlotHotkey : MonoBehaviour
    {
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            if (keyboard.qKey.wasPressedThisFrame) inv.UseQuickSlot(0);
            if (keyboard.eKey.wasPressedThisFrame) inv.UseQuickSlot(1);
        }
    }
}
