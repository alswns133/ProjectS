using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 대기(Idle) State에 붙여, 일정 시간 가만히 있으면 특수 대기 모션을 랜덤으로 발동시키는 SMB.
    /// 명조·원신처럼 "서 있으면 가끔 딴짓하는" 연출을 만든다.
    ///
    /// SMB로 만든 이유: Unity 애니메이터에는 <b>랜덤 전이가 없다</b>. 난수를 넣어줄 주체가 필요한데,
    /// Idle State에 직접 붙는 SMB가 "지금 대기 중인가"를 별도 판정 없이 알 수 있어 가장 정확하다
    /// (JumpAttackLoopBehaviour와 같은 패턴).
    ///
    /// 필요한 Animator 파라미터 2개:
    ///   doIdleSpecial (Trigger) — 특수 대기로 전이시키는 방아쇠
    ///   idleVariant   (Int)     — 어떤 특수 대기인지. 0 .. (variationCount-1)
    ///
    /// 전이 구성: Idle → Idle_Special_N  (조건: doIdleSpecial + idleVariant == N)
    ///            Idle_Special_N → Idle  (Exit Time)
    /// 이동 중단은 Idle 계열을 서브 스테이트 머신으로 묶고 Exit 노드로 복귀시키면 전이 수가 줄어든다.
    /// 점프는 Any State → Jump_Start(doJump)라 자동으로 끊긴다.
    /// </summary>
    public class IdleVariationBehaviour : StateMachineBehaviour
    {
        [Header("발동 간격 (초)")]
        [Tooltip("대기 진입 후 이 범위에서 무작위로 뽑은 시간이 지나면 특수 대기가 나온다.")]
        [SerializeField] private float minDelay = 8f;
        [SerializeField] private float maxDelay = 16f;

        [Header("변형")]
        [Tooltip("특수 대기 모션 개수. idleVariant에 0..(개수-1)이 들어간다.")]
        [SerializeField] private int variationCount = 2;

        [Tooltip("직전에 나온 모션이 연속으로 다시 나오지 않게 한다. 종류가 적을수록 체감 차이가 크다.")]
        [SerializeField] private bool avoidImmediateRepeat = true;

        [Header("Animator 파라미터 이름")]
        [SerializeField] private string triggerParam = "doIdleSpecial";
        [SerializeField] private string variantParam = "idleVariant";

        private int triggerHash;
        private int variantHash;
        private bool initialized;

        // 파라미터가 없는 컨트롤러에 잘못 붙었을 때 매 프레임 경고가 쏟아지는 것을 막는다.
        // (이 프로젝트에서 반복해서 겪은 'Parameter does not exist' 도배 방지)
        private bool paramsAvailable;

        private float timer;
        private float nextDelay;

        // 연속 중복 회피용. State 재진입과 무관하게 유지돼야 하므로 OnStateEnter에서 초기화하지 않는다.
        private int lastVariant = -1;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!initialized) Initialize(animator);

            timer = 0f;
            nextDelay = Random.Range(minDelay, maxDelay);
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!paramsAvailable) return;

            // 대기로 들어오는/나가는 블렌드 구간에는 시간을 세지 않는다.
            // 세면 걷다 멈추는 순간마다 타이머가 조금씩 쌓여 의도보다 일찍 발동한다.
            if (animator.IsInTransition(layerIndex)) return;

            timer += Time.deltaTime;
            if (timer < nextDelay) return;

            Fire(animator);
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!paramsAvailable) return;

            // 발동 직후 플레이어가 바로 움직여 전이가 특수 대기 대신 걷기로 갔다면
            // 트리거가 래치된 채 남는다. 그대로 두면 나중에 유령 특수 대기가 재생되므로 지운다
            // (PlayerAnimation의 ResetRollTrigger·ResetHitTriggers와 같은 방침).
            animator.ResetTrigger(triggerHash);
        }

        private void Initialize(Animator animator)
        {
            initialized = true;

            triggerHash = Animator.StringToHash(triggerParam);
            variantHash = Animator.StringToHash(variantParam);
            paramsAvailable = HasParameter(animator, triggerHash) && HasParameter(animator, variantHash);

            if (!paramsAvailable)
            {
                Debug.LogWarning(
                    $"[IdleVariationBehaviour] '{triggerParam}' 또는 '{variantParam}' 파라미터가 없어 특수 대기를 비활성화한다. " +
                    "컨트롤러에 두 파라미터를 추가하거나, 이 SMB를 떼어내라.", animator);
            }
        }

        /// <summary>특수 대기를 실제로 발동시킨다. 발동 후 다음 대기 시간을 새로 뽑는다.</summary>
        private void Fire(Animator animator)
        {
            int variant = PickVariant();

            animator.SetInteger(variantHash, variant);
            animator.SetTrigger(triggerHash);

            lastVariant = variant;

            // 전이가 곧바로 걸리면 OnStateEnter가 다시 값을 잡지만,
            // 어떤 이유로 전이가 걸리지 않았을 때 매 프레임 재발동하지 않도록 여기서도 초기화한다.
            timer = 0f;
            nextDelay = Random.Range(minDelay, maxDelay);
        }

        /// <summary>다음에 재생할 변형 번호를 고른다. avoidImmediateRepeat이면 직전 번호를 제외하고 뽑는다.</summary>
        private int PickVariant()
        {
            int count = Mathf.Max(1, variationCount);
            if (count == 1) return 0;

            if (!avoidImmediateRepeat || lastVariant < 0 || lastVariant >= count)
                return Random.Range(0, count);

            // 직전 번호를 뺀 나머지에서 뽑은 뒤 자리를 되돌린다 — 재추첨 루프 없이 균등하게 고른다.
            int picked = Random.Range(0, count - 1);
            if (picked >= lastVariant) picked++;

            return picked;
        }

        private static bool HasParameter(Animator animator, int nameHash)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.nameHash == nameHash) return true;
            }

            return false;
        }
    }
}
