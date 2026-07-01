using UnityEngine;

public class ComboResetBehaviour : StateMachineBehaviour
{
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.GetComponent<PlayerCombat>()?.ResetCombo();
    }

}
