using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using ProjectS.Core;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.UI
{
    /// <summary>
    /// 항상 보이는 채팅 창(로그 + 입력). 팝업 스택(UIManager)에 넣지 않고 상시 활성 오버레이로 둔다 —
    /// 로그가 늘 보여야 (1) 채팅이 닫혀 있는 사이 온 메시지를 놓치지 않고, (2) ESC가 채팅 때문에 막히지 않는다.
    /// <para>
    /// 판정 기준은 "입력창 포커스 여부"다:
    ///  - 포커스 O → 타이핑 중 → 게임 입력 억제(<see cref="SetGameplayInputSuspended"/>). ESC로 포커스 해제, Enter로 전송+해제.
    ///  - 포커스 X → 게임 조작 정상. Enter로 입력창 포커스(채팅 시작).
    /// raw 키보드를 읽는 다른 핫키들은 <see cref="UiTypingGuard"/>로 타이핑 중 함께 막힌다.
    /// </para>
    /// <para>
    /// 수신은 <see cref="ChatEvents.OnMessageReceived"/>를 직접 구독한다(별도 Presenter 없음).
    /// 상시 활성이라 언제 메시지가 와도 받아 로그에 찍는다.
    /// </para>
    /// </summary>
    public class ChatWindow : MonoBehaviour
    {
        [Header("View")]
        [SerializeField] private TMP_Text logText;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TMP_InputField input;

        [Header("로그")]
        [Tooltip("로그에 유지할 최대 줄 수. 넘으면 오래된 줄부터 버린다(TMP 정점 한계·메모리 누적 방지).")]
        [SerializeField] private int maxLines = 100;

        [Header("현재 입력 채널")]
        [SerializeField] private ChatChannel activeChannel = ChatChannel.General;

        // 최근 채팅 줄 버퍼. maxLines를 넘으면 오래된 줄부터 버리고 다시 조립한다(문자열·TMP 메시 무한 누적 방지).
        private readonly Queue<string> lines = new Queue<string>();

        // 직전 프레임의 포커스 상태. 포커스가 '바뀌는 순간'에만 게임 입력 억제를 토글하기 위해 들고 있는다.
        private bool wasFocused;

        // 전송으로 포커스를 푼 프레임. 그 '같은 Enter'가 아래 Update의 '열기'로 재활용돼 즉시 재포커스되는 것을 막는다.
        private int submitFrame = -1;

        private void Awake()
        {
            // onSubmit은 UnityEvent<string>. 넘어오는 문자열은 무시하고 SubmitMessage가 input.text를 직접 읽는다.
            if (input != null) input.onSubmit.AddListener(_ => SubmitMessage());
        }

        // 수신 구독은 활성/비활성과 짝을 맞춘다(상시 활성이라 사실상 항상 구독). Presenter를 따로 두지 않고 여기서 직접 받는다.
        private void OnEnable() => ChatEvents.OnMessageReceived += AppendLine;
        private void OnDisable() => ChatEvents.OnMessageReceived -= AppendLine;

        private void Update()
        {
            if (input == null) return;

            bool focused = input.isFocused;

            // 포커스가 바뀌는 순간에만 게임 입력 억제를 켜고/끈다.
            // (포커스 O = 타이핑 중이라 이동·공격 등 게임 입력 차단, 포커스 X = 평소대로 복구.)
            if (focused != wasFocused)
            {
                SetGameplayInputSuspended(focused);
                wasFocused = focused;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (focused)
            {
                // 타이핑 중 ESC → 포커스만 해제(게임 복귀). 창은 그대로 보인다. 전송(Enter)은 onSubmit이 담당.
                if (kb.escapeKey.wasPressedThisFrame) input.DeactivateInputField();
            }
            else
            {
                // 방금 전송으로 포커스를 푼 그 Enter는 '열기'로 재활용하지 않는다(같은 프레임 재포커스 방지).
                // 실행 순서상 onSubmit(전송·해제)이 이 Update보다 먼저 돌면, 해제된 그 프레임에 Enter가 아직
                // wasPressedThisFrame이라 여기서 곧바로 다시 포커스가 걸려 "포커스가 안 풀리는" 증상이 난다.
                if (Time.frameCount == submitFrame) return;

                // 포커스 아님 + Enter → 입력창 포커스(채팅 시작).
                // 이 Enter가 곧바로 빈 submit으로 새어도 SubmitMessage가 '유지'로 처리하므로 열자마자 닫히지 않는다.
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                    input.ActivateInputField();
            }
        }

        /// <summary>수신 메시지 한 줄을 로그에 추가한다(ChatEvents.OnMessageReceived 핸들러). 최대 <see cref="maxLines"/>줄만 유지한다.</summary>
        public void AppendLine(ChatMessage message)
        {
            lines.Enqueue($"<color=blue>{message.sender}</color>: {message.text}");
            while (lines.Count > maxLines) lines.Dequeue();

            if (logText != null) logText.text = string.Join("\n", lines);

            // 새 줄이 보이도록 맨 아래(최신)로 스크롤. 레이아웃 갱신 뒤 위치를 잡아야 정확하다.
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>
        /// Enter 전송. 내용이 있으면 보내고 <b>포커스를 해제</b>해 게임 조작으로 복귀한다.
        /// 빈 입력(입력창을 여는 그 Enter가 새어든 경우 포함)이면 전송을 건너뛰고 포커스만 유지한다.
        /// </summary>
        private void SubmitMessage()
        {
            if (input == null) return;

            string text = input.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                input.ActivateInputField();   // 빈 전송 → 포커스 유지(열자마자 닫힘 방지)
                return;
            }

            ChatEvents.FireSendRequested(activeChannel, text);
            input.text = string.Empty;
            submitFrame = Time.frameCount;  // 이 프레임의 Enter가 Update에서 재포커스로 재활용되지 않게 표시
            input.DeactivateInputField();   // 전송 후 포커스 해제 → 다음 프레임 Update가 게임 입력을 복구
        }

        /// <summary>
        /// 타이핑 동안 게임플레이 입력을 잠근다(이동·점프·공격·스킬·회피·상호작용·커서 토글 InputAction 일괄).
        /// 참조 단일 창구 규칙에 따라 PlayerManager.Instance.Player로 접근한다. 입력창(TMP_InputField)은
        /// EventSystem으로 동작해 이 억제와 무관하니 타이핑엔 안전하다.
        /// </summary>
        private void SetGameplayInputSuspended(bool suspended)
        {
            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null) player.Input.SetInputSuspended(suspended);
        }
    }
}
