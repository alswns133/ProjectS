using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 카메라 줌 제어. 마우스 휠 입력(ZoomDelta)으로 Cinemachine의
/// 카메라-타겟 거리(CameraDistance)를 조절한다.
/// ★ 이 스크립트가 붙은 오브젝트에 CinemachineCamera + ThirdPersonFollow(Body)가
///   있어야 한다. 없으면 follow가 null이 되어 줌이 조용히 비활성된다.
/// (플레이어가 아닌 카메라의 책임이라 Player 컴포넌트 패밀리와 분리)
/// </summary>
public class CameraRig : MonoBehaviour
{
    // 입력은 플레이어 오브젝트에 있으므로 인스펙터로 연결(다른 오브젝트라 GetComponent 불가).
    [SerializeField] private PlayerInputHandler input;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 50f;
    [SerializeField] private float minDistance = 2f;   // 줌인 한계(너무 붙지 않게)
    [SerializeField] private float maxDistance = 10f;  // 줌아웃 한계(너무 멀어지지 않게)

    // 실제로 거리를 들고 있는 Cinemachine Body 컴포넌트. 변하지 않으므로 1회 캐싱.
    private CinemachineThirdPersonFollow follow;
    private float distance; // 현재 줌 거리. follow.CameraDistance와 동기화해 사용

    private void Awake()
    {
        // 같은 오브젝트의 ThirdPersonFollow를 찾아 캐싱(매 프레임 GetComponent 회피).
        follow = GetComponent<CinemachineThirdPersonFollow>();

        // 시작점을 '현재 카메라의 실제 거리'로 맞춤. 안 하면 distance가 0에서 출발해
        // 첫 휠 입력에 카메라가 minDistance로 확 튀는 버그가 난다.
        if (follow == null)
            Debug.LogWarning($"{name}: ThirdPersonFollow가 없어 줌이 비활성됩니다.", this);
        else
            distance = follow.CameraDistance;
    }

    private void Update()
    {
        if (follow == null) return;   // Body가 없으면 줌 비활성(안전 가드)

        float d = input.ZoomDelta;
        if (Mathf.Abs(d) < 0.0001f) return;   // 입력 없으면 매 프레임 연산 건너뜀

        // 휠 방향으로 거리 가감 후 min/max로 제한. 휠 위로=거리 감소(줌인).
        distance = Mathf.Clamp(distance - d * zoomSpeed * Time.deltaTime, minDistance, maxDistance);
        follow.CameraDistance = distance;
    }
}
