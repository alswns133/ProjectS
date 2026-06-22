using UnityEngine;
using UnityEngine.InputSystem;


public class FreeLookMovement : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float runSpeed = 2;
    [SerializeField] private InputAction moveAction;
    private Vector3 movementDirection;
    private Camera gameCamera;

    private CharacterController controller;
    private float currentSpeed = 3;

    private float verticalVelocity = 0;
    private float gravityScale = 9.8f;

    [SerializeField] private float rotationDamping = 10;
    private Animator animator;
    // 사용하는 방법의 차이
    private readonly int xHash = Animator.StringToHash("X");
    private readonly int zHash = Animator.StringToHash("Z");
    private bool targetingState = false;

    [SerializeField] float sensitivity = 0.1f;

    [SerializeField]
    private float rotationSpeed = 180;
    [SerializeField]
    private float dragDeadZone = 0.1f;

    public void SetTargetingState(bool targetingState)
    {
        this.targetingState = targetingState;
        if (!targetingState)
            animator.SetFloat(xHash, 0);
    }

    // Hash 알고리즘 : 입력받은 값을 알고리즘으로 분리해서 고유한 식별값을 만들어내는 과정

    void Awake()
    {
        // 현재 마우스의 위치를 받음
        

        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
        gameCamera = Camera.main;
        // 상하좌우 키를 등록할 때는 세가지 델리게이트에 모두 등록해줘야 합니다.
        if (moveAction != null)
        {
            moveAction.started += OnMoveAxis;
            moveAction.performed += OnMoveAxis;
            moveAction.canceled += OnMoveAxis;
        }
    }


    void OnEnable()
    {
        if (moveAction != null) moveAction.Enable();

    }
    void OnDisable()
    {
        if (moveAction != null) moveAction.Disable();

    }

    // 키가 처음 입력되거나, 키가 업데이트 될 때 한 번 호출되는 메서드
    // 카메라의 방향성에 따라서 캐릭터의 이동이 발생되어야 함
    public void OnMoveAxis(InputAction.CallbackContext context)
    {
        // Vector2 moveInput = context.ReadValue<Vector2>();
    }

    void MoveAxis(Vector2 moveInput)
    {

        // 카메라의 전방 벡터
        Vector3 forward = gameCamera.transform.forward;
        // 카메라의 우측 벡터
        Vector3 right = gameCamera.transform.right;

        // 높이차가 발생할 수 있기에 y값을 0으로 ( 평면상에 있다고 가정하기 위함 )
        forward.y = 0;
        right.y = 0;
        // 길이가 1인 벡터로 변경
        forward.Normalize();
        right.Normalize();

        // 카메라의 전방 벡터와 우측 벡터를 기준으로 이동 방향을 구함
        movementDirection = (forward * moveInput.y + right * moveInput.x).normalized;
    }

    private void FaceMovementDirection(Vector3 movementDirection, float delta)
    {
        movementDirection.y = 0;
        if (movementDirection.magnitude > 0)
        {
            transform.rotation = Quaternion.Lerp(transform.rotation,
                                                 Quaternion.LookRotation(movementDirection.normalized),
                                                 rotationDamping * delta);
        }
    }


    private void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            verticalVelocity = -0.5f;
        }

        verticalVelocity -= gravityScale * Time.deltaTime;
        movementDirection.y = verticalVelocity;
    }

    public void RotateByMouseDrag()
    {
        // 현재 마우스가 이동한 델타 값을 구함
        // 이 방식으로 해야 커서가 왼쪽 측면으로 이동되어 있어도 가해지는 힘을 얻어올 수 있어 카메라의 회전이 가능
        Vector2 delta = Mouse.current.delta.ReadValue();
        if (Mathf.Abs(delta.x) < dragDeadZone)
            return;

        transform.Rotate(Vector3.up, delta.x * rotationSpeed * Time.deltaTime);
    }

    void Update()
    {
        Vector2 moveInput = moveAction.ReadValue<Vector2>();

        if (!targetingState)
        {
            // 이동할 방향을 바라보도록 처리
            FaceMovementDirection(movementDirection, Time.deltaTime);

            // 아래쪽의 애니메이션 실행하는 코드는 프리룩일경우에만 진행
            // 현재 값에서 서서히 목표로 하는 값으로 업데이트가 되도록 처리
            Vector3 direction = movementDirection;
            direction.y = 0;
            // direction.Normalize();
            if (direction.magnitude > 0.0001f)
                animator.SetFloat(zHash, 1, 0.1f, Time.deltaTime);
            else
                animator.SetFloat(zHash, 0, 0.1f, Time.deltaTime);
        }
        else
        {
            // 현재 카메라의 forward 방향을 바라보도록 처리
            Vector3 cameraDirection = gameCamera.transform.forward;
            FaceMovementDirection(cameraDirection, Time.deltaTime);

            // 입력한 값에 맞는 애니메이션이 출력되도록 처리
            animator.SetFloat(zHash, moveInput.y, 0.1f, Time.deltaTime);
            animator.SetFloat(xHash, moveInput.x, 0.1f, Time.deltaTime);
            RotateByMouseDrag();

        }



        // 카메라의 움직임에 따라서 키 변경이 발생되어야 하기 때문에 업데이트 상에서 입력된 값을 받아서 처리
        MoveAxis(moveInput);

        // 중력값 적용
        ApplyGravity();

        // 캐릭터를 카메라 방향을 기준으로 이동처리
        controller.Move(movementDirection * Time.deltaTime * currentSpeed);
    }
}
