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

    private PlayerStateMachine sm; // 전환(Exit→Enter)을 책임지는 머신. 내부 전용

    // ── 이동 잠금(공격·스킬 중 이동 차단) ────────────────────────────
    // 해제는 동작이 끝나 로코모션으로 돌아올 때 ComboResetBehaviour가 담당한다.
    // 안전장치: 해제 신호를 놓쳐도 이 시간 뒤 자동 해제
    [SerializeField] private float maxActionLockTime = 3f; 

    // 이동 잠금 타이머
    private float actionLockTimer;
    /// <summary>
    /// 이동이 잠겨 있는지 여부. 공격/스킬 중 true. FreeState가 이 값을 보고 수평 이동을 멈춘다.
    /// </summary>
    public bool IsMovementLocked { get; private set; }

    /// <summary>
    /// 이동 잠금 메서드
    /// </summary>
    public void LockMovement() 
    { 
        IsMovementLocked = true; 
        actionLockTimer = 0f; 
    }

    /// <summary>
    /// 이동 잠금 해제 메서드
    /// </summary>
    public void UnlockMovement() => IsMovementLocked = false;


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

        sm.Update(); // 현재 상태의 Update 위임 실행

        // 접지 여부는 상태와 무관하게 매 프레임 애니메이터에 반영
        Animation.SetGrounded(Movement.IsGrounded);
    }

    /// <summary>현재 상태를 next로 전환한다. 상태들이 자기 전환을 요청하는 공개 창구.</summary>
    public void ChangeState(IState next) => sm.ChangeState(next);

    // 입력 이벤트 핸들러: 외부 입력을 받아 컴포넌트/상태로 연결
    // 매 프레임 호출. 점프 버튼을 '누르고 있는 동안' 접지할 때마다 점프한다.
    // → 꾹 누르면 착지 즉시 다시 점프해서 연속 점프가 된다(기획 의도).
    private void TryJump()
    {
        if (!Input.JumpHeld) return;       // 버튼을 안 누르고 있으면 점프 안 함
        if (Stats.IsDead) return;          // ★ 죽었으면 무시
        if (IsMovementLocked) return;      // 이동 잠금 상태면 점프 무시(공격/스킬 중 점프 방지)
        if (!Movement.IsGrounded) return;  // 접지 상태에서만 점프(공중 점프 방지)
        Movement.Jump();
        Animation.PlayJump();
    }
    private void OnSkill(int n)
    {
        if (Stats.IsDead) return;
        Combat.UseSkill(n);
        Movement.SnapToCameraForward();
        LockMovement();                    // 스킬 동안 이동 잠금
    }

    private void OnAttack()
    {
        if (Stats.IsDead) return;        // 죽었으면 공격 무시(아까 패턴과 동일)
        if (!Movement.IsGrounded) return;  // 접지 상태에서만 점프(공중 점프 방지)
        Combat.OnAttackInput();
        Movement.SnapToCameraForward();
        LockMovement();                    // 공격(콤보 포함) 동안 이동 잠금
    }

    private void OnDied()
    {
        sm.ChangeState(DeadState);
    }
}
