using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float runSpeed = 2;

    [SerializeField] private LayerMask aimLayer;
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction runAction;
    [SerializeField] private Transform quad;

    private Vector3 lookingDirection = Vector3.zero;
    private float currentSpeed;

    private Vector3 movementDirection;
    private CharacterController controller;
    private float moveSpeed = 0;
    private bool isRunning = false;
    [SerializeField] Animator animator;
    // 중력값을 적용하기 위해 추가한 변수
    private float verticalVelocity = 0;
    [SerializeField] private float gravityScale = 9.8f;

    void Awake()
    {

        currentSpeed = walkSpeed;
        // 상하좌우 키를 등록할 때는 세가지 델리게이트에 모두 등록해줘야 합니다.
        if (moveAction != null)
        {
            moveAction.started += OnMoveAxis;
            moveAction.performed += OnMoveAxis;
            moveAction.canceled += OnMoveAxis;
        }
        if (runAction != null)
        {
            runAction.started += OnRun;
            runAction.performed += OnRun;
            runAction.canceled += OnRun;
        }
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }
    void OnEnable()
    {
        if (moveAction != null) moveAction.Enable();
        if (runAction != null) runAction.Enable();
    }
    void OnDisable()
    {
        if (moveAction != null) moveAction.Disable();
        if (runAction != null) runAction.Disable();
    }

    public void OnMoveAxis(InputAction.CallbackContext context)
    {
        Vector2 moveInput = context.ReadValue<Vector2>();
        movementDirection = new Vector3(moveInput.x, 0, moveInput.y);
    }
    // 키가 눌려진 상태라면 달리는 애니메이션 나오도록 처리합니다.
    public void OnRun(InputAction.CallbackContext context)
    {
        isRunning = true;
        if (context.phase == InputActionPhase.Canceled)
            isRunning = false;

        currentSpeed = walkSpeed;
        if (isRunning)
            currentSpeed = runSpeed;

        animator.SetBool("IsRunning", isRunning);
    }
    // 중력값 적용
    private void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -0.5f;
        }

        verticalVelocity -= gravityScale * Time.deltaTime;
        movementDirection.y = verticalVelocity;
    }
    private void ApplyMovement()
    {
        ApplyGravity();
        // 입력받은 값이 있을 때 이동처리 하는 코드
        if (movementDirection.magnitude > 0)
            controller.Move(movementDirection * Time.deltaTime * currentSpeed);

    }

    private void AimTowardMouse()
    {
        // 현재 마우스 스크린 좌표에서 레이를 발사합니다.
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out var hit, Mathf.Infinity))
        {
            // 레이와 충돌된 지점을 바라보도록 처리합니다.
            lookingDirection = hit.point - transform.position;
            lookingDirection.y = 0;
            lookingDirection.Normalize();
            transform.forward = lookingDirection;

            // 히트된 지점을 UI가 가리키도록 위치를 변경합니다.
            if (quad) quad.position = Camera.main.WorldToScreenPoint(hit.point);

        }
    }

    private void AnimatorControllers()
    {
        float x = Vector3.Dot(movementDirection, transform.right);
        float z = Vector3.Dot(movementDirection, transform.forward);
        // animator.SetFloat("X", 0.1);
        animator.SetFloat("X", x, 0.01f, Time.deltaTime);
        animator.SetFloat("Z", z, 0.01f, Time.deltaTime);


    }

    // Update is called once per frame
    void Update()
    {
        ApplyMovement();
        AimTowardMouse();
        AnimatorControllers();
    }
}
