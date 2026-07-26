using UnityEngine;
using ProjectS.Players;

namespace ProjectS.Movement
{
    /// <summary>
    /// [독립 이동 컨트롤러] WASD 이동 + 더블탭 달리기 + 구르기 + 점프 + 점프 대시 + 360도 카메라 기준 이동.
    /// 기존 Player 계열 스크립트는 참조(읽기)만 하고 수정하지 않는다.
    /// 지상 이동/구르기 전진은 루트모션이 담당하고, 점프·점프 대시는 코드가 처리한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FreeMoveController : MonoBehaviour
    {
        [Header("이동 속도")]
        [SerializeField] private float walkSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;

        [Header("회전")]
        [SerializeField] private float rotationDamping = 10f;

        [Header("중력")]
        [SerializeField] private float gravity = 9.8f;

        [Header("구르기")]
        [SerializeField] private float rollDuration = 0.5f;

        [Header("점프")]
        [SerializeField] private float jumpHeight = 10f;
        // 공중에서 입력 방향으로 관성을 꺾는 속도. 작을수록 관성 위주, 클수록 즉각 조작.
        [SerializeField] private float airControl = 8f;

        [Header("점프 대시")]
        [SerializeField] private float jumpDashSpeed = 12f;
        [SerializeField] private float jumpDashDuration = 0.55f;

        [Header("공중 공격(내려찍기)")]
        // 시전 순간 살짝 떠오르는 높이. 점프 직후 저공에서 시전해도 어색하지 않게 해준다(명조식).
        [SerializeField] private float diveRiseHeight = 0.25f;
        // 위 높이까지 올라가는 속도(m/s). Start 클립이 재생되는 동안 도달하도록 맞춘다.
        [SerializeField] private float diveRiseSpeed = 2f;
        // Loop 구간 하강 속도. 클수록 빠르게 내려꽂는다.
        [SerializeField] private float diveSpeed = 8f;
        // 하강 중 착지 예고(isLanding) 감지 거리. 일반 점프와 요구 타이밍이 달라 따로 둔다.
        // 작을수록 '닿는 순간'에 가깝게 임팩트가 시작되고, 크면 더 미리 시작된다.
        [SerializeField] private float diveLandingCheckDistance = 0.4f;

        [Header("착지 예고")]
        // 지면까지 이 거리 안으로 들어오면 착지 모션을 미리 시작한다(모션 길이에 맞춰 조정).
        [SerializeField] private float landingCheckDistance = 1.5f;
        [SerializeField] private LayerMask groundLayer;
        // 회피 판정용 '사실상 접지' 거리. controller.isGrounded는 발밑이 0.x cm만 떠도 false가 되어,
        // 그 틈에 지상 구르기가 공중 대시로 새는 문제가 있다(일반 점프 포함). 발밑 이 거리 안에 지면이
        // 있으면 접지로 본다. 실제 공중 대시를 의도하는 높이에는 닿지 않을 만큼 작게 둔다.
        [SerializeField] private float dodgeGroundCheckDistance = 0.3f;

        [Header("참조")]
        [SerializeField] private PlayerInputHandler input;
        [SerializeField] private string movingParam = "isMoving";

        // 공중에서 입력을 뗐을 때 관성이 줄어드는 속도. airControl보다 크게 잡아야 착지 시 미끄러지지 않는다.
        [SerializeField] private float airBrake = 30f;

        private CharacterController controller;
        private Animator animator;
        private Transform cam;

        private int movingHash;
        private int runningHash;
        private int rollHash;
        private int groundedHash;
        private int vertVelocityHash;
        private int landingHash;
        private int jumpDashHash;
        private int jumpHash;

        private float verticalVelocity;
        private float groundSpeedMagnitude;

        // 구르기 상태
        private bool isRolling;
        private bool wasGrounded;
        private float rollTimer;

        // 점프 대시 상태. 진행 중엔 중력을 끄고 수평으로만 이동한다.
        private bool isJumpDashing;
        private float jumpDashTimer;
        private Vector3 jumpDashDirection;
        // 대시 시작 시점의 높이. 대시 중 이 높이로 고정한다.
        private float jumpDashHeight;
        // 공중 대시는 착지 전까지 1회만 허용. 착지하면 재사용 가능해진다.
        private bool hasAirDashed;

        // 공격 잠금(ActionLocked)이 풀린 직후 첫 이동에서 입력 방향으로 즉시 회전시키기 위한 상태.
        // 공격 중 카메라 정면을 향한 채로 잠금이 풀리면, 루트모션이 그 정면 방향으로 밀어
        // 방향키와 다른 방향으로 잠깐 나가버린다. 이를 막기 위해 잠금 해제 직후 첫 이동을 스냅한다.


        // 회피 키가 직전 프레임에 눌려 있었는지. 누르는 순간만 잡아 연타 발동을 막는다.
        private bool wasRollHeld;

        // 공중 공격 상태. Hovering = Start 구간(체공), Diving = Loop 구간(하강).
        // 둘 다 잠금(ActionLocked) 중에만 의미가 있고, 착지하면 해제된다.
        private bool hovering;
        private float hoverHeight;       // 현재 유지 중인 높이(상승 구간에서 목표를 향해 올라간다)
        private float hoverTargetHeight; // 시전 높이 + diveRiseHeight
        private bool diving;

        // 점프 순간 캡처한 공중 관성 속도. 공중에서는 이 값을 유지하며 입력 방향으로만 서서히 조향한다.
        private Vector3 airVelocity;
        private Vector3 lastPosition;

        // 공중 피격 체공 상태와 고정할 높이. 공중 공격의 Hovering과 목적이 달라 따로 둔다
        // (저쪽은 diveRiseHeight만큼 살짝 '떠오르는' 연출이고, 이쪽은 맞은 그 높이에 '멈추는' 것).
        private bool hitHovering;
        private float hitHoverHeight;

        /// <summary>
        /// 공격/스킬 중 수평 이동·구르기·점프·대시를 막는 외부 잠금.
        /// 전투 조율자(FreeCombatController)가 Player.IsMovementLocked를 반영해 세팅한다.
        /// 중력·접지 처리는 계속되므로 제자리에서 공격/낙하는 정상 동작한다.
        /// </summary>
        public bool ActionLocked { get; set; }

        /// <summary>
        /// 공중에서 피격 모션이 재생되는 동안 높이를 고정한다(전투 조율자가 Hit 태그를 보고 세팅).
        /// 켜는 순간의 높이를 캡처해 그 자리에 멈추므로, 맞은 지점이 그대로 유지된다.
        ///
        /// 착지 예고(isLanding)는 하강 중(verticalVelocity &lt; 0)에만 켜지는데 체공 중에는 수직 속도를
        /// 0으로 잡아두므로, 체공하는 동안에는 착지 예고가 뜨지 않아 피격 모션이 중간에 착지 모션으로
        /// 새지 않는다. 체공이 풀리면 그 지점부터 정상적으로 낙하·착지 예고가 이어진다.
        /// </summary>
        public bool HitHovering
        {
            get => hitHovering;
            set
            {
                if (value && !hitHovering) hitHoverHeight = transform.position.y;
                hitHovering = value;
            }
        }

        /// <summary>
        /// 점프 입력만 따로 막는다(이동·회전은 허용). 공격 모션이 로코모션으로 블렌드되는 구간에서 쓴다.
        /// 그 구간에 점프하면 애니메이터가 전이 중이라 Any State → Jump_Start가 밀려
        /// 점프 모션이 공중에서 뒤늦게 재생된다. 블렌드가 끝난 뒤 점프하도록 미루는 용도.
        /// </summary>
        public bool JumpBlocked { get; set; }

        /// <summary>현재 접지 여부. 전투 조율자가 공격 가능 판정에 읽는다.</summary>
        public bool IsGrounded => controller != null && controller.isGrounded;

        /// <summary>현재 구르기 중 여부. 전투 조율자가 구르기 캔슬·무적 타이밍에 읽는다.</summary>
        public bool IsRolling => isRolling;

        /// <summary>현재 공중 대시(닷지) 중 여부. 전투 조율자가 공격 캔슬 판정에 읽는다.</summary>
        public bool IsJumpDashing => isJumpDashing;

        /// <summary>
        /// 우클릭 강공격(올려치기)으로 떠오른 상태인지. FreeCombatController가 발동 시 켜고,
        /// 착지·캔슬·점프공격 연계 시 끈다. 공중 대시 캔슬을 '올려치기 중에만' 허용하기 위한 구분값이다
        /// (점프공격 중이나 착지 직후에는 꺼져 있어 공중 대시가 새지 않는다).
        /// </summary>
        public bool StrongAttackRising { get; set; }

        /// <summary>
        /// 공중 공격 Start 구간의 체공. 켜는 순간 현재 높이를 캡처하고, diveRiseHeight만큼 위를 목표로
        /// 서서히 올라간 뒤 그 높이를 유지한다(LateUpdate에서 루트모션을 덮어써 고정).
        /// </summary>
        public bool Hovering
        {
            get => hovering;
            set
            {
                if (value && !hovering)
                {
                    hoverHeight = transform.position.y;
                    hoverTargetHeight = hoverHeight + diveRiseHeight;
                }
                hovering = value;
            }
        }

        /// <summary>
        /// 공중 공격 Loop 구간의 하강. true면 중력 대신 diveSpeed 고정 속도로 내려간다.
        /// 착지하면 Hovering과 함께 자동 해제된다.
        /// </summary>
        public bool Diving { get; set; }

        /// <summary>
        /// 공중 공격을 시작한다. Start 구간 체공을 켜고, 이전 하강 상태를 정리한다.
        /// 실제 하강 전환은 Loop 진입 시 <see cref="BeginDive"/>가 담당한다.
        /// </summary>
        public void BeginDiveHover()
        {
            Diving = false;
            Hovering = true;
        }

        /// <summary>
        /// 공중 공격 Loop 진입 시 호출한다. 체공을 끝내고 하강을 시작한다.
        /// 이미 체공이 풀렸다면(캔슬 등) 아무 것도 하지 않는다.
        /// </summary>
        public void BeginDive()
        {
            if (!Hovering) return;

            Hovering = false;
            Diving = true;
        }

        /// <summary>
        /// 카메라가 보는 수평 방향을 즉시 바라본다(공격 시작 시 방향 정렬용).
        ///
        /// ★ 반드시 Update 단계에서 즉시 적용해야 한다. LateUpdate로 미루면 안 된다 —
        /// 카메라가 추적하는 CameraPivot이 플레이어의 자식이라, 플레이어가 돌면 같이 끌려간다.
        /// 그걸 막으려고 CameraPivotController가 LateUpdate에서 피벗의 월드 회전을 원래대로
        /// 되돌리는데, 그 전제가 "캐릭터 회전은 Update에서 이미 끝났다"이다. 우리가 LateUpdate에서
        /// 회전하면 실행 순서에 따라 피벗 보정 뒤에 플레이어가 돌아, 그 프레임만 피벗이 딸려간 채
        /// 렌더링되고 다음 프레임에 되돌아오는 진동(화면 깜빡임)이 생긴다.
        /// </summary>
        public void SnapToCameraForward()
        {
            if (!TryCacheCamera()) return;

            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(fwd.normalized);
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();

            movingHash = Animator.StringToHash(movingParam);
            runningHash = Animator.StringToHash("isRunning");
            rollHash = Animator.StringToHash("doRoll");
            groundedHash = Animator.StringToHash("isGrounded");
            vertVelocityHash = Animator.StringToHash("verticalVelocity");
            landingHash = Animator.StringToHash("isLanding");
            jumpDashHash = Animator.StringToHash("doJumpDash");
            jumpHash = Animator.StringToHash("doJump");

            if (input == null) input = GetComponent<PlayerInputHandler>();

            lastPosition = transform.position;
        }

        private void Update()
        {
            if (!TryCacheCamera()) return;

            // 접지 여부와 수직 속도는 상태와 무관하게 매 프레임 반영.
            // 점프 모션은 이 두 값의 조건 전이로 재생된다(트리거 없음).
            bool grounded = controller.isGrounded;
            animator.SetBool(groundedHash, grounded);

            // 착지하면 공중 대시 사용을 초기화하고, 공중 공격의 체공/하강도 해제한다.
            // ★ Hovering을 반드시 끄는 이유: 체공이 남으면 LateUpdate가 매 프레임 Y를 고정해
            //   점프해도 몸이 안 뜨고 isGrounded가 계속 true가 된다.
            if (grounded)
            {
                hasAirDashed = false;
                Hovering = false;
                Diving = false;
                StrongAttackRising = false;   // 착지하면 올려치기 상승 상태 해제(착지 후 공중 대시 오발 방지)
                HitHovering = false;          // 체공이 남으면 LateUpdate가 Y를 계속 고정해 땅에 붙지 못한다
            }

            // 공중 피격 체공 중에는 수직 속도를 계속 0으로 눌러둔다. 안 그러면 중력이 쌓여
            // 체공이 풀리는 순간 그동안 누적된 속도로 갑자기 떨어진다(LateUpdate는 위치만 고정하지
            // 속도는 건드리지 않는다).
            if (hitHovering) verticalVelocity = 0f;

            // 착지 순간(공중 → 접지)에 공중 관성을 확실히 제거한다.
            if (grounded && !wasGrounded)
                airVelocity = Vector3.zero;
            wasGrounded = grounded;

            // 착지 예고: 하강 중이고 지면이 가까우면 미리 착지 모션을 시작한다.
            bool nearGround = false;
            if (verticalVelocity < 0f)
            {
                // 공중 공격 하강 중에는 전용 거리를 쓴다(임팩트 타이밍을 일반 점프와 따로 조절).
                // 한 프레임 낙하량보다 짧으면 빠른 하강에서 감지 구간을 건너뛸 수 있어 하한을 둔다.
                float landingDist = landingCheckDistance;
                if (Diving)
                {
                    float perFrameDrop = -verticalVelocity * Time.deltaTime * 2f;
                    landingDist = Mathf.Max(diveLandingCheckDistance, perFrameDrop);
                }

                nearGround = Physics.Raycast(
                    transform.position + Vector3.up * 0.1f,
                    Vector3.down,
                    landingDist,
                    groundLayer,
                    QueryTriggerInteraction.Ignore);
            }
            animator.SetBool(landingHash, nearGround);
            animator.SetFloat(vertVelocityHash, verticalVelocity);


            // 구르기 중: 일반 이동/회전을 막는다. 전진은 루트모션이 담당.
            if (isRolling)
            {
                rollTimer += Time.deltaTime;

                bool rollMoving = input.MoveInput.sqrMagnitude > 0.0001f;
                animator.SetBool(movingHash, rollMoving);
                animator.SetBool(runningHash, input.IsRunning);

                // 구르기 종료. 방향 정렬은 여기서 즉시 돌리지 않고, 이후 평소 회전 보간(FaceDirection)에 맡긴다.
                // 한 프레임에 스냅하면(특히 반대 방향) 모션이 뚝 끊겨 보인다.
                if (rollTimer >= rollDuration)
                    isRolling = false;

                wasRollHeld = input.RollHeld;
                return;
            }

            // 점프 대시 중: 중력을 끄고 수평으로만 이동한다(체공 높이 고정).
            if (isJumpDashing)
            {
                jumpDashTimer += Time.deltaTime;
                verticalVelocity = 0f;

                // 대시 방향은 StartJumpDash에서 시작 순간(대시 직전 입력)에 고정한다.
                // 대시 중 다른 방향키를 눌러도 방향을 바꾸지 않는다.
                controller.Move(jumpDashDirection * jumpDashSpeed * Time.deltaTime);

                if (jumpDashTimer >= jumpDashDuration)
                {
                    isJumpDashing = false;
                    airVelocity = jumpDashDirection * jumpDashSpeed;
                }

                wasRollHeld = input.RollHeld;
                return;
            }

            // 전투 잠금 중: 실제 수평 이동만 막고(중력/접지는 유지), 이동 "의도"는 애니메이터에 계속 전달한다.
            // isMoving을 false로 고정하면 공격 종료(End) 상태에서 End→Walk_Loop(isMoving=true) 전이가
            // 절대 안 걸려, 방향키를 쥔 채 착지해도 Idle로 새어버린다. 공격 State는 별도라 isMoving=true여도
            // 걷기로 새지 않고, 오직 End의 전이 분기만 이 값을 읽는다(점프 Jump_End와 동일 원리).
            if (ActionLocked)
            {
                // 후딜 캔슬: 공격 잠금 중이라도 지상에서 이동+회피키면 구르기로 즉시 캔슬한다(회피 최우선, 기획).
                // 구르기가 시작되면 FreeCombatController가 공격 캔슬 + 잠금 해제를 처리한다.
                if (input.RollHeld && IsEffectivelyGrounded() && input.MoveInput.sqrMagnitude > 0.0001f)
                {
                    StartRoll(input.MoveInput);
                    return;
                }

                // 공중 후딜 캔슬: 강공격 올려치기 상승 중일 때만 구르기가 아니라 점프 대시로 캔슬한다.
                // ★ StrongAttackRising으로 '올려치기 중'만 통과시킨다. 점프공격 중이나 착지 직후에는 꺼져 있어
                //   공중 대시가 새지 않는다(점프공격 하강 중 오발·isGrounded 깜빡임 착지 후 오발 모두 차단).
                // 공중 1회 제한(hasAirDashed)과 방향 입력 조건은 일반 공중 대시와 동일. StartJumpDash가
                // IsJumpDashing을 켜면 FreeCombatController가 공격을 캔슬하고 잠금을 푼다(지상 구르기 캔슬과 같은 경로).
                if (input.RollHeld && !wasRollHeld && StrongAttackRising && !IsEffectivelyGrounded() && !hasAirDashed
                    && input.MoveInput.sqrMagnitude > 0.0001f)
                {
                    StartJumpDash();
                    return;
                }

                // 공격 중에는 이동 조작이 없으므로 공중 관성을 정리한다.
                // 안 지우면 점프 시점의 방향(옛 카메라 방향)이 얼어붙은 채 남아, 잠금이 풀린 뒤
                // 접지 인식이 늦는 구간에서 공중 이동 분기가 그 방향으로 캐릭터를 밀어낸다.
                airVelocity = Vector3.zero;

                Vector3 locked = Vector3.zero;
                if (hovering)
                {
                    // Start 구간: 수직 속도를 죽이고 높이는 LateUpdate가 유지·상승시킨다.
                    verticalVelocity = 0f;
                }
                else if (Diving && !grounded)
                {
                    // Loop 구간: 중력 대신 고정 속도로 내려꽂는다.
                    verticalVelocity = -diveSpeed;
                    locked.y = verticalVelocity;
                }
                else if (StrongAttackRising)
                {
                    // 올려치기 상승: 클립의 루트모션(Y Bake 해제)이 상승을 담당하므로 코드 중력을 억제한다.
                    // 둘이 동시에 Y를 건드리면 상승이 덜컹거린다. 정점의 OnStrongAttackRiseEnd Animation Event가
                    // 이 플래그를 꺼 중력을 재개하면, 이후 낙하는 일반 점프 낙하(Jump_Loop)로 자연히 이어진다.
                    verticalVelocity = 0f;
                }
                else
                {
                    ApplyGravity(ref locked);
                }
                controller.Move(locked * Time.deltaTime);

                bool lockedMoving = input.MoveInput.sqrMagnitude > 0.0001f;
                animator.SetBool(movingHash, lockedMoving);
                animator.SetBool(runningHash, lockedMoving && input.IsRunning);
                wasRollHeld = input.RollHeld;
                return;
            }

            Vector2 moveInput = input.MoveInput;
            bool isRunning = input.IsRunning;
            bool isMoving = moveInput.sqrMagnitude > 0.0001f;

            // 회피 키를 누르는 순간만 잡는다(누르고 있는 내내 발동하지 않도록).
            bool rollPressed = input.RollHeld && !wasRollHeld;
            wasRollHeld = input.RollHeld;

            // 공중에서 회피 키 → 점프 대시(착지 전까지 1회만).
            // 방향 입력이 없는 중립 상태에서는 발동하지 않는다(지상 구르기와 동일한 규칙).
            // ★ 접지 판정은 IsEffectivelyGrounded로 한다. controller.isGrounded만 보면 발밑 0.x cm에서
            //   지상 회피가 공중 대시로 샌다(일반 점프 착지 직전 포함).
            if (rollPressed && !IsEffectivelyGrounded() && !hasAirDashed && isMoving)
            {
                StartJumpDash();
                return;
            }

            // 구르기 발동: 지상에서 이동 입력이 있을 때만(꾹 누르면 연속 발동).
            if (input.RollHeld && isMoving && IsEffectivelyGrounded())
            {
                StartRoll(moveInput);
                return;
            }

            // 점프: 버튼을 누르고 있으면 접지할 때마다 다시 점프(꾹 누르면 연속 점프).
            // JumpBlocked면 미룬다(공격 → 로코모션 블렌드 중). 계속 누르고 있으면 풀리는 즉시 점프한다.
            if (input.JumpHeld && grounded && verticalVelocity <= 0f && !JumpBlocked)
                Jump();

            Vector3 move;

            if (grounded)
            {
                // 지상: 입력 방향으로 즉시 이동.
                Vector3 dir = CameraRelative(moveInput);
                move = isMoving ? dir * (isRunning ? runSpeed : walkSpeed) : Vector3.zero;
            }
            else
            {
                // 공중: 입력이 있으면 그 방향으로 조향, 없으면 관성을 빠르게 줄인다.
                Vector3 target = isMoving
                    ? CameraRelative(moveInput) * airVelocity.magnitude
                    : Vector3.zero;
                float rate = isMoving ? airControl : airBrake;
                airVelocity = Vector3.MoveTowards(airVelocity, target, rate * Time.deltaTime);
                move = airVelocity;
            }

            FaceDirection(move);
            ApplyGravity(ref move);
            controller.Move(move * Time.deltaTime);

            animator.SetBool(movingHash, isMoving);
            animator.SetBool(runningHash, isRunning);
        }

        // 루트모션이 적용된 뒤(프레임 끝)에 실행된다.
        private void LateUpdate()
        {
            // 대시 중에는 루트모션이 끌어내린 높이를 여기서 되돌려 체공을 유지한다.
            // Update에서 고정하면 그 뒤에 적용되는 루트모션이 다시 끌어내리므로 반드시 LateUpdate에서 처리한다.
            if (isJumpDashing)
            {
                Vector3 dashPos = transform.position;
                dashPos.y = jumpDashHeight;
                transform.position = dashPos;
            }

            // 공중 공격 Start 구간: 시전 높이에서 diveRiseHeight까지 서서히 올라간 뒤 그 높이를 유지한다.
            // 상승·유지 모두 여기서 해야 뒤에 적용되는 루트모션이 다시 끌어내리지 못한다(대시 높이 고정과 동일).
            if (hovering)
            {
                hoverHeight = Mathf.MoveTowards(hoverHeight, hoverTargetHeight, diveRiseSpeed * Time.deltaTime);

                Vector3 hoverPos = transform.position;
                hoverPos.y = hoverHeight;
                transform.position = hoverPos;
            }

            // 공중 피격 체공: 맞은 높이를 그대로 유지한다. 위 Hovering과 달리 상승은 없다.
            // 여기(LateUpdate)에서 처리해야 뒤에 적용되는 루트모션이 다시 끌어내리지 못한다.
            if (hitHovering)
            {
                Vector3 hitPos = transform.position;
                hitPos.y = hitHoverHeight;
                transform.position = hitPos;
            }

            // 지상에서 이동 중일 때만 갱신. 최댓값을 유지해 회전 중인 느린 프레임에 끌려가지 않게 한다.
            if (Time.deltaTime > 0f && controller.isGrounded && input.MoveInput.sqrMagnitude > 0.0001f)
            {
                Vector3 delta = transform.position - lastPosition;
                delta.y = 0f;
                float speed = delta.magnitude / Time.deltaTime;
                groundSpeedMagnitude = Mathf.Max(groundSpeedMagnitude * 0.9f, speed);
            }

            lastPosition = transform.position;
        }

        // 점프: 수직 속도를 튀기고, 지상에서 내던 수평 속도를 공중 관성으로 넘긴다.
        private void Jump()
        {
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);

            // 점프 모션은 트리거로 발사한다. 조건 전이(isGrounded=false + verticalVelocity>10)로 걸면
            // 그 창이 0.2초뿐이라, 공격 종료 블렌딩이나 접지 판정 지연에 밀리면 모션을 통째로 놓친다.
            // 트리거는 래치되므로 창을 놓칠 수 없고, 1회 발사·1회 소비라 상승 중 재생되는 문제도 없다.
            animator.SetTrigger(jumpHash);

            // 방향은 현재 입력에서, 크기는 지상에서 측정한 실제 속도에서 가져온다.
            Vector2 moveInput = input.MoveInput;
            airVelocity = moveInput.sqrMagnitude > 0.0001f
                ? CameraRelative(moveInput) * groundSpeedMagnitude
                : Vector3.zero;
        }

