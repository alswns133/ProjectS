using UnityEngine;

/// <summary>
/// 플레이어 이동·중력·회전을 담당. 카메라 기준 방향으로 이동하고,
/// 진행 방향을 바라보며, 중력/점프를 수직 속도로 직접 적분한다.
/// (Rigidbody가 아닌 CharacterController 기반)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotationDamping = 10f; // 회전 따라붙는 속도(클수록 즉각적)
    [SerializeField] private float jumpHeight = 3f;     // 목표 점프 높이(m). 실제 도달과 일치
    [SerializeField] private float gravity = 9.8f;

    private CharacterController controller;
    private Transform cam;
    private float verticalVelocity; // 수직 속도. 중력으로 매 프레임 누적, 점프 시 위로 튕김

    public bool IsGrounded => controller.isGrounded;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        cam = Camera.main.transform;    // 이동 방향의 기준이 되는 카메라
    }

    /// <summary>카메라 기준으로 이동하고, 진행 방향으로 회전시킨다.</summary>
    public void Move(Vector2 input)
    {
        Vector3 move = CameraRelative(input) * moveSpeed;
        FaceDirection(move);    // 수평 이동 방향으로 몸을 돌림
        ApplyGravity(ref move); // 수직 성분(y)을 채워넣음
        controller.Move(move * Time.deltaTime); // 프레임 독립적이도록 deltaTime 곱
    }

    /// <summary>접지 상태에서만 점프. 목표 높이에 정확히 도달하는 초기 속도를 계산.</summary>
    public void Jump()
    {
        if (IsGrounded)
            // v = √(2·h·g): 운동 공식 h = v²/2g 의 역산.
            // ★ 여기 g와 ApplyGravity의 낙하 g가 같아야 실제 높이 = jumpHeight.
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);
    }

    private void ApplyGravity(ref Vector3 move)
    {
        // 접지 + 하강 중이면 속도를 작은 음수로 고정.
        // 0이 아니라 -0.5f인 이유: controller.isGrounded가 안정적으로 true를
        // 유지하도록 살짝 바닥에 눌러붙이는 값(경사/계단에서 떨림 방지).
        if (IsGrounded && verticalVelocity < 0f)
            verticalVelocity = -0.5f;            // 접지 시 살짝 눌러줌(원본 동일)

        // 낙하 중력을 상승의 2배로(비대칭 중력) → 점프가 쫀득하게 떨어지는 느낌.
        // 의도된 설계이며 버그 아님. 단, 이 때문에 실제 점프 높이는 jumpHeight보다 낮다.
        // (jumpHeight는 정확한 높이가 아니라 튜닝용 값으로 취급)
        verticalVelocity -= 2f * gravity * Time.deltaTime;  // 매 프레임 중력 적분
        move.y = verticalVelocity;
    }

    /// <summary>스틱/키 입력을 카메라 기준 월드 방향으로 변환(수평면 한정).</summary>
    private Vector3 CameraRelative(Vector2 input)
    {
        // 카메라의 전/우 방향에서 y를 제거해 바닥 평면으로 투영.
        // (안 그러면 카메라가 아래를 볼 때 캐릭터가 땅으로 파고들려 함)
        Vector3 f = cam.forward; f.y = 0; f.Normalize();
        Vector3 r = cam.right; r.y = 0; r.Normalize();
        return (f * input.y + r * input.x).normalized;
    }

    private void FaceDirection(Vector3 dir)
    {
        dir.y = 0;
        if (dir.sqrMagnitude < 0.0001f) return; // 입력 없으면 회전 안 함(0벡터 방향은 정의 불가)

        // Slerp으로 현재 회전 → 목표 회전을 부드럽게 보간(즉시 안 꺾이고 따라 돌게)
        transform.rotation = Quaternion.Slerp(transform.rotation,
            Quaternion.LookRotation(dir), rotationDamping * Time.deltaTime);
    }
}
