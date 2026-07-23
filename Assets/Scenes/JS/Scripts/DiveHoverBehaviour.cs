using UnityEngine;

namespace ProjectS.Combat
{
    /// <summary>
    /// 낙하 공격의 Loop State에 붙이는 StateMachineBehaviour.
    /// 체공은 공격 시작(Start)부터 FreeCombatController가 유지하고, 이 SMB는 Loop에 진입한 순간
    /// 체공을 끝내고 하강(다이브)을 시작하도록 알린다. → "Start 체공, Loop 시작과 동시에 하강".
    /// (구르기·콤보 SMB와 같은 방식. FreeMoveController/FreeCombatController는 우리 스크립트라 무수정 원칙과 무관)
    /// </summary>
    public class DiveHoverBehaviour : StateMachineBehaviour
    {
        // SMB는 컨트롤러 에셋에 붙는 객체라, Animator마다 생성되는 인스턴스에서 첫 진입 시 1회만 캐싱한다.
        private FreeCombatController combatController;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (combatController == null)
                combatController = animator.GetComponent<FreeCombatController>();

            combatController?.BeginDive();
        }
    }
}
