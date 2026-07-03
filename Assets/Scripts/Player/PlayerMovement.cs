using UnityEngine;

/// <summary>
/// CharacterController 기반 플레이어 이동 담당.
/// 카메라 기준 수평 이동, 회전, 중력, 점프 가능 여부를 한곳에서 관리한다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationDamping = 10f;
    [SerializeField] private float jumpHeight = 3f;
    [SerializeField] private float gravity = 9.8f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float checkRadius = 0.25f;
    [SerializeField] private LayerMask floorLayer;

    private CharacterController controller;
    private Transform cam;

    // CharacterController는 Rigidbody 속도를 들고 있지 않으므로 수직 속도를 직접 누적한다.
    private float verticalVelocity;

    public bool IsGrounded { get; private set; }

    // 점프 직후 몇 프레임 동안 접지 체크가 true로 남을 수 있어 verticalVelocity도 함께 본다.
    public bool CanJump => IsGrounded && verticalVelocity <= 0f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        TryCacheMainCamera();
    }

    public void Move(Vector2 input)
    {
        // 이동이 잠긴 상태에서도 PlayerFreeState가 zero 입력으로 호출한다.
        // 그래서 중력/접지는 계속 처리되고 수평 이동만 멈춘다.
        Vector3 move = CameraRelative(input) * moveSpeed;
        FaceDirection(move);
        ApplyGravity(ref move);
        controller.Move(move * Time.deltaTime);
    }

    public bool Jump()
    {
        if (!CanJump) return false;

        // v = sqrt(2gh). 이후 ApplyGravity에서 낙하 가속을 더 크게 주기 때문에
        // jumpHeight는 정확한 높이라기보다 튜닝 기준값에 가깝다.
        verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);
        return true;
    }

    public void SnapToCameraForward()
    {
        // 공격/스킬 시작 순간에는 부드러운 회전보다 카메라 방향 정렬이 우선이다.
        if (!TryCacheMainCamera()) return;

        Vector3 fwd = cam.forward;
        fwd.y = 0;

        if (fwd.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(fwd);
    }

    private void FixedUpdate()
    {
        if (groundCheck == null) return;

        // CharacterController.isGrounded 대신 별도 체크 포인트를 쓴다.
        // 경사/계단/모델 피벗 차이를 인스펙터에서 조절하기 쉽다.
        IsGrounded = Physics.CheckSphere(
            groundCheck.position,
            checkRadius,
            floorLayer,
            QueryTriggerInteraction.Ignore);
    }

    private void ApplyGravity(ref Vector3 move)
    {
        // 접지 중에는 작은 음수로 눌러 바닥과의 접촉을 안정화한다.
        if (IsGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        // 상승보다 낙하를 빠르게 해서 조작감이 늘어지지 않게 한다.
        verticalVelocity -= 2f * gravity * Time.deltaTime;
        move.y = verticalVelocity;
    }

    private Vector3 CameraRelative(Vector2 input)
    {
        // 씬 로드 순서상 카메라가 늦게 잡힐 수 있으므로 사용할 때도 한 번 더 캐싱한다.
        if (!TryCacheMainCamera()) return Vector3.zero;

        Vector3 f = cam.forward;
        f.y = 0;
        f.Normalize();

        Vector3 r = cam.right;
        r.y = 0;
        r.Normalize();

        return (f * input.y + r * input.x).normalized;
    }

    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return;

        // 이동 중 회전은 즉시 꺾지 않고 보간해 자연스럽게 따라가게 한다.
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(dir),
            rotationDamping * Time.deltaTime);
    }

    private bool TryCacheMainCamera()
    {
        if (cam != null) return true;
        if (Camera.main == null) return false;

        cam = Camera.main.transform;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, checkRadius);
    }
}