        /// <summary>
        /// 올려치기 상승 구간 시작. <b>올려치기 클립의 Animation Event</b>로, 발이 땅을 떠나는 프레임에 호출한다.
        /// 여기부터 정점(OnStrongAttackRiseEnd)까지 코드 중력을 억제해, 클립의 루트모션(Y Bake 해제)이
        /// 캐릭터를 그대로 띄우게 한다. 코드가 아니라 애니메이션이 높이를 결정하므로 보이는 위치와 실제
        /// 위치(루트)가 일치한다 → 카메라 추적·높이·이어치기 캡처가 전부 맞는다.
        /// (Animation Event가 문자열로 이 이름을 참조하므로 메서드명 변경에 주의.)
        /// </summary>
        public void OnStrongAttackRiseStart()
        {
            // 잔여 체공/하강 상태가 남아 있으면 상승 처리가 꼬이므로 먼저 정리한다.
            Hovering = false;
            Diving = false;
            verticalVelocity = 0f;      // 코드 낙하 잔재 제거(상승은 루트모션이 담당)
            StrongAttackRising = true;
        }

        /// <summary>
        /// 올려치기 상승 구간 끝. <b>선택적</b> Animation Event다.
        /// 보통은 필요 없다 — StrongAttack → Jump_Loop 전이가 잠금(Attack 태그)을 벗어나며 중력을 자동 재개하기 때문.
        /// 전이 Exit Time을 상승 정점(클립 끝 = 1.0)에 맞추면 정점에서 정확히 낙하로 이어진다.
        /// 클립 안에 '상승 후 하강' 구간이 따로 있어, 전이보다 먼저 특정 프레임에서 중력을 재개하고 싶을 때만 배치한다.
        /// (Animation Event가 문자열로 이 이름을 참조하므로 메서드명 변경에 주의.)
        /// </summary>
        public void OnStrongAttackRiseEnd()
        {
            StrongAttackRising = false;
            verticalVelocity = 0f;      // 정점에서 0부터 낙하 시작(부드러운 인계)
        }

