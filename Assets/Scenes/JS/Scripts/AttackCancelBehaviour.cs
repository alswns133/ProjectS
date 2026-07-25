using UnityEngine;
using ProjectS.Players;

namespace ProjectS.Combat
{
    /// <summary>
    /// 공격 State에 붙여, 그 공격의 진행도가 <see cref="cancelWindowStart"/>를 넘으면
    /// '다음 공격으로 캔슬'할 수 있게 입력 창을 연다(PlayerCombat.EndSkillCast 호출).
    ///
    /// 값을 <b>State마다 따로</b> 두는 것이 핵심이다 — 같은 SMB를 여러 공격 State에 붙이되 각자 값을
    /// 달리 주면, 공격별 연계 타이밍(후딜 길이)을 애니메이터에서 State를 보며 직접 튜닝할 수 있다.
    /// (대시공격은 빨리 열고, 강공격 올려치기는 준비 동작 뒤에 늦게 여는 식.)
    ///
    /// 코드 전역값 하나로 열던 방식의 두 약점을 함께 해결한다:
    ///  - 공격마다 다른 타이밍을 줄 수 없던 문제(이제 State별 값).
    ///  - 새 공격을 막 발동한 직후 '직전 공격'의 진행도로 창이 새던 문제(SMB는 자기 State 진행도만 본다).
    ///
    /// ★ 이동 잠금(ActionLocked)은 Tag 기반이라 그대로 유지된다. 여기서 여는 것은 '공격 입력'뿐이므로
    ///   후딜의 무게는 남고 다음 공격만 미리 받는다.
    /// ★ 히트 프레임보다 '뒤'에 열려야 한다. 창이 열리면 IsCastingSkill이 풀려, PlayerCombat이
    ///   그 이후 히트 프레임을 무효화한다(CanApplyHitFrame). 히트 이벤트를 넣을 때 순서에 주의.
    ///
    /// 새 공격(평타 각 타수·스킬 등)에도 이 SMB를 붙이고 값만 정하면 코드 수정 없이 연계가 된다.
    /// </summary>
    public class AttackCancelBehaviour : StateMachineBehaviour
    {
        [Tooltip("이 공격의 진행도(0~1)가 이 값을 넘으면 다음 공격으로 캔슬 가능해진다. 작을수록 빨리 이어진다.")]
        [Range(0f, 1f)] [SerializeField] private float cancelWindowStart = 0.5f;

        // SMB는 컨트롤러 에셋에 붙는 객체라, Animator마다 생성되는 인스턴스에서 첫 호출 시 1회만 캐싱한다.
        private PlayerCombat combat;

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (combat == null)
                combat = animator.GetComponent<PlayerCombat>();

            // 열 것이 없거나(시전 중 아님) 이미 열렸으면 끝. 전이 중에는 진행도 판정이 의미 없다.
            if (combat == null || !combat.IsCastingSkill) return;
            if (animator.IsInTransition(layerIndex)) return;

            if (stateInfo.normalizedTime < cancelWindowStart) return;

            combat.EndSkillCast();
        }
    }
}
