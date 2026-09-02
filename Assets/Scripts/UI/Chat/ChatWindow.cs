using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        [SerializeField] private GameObject textPrefab;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TMP_InputField input;

        [Header("로그")]
        [Tooltip("로그에 유지할 최대 줄 수. 넘으면 오래된 줄부터 버린다(TMP 정점 한계·메모리 누적 방지).")]
        [SerializeField] private int maxLines = 100;

        [Header("현재 입력 채널")]
        [SerializeField] private ChatChannel activeChannel = ChatChannel.General;

        private readonly List<TMP_Text> tmps = new();

        // 직전 프레임의 포커스 상태. 포커스가 '바뀌는 순간'에만 게임 입력 억제를 토글하기 위해 들고 있는다.
        private bool wasFocused;

        // 전송으로 포커스를 푼 프레임. 그 '같은 Enter'가 아래 Update의 '열기'로 재활용돼 즉시 재포커스되는 것을 막는다.
        private int submitFrame = -1;

        private int writeIndex = 0; // 현재 쓰고 있는 TMP_Text 인덱스

        // <noparse> 보호를 조기 종료시키는 '닫는 태그'를 모두 지우기 위한 패턴.
        // TMP 태그는 대소문자를 가리지 않고 여백도 허용하므로("</NOPARSE>", "</noparse >" 등),
        // 단순 "</noparse>" 문자열 치환은 그 변형들로 우회된다. 이 정규식으로 모든 변형을 제거해야
        // noparse 안쪽이 항상 글자 그대로만 렌더된다(유저·타 플레이어가 친 <color> 등 태그 주입 차단).
        private static readonly Regex NoparseCloseTag =
            new Regex(@"<\s*/\s*noparse\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void Awake()
        {
            // onSubmit은 UnityEvent<string>. 넘어오는 문자열은 무시하고 SubmitMessage가 input.text를 직접 읽는다.
            if (input != null)
            {
                // 입력창에서 <color> 등을 쳤을 때 미리보기 글자가 실제로 서식(색 등)으로 바뀌지 않고
                // 글자 그대로 보이게 하려고 richText를 끈다.
                // ★ 알려진 부작용: TMP_InputField는 IME '조합 중'인 한글을 <u>...</u> 밑줄 태그로 그리는데,
                //   richText를 끄면 그 태그가 밑줄이 아니라 "<u>글자</u>" 문자 그대로 노출된다(조합 확정 전 매 글자).
                //   이건 폰트 문제(□ 두부)와 별개다 — <,u,>는 폰트가 이미 가진 ASCII라, 폰트를 채워도 안 사라진다.
                //   조합 중 태그 노출이 거슬리면 richText=true로 바꿔야 하고, 그 대신 입력창 미리보기 서식이 살아난다
                //   (둘은 같은 richText 플래그라 동시 만족 불가). 전송된 메시지의 태그 주입 방지는 AppendLine의
                //   <noparse>(+ 닫는 태그 변형 제거)가 담당하므로, 이 설정은 순수 입력창 미리보기 취향 문제다.
                input.textComponent.richText = false;
                input.onSubmit.AddListener(_ => SubmitMessage());
            }

            if (textPrefab != null && scroll != null)
            {
                for (int i = 0; i < maxLines; i++)
                {
                    GameObject obj = Instantiate(textPrefab);

                    if (obj.TryGetComponent(out TMP_Text text))
                    {
                        obj.name = $"Chat_Line_{i}";
                        tmps.Add(text);
                        obj.transform.SetParent(scroll.content, false);
                        obj.SetActive(false);
                    }
                    else
                    {
                        Debug.LogError($"{obj.name}: 프리팹에 TMP_Text 없음", this);  // 설정 실수를 로딩 때 바로 잡음
                        Destroy(obj); // TMP_Text 없으면 쓸모 없으므로 제거
                    }

                }
            }
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
            // 새 줄을 찍기 '전에' 바닥 근처였는지 기록한다. 유저가 위로 올려 옛 채팅을 읽는 중에는
            // 새 메시지가 와도 자동으로 끌어내리지 않는다(안 그러면 스크롤이 계속 바닥으로 튕겨 조작을 방해).
            bool wasAtBottom = scroll == null || scroll.verticalNormalizedPosition <= 0.05f;

            // 유저가 친 태그(<color> 등)를 서식 명령이 아니라 글자로 보이게 한다.
            // TMP는 <noparse> 안쪽의 <,>를 태그로 해석하지 않는다(=&lt; 치환은 TMP에선 안 통함).
            // 단 유저가 닫는 noparse 태그를 끼워 넣으면 보호가 조기 종료되므로, 그 태그의 모든 변형
            // (대소문자·여백)을 먼저 제거해 우회를 막는다. ★ 이것이 태그 주입의 실제 방어선이다 —
            // 입력창의 richText 설정이 아니라(그건 로컬 미리보기일 뿐), 남이 네트워크로 보낸 메시지까지
            // 여기를 통과하기 때문. null 방어는 네트워크로 온 값이 비어 있을 수 있어 함께 둔다.
            string safeSender = NoparseCloseTag.Replace(message.sender ?? string.Empty, string.Empty);
            string safeText = NoparseCloseTag.Replace(message.text ?? string.Empty, string.Empty);

            // 풀이 준비돼 있으면(슬롯이 하나라도 있으면) 링 버퍼 슬롯을 재사용해 한 줄 찍는다.
            if (tmps.Count > 0)
                ReuseLine($"<color=blue><noparse>{safeSender}</noparse></color>: <noparse>{safeText}</noparse>");

            // 바닥에서 보고 있었을 때만 최신 줄을 따라 내려간다. 레이아웃 갱신 뒤 위치를 잡아야 정확하다.
            if (scroll != null && wasAtBottom)
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


        // TMP 메시지 풀링(링 버퍼). writeIndex 슬롯을 재사용해 맨 아래(최신)로 옮겨 한 줄 찍고,
        // 인덱스를 maxLines로 순환시킨다. 한 바퀴 돌면 가장 오래된 줄을 덮어써 항상 maxLines줄만 유지한다.
        // 미리 만든 슬롯을 인덱스로 바로 집으므로 빈 슬롯 탐색 반복문이 필요 없다.
        private void ReuseLine(string message)
        {
            TMP_Text line = tmps[writeIndex]; // 이번에 쓸 슬롯

            line.gameObject.SetActive(true);
            line.transform.SetAsLastSibling(); // 스크롤뷰 맨 아래(최신)로 이동
            line.SetText(message);

            writeIndex = (writeIndex + 1) % maxLines; // 다음 슬롯으로 순환(넘으면 가장 오래된 줄부터 재사용)
        }
    }
}