        // 점프 대시 시작: 입력 방향(없으면 바라보는 방향)으로 대시한다.
        private void StartJumpDash()
        {
            isJumpDashing = true;
            hasAirDashed = true;   // 착지 전까지 재사용 금지(공중 1회 제한)
            jumpDashTimer = 0f;
            jumpDashHeight = transform.position.y;   // 이 높이를 대시 내내 유지

            jumpDashDirection = ResolveDashDirection();
            transform.rotation = Quaternion.LookRotation(jumpDashDirection);

            verticalVelocity = 0f;
            animator.SetTrigger(jumpDashHash);
        }

        // 현재 입력 방향(카메라 기준)을 수평으로 반환. 입력이 없으면 현재 바라보는 방향.
        private Vector3 ResolveDashDirection()
        {
            Vector2 moveInput = input.MoveInput;
            if (moveInput.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = CameraRelative(moveInput);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) return dir.normalized;
            }

            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return fwd.normalized;
        }

        // 구르기 시작: 입력 방향을 즉시 바라보고 구르기 모션 트리거.
        private void StartRoll(Vector2 moveInput)
        {
            Vector3 dir = CameraRelative(moveInput);
            dir.y = 0;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);

            // 남아 있는 상승 속도를 정리한다(안전장치). 구르기는 지상 동작이라 0으로 리셋해도 안전하며,
            // 공중 상태에서 남은 수직 속도가 구르기 후 유령 점프로 새는 것을 막는다.
            verticalVelocity = 0f;

            isRolling = true;
            rollTimer = 0f;
            animator.SetTrigger(rollHash);
        }

        private Vector3 CameraRelative(Vector2 input)
        {
            Vector3 f = cam.forward; f.y = 0; f.Normalize();
            Vector3 r = cam.right; r.y = 0; r.Normalize();
            return (f * input.y + r * input.x).normalized;
        }

        /// <summary>
        /// 회피·공격 분기용 '사실상 접지' 판정. controller.isGrounded(발밑 0.x cm만 떠도 false)의 틈을
        /// 짧은 레이캐스트로 메운다. 지상 회피가 공중 대시로 새거나(회피), Jump_Attack_End처럼 이미
        /// 착지한 모션에서 공중 공격이 재발동되는 것(공격)을 막는다. 물리/중력 판정에는 쓰지 않는다.
        /// </summary>
        public bool IsEffectivelyGrounded()
        {
            if (controller.isGrounded) return true;

            return Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                0.1f + dodgeGroundCheckDistance,
                groundLayer,
                QueryTriggerInteraction.Ignore);
        }

        private void FaceDirection(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                rotationDamping * Time.deltaTime);
        }

        private void ApplyGravity(ref Vector3 move)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
                airVelocity = Vector3.zero;   // 착지 시 관성 제거 → 미끄러지지 않음
            }

            verticalVelocity -= 2f * gravity * Time.deltaTime;
            move.y = verticalVelocity;
        }

        private bool TryCacheCamera()
        {
            if (cam != null) return true;
            if (Camera.main == null) return false;
            cam = Camera.main.transform;
            return true;
        }
    }
}
