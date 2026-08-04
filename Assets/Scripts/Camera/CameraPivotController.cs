using UnityEngine;
using UnityEngine.InputSystem;

// 현재 하이어라키 윈도우에 배치된 모든 게임오브젝트의 Update가 호출
// 그 이후 모든 게임 오브젝트의 LateUpdate가 호출
public class CameraPivotController : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.1f;
    [SerializeField] private float minPitch = -70;
    [SerializeField] private float maxPitch = 70;
    private float yaw;
    private float pitch;

    private Quaternion desiredWorldRotation;

    // true면 마우스 델타를 읽지 않는다. 스킬 연출(각성기 등)처럼 카메라가 코드로 제어되는 동안
    // 플레이어 조작이 섞이면 안 되는 구간에서 켠다. yaw/pitch를 그 상태로 얼려 두므로,
    // 연출이 끝나 평소 카메라로 돌아왔을 때 연출 시작 전 시점 그대로 이어진다
    // (막지 않으면 연출 중 마우스가 쌓은 회전이 복귀 순간 화면을 홱 돌려버린다).
    private bool inputLocked;

    void Start()
    {
        // 시작 시점의 회전값을 받음
        Vector3 euler = transform.rotation.eulerAngles;
        // 시작 시점의 x축 회전값과 y축 회전값을 받음
        yaw = NormalizeAngle(euler.y);
        pitch = NormalizeAngle(euler.x);
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;
        return angle;
    }


    void Update()
    {
        if (!inputLocked)
        {
            // 현재 마우스가 이동한 델타값
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * sensitivity;

            // 상하 회전을 min/maxPitch로 제한한다. 제한이 없으면 ±90도를 넘는 순간
            // 카메라가 뒤집혀(롤 반전) 화면이 반전된 것처럼 보인다.
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
        }

        desiredWorldRotation = Quaternion.Euler(pitch, yaw, 0f);

    }

    /// <summary>
    /// 마우스로 시점을 돌리는 조작을 막거나 푼다. 스킬 연출처럼 카메라를 코드/컷신이
    /// 통째로 가져가는 동안, 그 밑에서 대기 중인 평소 카메라가 마우스 입력을 계속 쌓지
    /// 않도록 막는다. 잠그는 동안 yaw/pitch는 그 순간 값에 얼어붙는다.
    /// </summary>
    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
    }

    // LateUpdate 메서드는 모든 Update 메서드가 호출된 이후 호출되는 메서드
    // Cinemachine Third Person Follow 를 사용하게 되면 대상이 되는 게임 오브젝트의 회전값을 참고해서 내부적으로 카메라를 회전
    // 캐릭터의 회전은 캐릭터 클래스의 Update에서 처리하고 있음
    // 캐릭터의 회전이 완료된 이후 카메라가 참고하고 있는 게임 오브젝트의 회전값을 변경해서 회전을 방지

    void LateUpdate()
    {
        transform.rotation = desiredWorldRotation;
    }
}
