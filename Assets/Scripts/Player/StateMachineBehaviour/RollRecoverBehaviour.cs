using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 구르기 State에 붙여, 능동 구간이 끝난 뒤부터 로코모션 복귀를 허용하는 플래그(rollRecovered)를 켠다.
    /// 이 플래그를 조건으로 쓰는 Roll→Walk/Run 전이는 Exit Time 없이 isMoving을 '연속' 판정하므로,
    /// 방향키를 늦게 다시 눌러도 Idle을 거치지 않고 곧바로 Walk/Run으로 간다.
    /// </summary>
    public class RollRecoverBehaviour : StateMachineBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float recoverStart = 0.6f; // 이 진행도부터 복귀 허용
        [SerializeField] private string recoveredParam = "rollRecovered";

        private int hash;
        private bool hashed;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!hashed) { hash = Animator.StringToHash(recoveredParam); hashed = true; }
            animator.SetBool(hash, false);   // 새 구르기마다 복귀 금지로 초기화
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (stateInfo.normalizedTime >= recoverStart)
                animator.SetBool(hash, true);
        }
    }
}
