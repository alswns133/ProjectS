using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectS.Players
{
    /// <summary>
    /// 입력을 '의도'로 번역하는 단일 창구. New Input System의 InputAction을 읽어
    /// 다른 컴포넌트엔 의미 있는 값/이벤트(MoveInput, JumpHeld, Attacked 등)만 노출한다.
    /// 입력 '소스'와 게임 '로직'을 분리 → 나중에 AI·네트워크가 같은 표면을 흉내 낼 수 있다.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        // 연속 입력: 매 프레임 값이 필요 → 폴링(프로퍼티로 실시간 read)
        [Header("연속 입력")]
        [SerializeField] private InputAction moveAction;
        [SerializeField] private InputAction zoomAction;

        // 이산 입력: 누른 '순간'이 중요 → 이벤트(콜백)
        [Header("단발 입력")]
        [SerializeField] private InputAction jumpAction;
        [SerializeField] private InputAction skillAction;
        [SerializeField] private InputAction attackAction;
        [SerializeField] private InputAction strongAttackAction;   // 강공격(기본 바인딩: 우클릭)
        [SerializeField] private InputAction rollAction;   // 구르기(기본 바인딩: Left Shift)
        [SerializeField] private InputAction cursorToggleAction;   // 커서 잠금 토글(기본 바인딩: Left Alt)

        // NPC 상호작용(대화·퀘스트 수락/반납). 새로 추가한 필드라 인스펙터에 값이 없을 때를 대비해
        // 기본 바인딩(F)을 코드에서 준다. 인스펙터에서 다른 키로 바꿔도 된다.
        [SerializeField] private InputAction interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/f");

        // 달리기 판정: 이동 입력이 끊겼다가 이 시간(초) 안에 다시 시작되면 더블탭으로 본다.
        [Header("달리기")]
        [SerializeField] private float doubleTapWindow = 0.3f;

        // 연속 입력은 프로퍼티로 노출. 호출 시점에 즉시 읽으므로 Update 실행 순서에 안 휘둘림.
        public Vector2 MoveInput => moveAction.ReadValue<Vector2>();
        public float ZoomDelta => zoomAction.ReadValue<Vector2>().y;

        /// <summary>
        /// 이동 키 더블탭으로 켜지는 달리기 여부. 이동 입력을 완전히 떼면 해제되고,
        /// 다시 누르면 걷기부터 시작한다. 판정은 여기(입력 해석)에서 끝내고,
        /// 속도·애니메이션 반영은 수신측(PlayerFreeState)이 담당한다.
        /// </summary>
        public bool IsRunning { get; private set; }

        // 직전 이동 시작 시각. 두 번의 '시작' 간격으로 더블탭을 판정한다.
        // (시작-시작 간격 기준이라, 오래 걷다 떼고 바로 다시 눌러도 달리기로 오인하지 않는다.)
        private float lastMoveStartTime = float.NegativeInfinity;

        public bool AttackHeld => attackAction.IsPressed();   // 지금 공격 버튼이 눌려있나
        public bool JumpHeld => jumpAction.IsPressed();       // 지금 점프 버튼이 눌려있나(꾹 누르면 연속 점프용)
        public bool RollHeld => rollAction.IsPressed();       // 지금 회피 버튼이 눌려있나(꾹 누르면 연속 회피용)

        // 이산 입력은 이벤트로 노출. 구독자(Player·CameraRig)는 입력 출처를 몰라도 됨.
        // 점프는 '꾹 누르면 연속 점프' 설계라 이벤트가 아닌 JumpHeld 폴링으로 처리한다.
        public event Action Attacked;
        public event Action StrongAttacked;      // 우클릭 강공격. 쿨타임 판정은 수신측(PlayerCombat)
        public event Action<int> SkillPressed;   // 인자 = 눌린 스킬 번호
        public event Action CursorTogglePressed; // 커서 잠금 토글. 잠금/해제 상태 소유는 수신측(Player)

        /// <summary>
        /// NPC 상호작용 키를 누른 순간 발행된다. 근접 판정·대상 선택은 수신측(예: QuestGiver)이 정한다.
        /// 입력 출처를 몰라도 되도록 이벤트로만 노출한다(입력 경계).
        /// </summary>
        public event Action Interacted;

        // 구르기는 점프와 같은 '꾹 누르면 연속 발동' 설계라 이벤트가 아닌 RollHeld 폴링으로 처리한다.
        // 이벤트(started) 방식이면 '누른 순간' 1회뿐이라 연속 회피를 만들 수 없다.

        // 컷신(보스 등장 등)으로 게임플레이 입력을 통째로 멈춘 상태.
        // true면 모든 InputAction을 Disable한다 → MoveInput=0, 각종 Held=false가 되고
        // 이산 입력 이벤트(Attacked/SkillPressed 등)의 콜백도 발화하지 않아 폴링·이벤트 양쪽이 함께 막힌다.
        private bool inputSuspended;

        /// <summary>컷신 등으로 게임플레이 입력이 통째로 멈춰 있는지.</summary>
        public bool InputSuspended => inputSuspended;

        /// <summary>
        /// 게임플레이 입력을 통째로 멈추거나 되살린다. 보스 등장 연출처럼 모든 조작을 막아야 하는 동안
        /// <see cref="Player.BeginCutscene"/>가 켜고 <see cref="Player.EndCutscene"/>가 끈다.
        /// 멈추면 이동/줌 폴링값이 0이 되고 이산 입력 콜백도 발화하지 않으므로, 개별 게이트를 일일이
        /// 손대지 않아도 이동·점프·공격·스킬·회피·상호작용·커서 토글이 한 번에 차단된다.
        /// </summary>
        /// <param name="suspended">true=모든 입력 차단, false=평소대로 복구.</param>
        public void SetInputSuspended(bool suspended)
        {
            if (inputSuspended == suspended) return;

            inputSuspended = suspended;

            if (suspended)
            {
                DisableActions();
                IsRunning = false;   // 멈춘 순간 달리기(더블탭) 상태도 초기화 — 복귀 시 걷기부터 시작
            }
            else
            {
                EnableActions();
            }
        }

        // InputAction은 Enable해야 입력을 받기 시작한다(에셋이 아닌 직접 필드 방식).
        // OnEnable과 입력 재개(SetInputSuspended)에서 함께 쓰려고 한곳에 모았다.
        private void EnableActions()
        {
            moveAction.Enable();
            zoomAction.Enable();
            jumpAction.Enable();
            skillAction.Enable();
            attackAction.Enable();
            strongAttackAction.Enable();
            rollAction.Enable();
            cursorToggleAction.Enable();
            interactAction.Enable();
        }

        private void DisableActions()
        {
            moveAction.Disable();
            zoomAction.Disable();
            jumpAction.Disable();
            skillAction.Disable();
            attackAction.Disable();
            strongAttackAction.Disable();
            rollAction.Disable();
            cursorToggleAction.Disable();
            interactAction.Disable();
        }

        private void OnEnable()
        {
            // 컷신으로 입력이 멈춘 채 컴포넌트가 재활성되면 그 상태를 존중한다(멈춘 채로 유지).
            if (!inputSuspended) EnableActions();

            moveAction.started += OnMoveStarted;
            moveAction.canceled += OnMoveCanceled;
            skillAction.started += OnSkill;
            attackAction.started += OnAttack;
            strongAttackAction.started += OnStrongAttack;
            cursorToggleAction.started += OnCursorToggle;
            interactAction.started += OnInteract;
        }

        // 구독/활성화의 정확한 짝. OnEnable에서 +=/Enable 했으면 여기서 -=/Disable.
        // 빠뜨리면 중복 구독·입력 누수가 쌓인다(원본의 누락 버그를 여기서 교정).
        private void OnDisable()
        {
            moveAction.started -= OnMoveStarted;
            moveAction.canceled -= OnMoveCanceled;
            skillAction.started -= OnSkill;
            attackAction.started -= OnAttack;
            strongAttackAction.started -= OnStrongAttack;
            cursorToggleAction.started -= OnCursorToggle;
            interactAction.started -= OnInteract;

            DisableActions();
        }

        private void OnSkill(InputAction.CallbackContext ctx)
        {
            // 눌린 키 이름("1","2"...)을 숫자로 파싱해 스킬 번호로 전달.
            // 원본의 switch(case "1".."5")를 한 줄로 대체.
            // 주의: 여기선 번호를 거르지 않는다 → 유효 범위 검증은 수신측(PlayerAnimation.PlaySkill).
            if (int.TryParse(ctx.control.name, out int n))
                SkillPressed?.Invoke(n);
        }

        private void OnAttack(InputAction.CallbackContext _)
        {
            Attacked?.Invoke();
        }

        private void OnStrongAttack(InputAction.CallbackContext _)
        {
            StrongAttacked?.Invoke();
        }

        private void OnCursorToggle(InputAction.CallbackContext _)
        {
            CursorTogglePressed?.Invoke();
        }

        private void OnInteract(InputAction.CallbackContext _)
        {
            Interacted?.Invoke();
        }

        // started는 이동 입력이 '없음 → 있음'으로 바뀌는 순간만 발화한다.
        // WASD 콤포지트라 걷는 도중 다른 방향 키를 추가로 눌러도 재발화하지 않으므로
        // 대각선 입력이 더블탭으로 오인될 걱정은 없다.
        private void OnMoveStarted(InputAction.CallbackContext _)
        {
            if (Time.time - lastMoveStartTime <= doubleTapWindow)
                IsRunning = true;

            lastMoveStartTime = Time.time;
        }

        private void OnMoveCanceled(InputAction.CallbackContext _)
        {
            IsRunning = false;
        }
    }
}
