using UnityEngine;

namespace ProjectS.Combat
{
    /// <summary>
    /// 이 State가 활성인 동안 지정한 Bool 파라미터를 true로 켜고, 빠져나가면 false로 되돌린다.
    /// "지금 이 State가 재생 중인가"를 다른 전이의 조건으로 쓰고 싶을 때 붙인다 — Animator 조건은
    /// 파라미터만 볼 수 있고 "현재 State가 X인지"를 직접 물을 수 없어서, 대신 이 SMB가 그 사실을
    /// 파라미터로 노출해준다.
    ///
    /// 예(비대칭 피격 경직): 강한 피격(Hit_Large) 재생 중엔 약한 피격(Hit)이 끼어들지 못하게 하려면,
    /// Hit_Large에 이 SMB를 붙여 파라미터(예: isHitLarge)를 켜고, `Any State → Hit` 전이 조건에
    /// "isHitLarge == false"를 추가한다. `Any State → Hit_Large`는 그런 조건 없이 그대로 두면
    /// 강한 피격은 언제나 끼어들고, 약한 피격은 강한 피격이 재생 중일 때만 막히는 비대칭 구조가 된다.
    ///
    /// 같은 SMB를 다른 State에 붙이고 파라미터 이름만 바꾸면 다른 용도로도 재사용 가능하다
    /// (예: 다운 상태 판정, 각성기 재생 중 판정 등).
    /// </summary>
    public class StateBoolFlagBehaviour : StateMachineBehaviour
    {
        [Tooltip("이 State가 활성인 동안 true, 빠져나가면 false가 되는 Bool 파라미터 이름.")]
        [SerializeField] private string parameterName = "";

        private int hash;
        private bool hashed;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!hashed)
            {
                hash = Animator.StringToHash(parameterName);
                hashed = true;
            }

            animator.SetBool(hash, true);
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            animator.SetBool(hash, false);
        }
    }
}
