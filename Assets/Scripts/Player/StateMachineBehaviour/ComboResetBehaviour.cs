using UnityEngine;

public class ComboResetBehaviour : StateMachineBehaviour
{
    // 로코모션(평상시) 상태에 진입한 순간 = 공격·스킬 동작이 끝난 시점.
    // 이때 이동 잠금을 풀어 다시 움직일 수 있게 한다.
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.GetComponent<Player>()?.UnlockMovement();
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.GetComponent<PlayerCombat>()?.ResetCombo();
    }

}
