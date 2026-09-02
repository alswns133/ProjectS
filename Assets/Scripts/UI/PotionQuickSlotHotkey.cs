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

            // 채팅 등 텍스트 입력 중에는 무시한다 — raw 키 읽기라 PlayerInputHandler의 입력 억제가 닿지 않아,
            // 채팅에 'q'/'e'를 치면 포션이 소모되기 때문(다른 raw 핫키들과 동일한 포커스 기준 게이트).
            if (UiTypingGuard.IsTypingInInputField()) return;

            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            if (keyboard.qKey.wasPressedThisFrame) inv.UseQuickSlot(0);
            if (keyboard.eKey.wasPressedThisFrame) inv.UseQuickSlot(1);
        }
    }
}
