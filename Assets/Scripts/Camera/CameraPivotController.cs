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
    // 잠그고 푸는 시점은 연출을 거는 쪽(PlayerCameraEffects)이 정한다 — 여기서 조건을 따져
    // 스스로 풀지 않는다. 그렇게 하면 "언제 조작이 돌아오는지" 예측할 수 없어진다.
    private bool inputLocked;

    // ── 연출용 궤도 회전(각성기 등에서 카메라를 한 바퀴 돌리는 연출) ──────────
    // 남은 회전량(도, 항상 양수). 0보다 크면 연출 중이다. yaw만 돌린다 — 실제 카메라 위치는
    // Cinemachine ThirdPersonFollow가 이 yaw를 기준으로 캐릭터 주위를 도는 방식이라,
    // 별도 카메라 오브젝트 없이 이 값만 시간에 따라 바꾸면 궤도 연출이 만들어진다.
    private float orbitRemaining;

    // 초당 회전 각도. 부호가 회전 방향(+면 시계 방향)이다.
    private float orbitSpeed;

    // 연출 중 상하 각도(부감/앙각)도 지정 값으로 옮길지. 높이·각도 조절은 이 pitch로 표현한다 —
    // ThirdPersonFollow는 pitch가 커질수록(위에서 내려다볼수록) 캐릭터를 도는 반경 위쪽에서
    // 비추게 되므로, 별도 "높이" 파라미터 없이 pitch만으로 "얼마나 높은 곳에서 비출지"가 정해진다.
    private bool orbitUsePitch;
    private float orbitPitch;

    // 연출 전 상하 각도. 연출이 끝나면 이 값으로 되돌린다.
    private float pitchBeforeOrbit;

    // 위 값을 이미 잡아 뒀는지. 연출을 여러 개 이어 붙일 때(회전이 끝나 멈춰 있는 동안
    // 다음 연출을 거는 경우) 매번 새로 찍으면 '연출 중 각도'가 원본으로 기록되어,
    // 마지막에 복귀할 때 스킬을 쓰기 전 각도가 아니라 연출 중 각도로 돌아가 버린다.
    // 회전 진행 여부(IsOrbiting)로 판단할 수 없는 이유: 회전은 끝났지만 연출은 계속되는
    // '멈춰서 대기하는' 구간이 있기 때문이다.
    private bool pitchCaptured;

    /// <summary>연출용 궤도 회전이 진행 중인지 여부.</summary>
    public bool IsOrbiting => orbitRemaining > 0f;

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
        // 마우스 포인터가 보이는 상태(커서 잠금 해제)면 마우스는 UI 조작용이므로
        // 카메라 회전에 반영하지 않는다. 연출(orbit)은 아래에서 계속 진행한다.
        if (!inputLocked && Cursor.lockState == CursorLockMode.Locked)
        {
            // 현재 마우스가 이동한 델타값
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * sensitivity;

            // 상하 회전을 min/maxPitch로 제한한다. 제한이 없으면 ±90도를 넘는 순간
            // 카메라가 뒤집혀(롤 반전) 화면이 반전된 것처럼 보인다.
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
        }

        if (IsOrbiting) UpdateOrbit();

        // 각도(pitch)는 회전과 분리해서 적용한다. 회전 안에 넣어 두면 Orbit Degrees가 0인 슬롯
        // (돌지 않고 각도만 바꾸는 연출)에서 각도가 통째로 무시된다.
        if (orbitUsePitch && pitchCaptured) pitch = SmoothTo(pitch, orbitPitch);

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

    /// <summary>
    /// 카메라를 지정 각도만큼 지정 시간에 걸쳐 돈다(각성기 시전 연출 등).
    /// 360을 주면 한 바퀴 돌아 시작 방향으로 정확히 돌아온다.
    /// 실제로 도는 것은 이 yaw 값이고, Cinemachine ThirdPersonFollow가 그 각도를 따라
    /// 캐릭터 주위를 도는 위치를 매 프레임 계산하므로 별도 카메라 없이 궤도 연출이 만들어진다.
    /// </summary>
    /// <param name="degrees">회전 각도. 음수면 반대 방향으로 돈다.</param>
    /// <param name="duration">도는 데 걸리는 시간(초).</param>
    /// <param name="usePitch">true면 도는 동안 상하 각도(높이·앙각)도 지정 값으로 옮긴다.</param>
    /// <param name="pitchAngle">연출 중 상하 각도(도). 양수면 위에서 내려다본다(더 높은 곳에서 비추는 느낌).</param>
    public void StartOrbit(float degrees, float duration, bool usePitch = false, float pitchAngle = 0f)
    {
        // 각도가 0이어도 그냥 빠져나오지 않는다 — 돌지 않고 상하 각도만 바꾸는 연출이 있다.
        // (여기서 리턴해 버리면 그런 슬롯은 pitch 설정까지 통째로 무시됐다.)
        orbitRemaining = Mathf.Abs(degrees);
        orbitSpeed = Mathf.Approximately(degrees, 0f) ? 0f : degrees / Mathf.Max(0.01f, duration);

        orbitUsePitch = usePitch;
        orbitPitch = Mathf.Clamp(pitchAngle, minPitch, maxPitch);

        // 연출을 처음 걸 때만 '원래 각도'를 기록한다. 연출이 끝날 때(StopOrbit)까지 유지되므로,
        // 중간에 회전이 멈췄다가 다음 연출로 이어져도 최종 복귀 지점은 스킬 시작 전 각도다.
        if (!pitchCaptured)
        {
            pitchBeforeOrbit = pitch;
            pitchCaptured = true;
        }
    }

    /// <summary>
    /// 진행 중인 궤도 회전을 즉시 멈춘다. 연출이 캔슬되거나 스킬이 끝났을 때 호출한다.
    /// yaw는 돌던 자리에서 그대로 멈추고(원위치로 튕기면 화면이 홱 돌아 더 어색하다),
    /// pitch를 바꿨다면 연출 전 각도로 그 프레임에 되돌린다.
    /// 입력 잠금은 여기서 건드리지 않는다 — 잠금/해제는 호출한 쪽이 관리한다.
    /// </summary>
    public void StopOrbit()
    {
        orbitRemaining = 0f;

        // 각도는 그 프레임에 바로 원위치시킨다. 서서히 되돌리면 그동안 플레이어의 마우스 조작과
        // 복귀 보간이 서로 밀어내며 카메라가 겉도는 느낌이 난다(조건을 붙여 취소를 판정하는 방식은
        // 미세한 마우스 흔들림까지 걸려 더 이상하게 동작했다).
        if (orbitUsePitch && pitchCaptured) pitch = pitchBeforeOrbit;

        pitchCaptured = false;
    }

    private void UpdateOrbit()
    {
        // 남은 각도보다 더 돌지 않게 잘라낸다 → 360도를 주면 정확히 제자리에서 끝난다.
        float step = Mathf.Min(Mathf.Abs(orbitSpeed) * Time.deltaTime, orbitRemaining);

        yaw += Mathf.Sign(orbitSpeed) * step;
        orbitRemaining -= step;
    }

    // 프레임 레이트에 영향받지 않는 지수 감쇠 보간.
    // Lerp에 Time.deltaTime을 그대로 넣으면 프레임이 높을수록 빨리 도달해 연출 속도가 기기마다 달라진다.
    private static float SmoothTo(float current, float target)
    {
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-5f * Time.deltaTime));
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
