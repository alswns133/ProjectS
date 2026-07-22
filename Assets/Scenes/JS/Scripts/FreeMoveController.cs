using UnityEngine;
using ProjectS.Players;

namespace ProjectS.Movement
{
    /// <summary>
    /// [독립 이동 컨트롤러] WASD 이동 + 더블탭 달리기 + 구르기 + 360도 카메라 기준 이동.
    /// 기존 Player 계열 스크립트는 참조(읽기)만 하고 수정하지 않는다.
    /// 이동/구르기 전진은 루트모션이 담당하고, 이 스크립트는 방향·중력·파라미터를 처리한다.
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
        [SerializeField] private float rollDuration = 0.5f;   // Speed 2에 맞춰 절반으로

        [Header("참조")]
        [SerializeField] private PlayerInputHandler input;
        [SerializeField] private string movingParam = "isMoving";

        private CharacterController controller;
        private Animator animator;
        private Transform cam;

        private int movingHash;
        private int runningHash;
        private int rollHash;

        private float verticalVelocity;

        // 구르기 상태. 구르는 동안 일반 이동을 막고 루트모션에 전진을 맡긴다.
        private bool isRolling;
        private float rollTimer;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();

            movingHash = Animator.StringToHash(movingParam);
            runningHash = Animator.StringToHash("isRunning");
            rollHash = Animator.StringToHash("doRoll");

            if (input == null) input = GetComponent<PlayerInputHandler>();
        }

        private void Update()
        {
            if (!TryCacheCamera()) return;

            if (isRolling)
            {
                rollTimer += Time.deltaTime;

                bool moving = input.MoveInput.sqrMagnitude > 0.0001f;
                animator.SetBool(movingHash, moving);
                animator.SetBool(runningHash, input.IsRunning);

                if (rollTimer >= rollDuration)
                {
                    isRolling = false;

                    // 구르기 종료 시 현재 입력 방향을 즉시 바라보게 한다(보간 없이).
                    // 안 하면 구르던 방향에서 새 방향까지 서서히 도느라 잠깐 직진한다.
                    if (moving)
                    {
                        Vector3 exitDir = CameraRelative(input.MoveInput);
                        exitDir.y = 0;
                        if (exitDir.sqrMagnitude > 0.0001f)
                            transform.rotation = Quaternion.LookRotation(exitDir);
                    }
                }

                return;
            }

            Vector2 moveInput = input.MoveInput;
            bool isRunning = input.IsRunning;
            bool isMoving = moveInput.sqrMagnitude > 0.0001f;

            // 구르기 발동: 이동 입력이 있을 때만(제자리 구르기 방지). Shift 눌러 발동.
            if (input.RollHeld && isMoving)
            {
                StartRoll(moveInput);
                return;
            }

            // 카메라 기준 이동 방향.
            Vector3 dir = CameraRelative(moveInput);
            Vector3 move = isMoving ? dir * (isRunning ? runSpeed : walkSpeed) : Vector3.zero;

            FaceDirection(move);
            ApplyGravity(ref move);
            controller.Move(move * Time.deltaTime);

            animator.SetBool(movingHash, isMoving);
            animator.SetBool(runningHash, isRunning);
        }

        // 구르기 시작: 입력 방향을 즉시 바라보고 구르기 모션 트리거.
        private void StartRoll(Vector2 moveInput)
        {
            Vector3 dir = CameraRelative(moveInput);
            dir.y = 0;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir);   // 구를 방향을 즉시 바라봄

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
                verticalVelocity = -2f;

            verticalVelocity -= gravity * Time.deltaTime;
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
