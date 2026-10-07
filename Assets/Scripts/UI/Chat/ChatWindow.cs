using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
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
    /// 채팅 창(로그 + 입력). 팝업 스택(UIManager)에 넣지 않고 상시 활성 오버레이로 둔다.
    /// <para>
    /// 컴포넌트는 늘 활성으로 두되, <b>시각 표시만 <see cref="CanvasGroup"/>.alpha로 자동 숨김</b>한다.
    /// 메시지 수신·입력창 포커스 시 <see cref="showDuration"/>초간 나타났다가 이후 다시 숨는다
    /// (<see cref="BumpVisible"/>/<see cref="UpdateVisibility"/>).
    /// GameObject 자체를 끄지 않는 이유: 끄면 <see cref="OnDisable"/>에서 수신 구독이 끊겨
    /// (1) 숨어 있는 사이 온 메시지를 놓치고, (2) 다시 여는 Enter 감지(<see cref="Update"/>)도 죽는다.
    /// </para>
    /// <para>
    /// 게임 입력 억제 판정 기준은 "입력창 포커스 여부"다:
    ///  - 포커스 O → 타이핑 중 → 게임 입력 억제(<see cref="SetGameplayInputSuspended"/>). ESC로 포커스 해제, Enter로 전송+해제
    ///    (빈 입력에서 Enter는 전송 없이 해제 = 채팅 닫기).
    ///  - 포커스 X → 게임 조작 정상. Enter로 입력창 포커스(채팅 시작). 상점·강화창 등 다른 창이 떠 있으면 열지 않는다
    ///    (<see cref="UIManager.IsAnyWindowOpen"/>).
    /// raw 키보드를 읽는 다른 핫키들은 <see cref="UiTypingGuard"/>로 타이핑 중 함께 막힌다.
    /// </para>
    /// <para>
    /// 입력 채널은 전체/파티 두 가지다. 포커스 중 Tab 또는 채널 라벨 클릭으로 바꾸고(<see cref="ToggleChannel"/>),
    /// 파티 없이 파티 채널로 보내면 "파티가 없습니다" 알림만 띄우고 보내지 않는다. 서버(ChatManager.CmdSend)도 소속을
    /// 다시 검사해 파티원에게만 TargetRpc로 보낸다 — 클라 판정은 안내용이고 실제 차단은 서버가 한다.
    /// </para>
    /// <para>
    /// 수신은 <see cref="ChatEvents.OnMessageReceived"/>를 직접 구독한다(별도 Presenter 없음).
    /// 상시 활성이라 숨어 있어도 언제 메시지가 와도 받아 로그에 찍는다.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ChatWindow : MonoBehaviour
    {
        [Header("View")]
        [SerializeField] private GameObject textPrefab;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TMP_InputField input;

        [Header("로그")]
        [Tooltip("로그에 유지할 최대 줄 수. 넘으면 오래된 줄부터 버린다(TMP 정점 한계·메모리 누적 방지).")]
        [SerializeField] private int maxLines = 100;

        [Header("입력 채널 (전체/파티)")]
        [Tooltip("지금 입력 채널. 입력창 포커스 중 Tab, 또는 채널 드롭다운/라벨 클릭으로 전체↔파티를 바꾼다.")]
        [SerializeField] private ChatChannel activeChannel = ChatChannel.General;
        [Tooltip("채널 드롭다운(UI ▸ Dropdown - TextMeshPro). 옵션은 코드가 [전체, 파티] 순서로 채우므로 인스펙터 Options는 비워 둬도 된다. " +
                 "드롭다운을 쓰면 channelLabel은 비운다(둘 중 하나만).")]
        [SerializeField] private TMP_Dropdown channelDropdown;
        [Tooltip("드롭다운 대신 글자 라벨로 표시할 때(UI ▸ Text - TextMeshPro). 클릭 전환을 쓰려면 같은 오브젝트에 ChatChannelToggle도 붙인다.")]
        [SerializeField] private TMP_Text channelLabel;
        [Tooltip("전체 채널 표기(드롭다운 옵션·라벨 공용).")]
        [SerializeField] private string generalLabel = "[전체]";
        [Tooltip("파티 채널 표기(드롭다운 옵션·라벨 공용). 색은 partyColor를 쓴다.")]
        [SerializeField] private string partyLabel = "[파티]";
        [Tooltip("파티 없이 파티 채널로 보내려 할 때 채팅에 띄울 알림.")]
        [SerializeField] private string noPartyNotice = "파티가 없습니다.";

        // 전체 채널일 때 표기 색. 인스펙터에서 라벨/드롭다운 글자에 지정해 둔 색을 Awake에서 기억해 둔다(파티 색에서 되돌릴 때 사용).
        private Color generalLabelColor = Color.white;

        private readonly List<TMP_Text> tmps = new();

        // 직전 프레임의 '채팅 조작 중' 상태(입력창 포커스 또는 채널 드롭다운 조작). 바뀌는 순간에만 게임 입력 억제를 토글한다.
        private bool wasActive;

        // 전송으로 포커스를 푼 프레임. 그 '같은 Enter'가 아래 Update의 '열기'로 재활용돼 즉시 재포커스되는 것을 막는다.
        private int submitFrame = -1;

        // Enter로 열기를 예약한 프레임(-1 = 예약 없음). 숨김 상태에선 CanvasGroup.interactable이 꺼져 있어,
        // 그 프레임에 바로 ActivateInputField를 부르면 TMP가 '상호작용 불가'로 보고 조용히 무시한다
        // (채팅이 숨어 있을 때 Enter를 두 번 눌러야 열리던 원인). 그래서 이 프레임엔 interactable만 켜고
        // 실제 포커스는 다음 프레임에 건다.
        private int openRequestFrame = -1;

        // 입력창에 포커스를 건 프레임. 여는 Enter가 빈 submit으로 새어 '열자마자 닫히는' 것을 거르는 데 쓴다.
        private int activateFrame = -1;

        [Header("자동 숨김")]
        [Tooltip("메시지 수신·포커스 해제 후 채팅을 보여줄 시간(초). 지나면 숨긴다.")]
        [SerializeField] private float showDuration = 5f;
        [Tooltip("페이드 시간(초). 0이면 즉시 전환.")]
        [SerializeField] private float fadeDuration = 0.25f;

        [Header("색 (닉네임=나/남 구분, 말머리·본문=채널 구분)")]
        [Tooltip("내가 보낸 채팅의 닉네임 색. 남의 채팅과 한눈에 구분하기 위함.")]
        [SerializeField] private Color myNameColor = new Color32(0x4F, 0xC3, 0xF7, 0xFF);   // 밝은 파랑
        [Tooltip("다른 사람이 보낸 채팅의 닉네임 색.")]
        [SerializeField] private Color otherNameColor = new Color32(0xB3, 0x9D, 0xDB, 0xFF); // 밝은 보라
        [Tooltip("파티 채팅의 말머리·본문 색. 전체 채팅과 섞여도 파티원끼리 한 말을 가려내기 위함.")]
        [SerializeField] private Color partyColor = new Color32(0xFF, 0xB7, 0x4D, 0xFF);     // 주황
        [Tooltip("파티 채팅 줄 앞에 붙는 말머리.")]
        [SerializeField] private string partyPrefix = "[파티]";

        [Header("진단 로그 → 시스템 알림 (디버그, 체크 끄면 빠짐)")]
        [SerializeField] private bool showDiagnosticsInChat = true;
        [SerializeField] private string diagnosticPrefix = "[진단]";

        private readonly Queue<string> pendingDiagnostics = new();
        private CanvasGroup canvasGroup;

        // 이 시각(Time.unscaledTime) 전까지 표시. 포커스 중엔 이 값과 무관하게 표시.
        private float hideTime = -1f;

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

                // 표시 컴포넌트(textComponent)뿐 아니라 입력 필드 자체의 richText도 끈다. TMP_InputField는
                // IME '조합 중' 문자열에 밑줄용 <u> 마크업을 붙이는데, 이 삽입 여부가 필드의 richText를 따르는
                // 경우가 있어(표시 컴포넌트만 꺼도 <u>가 남는 원인) 둘 다 꺼서 조합 마크업 자체를 없애 본다.
                input.richText = false;

                input.onSubmit.AddListener(_ => SubmitMessage());

                // Tab은 채널 전환 키라 글자로 들어가면 안 된다. 단일 줄 필드는 TMP가 Tab을 스스로 거르지만,
                // 줄 타입이 Multi Line Submit 등으로 바뀌면 '\t'가 본문에 섞이므로 여기서 확실히 막는다('\0' = 입력 거부).
                // ★ onValidateInput을 지정하면 characterValidation보다 우선한다 — 채팅 필드는 Validation=None 전제.
                input.onValidateInput = (_, __, c) => c == '\t' ? '\0' : c;
            }

            if (channelDropdown != null)
            {
                // 옵션 순서가 곧 채널 매핑(0=전체, 1=파티)이라 인스펙터 값에 맡기지 않고 코드가 채운다.
                channelDropdown.ClearOptions();
                channelDropdown.AddOptions(new List<string> { generalLabel, partyLabel });
                channelDropdown.onValueChanged.AddListener(OnChannelDropdownChanged);
                if (channelDropdown.captionText != null) generalLabelColor = channelDropdown.captionText.color;
            }
            else if (channelLabel != null)
            {
                generalLabelColor = channelLabel.color;
            }
            RefreshChannelLabel();

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

            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        // logMessageReceived는 씬 로드 중/다른 스레드에서도 올 수 있어, 여기선 큐에만 담고 UI는 Update에서 그린다.
        private void OnDiagnosticLog(string condition, string stackTrace, LogType type)
        {
            if (string.IsNullOrEmpty(condition) || !condition.StartsWith(diagnosticPrefix)) return;
            pendingDiagnostics.Enqueue(condition);
        }

        // 수신 구독은 활성/비활성과 짝을 맞춘다(상시 활성이라 사실상 항상 구독). Presenter를 따로 두지 않고 여기서 직접 받는다.
        private void OnEnable()
        {
            ChatEvents.OnMessageReceived += AppendLine;
            if (showDiagnosticsInChat)
                Application.logMessageReceived += OnDiagnosticLog;
        }
        private void OnDisable()
        { 
            ChatEvents.OnMessageReceived -= AppendLine;
            Application.logMessageReceived -= OnDiagnosticLog;   // 구독 안 했어도 해제는 안전
        }

        private void Update()
        {
            if (input == null) return;

            while (pendingDiagnostics.Count > 0)
                ChatEvents.FireSystemNotice($"[{System.DateTime.Now:HH:mm:ss.fff}] {pendingDiagnostics.Dequeue()}");

            bool focused = input.isFocused;

            // 채널 드롭다운을 누르는 순간 EventSystem 선택이 입력창 → 드롭다운으로 넘어가 포커스가 풀린다.
            // 이걸 '채팅 종료'로 보면 레이캐스트가 꺼져 드롭다운 클릭·목록 선택이 먹지 않고, 게임 입력도 풀려
            // 목록을 고르는 클릭이 공격으로 샌다. 그래서 드롭다운 조작도 입력창 포커스와 같은 '채팅 조작 중'으로 친다.
            bool picking = IsPickingChannel();
            bool active = focused || picking;

            // 조작 상태가 바뀌는 순간에만 게임 입력 억제를 켜고/끈다.
            // (조작 중 = 타이핑/채널 선택 중이라 이동·공격 등 게임 입력 차단, 아니면 평소대로 복구.)
            if (active != wasActive)
            {
                SetGameplayInputSuspended(active);
                wasActive = active;

                if (!active) BumpVisible(); // ← 전송/ESC로 풀려도 바로 안 사라지고 5초 더
            }

            UpdateVisibility(active);

            if (picking)
            {
                // 목록이 닫혔는데 선택만 드롭다운에 남아 있으면(항목을 고름·바깥을 눌러 취소·누른 채 밖에서 뗌)
                // 입력창으로 돌려보내 바로 이어서 타이핑하게 한다. 남겨 두면 Enter가 드롭다운을 다시 펼친다.
                // 뗀 그 프레임은 건너뛴다 — Update 순서상 드롭다운의 클릭(펼치기)보다 먼저 돌 수 있어, 펼치기 직전에 입력창으로 빼앗게 된다.
                Mouse mouse = Mouse.current;
                bool pressing = mouse != null && (mouse.leftButton.isPressed || mouse.leftButton.wasReleasedThisFrame);
                if (!channelDropdown.IsExpanded && !pressing) FocusInput();
                return;   // 드롭다운이 키 입력(방향키·Enter·ESC)을 처리하는 동안엔 채팅 단축키를 쉰다
            }

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (focused)
            {
                // 이미 열려 있으니 남아 있던 열기 예약은 버린다(나중에 포커스가 풀리는 순간 저절로 다시 열리는 것 방지).
                openRequestFrame = -1;

                // 타이핑 중 ESC → 포커스만 해제(게임 복귀). 창은 그대로 보인다. 전송(Enter)은 onSubmit이 담당.
                if (kb.escapeKey.wasPressedThisFrame) input.DeactivateInputField();

                // 타이핑 중 Tab → 전체↔파티 전환. 포커스 중엔 결성창 Tab(PartyWindowOpener)이 입력 중이라 스스로 빠진다.
                else if (kb.tabKey.wasPressedThisFrame) ToggleChannel();
            }
            else
            {
                // 방금 전송으로 포커스를 푼 그 Enter는 '열기'로 재활용하지 않는다(같은 프레임 재포커스 방지).
                // 실행 순서상 onSubmit(전송·해제)이 이 Update보다 먼저 돌면, 해제된 그 프레임에 Enter가 아직
                // wasPressedThisFrame이라 여기서 곧바로 다시 포커스가 걸려 "포커스가 안 풀리는" 증상이 난다.
                if (Time.frameCount == submitFrame) return;

                // 연출로 UI가 숨겨졌거나, 상점·강화창 같은 다른 창이 떠 있는 동안엔 Enter로 채팅을 열지 않는다.
                // 숨김 중엔 안 보이는 입력창에 포커스가 걸리고, 창 사용 중엔 Enter가 채팅으로 새어 게임 입력이 잠긴다.
                // 예약해 둔 열기도 함께 취소한다(Enter를 누른 다음 프레임에 창이 열린 경우).
                if (UIManager.IsHidden || UIManager.IsAnyWindowOpen)
                {
                    openRequestFrame = -1;
                    return;
                }

                // 지난 프레임에 예약한 열기를 수행한다. 그 사이 interactable이 반영돼 ActivateInputField가 통과한다.
                if (openRequestFrame >= 0 && Time.frameCount > openRequestFrame)
                {
                    openRequestFrame = -1;
                    activateFrame = Time.frameCount;
                    input.ActivateInputField();
                    return;
                }

                // 포커스 아님 + Enter → 표시·상호작용을 먼저 켜고 포커스는 다음 프레임에 건다(openRequestFrame 주석 참고).
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                {
                    BumpVisible();
                    if (canvasGroup != null) canvasGroup.interactable = true;
                    openRequestFrame = Time.frameCount;
                }
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
            {
                if (message.channel == ChatChannel.System)
                {
                    // 색은 우리가 코드로 지정한 로컬 값이라 <noparse> '바깥'에 둬야 실제 색으로 렌더된다.
                    // 본문(safeText)만 noparse로 보호. sender(색 hex)는 네트워크가 아니라 우리 코드가 넣은 값이라 안전.
                    string color = string.IsNullOrEmpty(message.sender) ? "#FFEB3B" : message.sender;
                    ReuseLine($"<color={color}><noparse>{safeText}</noparse></color>");
                }
                else
                {
                    ReuseLine(FormatChatLine(message, safeSender, safeText));
                }

            }

            // 바닥에서 보고 있었을 때만 최신 줄을 따라 내려간다. 레이아웃 갱신 뒤 위치를 잡아야 정확하다.
            if (scroll != null && wasAtBottom)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
            }

            BumpVisible();
        }

        // 유저 채팅 한 줄의 서식. 두 축을 겹치지 않게 나눈다 —
        //  - 닉네임 색 = 나/남(isMine). 누가 말했는지.
        //  - 말머리·본문 색 = 채널. 전체는 말머리 없이 기본색, 파티만 말머리+색을 붙인다.
        //    (마을에서 대부분인 전체 채팅 줄마다 [전체]가 붙으면 로그가 시끄러워져, 드문 쪽만 표시한다.)
        // 색 태그는 우리 인스펙터 값이라 <noparse> 바깥에 두고, 네트워크로 온 이름·본문만 noparse로 보호한다.
        private string FormatChatLine(ChatMessage message, string safeSender, string safeText)
        {
            string nameHex = ColorUtility.ToHtmlStringRGB(message.isMine ? myNameColor : otherNameColor);
            string name = $"<color=#{nameHex}><noparse>{safeSender}</noparse></color>";

            if (message.channel == ChatChannel.Party)
            {
                string partyHex = ColorUtility.ToHtmlStringRGB(partyColor);
                return $"<color=#{partyHex}>{partyPrefix} </color>{name}<color=#{partyHex}>: <noparse>{safeText}</noparse></color>";
            }

            return $"{name}: <noparse>{safeText}</noparse>";
        }

        /// <summary>
        /// Enter 전송. 내용이 있으면 보내고 <b>포커스를 해제</b>해 게임 조작으로 복귀한다.
        /// 빈 입력이면 전송 없이 포커스만 해제한다(Enter로 열고 Enter로 닫기). 단 입력창을 여는 그 Enter가
        /// 새어든 경우(포커스를 건 직후)는 포커스를 유지해 열자마자 닫히지 않게 한다.
        /// </summary>
        private void SubmitMessage()
        {
            if (input == null) return;

            string text = input.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                if (Time.frameCount - activateFrame <= 1)
                {
                    submitFrame = Time.frameCount;
                    input.ActivateInputField();   // 여는 Enter가 샌 빈 전송 → 포커스 유지
                    return;
                }

                input.text = string.Empty;      // 공백만 친 경우도 비워서 닫는다
                submitFrame = Time.frameCount;  // 닫은 그 Enter가 Update에서 다시 '열기'로 재활용되지 않게 표시
                input.DeactivateInputField();
                return;
            }

            // 파티가 없는데 파티 채널로 보내려 하면 알림만 띄우고 보내지 않는다.
            // 친 글은 지우지 않고 포커스도 유지한다 — Tab으로 전체 채널로 바꿔 그대로 다시 보낼 수 있게 하기 위함.
            // submitFrame: TMP가 Enter로 포커스를 푼 같은 프레임에 Update가 그 Enter를 '열기'로 예약하지 않게 한다.
            // (서버 CmdSend도 소속을 다시 검사한다. 여기는 즉시 안내용, 실제 차단은 서버 몫.)
            if (activeChannel == ChatChannel.Party && !HasParty())
            {
                ChatEvents.FireSystemNotice(noPartyNotice, "#FF8A80");
                submitFrame = Time.frameCount;
                input.ActivateInputField();
                return;
            }

            ChatEvents.FireSendRequested(activeChannel, text);
            input.text = string.Empty;
            submitFrame = Time.frameCount;  // 이 프레임의 Enter가 Update에서 재포커스로 재활용되지 않게 표시
            input.DeactivateInputField();   // 전송 후 포커스 해제 → 다음 프레임 Update가 게임 입력을 복구
        }

        /// <summary>
        /// 입력 채널을 전체↔파티로 바꾼다. 입력창 포커스 중 Tab, 또는 채널 라벨 클릭(<see cref="ChatChannelToggle"/>)이 부른다.
        /// </summary>
        /// <remarks>
        /// 파티가 없어도 파티 채널로 바꾸는 것 자체는 막지 않는다. 판정은 보낼 때(<see cref="SubmitMessage"/>) 한 번만 한다 —
        /// 전환 때도 막으면 "왜 안 바뀌지?"가 되고, 파티가 생기기 전 미리 바꿔 두는 흐름도 끊긴다.
        /// 라벨 클릭은 입력창 바깥을 누르는 것이라 EventSystem이 입력창 선택을 풀 수 있어, 포커스가 빠졌으면 다시 건다.
        /// </remarks>
        public void ToggleChannel()
        {
            activeChannel = activeChannel == ChatChannel.Party ? ChatChannel.General : ChatChannel.Party;
            RefreshChannelLabel();
            FocusInput();
        }

        // 드롭다운에서 항목을 골랐을 때(onValueChanged). 옵션 순서 0=전체, 1=파티(Awake에서 코드로 채움).
        // 고른 뒤 바로 이어서 타이핑할 수 있게 입력창 포커스를 되돌린다.
        private void OnChannelDropdownChanged(int index)
        {
            activeChannel = index == 1 ? ChatChannel.Party : ChatChannel.General;
            RefreshChannelLabel();
            FocusInput();
        }

        // 입력창 포커스가 빠져 있으면 다시 건다. 채널 클릭·드롭다운 선택처럼 입력창 바깥을 누른 뒤 타이핑으로 복귀할 때 쓴다.
        private void FocusInput()
        {
            if (input == null || input.isFocused) return;

            activateFrame = Time.frameCount;
            input.ActivateInputField();
        }

        // 채널 드롭다운을 조작 중인지 — EventSystem 선택이 드롭다운 자신이나 펼친 목록 항목(드롭다운 자식으로 생성됨)에 있으면 true.
        private bool IsPickingChannel()
        {
            if (channelDropdown == null) return false;

            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            return selected != null && selected.transform.IsChildOf(channelDropdown.transform);
        }

        // 표시에 현재 채널을 반영한다. 파티는 로그의 말머리 색과 같게 맞춰 "이 줄이 저 색으로 나간다"를 바로 알게 한다.
        // 드롭다운은 SetValueWithoutNotify로 맞춘다 — Tab 전환이 onValueChanged를 다시 불러 되돌아오는 것을 막기 위함.
        private void RefreshChannelLabel()
        {
            bool party = activeChannel == ChatChannel.Party;
            Color color = party ? partyColor : generalLabelColor;

            if (channelDropdown != null)
            {
                channelDropdown.SetValueWithoutNotify(party ? 1 : 0);
                if (channelDropdown.captionText != null) channelDropdown.captionText.color = color;
            }

            if (channelLabel != null)
            {
                channelLabel.text = party ? partyLabel : generalLabel;
                channelLabel.color = color;
            }
        }

        // 파티에 소속돼 있는지. UI는 미러를 모르므로 PlayerPresence가 아니라 IPartySource 계약으로 본다.
        // 초대 응답 대기(Invited)는 아직 파티가 아니다. 출발 카운트다운(Departing)은 파티가 있는 상태다.
        private static bool HasParty()
        {
            IPartySource party = PartySourceProvider.Current;
            return party != null && (party.Phase == PartyPhase.Formed || party.Phase == PartyPhase.Departing);
        }

        /// <summary>
        /// 타이핑 동안 게임플레이 입력을 잠근다(이동·점프·공격·스킬·회피·상호작용 InputAction 일괄).
        /// 커서 토글(Alt)은 잠그지 않는다(allowCursorToggle) — 타이핑 중에도 Alt로 마우스를 꺼내 채널 드롭다운·입력칸을
        /// 누를 수 있어야 하기 때문이다. 컷신 등 다른 주인이 함께 잠그고 있으면 그쪽 규칙대로 Alt도 막힌다.
        /// 참조 단일 창구 규칙에 따라 PlayerManager.Instance.Player로 접근한다. 입력창(TMP_InputField)은
        /// EventSystem으로 동작해 이 억제와 무관하니 타이핑엔 안전하다.
        /// </summary>
        private void SetGameplayInputSuspended(bool suspended)
        {
            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null) player.Input.SetInputSuspended(suspended, this, allowCursorToggle: true);
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

        /// <summary>메시지 수신·창 열기·포커스 해제 시 호출. 지금부터 showDuration초 동안 표시되게 타이머를 민다.</summary>
        private void BumpVisible()
        {
            hideTime = Time.unscaledTime + showDuration; // timeScale=0에서도 흘러야 하므로 unscaled
        }

        private void UpdateVisibility(bool focused)
        {
            if (canvasGroup == null) return;

            bool shouldShow = focused || Time.unscaledTime < hideTime;

            float target = shouldShow ? 1f : 0f;

            canvasGroup.alpha = fadeDuration > 0f
                ? Mathf.MoveTowards(canvasGroup.alpha, target, Time.unscaledDeltaTime / fadeDuration)
                : target;

            canvasGroup.interactable = shouldShow; // 페이드 인 중에도 켜둬 첫 타이핑 지연 방지

            // 보이는 동안엔 클릭을 받는다 — 입력칸을 눌러 바로 채팅을 시작하고, 드롭다운으로 채널을 고르기 위함.
            // 숨으면(알파 0으로 가는 중 포함) 끈다. 안 보이는 창이 화면 구석의 클릭을 가로채면 안 되기 때문이다.
            // (TPS 조작 중엔 커서가 화면 중앙에 잠겨 있어 구석의 채팅창을 누를 일이 없다.)
            canvasGroup.blocksRaycasts = shouldShow;
        }
    }
}
