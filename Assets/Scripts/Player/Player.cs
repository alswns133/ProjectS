using UnityEngine;
using ProjectS.Core;
using ProjectS.Events;

namespace ProjectS.Players
{
    /// <summary>
    /// 플레이어의 중앙 컨텍스트(뇌). 각 기능 컴포넌트를 한곳에서 보유하고,
    /// 상태 머신을 구동하며, 외부 입력 이벤트를 상태 전환으로 중재한다.
    /// 상태들은 이 Player를 통해 컴포넌트(Movement, Animation 등)에 접근한다.
    /// </summary>
    // RequireComponent: Player를 붙이면 아래 부품들이 자동으로 함께 추가된다.
    // → 팀원이 컴포넌트를 빠뜨리는 실수를 '구조'가 막아준다.
    [RequireComponent(typeof(PlayerInputHandler))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerAnimation))]
    [RequireComponent(typeof(PlayerCombat))]
    [RequireComponent(typeof(PlayerStats))]
    [RequireComponent(typeof(PlayerEffects))]
    // 미니맵 등록도 부품처럼 강제한다. 자동 추가 시 type 기본값이 Enemy이므로,
    // 플레이어는 인스펙터에서 MinimapMarkerSource의 type을 Player로 한 번 바꿔 줘야 한다.
    [RequireComponent(typeof(MinimapMarkerSource))]

    public class Player : MonoBehaviour
    {
        // 컴포넌트 참조: 외부에선 읽기만(접근은 허용, 교체는 금지) → { get; private set; }
        public PlayerInputHandler Input { get; private set; }
        public PlayerMovement Movement { get; private set; }
        public PlayerAnimation Animation { get; private set; }
        public PlayerCombat Combat { get; private set; }

        public PlayerStats Stats { get; private set; } 

        // 상태 인스턴스도 외부(상태끼리 전환)에서 참조하므로 읽기 전용 공개
        public PlayerFreeState FreeState { get; private set; }

        public PlayerDeadState DeadState { get; private set; }

        public PlayerRollState RollState { get; private set; }
        public PlayerHitState HitState { get; private set; }
        public PlayerEffects Effect { get; private set; }

        private PlayerStateMachine sm; // 전환(Exit→Enter)을 책임지는 머신. 내부 전용

        /// <summary>
        /// 구르기 중 여부. 구르는 동안 점프·공격·스킬·재구르기 입력을 차단하는 게이트.
        /// 별도 플래그 대신 상태 머신의 현재 상태로 판정 → 플래그 동기화 실수가 원천 차단된다.
        /// </summary>
        public bool IsRolling => sm.Current == RollState;

        /// <summary>
        /// 피격 경직 중 여부. 경직 동안 점프·공격·스킬 입력을 차단하는 게이트.
        /// 구르기는 차단하지 않는다(경직을 회피로 캔슬하는 조작 허용 — 회피 최우선 철학).
        /// </summary>
        public bool IsStaggered => sm.Current == HitState;

        /// <summary>
        /// 구르기·피격·사망으로 전투 동작이 중단된 상태인지 여부.
        /// 입력(OnAttack/OnSkill)은 이미 이 조건으로 막지만, 애니메이션 이벤트(이펙트·검기)는
        /// 클립이 블렌드 아웃되며 뒤늦게 도착할 수 있어 같은 조건으로 한 번 더 막아야 한다.
        /// 안 막으면 구르기 캔슬 순간 뒤늦은 이벤트가 구르기 방향으로 이펙트/검기를 내보낸다.
        /// </summary>
        public bool IsActionInterrupted => IsRolling || IsStaggered || Stats.IsDead;

        // ── 이동 잠금(공격·스킬 중 이동 차단) ────────────────────────────
        // 해제는 동작이 끝나 로코모션으로 돌아올 때 ComboResetBehaviour가 담당한다.
        // 안전장치: 해제 신호를 놓쳐도 이 시간 뒤 자동 해제
        [SerializeField] private float maxActionLockTime = 3f;

        // 회피(구르기) 1회당 스태미나 소모량. 잔량 판정·차감은 PlayerStats가 담당하고,
        // 여기는 '얼마를 쓸지'만 안다(입력 중재자가 비용을 소유).
        [Header("구르기")]
        [SerializeField] private float rollStaminaCost = 20f;

        [Header("점프")]
        [SerializeField, Min(0f)] private float autoJumpDelay = 0.08f;

        // 이동 잠금이 시작된 뒤 흐른 시간. 해제 신호(로코모션 복귀)를 놓쳐도
        // 안전장치로 잠금을 풀기 위해 잰다.
        private float actionLockTimer;
        private float stableGroundedTime;

        /// <summary>
        /// 이동이 잠겨 있는지 여부. 공격/스킬 발동 중 true.
        /// FreeState가 매 프레임 이 값을 확인해, 잠금 중이면 수평 이동을 멈춘다.
        /// </summary>
        public bool IsMovementLocked { get; private set; }

        /// <summary>
        /// 공격/스킬이 실제로 발동하는 순간 이동을 잠근다(Player.OnAttack/OnSkill에서 호출).
        /// 안전장치 타이머도 함께 리셋하므로, 콤보로 잠금이 갱신될 때마다 제한 시간이 다시 시작된다.
        /// </summary>
        public void LockMovement()
        {
            IsMovementLocked = true;
            actionLockTimer = 0f;
        }

        /// <summary>
        /// 공격/스킬 애니메이션이 끝나 로코모션 상태로 돌아왔을 때 이동 제한을 해제한다.
        /// 주로 ComboResetBehaviour(로코모션 진입 시)가 호출하며, 이 호출을 놓치면 안전장치 타이머가 대신 푼다.
        /// 스킬 시전 플래그도 함께 정리한다 → 안전장치 경로로 풀릴 때도
        /// 시전 상태가 남아 공격 입력이 영영 막히는 일이 없게 한다.
        /// </summary>
        public void UnlockMovement()
        {
            IsMovementLocked = false;
            Combat.EndSkillCast();
            // 점프 공격 호버링 해제. 모션이 끝나 로코모션으로 복귀하는 이 시점이
            // "모션이 끝나면 다시 내려간다"(기획)에 해당한다. 안전장치 경로로 풀릴 때도 함께 풀린다.
            Movement.SetHover(false);
        }


        private void Awake()
        {
            // 컴포넌트 캐싱은 Awake에서 1회만. 매 프레임 GetComponent를 피하기 위함.
            Input = GetComponent<PlayerInputHandler>();
            Movement = GetComponent<PlayerMovement>();
            Animation = GetComponent<PlayerAnimation>();
            Combat = GetComponent<PlayerCombat>();
            Stats = GetComponent<PlayerStats>();
            Effect = GetComponent<PlayerEffects>();

            sm = new PlayerStateMachine();
            // 상태를 미리 생성해 보관 → 전환할 때마다 new 하지 않으므로 GC 부담이 없다.
            FreeState = new PlayerFreeState(this);
            DeadState = new PlayerDeadState(this);
            RollState = new PlayerRollState(this);
            HitState = new PlayerHitState(this);
        }

        // 이벤트 구독/해제는 OnEnable↔OnDisable 짝으로. 짝을 안 맞추면 중복 구독이 쌓인다.
        private void OnEnable()
        {
            Input.SkillPressed += OnSkill;
            Input.Attacked += OnAttack;
            Input.StrongAttacked += OnStrongAttack;
            Input.CursorTogglePressed += OnCursorToggle;
            Combat.ComboStepStarted += OnComboStepStarted;
            Combat.TargetHit += OnTargetHit;
            Stats.Damaged += OnDamaged;
            PlayerEvents.OnPlayerDied += OnDied;   // 죽음 구독
        }
        private void OnDisable()
        {
            Input.SkillPressed -= OnSkill;
            Input.Attacked -= OnAttack;
            Input.StrongAttacked -= OnStrongAttack;
            Input.CursorTogglePressed -= OnCursorToggle;
            Combat.ComboStepStarted -= OnComboStepStarted;
            Combat.TargetHit -= OnTargetHit;
            Stats.Damaged -= OnDamaged;
            PlayerEvents.OnPlayerDied -= OnDied;   // 죽음 구독 해제
        }

        private void Start()
        {
            SetCursorLocked(true);     // 시작은 커서 잠금 상태(이후 Alt로 토글)
            sm.ChangeState(FreeState); // 시작 상태 진입
        }

        // Alt(커서 토글 키)를 누를 때마다 잠금 ↔ 해제를 오간다.
        // 현재 상태를 별도 플래그가 아닌 Cursor.lockState로 판정 → 에디터 ESC 등
        // 외부 요인으로 잠금이 풀려도 다음 토글이 실제 상태 기준으로 동작한다.
        private void OnCursorToggle()
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            // 이동 잠금 안전장치: 혹시 해제 신호를 놓쳐도 최대 시간이 지나면 스스로 풀린다.
            if (IsMovementLocked)
            {
                actionLockTimer += Time.deltaTime;
                if (actionLockTimer >= maxActionLockTime) UnlockMovement();
            }

            UpdateStableGroundedTime();

            // 점프 버튼을 누르고 있으면 착지할 때마다 자동으로 다시 점프(꾹 누르면 연속 점프)
            TryJump();

            // 회피 버튼을 누르고 있으면 회피가 끝날 때마다 자동으로 다시 회피(꾹 누르면 연속 회피)
            TryRoll();

            sm.Update(); // 현재 상태의 Update 위임 실행

            // 접지 여부와 수직 속도는 상태와 무관하게 매 프레임 애니메이터에 반영.
            // 점프/낙하/착지 모션은 이 두 값의 조건 전이로 재생된다(트리거 없음).
            Animation.SetGrounded(Movement.IsGrounded);
            Animation.SetVerticalVelocity(Movement.VerticalVelocity);

            // 착지하면 점프 공격 사용권 회복. IsStablyGrounded를 쓰는 이유:
            // 점프 직후 접지 체크가 몇 프레임 true로 남는 잔존 구간에 리셋되는 것을 막는다.
            if (Movement.IsStablyGrounded)
            {
                jumpAttackUsed = false;

                // 착지 직전 공중 클릭 레이스 정리: 트리거가 세팅됐지만 애니메이터가 전환을
                // 평가하기 전에 착지하면 점프 공격이 발동되지 못한다. 이때 로코모션을 벗어난 적이
                // 없어 ComboResetBehaviour의 정리도 안 타므로, 래치된 트리거(유령 점프 공격의 원인)와
                // 호버링·시전 플래그가 그대로 남는다 → 접지 상태에서 직접 정리한다.
                // 정상 점프 공격은 공중 호버링 중(비접지)이라 이 블록에 들어오지 않는다.
                Animation.ResetJumpAttackTrigger();
                if (Movement.IsHovering)
                {
                    Combat.CancelAction();
                    UnlockMovement();   // 내부에서 SetHover(false)도 함께 처리
                }
            }
        }

        /// <summary>현재 상태를 next로 전환한다. 상태들이 자기 전환을 요청하는 공개 창구.</summary>
        public void ChangeState(IState next) => sm.ChangeState(next);

        // Update에서 매 프레임 호출(이벤트가 아닌 폴링). 점프 버튼을 '누르고 있는 동안'
        // 접지할 때마다 다시 점프시켜, 꾹 누르면 연속 점프가 되게 한다(기획 의도).
        // 이벤트(started) 방식이면 '누른 순간' 1회뿐이라 이 연속 점프를 만들 수 없다.
        private void TryJump()
        {
            if (!Input.JumpHeld) return;       // 버튼을 안 누르고 있으면 점프 안 함
            if (Stats.IsDead) return;          // ★ 죽었으면 무시
            if (IsRolling) return;             // 구르기 중 점프 금지(회피 커밋 유지)
            if (IsStaggered) return;           // 피격 경직 중 점프 금지
            if (IsMovementLocked) return;      // 이동 잠금 상태면 점프 무시(공격/스킬 중 점프 방지)
            if (stableGroundedTime < autoJumpDelay) return;  // 착지 직후 모션이 정리될 짧은 여유

            // 접지/상승 판정은 Movement가 단일 소유(CanJump). 실패하면 여기서 끝.
            // 점프 모션은 따로 쏘지 않는다: 매 프레임 반영되는 verticalVelocity/isGrounded
            // 조건 전이가 물리 점프를 그대로 따라오므로, 연속 점프에서 모션이 씹힐 프레임이 없다.
            if (!Movement.Jump()) return;
            stableGroundedTime = 0f;
        }

        private void UpdateStableGroundedTime()
        {
            if (Movement.IsStablyGrounded)
            {
                stableGroundedTime += Time.deltaTime;
                return;
            }

            stableGroundedTime = 0f;
        }

        private void OnSkill(int n)
        {
            if (Stats.IsDead) return;
            if (IsRolling) return;             // 구르기 중 스킬 금지(회피 커밋 유지)
            if (IsStaggered) return;           // 피격 경직 중 스킬 금지

            // 동작 중(스킬 시전·공격 콤보 = 이동 잠금 중)에는 새 스킬을 받지 않는다.
            // 막지 않으면 시전 중 누른 스킬의 트리거가 래치되어 현재 스킬이 끝나자마자
            // 연달아 발동하고, 그 순간 쿨타임도 이미 소모된 상태가 된다.
            // 해제는 로코모션 복귀(ComboResetBehaviour) 또는 안전장치 타이머가 이미 담당하므로
            // 별도의 '시전 중' 플래그를 새로 만들지 않고 이동 잠금을 게이트로 재사용한다.
            if (IsMovementLocked) return;

            // ★ 판정 순서: 쿨타임 → 게이지 → 발동.
            //   쿨타임 중이면 게이지를 건드리지 않고, 게이지가 부족하면 쿨타임도 시작하지 않는다
            //   → 어느 한쪽만 소모되는 사고가 없다. 발동에 성공했을 때만 방향 정렬 + 이동 잠금.
            if (!Combat.CanUseSkill(n)) return;
            if (!Stats.TryUseSkillGauge(Combat.GetSkillGaugeCost(n))) return;
            if (!Combat.UseSkill(n)) return;   // 쿨타임은 위에서 확인했으므로 사실상 항상 성공
            Movement.SnapToCameraForward();
            LockMovement();                    // 스킬이 실제로 발동할 때만 이동 잠금
        }

        // 점프 공격을 이미 썼는지 여부. '점프 1회당 공격 1회' 제한(기획).
        // 착지(IsStablyGrounded)하면 Update에서 리셋된다.
        private bool jumpAttackUsed;

        // 좌클릭 중재. 상황을 보고 점프 공격/달리기 공격/일반 콤보 중 하나로 라우팅한다.
        private void OnAttack()
        {
            if (Stats.IsDead) return;        // 죽었으면 공격 무시(아까 패턴과 동일)
            if (IsRolling) return;           // 구르기 중 공격 금지(회피 커밋 유지)
            if (IsStaggered) return;         // 피격 경직 중 공격 금지

            // 스킬/단타 공격 시전 중 클릭 차단. 막지 않으면 Attack 트리거가 래치된 채 대기하다가
            // 시전이 끝나는 순간 1타가 자동 발동한다.
            // (IsMovementLocked가 아닌 전용 플래그인 이유: 콤보 연타는 잠금 중에도 허용해야 함)
            if (Combat.IsCastingSkill) return;

            // 공중 클릭 = 점프 공격(단타). 점프 1회당 1회만 허용한다(기획).
            if (!Movement.IsGrounded)
            {
                if (jumpAttackUsed) return;

                jumpAttackUsed = true;
                Combat.UseJumpAttack();
                Movement.SnapToCameraForward();  // 공중에서도 카메라가 보는 방향으로 공격(기획)
                Movement.SetHover(true);         // 모션 동안 높이 고정 → 종료 시 UnlockMovement가 낙하 재개
                LockMovement();
                return;
            }

            // 달리는 중 클릭 = 달리기 공격(단타). 콤보로 이어지지 않는다(기획).
            if (Input.IsRunning)
            {
                Combat.UseRunAttack();
                Movement.SnapToCameraForward();
                LockMovement();
                return;
            }

            Combat.OnAttackInput();
            Movement.SnapToCameraForward();
            LockMovement();                    // 공격(콤보 포함) 동안 이동 잠금
        }

        // 우클릭 강공격 중재. 좌클릭 콤보 도중에도 발동한다(기획: 강공격이 콤보를 캔슬).
        // 그래서 OnSkill과 달리 IsMovementLocked를 확인하지 않는다.
        // 콤보 정리(트리거·버퍼 클리어)는 Combat.UseStrongAttack 안의 CancelAction이 담당한다.
        private void OnStrongAttack()
        {
            if (Stats.IsDead) return;
            if (IsRolling) return;               // 구르기 커밋 유지
            if (IsStaggered) return;             // 피격 경직 중 강공격 금지
            if (!Movement.IsGrounded) return;    // 공중 발동 방지(좌클릭과 동일)

            // 스킬/강공격 시전 중에는 불가. 이미 쿨타임을 소모한 동작을 도중에 끊지 않는다.
            if (Combat.IsCastingSkill) return;

            if (!Combat.UseStrongAttack()) return;   // 쿨타임 중이면 아무 일도 일어나지 않음(잠금 X)
            Movement.SnapToCameraForward();
            LockMovement();
        }

        // 구르기 입력 중재. TryJump처럼 Update에서 매 프레임 폴링한다(꾹 누르면 연속 회피 기획).
        // 조건을 통과하면 상태 전환만 하고, 방향 계산·무적·이동·캔슬 처리는
        // 전부 각 상태 안에 있다(세부 구현은 상태가 소유).
        // 이동 잠금을 확인하지 않는 이유: 구르기는 공격/스킬을 캔슬하는 최우선 회피 동작(기획).
        private void TryRoll()
        {
            if (!Input.RollHeld) return;       // 버튼을 안 누르고 있으면 회피 안 함
            if (Stats.IsDead) return;
            if (IsRolling) return;             // 회피 중 재입력 무시 → 끝나는 프레임부터 다음 회피 발동

            if (!Movement.IsStablyGrounded) return;       // 공중에서는 구르기를 발동하지 않음

            if (Input.MoveInput.sqrMagnitude < 0.0001f) return;  // 무입력(Idle) 구르기 금지(기획)

            if (!Stats.TryUseStamina(rollStaminaCost)) return;
            ChangeState(RollState);
        }

        // 콤보 타수가 실제 시작될 때마다 잠금을 갱신한다(OnAttackStart Animation Event 경유).
        // 꾹 누르기 콤보는 OnAttack(클릭)을 거치지 않으므로, 이 갱신이 없으면
        // ① 안전장치 타이머가 콤보 도중 잠금을 풀고
        // ② 콤보 루프(로코모션 복귀→재공격) 후에는 아예 잠기지 않은 채 공격한다.
        private void OnComboStepStarted()
        {
            // 클릭 콤보와 동일하게, 홀드 콤보도 타수마다 카메라 방향으로 다시 정렬한다(기획).
            Movement.SnapToCameraForward();
            LockMovement();
        }

        // 공격/스킬 적중마다 스킬 게이지(SG)를 회복한다. 자연 회복(PlayerStats의 초당 회복)과
        // 더해지는 두 번째 수급 경로다. 회복량은 SkillTable 행(SgGain)이 소유하고,
        // 여기는 이벤트를 연결만 한다.
        private void OnTargetHit(float gaugeGain) => Stats.GainSkillGauge(gaugeGain);

        // 데미지가 실제로 적용됐을 때 피격 경직으로 전환한다.
        // 구르기 중에는 진입하지 않는다: 일반 공격은 무적으로 애초에 안 들어오고,
        // 즉사기(무적 관통)가 치명이 아니었던 경우에도 회피 커밋은 유지한다(기획).
        private void OnDamaged()
        {
            if (Stats.IsDead) return;
            if (IsRolling) return;

            ChangeState(HitState);
        }

        private void OnDied()
        {
            sm.ChangeState(DeadState);
        }
    }
}
