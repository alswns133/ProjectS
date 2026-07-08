using UnityEngine;

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

    public PlayerAirRollState AirRollState { get; private set; }

    private PlayerStateMachine sm; // 전환(Exit→Enter)을 책임지는 머신. 내부 전용

    /// <summary>
    /// 구르기(지상·공중) 중 여부. 구르는 동안 점프·공격·스킬·재구르기 입력을 차단하는 게이트.
    /// 별도 플래그 대신 상태 머신의 현재 상태로 판정 → 플래그 동기화 실수가 원천 차단된다.
    /// </summary>
    public bool IsRolling => sm.Current == RollState || sm.Current == AirRollState;

    // ── 이동 잠금(공격·스킬 중 이동 차단) ────────────────────────────
    // 해제는 동작이 끝나 로코모션으로 돌아올 때 ComboResetBehaviour가 담당한다.
    // 안전장치: 해제 신호를 놓쳐도 이 시간 뒤 자동 해제
    [SerializeField] private float maxActionLockTime = 3f;

    // 회피(구르기·공중 대시) 1회당 스태미나 소모량. 잔량 판정·차감은 PlayerStats가 담당하고,
    // 여기는 '얼마를 쓸지'만 안다(입력 중재자가 비용을 소유).
    [Header("Roll")]
    [SerializeField] private float rollStaminaCost = 20f;

    // 이동 잠금이 시작된 뒤 흐른 시간. 해제 신호(로코모션 복귀)를 놓쳐도
    // 안전장치로 잠금을 풀기 위해 잰다.
    private float actionLockTimer;

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
    }


    private void Awake()
    {
        // 컴포넌트 캐싱은 Awake에서 1회만. 매 프레임 GetComponent를 피하기 위함.
        Input = GetComponent<PlayerInputHandler>();
        Movement = GetComponent<PlayerMovement>();
        Animation = GetComponent<PlayerAnimation>();
        Combat = GetComponent<PlayerCombat>();
        Stats = GetComponent<PlayerStats>();

        sm = new PlayerStateMachine();
        // 상태를 미리 생성해 보관 → 전환할 때마다 new 하지 않으므로 GC 부담이 없다.
        FreeState = new PlayerFreeState(this);
        DeadState = new PlayerDeadState(this);
        RollState = new PlayerRollState(this);
        AirRollState = new PlayerAirRollState(this);
    }

    // 이벤트 구독/해제는 OnEnable↔OnDisable 짝으로. 짝을 안 맞추면 중복 구독이 쌓인다.
    private void OnEnable()
    {
        Input.SkillPressed += OnSkill;
        Input.Attacked += OnAttack;
        PlayerEvents.OnPlayerDied += OnDied;   // 죽음 구독
    }
    private void OnDisable()
    {
        Input.SkillPressed -= OnSkill;
        Input.Attacked -= OnAttack;
        PlayerEvents.OnPlayerDied -= OnDied;   // 죽음 구독 해제
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;   // 원본의 커서 잠금
        Cursor.visible = false;
        sm.ChangeState(FreeState); // 시작 상태 진입
    }

    private void Update()
    {
        // 이동 잠금 안전장치: 혹시 해제 신호를 놓쳐도 최대 시간이 지나면 스스로 풀린다.
        if (IsMovementLocked)
        {
            actionLockTimer += Time.deltaTime;
            if (actionLockTimer >= maxActionLockTime) UnlockMovement();
        }

        // 점프 버튼을 누르고 있으면 착지할 때마다 자동으로 다시 점프(꾹 누르면 연속 점프)
        TryJump();

        // 회피 버튼을 누르고 있으면 회피가 끝날 때마다 자동으로 다시 회피(꾹 누르면 연속 회피)
        TryRoll();

        sm.Update(); // 현재 상태의 Update 위임 실행

        // 접지 여부는 상태와 무관하게 매 프레임 애니메이터에 반영
        Animation.SetGrounded(Movement.IsGrounded);
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
        if (IsMovementLocked) return;      // 이동 잠금 상태면 점프 무시(공격/스킬 중 점프 방지)

        // 접지/상승 판정은 Movement가 단일 소유(CanJump). 실패하면 여기서 끝
        // → 트리거가 래치된 채 남아 착지 후 점프 모션이 한 번 더 재생되는 것을 막는다.
        if (!Movement.Jump()) return;
        Animation.PlayJump();              // 실제로 점프했을 때만 모션 트리거
    }
    private void OnSkill(int n)
    {
        if (Stats.IsDead) return;
        if (IsRolling) return;             // 구르기 중 스킬 금지(회피 커밋 유지)

        // 동작 중(스킬 시전·공격 콤보 = 이동 잠금 중)에는 새 스킬을 받지 않는다.
        // 막지 않으면 시전 중 누른 스킬의 트리거가 래치되어 현재 스킬이 끝나자마자
        // 연달아 발동하고, 그 순간 쿨타임도 이미 소모된 상태가 된다.
        // 해제는 로코모션 복귀(ComboResetBehaviour) 또는 안전장치 타이머가 이미 담당하므로
        // 별도의 '시전 중' 플래그를 새로 만들지 않고 이동 잠금을 게이트로 재사용한다.
        if (IsMovementLocked) return;

        // ★ 쿨타임 판정을 이동 잠금보다 먼저.
        //   스킬이 실제로 나갔을 때만 방향 정렬 + 이동 잠금 → 쿨타임 중엔 그냥 계속 움직인다.
        if (!Combat.UseSkill(n)) return;   // 쿨타임 중/없는 스킬이면 여기서 종료(잠금 X)
        Movement.SnapToCameraForward();
        LockMovement();                    // 스킬이 실제로 발동할 때만 이동 잠금
    }

    private void OnAttack()
    {
        if (Stats.IsDead) return;        // 죽었으면 공격 무시(아까 패턴과 동일)
        if (IsRolling) return;           // 구르기 중 공격 금지(회피 커밋 유지)

        // 스킬 시전 중 클릭 차단. 막지 않으면 Attack 트리거가 래치된 채 대기하다가
        // 스킬이 끝나는 순간 1타가 자동 발동한다.
        // (IsMovementLocked가 아닌 전용 플래그인 이유: 콤보 연타는 잠금 중에도 허용해야 함)
        if (Combat.IsCastingSkill) return;
        if (!Movement.IsGrounded) return;  // 접지 상태에서만 점프(공중 점프 방지)

        Combat.OnAttackInput();
        Movement.SnapToCameraForward();
        LockMovement();                    // 공격(콤보 포함) 동안 이동 잠금
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

        // 점프 직후 '상승 중인데 접지 체크는 아직 true'인 잔존 구간에는 회피를 미룬다.
        // 이 구간에 공중 대시를 허용하면 AirDash가 상승을 죽여 접지 판정이 풀리지 않고,
        // 애니메이터가 isGrounded=true에 묶여 공중 스테이트로 못 가 doAirRoll을 소비하지 못한다
        // (스태미나만 소모되고 모션이 안 나가는 증상). 몇 프레임 뒤 진짜 공중이 되면 발동된다.
        if (Movement.IsGrounded && !Movement.IsStablyGrounded) return;

        // 공중이면 공중 대시로 분기. 횟수 제한은 없고 스태미나가 실질적 제한이다(기획).
        if (!Movement.IsStablyGrounded)
        {
            // 접지 체크 플리커(경사·이음새에서 1프레임 false) 오판 방지:
            // 발밑 여유 높이가 확보된 '진짜 공중'에서만 대시를 허용한다.
            // 미달이면 발동을 미룬다 → 꾹 누르고 있으면 충분히 뜬 프레임에 자동 발동.
            if (!Movement.HasAirRollClearance) return;

            // 스태미나 판정은 모든 조건 통과 후 마지막에 → 발동 못 하는 상황에서 소모되는 일이 없다.
            if (!Stats.TryUseStamina(rollStaminaCost)) return;
            ChangeState(AirRollState);
            return;
        }

        if (Input.MoveInput.sqrMagnitude < 0.0001f) return;  // 무입력(Idle) 구르기 금지(기획)

        if (!Stats.TryUseStamina(rollStaminaCost)) return;
        ChangeState(RollState);
    }

    private void OnDied()
    {
        sm.ChangeState(DeadState);
    }
}
