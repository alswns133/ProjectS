using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// Animator 파라미터 브릿지. 몬스터 Animator는 이 클래스를 통해서만 제어한다.
    /// 파라미터 이름은 애니메이터 담당자와 공유하는 계약이므로 여기에만 정의한다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class EnemyAnimation : MonoBehaviour
    {
        // 파라미터 이름을 매번 문자열로 넘기면 내부 해싱 비용 + 오타 위험이 있다.
        // 시작 시 1회 해싱해 int로 캐싱한다. static readonly: 인스턴스 공통·불변.
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
        private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
        private static readonly int DoDetect = Animator.StringToHash("doDetect");
        private static readonly int DoAttack = Animator.StringToHash("doAttack");
        private static readonly int HitState = Animator.StringToHash("Hit");
        private static readonly int HitAirState = Animator.StringToHash("Hit_Air");
        private static readonly int DieState = Animator.StringToHash("Die");
        private static readonly int DieAirState = Animator.StringToHash("Die_Air");

        // 이동 블렌드 값이 튀지 않게 부드럽게 따라가는 감쇠 시간.
        private const float Damp = 0.1f;

        private Animator animator;

        private void Awake()
        {
            animator = GetComponent<Animator>();
        }

        /// <summary>현재 이동 속력. 이동/걷기 애니메이션 블렌드 값으로 쓴다.</summary>
        public void SetSpeed(float speed) => animator.SetFloat(Speed, speed, Damp, Time.deltaTime);

        /// <summary>감쇠 없이 이동 블렌드 값을 즉시 바꾼다. 공격 진입처럼 모션을 바로 끊을 때 쓴다.</summary>
        public void SetSpeedImmediate(float speed) => animator.SetFloat(Speed, speed);

        /// <summary>
        /// 대기 애니메이션 2종 중 어떤 것을 재생할지 전달한다.
        /// IdleState가 진입 시 0/1 중 하나를 고르고, Animator는 IdleVariant 값으로 대기 클립을 분기한다.
        /// </summary>
        public void SetIdleVariant(int index) => animator.SetFloat(IdleVariant, index);

        /// <summary>플레이어 발견 연출 트리거. DetectState 진입 시 1회 호출한다.</summary>
        public void PlayDetect() => animator.SetTrigger(DoDetect);

        /// <summary>
        /// 공격 트리거. AttackIndex로 공격 1/2/3을 고른 뒤 doAttack을 켠다.
        /// Animator 전이는 AttackIndex 값에 따라 각 공격 클립으로 분기시키면 된다.
        /// 인덱스 유효성은 EnemyCombat의 공격 슬롯과 Animator Controller 설정이 함께 맞아야 하는 계약이다.
        /// </summary>
        public void PlayAttack(int attackIndex)
        {
            animator.SetInteger(AttackIndex, attackIndex);
            animator.SetTrigger(DoAttack);
        }

        /// <summary>
        /// 지금 재생 중인 상태가 지정 태그를 가진 클립인지 여부. 해당 상태들에 애니메이터에서 태그를
        /// 달아 두어야 한다(공격은 "Attack", 발견은 "Detect"). 전환(IsInTransition) 중에는 아직 그 상태에
        /// 진입하기 전이라 false를 돌려준다. 상태가 트리거를 켠 뒤 "클립에 실제로 들어갔는지" 확인하는 데 쓴다
        /// (진입 전에 종료 판정을 하면 이전 로코모션의 normalizedTime을 보고 즉시 빠져나간다).
        /// </summary>
        public bool IsPlaying(string stateTag)
            => !animator.IsInTransition(0)
            && animator.GetCurrentAnimatorStateInfo(0).IsTag(stateTag);

        /// <summary>
        /// 현재 상태의 클립이 끝까지 재생됐는지 여부(normalizedTime 기준). 루프가 아닌 공격 클립의
        /// 종료 판정에 쓴다. 클립 길이를 인스펙터에 손으로 적지 않고 애니메이터에서 직접 읽는 방식이다.
        /// </summary>
        /// <param name="threshold">
        /// 종료로 볼 진행도. 1.0 정확히 노리면 프레임 타이밍상 그 순간을 놓쳐 한 바퀴 더 돌 수 있어
        /// 살짝 못 미친 값을 기본으로 쓴다. State Speed는 normalizedTime에 이미 반영돼 있어 따로 볼 필요 없다.
        /// </param>
        public bool IsCurrentStateFinished(float threshold = 0.98f)
            => !animator.IsInTransition(0)
            && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= threshold;

        /// <summary>
        /// 지상 피격 트리거
        /// </summary>
        public void PlayHit() => animator.Play(HitState, 0, 0f);

        /// <summary>
        /// 공중 경직 트리거
        /// </summary>
        public void PlayHitAir() => animator.Play(HitAirState, 0, 0f);

        /// <summary>
        /// 지상 사망 트리거
        /// </summary>
        public void PlayDie() => animator.Play(DieState, 0, 0f);

        /// <summary>
        /// 공중 사망 트리거
        /// </summary>
        public void PlayDieAir() => animator.Play(DieAirState, 0, 0f);

        /// <summary>
        /// 공중 사망: 지금 재생 중인 공중 피격 클립의 진행도를 이어받아 Die_Air 모션을 재생
        /// 같은 진행도에서 이어받아 낙하 궤적을 자연스럽게 이어 착지하며 죽게 한다.
        /// </summary>
        public void PlayDieAirContinuing()
        {
            float t = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            t -= Mathf.Floor(t);
            animator.Play(DieAirState, 0, t);
        }

        /// <summary>
        /// 현재 Hit_Air 클립 진행도(0~1). Hit_Air 재생 중이 아니면 -1
        /// </summary>
        public float HitAirNormalizedTime
        {
            get
            {
                AnimatorStateInfo s = animator.GetCurrentAnimatorStateInfo(0);
                if (!s.IsName("Hit_Air")) return -1f;
                return s.normalizedTime;
            }
        }
    }
}
