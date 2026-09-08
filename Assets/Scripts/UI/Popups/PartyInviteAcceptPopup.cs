using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 초대를 받았을 때 뜨는 작은 수락 팝업(docs/PARTY_WINDOW_UI.md §7).
    /// 누가 불렀는지 · 어디로 가는지 · 남은 시간만 보여주고 수락/거절을 받는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>한때 결성창이 이 역할까지 했다가 떼어냈다(2026-09-07).</b> "어차피 수락하면 볼 창"이라는
    /// 이유로 합쳤는데, 로스터 카드가 초상화 크기로 커지면서 <b>"수락할래?" 하나 물으려고 카드 둘 ·
    /// 던전 줄 · 카운트다운을 전부 보여주는</b> 꼴이 됐다. 받는 쪽이 판단에 필요한 것은 셋뿐이다.
    /// </para>
    /// <para>
    /// <b>초대자 줄은 레벨과 클래스를 함께 적는다.</b> 카드에서는 클래스를 아이콘으로 두어 이름표가
    /// 필요 없었지만, 여기는 한 줄짜리 글이라 이름이 있어야 그 줄이 제 몫을 한다.
    /// </para>
    /// <para>
    /// <b>파티 수락이 아니라 "이 던전에 같이 들어가기" 수락이다.</b> 그래서 던전 이름과 난이도가
    /// 반드시 보여야 한다 — 그것 없이는 무엇에 동의하는지 알 수 없다.
    /// </para>
    /// <para>
    /// <b>국면이 <see cref="PartyPhase.Invited"/>를 벗어나면 스스로 닫는다.</b> 시간이 다 되거나
    /// 초대가 취소되면 답할 대상이 사라지는데, 그대로 떠 있으면 눌러도 아무 일이 없는 창이 남는다.
    /// </para>
    /// </remarks>
    public class PartyInviteAcceptPopup : BasePopup
    {
        [Header("데이터원")]
        [Tooltip("파티 상태를 물어볼 곳. 결성창·슬롯 바와 같은 오브젝트를 물린다.")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        [Header("표시")]
        [Tooltip("누가 불렀는지. {0}에 닉네임이 들어간다.")]
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private string messageFormat = "{0} 님이 파티에 초대했습니다";
        [Tooltip("초대자의 레벨과 클래스를 한 줄로. 아래 형식과 이름표를 쓴다.")]
        [SerializeField] private TMP_Text inviterMetaText;

        [Tooltip("{0}=레벨, {1}=클래스 이름.")]
        [SerializeField] private string metaFormat = "Lv.{0} {1}";

        [Tooltip("클래스 이름. 인덱스는 CharacterSaveData.characterType과 같다(카드의 classIcons와 같은 순서).")]
        [SerializeField] private string[] classNames = { "검사", "거너" };
        [SerializeField] private TMP_Text dungeonNameText;
        [SerializeField] private TMP_Text difficultyText;
        [SerializeField] private CountdownView countdown;
        [SerializeField] private string countdownTitle = "응답 제한";

        [Header("응답")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;

        private IPartySource source;

        // 이미 답했는지(수락/거절). 답 없이 닫힐 때만 거절로 치기 위해, 그리고 한 번만 답하기 위해 둔다.
        // ★ 수락 클릭 → CloseSelf 시점엔 아직 서버 왕복 전이라 Phase가 Invited다. 이 가드가 없으면
        //   OnHide가 "아직 Invited인데 답 안 함"으로 오인해 수락 직후 거절을 덧쏜다.
        private bool answered;

        // 받는 창의 응답 타이머는 로컬로 센다 — 받는 사람 혼자 보는 값이라 서버 동기가 필요 없고(출발
        // 카운트다운과 달리 두 사람이 같은 값을 볼 이유가 없다), 게이지가 매 프레임 매끄럽게 흐른다.
        // 시작값은 네트워크가 준 남은 시간(RemainingSeconds), 없으면 전체 창(PhaseDuration)에서 시작한다.
        private float timerRemaining;
        private float timerTotal;

        protected override void OnInit()
        {
            source = partySourceBehaviour as IPartySource;

            // 슬롯을 비워 두면 등록된 파티 소스를 자동으로 받아온다(창마다 크로스 오브젝트로 끌어다 꽂지 않게).
            if (source == null && partySourceBehaviour == null) source = PartySourceProvider.Current;

            if (source == null)
            {
                Debug.LogError(partySourceBehaviour == null
                    ? "[PartyInviteAcceptPopup] partySourceBehaviour가 비어 있고 등록된 파티 소스도 없다 — 초대를 받을 수 없다."
                    : $"[PartyInviteAcceptPopup] {partySourceBehaviour.GetType().Name}은 IPartySource를 구현하지 않는다.", this);
            }

            Debug.Log($"[진단][PartyInviteAcceptPopup] partySource={(source as MonoBehaviour != null ? $"{((MonoBehaviour)source).name}#{((MonoBehaviour)source).GetInstanceID()}" : "null")} (NetworkPartySource OnEnable의 #ID와 같아야 함)", this);

            if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
            if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);
        }

        protected override void OnShow()
        {
            answered = false;

            if (source != null) source.OnChanged += OnSourceChanged;
            if (countdown != null) countdown.SetTitle(countdownTitle);

            // 로컬 타이머 초기화. 네트워크가 준 남은 시간을 우선 쓰되, 없으면(0) 전체 창부터 센다.
            timerTotal = source != null ? source.PhaseDuration : 0f;
            timerRemaining = source != null ? source.RemainingSeconds : 0f;
            if (timerRemaining <= 0.01f) timerRemaining = timerTotal;
            if (timerTotal <= 0.01f) timerTotal = timerRemaining;

            Debug.Log($"[진단][AcceptPopup] OnShow: source={(source != null)}, phase={source?.Phase}, " +
                      $"countdown연결={(countdown != null)}, remaining0={timerRemaining:0.0}, total0={timerTotal:0.0}", this);

            Redraw();
        }

        protected override void OnHide()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;

            // 답 없이 닫혔고(ESC·back) 초대가 아직 살아 있으면 거절로 친다 — 안 그러면 초대자가 서버
            // 타임아웃까지 "초대 중…"에 갇힌다. Phase가 Invited가 아니면 이미 결판난 초대(성립·취소·만료)라
            // 답을 보내면 안 된다(수락 직후 닫힘도 이 검사와 answered 가드로 걸러진다).
            if (!answered && source != null && source.Phase == PartyPhase.Invited)
            {
                answered = true;
                source.DeclineInvite();
            }
        }

        private void OnDestroy()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        // 남은 시간은 여기서 직접 읽는다. 데이터원이 매 프레임 알림을 쏘면 다른 화면까지 다시 그려진다.
        private void Update()
        {
            if (source == null || source.Phase != PartyPhase.Invited) return;

            // 로컬 타이머를 매 프레임 줄여 게이지에 밀어 넣는다(unscaled — UI 대기라 timeScale 영향 안 받게).
            timerRemaining -= Time.unscaledDeltaTime;
            countdown?.Set(timerRemaining, timerTotal);

            // 정확히 0이 되면 자동 거절하고 창을 닫는다. 서버 타임아웃은 초대자에게만 통지하고 받는 창의
            // Phase는 계속 Invited라, 여기서 0을 보고 스스로 닫지 않으면 게이지가 0에 붙은 채 남는다.
            if (timerRemaining <= 0f)
            {
                answered = true;          // 만료 처리 — OnHide의 중복 거절을 막는다.
                source.DeclineInvite();   // 서버 통지(이미 만료면 no-op) + 로컬 초대 정리 → Phase None
                CloseSelf();
            }
        }

        private void OnSourceChanged()
        {
            // 시간이 다 되거나 초대가 취소되면 답할 대상이 없다. 남겨 두면 먹통인 창이 된다.
            if (source == null || source.Phase != PartyPhase.Invited)
            {
                CloseSelf();
                return;
            }

            Redraw();
        }

        private void Redraw()
        {
            if (source == null) return;

            PartyMemberInfo inviter = source.Inviter;
            if (messageText != null)
            {
                messageText.text = string.Format(messageFormat, inviter != null ? inviter.Nickname : string.Empty);
            }

            if (inviterMetaText != null) inviterMetaText.text = DescribeMeta(inviter);

            if (dungeonNameText != null) dungeonNameText.text = source.DungeonName;
            if (difficultyText != null) difficultyText.text = source.DifficultyLabel;
        }

        // 레벨과 클래스를 한 줄로 합친다. 카드에서는 클래스를 아이콘으로 두어 이름표가 필요 없었지만,
        // 여기는 글자 한 줄이라 이름이 있어야 한다. 표는 인스펙터에 두어 기획이 직접 고칠 수 있게 한다.
        private string DescribeMeta(PartyMemberInfo member)
        {
            if (member == null) return string.Empty;

            // 범위를 벗어나면 클래스를 비운다 — 엉뚱한 직업을 적는 것보다 레벨만 보이는 편이 낫다.
            bool hasName = classNames != null
                        && member.CharacterType >= 0
                        && member.CharacterType < classNames.Length;

            string className = hasName ? classNames[member.CharacterType] : string.Empty;
            return string.Format(metaFormat, member.Level, className).TrimEnd();
        }

        private void OnAcceptClicked()
        {
            answered = true;
            source?.AcceptInvite();
            CloseSelf();
        }

        private void OnDeclineClicked()
        {
            answered = true;
            source?.DeclineInvite();
            CloseSelf();
        }

        // ESC(BasePopup.CanCloseByBack)와 같은 길로 닫는다. 직접 gameObject를 끄면
        // UIManager의 열린 팝업 목록에 죽은 항목이 남는다.
        private void CloseSelf()
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<PartyInviteAcceptPopup>();
        }
    }
}
