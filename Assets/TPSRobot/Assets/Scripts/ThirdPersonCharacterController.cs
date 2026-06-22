using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ThirdPersonCharacterController : MonoBehaviour
{
    private readonly int xHash = Animator.StringToHash("X");
    private readonly int zHash = Animator.StringToHash("Z");
    private Camera gameCamera;
    private CharacterController controller;
    private Animator animator;

    [SerializeField] private InputAction moveAction;
    public InputAction cameraAction;

    // [추가] 마우스 휠 입력을 위한 Input Action
    [SerializeField] private InputAction zoomAction;
    [SerializeField] private InputAction jumpAction;
    [SerializeField] private InputAction skillAction;

    [SerializeField] private float rotationDamping = 10;
    [SerializeField] public float currentSpeed = 3;
    private float verticalVelocity = 0;
    private float gravityScale = 9.8f;
    [SerializeField] private float jumpHeight = 3f;
    private Vector3 movementDirection;

    public CinemachineCamera aim;
    private int aimCameraOrder = 20;
    private bool targetingState = false;
    private bool isJump = false;
    private bool isSkill = false;

    // [추가] 줌 관련 변수 설정
    [Header("Zoom Settings")]
    [SerializeField] private CinemachineCamera freeLookCamera; // 평소에 사용하는 시네머신 카메라
    [SerializeField] private float minDistance = 2f;          // 최소 거리 (줌인 한계)
    [SerializeField] private float maxDistance = 10f;         // 최대 거리 (줌아웃 한계)
    [SerializeField] private float zoomSpeed = 0.5f;          // 줌 속도
    private float currentDistance = 5f;                       // 현재 거리 기본값

    public bool TargetingState => targetingState;

    private void ApplyGravity()
    {
        animator.SetBool("isGrounded", controller.isGrounded);
        if (controller.isGrounded)
        {
            
            verticalVelocity = -0.5f;
            if (isJump)
                verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravityScale);
        }
        //else
        verticalVelocity -= 2f * gravityScale * Time.deltaTime;

        movementDirection.y = verticalVelocity;
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if(context.phase == InputActionPhase.Started)
        {
            animator.SetTrigger("doJump");
            isJump = true;
        }
        else
            isJump = false;
    }

    public void IsSkill(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            string keyName = context.control.name;

            switch (keyName)
            {
                case "1":
                    ExecuteSkill(1);
                    break;
                case "2":
                    ExecuteSkill(2);
                    break;
                case "3":
                    ExecuteSkill(3);
                    break;
                case "4":
                    ExecuteSkill(4);
                    break;
                case "5":
                    ExecuteSkill(5);
                    break;
            }
        }
        else
            isSkill = false;
    }

    private void ExecuteSkill(int skillNumber)
    {
        switch (skillNumber)
        {
            case 1:
                animator.SetTrigger("Skill1");
                break;
            case 2:
                animator.SetTrigger("Skill2");
                break;
            case 3:
                animator.SetTrigger("Skill3");
                break;
            case 4:
                animator.SetTrigger("Skill4");
                break;
            case 5:
                animator.SetTrigger("Skill5");
                break;
        }

    }

    void UpdateFreeLookMovement()
    {
        Vector2 moveInput = moveAction.ReadValue<Vector2>();

        //movementDirection = GetCameraRelativeMoveDirection(moveInput);

        Vector3 moveDir = GetCameraRelativeMoveDirection(moveInput);

        movementDirection = moveDir * currentSpeed;

        if (!targetingState)
        {
            FaceMovementDirection(movementDirection, Time.deltaTime);

            Vector3 direction = movementDirection;
            direction.y = 0;
            if (direction.magnitude > 0.0001f)
                animator.SetFloat(zHash, 1, 0.1f, Time.deltaTime);
            else
                animator.SetFloat(zHash, 0, 0.1f, Time.deltaTime);
        }
        else
        {
            Vector3 cameraDirection = gameCamera.transform.forward;
            FaceMovementDirection(cameraDirection, Time.deltaTime);

            animator.SetFloat(zHash, moveInput.y, 0.1f, Time.deltaTime);
            animator.SetFloat(xHash, moveInput.x, 0.1f, Time.deltaTime);
        }


        ApplyGravity();
        controller.Move(movementDirection * Time.deltaTime);

    }

    // [추가] 마우스 휠 입력을 받아 카메라 거리를 조절하는 함수
    void UpdateZoom()
    {
        if (zoomAction == null || freeLookCamera == null) return;

        // 마우스 휠 입력 값 읽기 (위로 굴리면 양수, 아래로 굴리면 음수)
        float scrollInput = zoomAction.ReadValue<Vector2>().y;

        if (Mathf.Abs(scrollInput) > 0.0001f)
        {
            // Cinemachine3개의 바디 컴포넌트(ThirdPersonFollow)를 가져옵니다.
            var followComponent = freeLookCamera.GetComponent<CinemachineThirdPersonFollow>();

            if (followComponent != null)
            {
                // 입력 방향에 따라 현재 거리 계산 (휠을 위로 올리면 거리가 줄어듦 = 줌인)
                currentDistance -= scrollInput * zoomSpeed * Time.deltaTime;
                // 최소/최대 제한
                currentDistance = Mathf.Clamp(currentDistance, minDistance, maxDistance);

                // 시네머신 카메라의 거리에 적용
                followComponent.CameraDistance = currentDistance;
            }
        }
    }

    private void FaceMovementDirection(Vector3 movementDirection, float delta)
    {
        Vector3 targetDirection = movementDirection;
        movementDirection.y = 0;

        if (movementDirection.magnitude > 0)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation,
                                                 Quaternion.LookRotation(movementDirection.normalized),
                                                 rotationDamping * delta);
        }
    }

    Vector3 GetCameraRelativeMoveDirection(Vector2 moveInput)
    {
        Vector3 forward = gameCamera.transform.forward;
        Vector3 right = gameCamera.transform.right;

        forward.y = 0;
        right.y = 0;
        forward.Normalize();
        right.Normalize();

        return (forward * moveInput.y + right * moveInput.x).normalized;
    }

    void OnEnable()
    {
        if (moveAction != null) moveAction.Enable();
        if (cameraAction != null) cameraAction.Enable();
        if (zoomAction != null) zoomAction.Enable(); // [추가] 활성화
        if (jumpAction != null) jumpAction.Enable();
        if (skillAction != null) skillAction.Enable();
    }

    void OnDisable()
    {
        if (moveAction != null) moveAction.Disable();
        if (cameraAction != null) cameraAction.Disable();
        if (zoomAction != null) zoomAction.Disable(); // [추가] 비활성화
        if (jumpAction != null) jumpAction.Disable();
        if (skillAction != null) skillAction.Disable();
    }

    void Start()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        gameCamera = Camera.main;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;


        if (cameraAction != null)
        {
            cameraAction.started += OnAim;
            cameraAction.canceled += OnAim;
        }

        if (skillAction != null)
        {
            skillAction.started += IsSkill;
            skillAction.canceled += IsSkill;

        }

        // [추가] 시작할 때 카메라의 초기 거리를 현재 설정된 값으로 동기화
        if (freeLookCamera != null)
        {
            var followComponent = freeLookCamera.GetComponent<CinemachineThirdPersonFollow>();
            if (followComponent != null)
            {
                currentDistance = followComponent.CameraDistance;
            }
        }

        if(jumpAction != null)
        {
            jumpAction.started += OnJump;
            jumpAction.canceled += OnJump;
        }

    }

    public void OnAim(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Started)
        {
            targetingState = true;
            aim.Priority = aimCameraOrder;
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            targetingState = false;
            aim.Priority = 0;
            animator.SetFloat(xHash, 0);
        }
    }

    void Update()
    {
        UpdateFreeLookMovement();
        UpdateZoom(); // [추가] 매 프레임 줌 상태 업데이트
    }
}