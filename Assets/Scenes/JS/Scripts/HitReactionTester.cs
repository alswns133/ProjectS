using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Players;

namespace ProjectS.Debugging
{
    /// <summary>
    /// ★ 테스트 전용. 아직 적이 없어 실제 피격으로 애니메이터 State를 확인할 수 없는 동안,
    /// 지정 키로 <see cref="PlayerAnimation.PlayHit"/>을 직접 호출해 피격 모션만 눈으로 본다.
    ///
    /// 데미지·경직 시간·무적 판정은 전혀 거치지 않는다 — 순수하게 "이 트리거를 세우면
    /// 원하는 State로 잘 넘어가는가"만 확인하는 용도다. 실제 피격 파이프라인
    /// (PlayerStats.TakeDamage → Damaged 이벤트 → 경직 반응, PlayerHitState를 참고해
    /// FreeCombatController에 미러링 예정)이 만들어지면 이 스크립트는 치운다.
    ///
    /// PlayerAnimation.PlayHit(bool isLarge)은 민준님 코드의 public 메서드를 그대로 호출하므로
    /// doHit/doHitLarge 트리거 이름을 여기서 하드코딩하지 않는다(민준님 코드 수정 없음).
    /// </summary>
    public class HitReactionTester : MonoBehaviour
    {
        [SerializeField] private PlayerAnimation anim;

        [SerializeField]
        private InputAction hitAction =
            new InputAction("TestHit", InputActionType.Button, "<Keyboard>/h");

        [SerializeField]
        private InputAction hitLargeAction =
            new InputAction("TestHitLarge", InputActionType.Button, "<Keyboard>/l");

        private void Awake()
        {
            if (anim == null) anim = GetComponent<PlayerAnimation>();
        }

        private void OnEnable()
        {
            hitAction.performed += OnHit;
            hitAction.Enable();

            hitLargeAction.performed += OnHitLarge;
            hitLargeAction.Enable();
        }

        private void OnDisable()
        {
            hitAction.performed -= OnHit;
            hitAction.Disable();

            hitLargeAction.performed -= OnHitLarge;
            hitLargeAction.Disable();
        }

        private void OnHit(InputAction.CallbackContext ctx) => anim.PlayHit(false);

        private void OnHitLarge(InputAction.CallbackContext ctx) => anim.PlayHit(true);
    }
}
