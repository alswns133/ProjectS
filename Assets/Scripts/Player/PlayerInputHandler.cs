using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 입력을 '의도'로 번역하는 단일 창구. New Input System의 InputAction을 읽어
/// 다른 컴포넌트엔 의미 있는 값/이벤트(MoveInput, JumpHeld, Attacked 등)만 노출한다.
/// 입력 '소스'와 게임 '로직'을 분리 → 나중에 AI·네트워크가 같은 표면을 흉내 낼 수 있다.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    // 연속 입력: 매 프레임 값이 필요 → 폴링(프로퍼티로 실시간 read)
    [Header("Continuous")]
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction zoomAction;

    // 이산 입력: 누른 '순간'이 중요 → 이벤트(콜백)
    [Header("Discrete")]
    [SerializeField] private InputAction jumpAction;
    [SerializeField] private InputAction skillAction;
    [SerializeField] private InputAction attackAction;

    // 연속 입력은 프로퍼티로 노출. 호출 시점에 즉시 읽으므로 Update 실행 순서에 안 휘둘림.
    public Vector2 MoveInput => moveAction.ReadValue<Vector2>();
    public float ZoomDelta => zoomAction.ReadValue<Vector2>().y;

    public bool AttackHeld => attackAction.IsPressed();   // 지금 공격 버튼이 눌려있나
    public bool JumpHeld => jumpAction.IsPressed();       // 지금 점프 버튼이 눌려있나(꾹 누르면 연속 점프용)

    // 이산 입력은 이벤트로 노출. 구독자(Player·CameraRig)는 입력 출처를 몰라도 됨.
    // 점프는 '꾹 누르면 연속 점프' 설계라 이벤트가 아닌 JumpHeld 폴링으로 처리한다.
    public event Action Attacked;
    public event Action<int> SkillPressed;   // 인자 = 눌린 스킬 번호

    private void OnEnable()
    {
        // InputAction은 Enable해야 입력을 받기 시작한다. (에셋이 아닌 직접 필드 방식)
        moveAction.Enable();
        zoomAction.Enable();
        jumpAction.Enable();
        skillAction.Enable();
        attackAction.Enable();

        skillAction.started += OnSkill;
        attackAction.started += OnAttack;
    }

    // 구독/활성화의 정확한 짝. OnEnable에서 +=/Enable 했으면 여기서 -=/Disable.
    // 빠뜨리면 중복 구독·입력 누수가 쌓인다(원본의 누락 버그를 여기서 교정).
    private void OnDisable()
    {
        skillAction.started -= OnSkill;
        attackAction.started -= OnAttack;

        moveAction.Disable();
        zoomAction.Disable();
        jumpAction.Disable();
        skillAction.Disable();
        attackAction.Disable();
    }

    private void OnSkill(InputAction.CallbackContext ctx)
    {
        // 눌린 키 이름("1","2"...)을 숫자로 파싱해 스킬 번호로 전달.
        // 원본의 switch(case "1".."5")를 한 줄로 대체.
        // 주의: 여기선 번호를 거르지 않는다 → 유효 범위 검증은 수신측(PlayerAnimation.PlaySkill).
        if (int.TryParse(ctx.control.name, out int n))
            SkillPressed?.Invoke(n);
    }

    private void OnAttack(InputAction.CallbackContext _) => Attacked?.Invoke();
}