using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// Animator 파라미터 브릿지. 다른 컴포넌트가 Animator를 직접 만지지 않고
    /// 이 클래스의 의미 있는 메서드(SetForward, PlayRoll 등)를 통해서만 제어한다.
    /// 파라미터 이름 문자열은 한곳(여기)에만 두어 오타·중복을 막는다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimation : MonoBehaviour
    {
        // 파라미터 이름을 매번 문자열로 넘기면 내부에서 해싱 비용 + 오타 위험.
        // 시작 시 1회 해싱해 int로 캐싱한다. static readonly: 인스턴스 공통·불변.
        private static readonly int Z = Animator.StringToHash("Z");
        private static readonly int Moving = Animator.StringToHash("isMoving");
        private static readonly int Running = Animator.StringToHash("isRunning");
        private static readonly int Grounded = Animator.StringToHash("isGrounded");
        private static readonly int VertVelocity = Animator.StringToHash("verticalVelocity");
        private static readonly int DoDie = Animator.StringToHash("doDie");
        private static readonly int DoDieLarge = Animator.StringToHash("doDieLarge");
        private static readonly int DoRoll = Animator.StringToHash("doRoll");
        private static readonly int DoHit = Animator.StringToHash("doHit");
        private static readonly int DoHitLarge = Animator.StringToHash("doHitLarge");

        // 스킬 트리거 해시 테이블. [0]은 더미 — 스킬 번호(1~)를 인덱스로 바로 쓰기 위함.
        // 따라서 유효 번호는 1..Length-1. 스킬을 늘리면 여기에 추가.
        private static readonly int[] Skill =
        {
            0,
            Animator.StringToHash("Skill1"),
            Animator.StringToHash("Skill2"),
            Animator.StringToHash("Skill3"),
            Animator.StringToHash("Skill4"),
        };

        private static readonly int Attack = Animator.StringToHash("Attack");
        private static readonly int StrongAttack = Animator.StringToHash("StrongAttack");
        private static readonly int RunAttack = Animator.StringToHash("RunAttack");
        private static readonly int JumpAttack = Animator.StringToHash("JumpAttack");

        [SerializeField] private RuntimeAnimatorController villageController;  // 원본
        [SerializeField] private AnimatorOverrideController dungeonController; // 오버라이드

        private const float Damp = 0.1f;   // SetFloat 감쇠 시간. 값이 즉시 안 튀고 부드럽게 따라감
        private Animator animator;

        // 로코모션 규약이 컨트롤러마다 다르다. Player.controller는 Z 블렌드 트리(정지/걷기/달리기 혼합),
        // Haru.controller는 isMoving/isRunning bool로 도는 3단 State 머신(Idle→Start→Loop→Stop)이다.
        // 없는 파라미터에 SetBool을 하면 Unity가 매 프레임 경고를 뱉으므로 Awake에서 한 번만 확인해 둔다.
        // 두 규약이 하나로 통일되면 이 플래그와 SetLocomotion의 가드는 지운다.
        private bool hasLocomotionBools;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            villageController = animator.runtimeAnimatorController;

            // AnimatorOverrideController는 원본의 파라미터 목록을 그대로 물려받으므로
            // 던전 컨트롤러로 교체돼도 이 판정은 유효하다 → 1회 검사로 충분.
            hasLocomotionBools = HasParameter(Moving) && HasParameter(Running);
        }

        /// <summary>전진량(Z)을 부드럽게 갱신. 자유 시점은 진행 방향 회전이라 Z만 쓴다.</summary>
        public void SetForward(float z) => animator.SetFloat(Z, z, Damp, Time.deltaTime);

        /// <summary>
        /// 이동/달리기 여부를 bool 파라미터로 전달한다(3단 로코모션 컨트롤러 전용 규약).
        /// Start/Loop/Stop 클립을 쓰는 컨트롤러는 Z 하나로 상태를 못 고르고, 이 두 값의
        /// 조건 전이로 Idle↔걷기↔달리기를 오간다. SetForward와 함께 매 프레임 호출한다.
        /// 파라미터가 없는 컨트롤러(Player.controller)에서는 조용히 무시된다.
        /// </summary>
        public void SetLocomotion(bool isMoving, bool isRunning)
        {
            if (!hasLocomotionBools) return;

            animator.SetBool(Moving, isMoving);
            animator.SetBool(Running, isRunning);
        }

        private bool HasParameter(int nameHash)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.nameHash == nameHash) return true;
            }

            return false;
        }

        public void SetGrounded(bool v) => animator.SetBool(Grounded, v);

        /// <summary>
        /// 수직 속도를 애니메이터에 전달한다(Player가 매 프레임 호출).
        /// 점프 모션은 트리거가 아니라 isGrounded + verticalVelocity 조건 전이로 재생된다
        /// → 물리가 점프한 프레임에 모션이 반드시 따라오므로 연속 점프에서 씹히지 않는다.
        /// SetForward와 달리 감쇠를 걸지 않는 이유: 부호(상승/하강)가 전이 조건이라 즉시 반영돼야 한다.
        /// </summary>
        public void SetVerticalVelocity(float v) => animator.SetFloat(VertVelocity, v);

        /// <summary>
        /// 구르기 트리거. 구르기 상태 진입 시 1회 호출된다.
        /// 방향 파라미터가 없는 이유: 캐릭터가 구를 방향을 먼저 바라보고(FaceInstantly)
        /// 앞구르기 클립 하나만 재생하는 설계라, 애니메이터는 방향을 몰라도 된다.
        /// </summary>
        public void PlayRoll() => animator.SetTrigger(DoRoll);

        /// <summary>
        /// 구르기 트리거 해제. 구르기 상태 Exit에서 호출된다.
        /// 연속 회피 중 애니메이터가 트리거를 소비하지 못한 채(블렌드 중 등) 상태가 끝나면
        /// 래치된 트리거가 남아 나중에 유령 구르기가 재생되는 것을 막는다.
        /// </summary>
        public void ResetRollTrigger() => animator.ResetTrigger(DoRoll);

        /// <summary>n번 스킬 트리거. 범위를 벗어난 n은 조용히 무시(예외 대신 안전).</summary>
        public void PlaySkill(int n)
        {
            if (n >= 1 && n < Skill.Length)
                animator.SetTrigger(Skill[n]);
        }

        public void PlayAttackTrigger()
        {
            animator.SetTrigger(Attack);
        }

        public void ResetAttackTrigger() => animator.ResetTrigger(Attack);

        /// <summary>
        /// 래치된 강공격·달리기 공격 트리거를 지운다. 일반 공격(Attack)만 ClearAttackBuffer가 지우므로,
        /// 피격·구르기로 캔슬될 때 이 둘이 남아 나중에 유령 발동하는 것을 CancelAction에서 함께 막는다.
        /// </summary>
        public void ResetStrongAttackTrigger() => animator.ResetTrigger(StrongAttack);

        public void ResetRunAttackTrigger() => animator.ResetTrigger(RunAttack);

        public void ResetJumpAttackTrigger() => animator.ResetTrigger(JumpAttack);

        /// <summary>우클릭 강공격 트리거. PlayerCombat.UseStrongAttack이 발동에 성공했을 때만 호출한다.</summary>
        public void PlayStrongAttack() => animator.SetTrigger(StrongAttack);

        /// <summary>달리기 공격(단타) 트리거. 달리는 중 클릭 시 Player가 라우팅한다.</summary>
        public void PlayRunAttack() => animator.SetTrigger(RunAttack);

        /// <summary>점프 공격(단타) 트리거. 공중 클릭 시 Player가 라우팅한다.</summary>
        public void PlayJumpAttack() => animator.SetTrigger(JumpAttack);

        /// <summary>
        /// 피격 트리거. HitState 진입 시 1회 호출한다.
        /// 강한 피격(isLarge)은 별도 모션(doHitLarge)으로 분기한다.
        /// </summary>
        public void PlayHit(bool isLarge) => animator.SetTrigger(isLarge ? DoHitLarge : DoHit);

        /// <summary>
        /// 피격 트리거 해제. HitState Exit에서 호출한다.
        /// 경직 중 사망/구르기로 끊겼을 때 래치된 트리거가 남아
        /// 나중에 유령 피격 모션이 재생되는 것을 막는다(ResetRollTrigger와 같은 방침).
        /// </summary>
        public void ResetHitTriggers()
        {
            animator.ResetTrigger(DoHit);
            animator.ResetTrigger(DoHitLarge);
        }

        /// <summary>사망 트리거. 강한 공격에 죽었으면(byLargeHit) 별도 모션(doDieLarge)으로 분기한다.</summary>
        public void PlayDie(bool byLargeHit) => animator.SetTrigger(byLargeHit ? DoDieLarge : DoDie);
    }
}
