using UnityEngine;
using ProjectS.Debugging;

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
        private static readonly int RollHeld = Animator.StringToHash("rollHeld");
        private static readonly int DoHit = Animator.StringToHash("doHit");
        private static readonly int DoHitLarge = Animator.StringToHash("doHitLarge");

        // 보스 잡기(구속) 모션 트리거. 던전 컨트롤러에만 있는 파라미터라, 없으면(마을/기존 컨트롤러)
        // 매 프레임 경고가 나지 않게 hasGrabbed로 게이트한다(doJump/isLanding과 같은 방침).
        private static readonly int DoGrabbed = Animator.StringToHash("doGrabbed");

        // 자유 이동(FreeMove 이관) 캐릭터 전용 파라미터. 3단 로코모션 컨트롤러(Haru 계열)만 갖고 있고,
        // Z 블렌드 트리 컨트롤러(Player.controller)에는 없다. 없는 파라미터에 SetTrigger/SetBool을 하면
        // Unity가 매 프레임 경고를 뱉으므로, Awake에서 존재 여부를 검사해 없으면 조용히 무시한다.
        private static readonly int DoJump = Animator.StringToHash("doJump");
        private static readonly int DoJumpDash = Animator.StringToHash("doJumpDash");
        private static readonly int IsLanding = Animator.StringToHash("isLanding");

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

        // 각 파라미터의 존재 여부. 컨트롤러마다 파라미터 집합이 달라, 없는 것에 Set을 하면
        // 매 프레임 경고가 나므로 Awake에서 1회 검사해 게이트로 쓴다(hasLocomotionBools와 같은 방침).
        private bool hasForward;
        private bool hasJump;
        private bool hasJumpDash;
        private bool hasLanding;
        private bool hasDie;
        private bool hasDieLarge;
        private bool hasRollHeld;
        private bool hasGrabbed;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            villageController = animator.runtimeAnimatorController;

            RefreshParameterCache();
        }

        // 컨트롤러가 노출하는 파라미터 존재 여부를 다시 계산한다.
        // 마을/던전은 클립만 다른 한 base의 오버라이드가 아니라 base 컨트롤러 자체가 다르다
        // (Player_Village에는 doDie/공격계 파라미터가 없고 Player_Dungeon에는 있다).
        // 따라서 컨트롤러를 교체할 때마다 반드시 다시 계산해야 한다 — Awake 1회 캐시는 교체 후 어긋나
        // 마을 컨트롤러로 시작하면 hasDie가 false로 굳어 던전에서 사망 모션이 안 나온다(직접 원인이었음).
        private void RefreshParameterCache()
        {
            hasLocomotionBools = HasParameter(Moving) && HasParameter(Running);

            hasForward = HasParameter(Z);
            hasJump = HasParameter(DoJump);
            hasJumpDash = HasParameter(DoJumpDash);
            hasLanding = HasParameter(IsLanding);
            hasDie = HasParameter(DoDie);
            hasDieLarge = HasParameter(DoDieLarge);
            hasRollHeld = HasParameter(RollHeld);
            hasGrabbed = HasParameter(DoGrabbed);
        }

        /// <summary>
        /// 마을 컨트롤러(Awake에서 캡처한 원본 base)로 전환한다. 씬 진입 시 <see cref="Player.EnterVillage"/>가 호출한다.
        /// 이미 그 컨트롤러면 재대입하지 않는다 — runtimeAnimatorController 대입은 애니메이터를 재초기화해
        /// 현재 State·파라미터를 기본값(Idle)으로 되돌리므로, 같은 것을 다시 넣으면 매 호출마다 Idle로 튄다.
        /// </summary>
        public void UseVillageController()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (villageController == null || animator.runtimeAnimatorController == villageController) return;

            animator.runtimeAnimatorController = villageController;

            // 마을 base는 던전 base와 파라미터 집합이 다르므로 교체 후 캐시를 다시 계산한다.
            RefreshParameterCache();
        }

        /// <summary>
        /// 던전 컨트롤러(AnimatorOverrideController)로 전환한다. 씬 진입 시 <see cref="Player.EnterDungeon"/>가 호출한다.
        /// 마을/던전은 base 컨트롤러 자체가 달라(파라미터 집합이 다름) 전환 후 파라미터 캐시를 다시 계산한다.
        /// 비어 있으면 경고만 남기고 전환하지 않는다(null을 대입하면 애니메이터가 통째로 꺼져 아무 모션도 안 나온다).
        /// </summary>
        public void UseDungeonController()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (dungeonController == null)
            {
                Debug.LogWarning("[PlayerAnimation] dungeonController가 비어 있어 던전 컨트롤러로 전환할 수 없습니다.", this);
                return;
            }
            if (animator.runtimeAnimatorController == dungeonController) return;

            animator.runtimeAnimatorController = dungeonController;

            // 던전 base에만 있는 파라미터(doDie/doDieLarge, 공격계 등)를 캐시에 반영한다.
            RefreshParameterCache();
        }

        /// <summary>
        /// 이동 잠금·연계 판정에 태그 기반 모델(FreeMove/FreeCombat 이관)을 쓸지 여부.
        /// 3단 로코모션 컨트롤러(자유 이동 캐릭터)만 공격 State에 Tag를 달고 doJump/isLanding 등을 쓴다.
        /// Z 블렌드 트리 컨트롤러(기존 캐릭터)는 ComboResetBehaviour 기반의 기존 잠금 모델을 그대로 쓴다.
        /// hasLocomotionBools(isMoving/isRunning 존재 여부)를 대리 신호로 삼아 캐릭터별로 잠금 모델을 가른다.
        ///
        /// ★ 조회 시점에 직접 계산한다(Awake 캐시 값을 안 쓴다). Player가 이 값을 Awake에서 읽는데,
        ///   같은 GameObject의 컴포넌트 간 Awake 실행 순서는 보장되지 않아 PlayerAnimation.Awake보다
        ///   Player.Awake가 먼저 돌면 캐시가 아직 false라 태그 기반 판정이 통째로 꺼졌다(강공격 낙하가
        ///   안전장치 타이머까지 vv=0으로 굳던 직접 원인). animator만 확보해 바로 계산하면 순서와 무관하다.
        /// </summary>
        public bool UsesMotionTags
        {
            get
            {
                if (animator == null) animator = GetComponent<Animator>();
                return HasParameter(Moving) && HasParameter(Running);
            }
        }

        /// <summary>현재 레이어0가 전이 중인지. Player가 공격/피격 태그 전이를 판정할 때 읽는다.</summary>
        public bool IsInTransition => animator.IsInTransition(0);

        /// <summary>현재 State의 Tag 해시. Player의 이동 잠금/연계 판정용(Animator 접근은 이 클래스로 일원화).</summary>
        public int CurrentStateTagHash => animator.GetCurrentAnimatorStateInfo(0).tagHash;

        /// <summary>전이 중일 때 진입할 다음 State의 Tag 해시.</summary>
        public int NextStateTagHash => animator.GetNextAnimatorStateInfo(0).tagHash;

        /// <summary>지정 Bool 파라미터 값. 지속 조준(continuousAim) 등 State가 노출한 플래그를 읽을 때 쓴다.</summary>
        public bool GetBool(int hash) => animator.GetBool(hash);

        /// <summary>전진량(Z)을 부드럽게 갱신. 자유 시점은 진행 방향 회전이라 Z만 쓴다.</summary>
        public void SetForward(float z)
        {
            if (!hasForward) return;   // Z 파라미터가 없는 3단 로코모션 컨트롤러에서는 무시(경고 방지)
            animator.SetFloat(Z, z, Damp, Time.deltaTime);
        }

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
        /// 착지 예고(isLanding)를 전달한다(자유 이동 컨트롤러 전용). 하강 중 지면이 가까우면 켜서
        /// 착지 모션을 미리 시작하게 한다. Player가 매 프레임 레이캐스트 결과로 호출한다.
        /// 파라미터가 없는 컨트롤러에서는 조용히 무시된다.
        /// </summary>
        public void SetLanding(bool v)
        {
            if (!hasLanding) return;
            animator.SetBool(IsLanding, v);
        }

        /// <summary>
        /// 점프 트리거(자유 이동 컨트롤러 전용). 조건 전이(isGrounded+verticalVelocity) 방식과 달리
        /// 트리거는 래치되어 좁은 상승 창을 놓치지 않으므로, 공격 종료 블렌드 중 점프도 확실히 재생된다.
        /// Player.TryJump가 Movement.Jump() 성공 시 호출한다. 파라미터가 없으면 무시(조건 전이 방식과 병행 가능).
        /// </summary>
        public void PlayJump()
        {
            if (hasJump) animator.SetTrigger(DoJump);
        }

        public void ResetJumpTrigger()
        {
            if (hasJump) animator.ResetTrigger(DoJump);
        }

        /// <summary>공중 대시(점프 대시) 트리거. PlayerJumpDashState 진입 시 호출한다.</summary>
        public void PlayJumpDash()
        {
            if (hasJumpDash) animator.SetTrigger(DoJumpDash);
        }

        public void ResetJumpDashTrigger()
        {
            if (hasJumpDash) animator.ResetTrigger(DoJumpDash);
        }

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
            DevLog.Log(Skill[n].ToString());
            
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

        /// <summary>
        /// 보스에게 잡혔을 때 구속(발버둥) 모션 트리거. PlayerGrabbedState 진입 시 1회 호출한다.
        /// 던전 컨트롤러에만 있는 파라미터라, 없으면(마을/기존 컨트롤러) 조용히 무시한다(경고 방지).
        /// </summary>
        public void PlayGrabbed()
        {
            if (hasGrabbed) animator.SetTrigger(DoGrabbed);
        }

        /// <summary>
        /// 구속 트리거 해제. 잡기 해제(PlayerGrabbedState.Exit)에서 호출해, 래치된 트리거가 남아
        /// 나중에 유령 구속 모션이 재생되는 것을 막는다(ResetHitTriggers와 같은 방침).
        /// </summary>
        public void ResetGrabbedTrigger()
        {
            if (hasGrabbed) animator.ResetTrigger(DoGrabbed);
        }

        /// <summary>
        /// 사망 트리거. 강한 공격에 죽었으면(byLargeHit) 별도 모션(doDieLarge)으로 분기한다.
        /// 컨트롤러마다 사망 파라미터 유무가 다르다(Free/JS 컨트롤러엔 doDie/doDieLarge가 없어 가드 없이
        /// SetTrigger하면 "Parameter does not exist" 경고가 난다). 있는 트리거만 쏘고, 요청한 종류가 없으면
        /// 다른 사망 트리거로 폴백하며, 둘 다 없으면 경고 없이 조용히 무시한다(사망 로직 자체는 정상 진행).
        /// </summary>
        public void PlayDie(bool byLargeHit)
        {
            if (byLargeHit ? hasDieLarge : hasDie)
            {
                animator.SetTrigger(byLargeHit ? DoDieLarge : DoDie);
                return;
            }

            if (hasDie) animator.SetTrigger(DoDie);
            else if (hasDieLarge) animator.SetTrigger(DoDieLarge);
        }

        /// <summary>
        /// 사망 모션에서 빠져나와 기본(로코모션) 상태로 되돌린다. 부활(<see cref="Player.Revive"/>) 시 호출한다.
        /// 사망 State는 스스로 빠져나가는 전이가 없어, 죽은 자리에서 부활할 때 명시적으로 리셋해야 한다.
        /// 래치된 사망 트리거를 지운 뒤 애니메이터를 재바인드해 기본 State(Idle)로 되돌리고, 그 자리에서
        /// 한 프레임 평가(Update(0))해 바인드 포즈(T포즈)가 한 프레임 번쩍이는 것을 막는다.
        /// (마을 복귀 경로는 컨트롤러 교체가 애니메이터를 어차피 Idle로 되돌리지만, 죽은 자리 부활은 이 경로가 필요하다.)
        /// </summary>
        public void ResetDeath()
        {
            if (animator == null) animator = GetComponent<Animator>();

            animator.ResetTrigger(DoDie);
            animator.ResetTrigger(DoDieLarge);
            animator.Rebind();
            animator.Update(0f);
        }

        public void SetRollHeld(bool held)
        {
            if (!hasRollHeld) return;
            animator.SetBool(RollHeld, held);
        }
    }
}
