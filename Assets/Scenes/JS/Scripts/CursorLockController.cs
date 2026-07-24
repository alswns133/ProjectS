using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectS.Movement
{
    /// <summary>
    /// 플레이 중 마우스 커서를 게임 화면에 가둔다(잠금 + 숨김).
    /// Alt를 <b>누르고 있는 동안</b>만 커서가 나타나고 시점 조작이 멈춘다(UI 클릭용). 떼면 원래대로 복귀.
    ///
    /// 원래 Player.Start()/OnCursorToggle이 하던 일인데, 참조 전용 구조에서는 Player 컴포넌트가
    /// 비활성이라 그 경로가 돌지 않는다(커서가 계속 남아 화면 밖이 클릭되는 문제).
    ///
    /// PlayerInputHandler의 CursorTogglePressed를 쓰지 않는 이유:
    /// 그 이벤트는 '누른 순간'만 알려줘 떼는 시점을 알 수 없다 → 홀드 방식을 만들 수 없다.
    /// 그래서 자체 InputAction을 들고 IsPressed()로 눌림 상태를 직접 읽는다(민준님 스크립트는 수정하지 않음).
    /// </summary>
    public class CursorLockController : MonoBehaviour
    {
        // 기본 바인딩 = 왼쪽 Alt. 인스펙터에서 바인딩을 추가/변경할 수 있다.
        [SerializeField]
        private InputAction cursorHoldAction =
            new InputAction("CursorHold", InputActionType.Button, "<Keyboard>/leftAlt");

        // 플레이 시작과 동시에 잠글지 여부. 에디터에서 UI를 자주 만질 땐 꺼두고 쓸 수 있다.
        [SerializeField] private bool lockOnStart = true;

        // 커서를 푸는 동안 잠시 꺼둘 시점 조작 컴포넌트(Cinemachine 입력 컨트롤러나 카메라 룩 스크립트).
        // 여기에 넣어두면 Alt를 누르는 동안 시점이 돌지 않아 UI를 편히 클릭할 수 있다.
        [SerializeField] private Behaviour[] lookControllers;

        // 직전 프레임의 Alt 눌림 상태. 상태가 '바뀌는 순간'에만 적용해,
        // 에디터에서 ESC로 커서를 푼 뒤 매 프레임 다시 잠겨버리는 것을 막는다.
        private bool wasHolding;

        private void OnEnable()
        {
            cursorHoldAction.Enable();
        }

        private void OnDisable()
        {
            cursorHoldAction.Disable();

            // 플레이 종료·비활성 시 커서를 돌려놓지 않으면 에디터에서 커서가 잠긴 채로 남는다.
            SetLocked(false);
            SetLookEnabled(true);
        }

        private void Start()
        {
            if (lockOnStart)
            {
                SetLocked(true);
                SetLookEnabled(true);
            }
        }

        private void Update()
        {
            bool holding = cursorHoldAction.IsPressed();
            if (holding == wasHolding) return;   // 변화가 있을 때만 적용(ESC 등 외부 조작과 싸우지 않음)

            wasHolding = holding;

            // 누르는 동안: 커서 표시 + 시점 조작 정지 / 떼면: 커서 숨김 + 시점 조작 복귀
            SetLocked(!holding);
            SetLookEnabled(!holding);
        }

        private void SetLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void SetLookEnabled(bool value)
        {
            if (lookControllers == null) return;

            foreach (Behaviour look in lookControllers)
            {
                if (look != null) look.enabled = value;
            }
        }
    }
}
