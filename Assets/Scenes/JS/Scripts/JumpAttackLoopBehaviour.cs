using UnityEngine;
using ProjectS.Movement;

namespace ProjectS.Combat
{
    /// <summary>
    /// 공중 공격(JumpAttack)의 <b>Loop State</b>에 붙이는 StateMachineBehaviour.
    /// 체공은 공격 시작(Start 구간)부터 FreeMoveController가 유지하고,
    /// 이 SMB가 Loop에 진입한 순간 체공을 끝내고 하강을 시작시킨다.
    /// → "Start 모션 중 체공, Loop 모션 중 하강".
    ///
    /// OnStateEnter를 쓰는 이유: Loop로 넘어가는 시점이 곧 하강 시작 시점이라
    /// 코드 타이머로 재는 것보다 애니메이션과 정확히 동기화된다.
    /// </summary>
    public class JumpAttackLoopBehaviour : StateMachineBehaviour
    {
        // SMB는 컨트롤러 에셋에 붙는 객체라, Animator마다 생성되는 인스턴스에서 첫 호출 시 1회만 캐싱한다.
        private FreeMoveController move;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (move == null)
                move = animator.GetComponent<FreeMoveController>();

            move?.BeginDive();
        }
    }
}
