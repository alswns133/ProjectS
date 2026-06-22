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

    public ThirdPersonCharacterController thirdpersoncharactercontroller;

    void Start()
    {
        thirdpersoncharactercontroller = GetComponentInParent<ThirdPersonCharacterController>();
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
        // 현재 마우스가 이동한 델타값
        Vector2 delta = Mouse.current.delta.ReadValue();
        yaw += delta.x * sensitivity;

        if (thirdpersoncharactercontroller.TargetingState)
        {
            pitch = 0f;
        }
        else
            pitch -= delta.y * sensitivity;

        desiredWorldRotation = Quaternion.Euler(pitch, yaw, 0f);

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
