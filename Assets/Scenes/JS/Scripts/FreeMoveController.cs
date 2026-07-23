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

        [Header("착지 예고")]
        // 지면까지 이 거리 안으로 들어오면 착지 모션을 미리 시작한다(모션 길이에 맞춰 조정).
        [SerializeField] private float landingCheckDistance = 1.5f;
        [SerializeField] private LayerMask groundLayer;

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

        // 회피 키가 직전 프레임에 눌려 있었는지. 누르는 순간만 잡아 연타 발동을 막는다.
        private bool wasRollHeld;

        // 점프 순간 캡처한 공중 관성 속도. 공중에서는 이 값을 유지하며 입력 방향으로만 서서히 조향한다.
        private Vector3 airVelocity;
        private Vector3 lastPosition;

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

            // 착지하면 공중 대시 사용을 초기화한다(공중 1회 제한).
            if (grounded) hasAirDashed = false;

            // 착지 순간(공중 → 접지)에 공중 관성을 확실히 제거한다.
            if (grounded && !wasGrounded)
                airVelocity = Vector3.zero;
            wasGrounded = grounded;

            // 착지 예고: 하강 중이고 지면이 가까우면 미리 착지 모션을 시작한다.
            bool nearGround = false;
            if (verticalVelocity < 0f)
            {
                nearGround = Physics.Raycast(
                    transform.position + Vector3.up * 0.1f,
                    Vector3.down,
                    landingCheckDistance,
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

                if (rollTimer >= rollDuration)
                {
                    isRolling = false;

                    // 구르기 종료 시 현재 입력 방향을 즉시 바라보게 한다(보간 없이).
                    if (rollMoving)
                    {
                        Vector3 exitDir = CameraRelative(input.MoveInput);
                        exitDir.y = 0;
                        if (exitDir.sqrMagnitude > 0.0001f)
                            transform.rotation = Quaternion.LookRotation(exitDir);
                    }
                }

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

            Vector2 moveInput = input.MoveInput;
            bool isRunning = input.IsRunning;
            bool isMoving = moveInput.sqrMagnitude > 0.0001f;

            // 회피 키를 누르는 순간만 잡는다(누르고 있는 내내 발동하지 않도록).
            bool rollPressed = input.RollHeld && !wasRollHeld;
            wasRollHeld = input.RollHeld;

            // 공중에서 회피 키 → 점프 대시(착지 전까지 1회만).
            if (rollPressed && !grounded && !hasAirDashed)
            {
                StartJumpDash();
                return;
            }

            // 구르기 발동: 지상에서 이동 입력이 있을 때만(꾹 누르면 연속 발동).
            if (input.RollHeld && isMoving && grounded)
            {
                StartRoll(moveInput);
                return;
            }

            // 점프: 버튼을 누르고 있으면 접지할 때마다 다시 점프(꾹 누르면 연속 점프).
            if (input.JumpHeld && grounded && verticalVelocity <= 0f)
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

            // 방향은 현재 입력에서, 크기는 지상에서 측정한 실제 속도에서 가져온다.
            Vector2 moveInput = input.MoveInput;
            airVelocity = moveInput.sqrMagnitude > 0.0001f
                ? CameraRelative(moveInput) * groundSpeedMagnitude
                : Vector3.zero;
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
