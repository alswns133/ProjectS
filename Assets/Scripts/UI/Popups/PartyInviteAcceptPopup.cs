using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티원에게 "같이 갈래?"를 묻는 작은 응답 팝업(docs/PARTY_WINDOW_UI.md §7·§8).
    /// 초대를 받았을 때(<see cref="PartyPhase.Invited"/>)와 파티장이 출발을 걸었을 때
    /// (<see cref="PartyPhase.Departing"/>) 같은 창을 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>한때 결성창이 이 역할까지 했다가 떼어냈다(2026-09-07).</b> "어차피 수락하면 볼 창"이라는
    /// 이유로 합쳤는데, 로스터 카드가 초상화 크기로 커지면서 <b>"수락할래?" 하나 물으려고 카드 둘 ·
    /// 던전 줄 · 카운트다운을 전부 보여주는</b> 꼴이 됐다. 받는 쪽이 판단에 필요한 것은 셋뿐이다.
    /// </para>
    /// <para>
    /// <b>초대와 출발이 한 창인 이유(2026-09-17 TH)</b>: 둘 다 "누가 · 어느 던전으로 · 몇 초 안에"를 보여주고
    /// 예/아니오를 받는 같은 모양이다. 창을 둘로 두면 레이아웃을 고칠 때마다 두 벌을 맞춰야 한다.
    /// 대신 <b>글자만 다른 게 아니다</b> — 버튼이 부르는 요청, 타이머의 출처, 0초 처리가 모드마다 다르다.
    /// 모드는 열릴 때 <see cref="IPartySource.Phase"/>로 한 번 정하고, 그 국면을 벗어나면 스스로 닫는다.
    /// 초대는 파티가 없을 때만, 출발은 파티가 있을 때만 생기므로 두 모드가 동시에 필요할 일은 없다
    /// (UIManager가 타입당 인스턴스 하나만 두는 제약에 걸리지 않는다).
    /// </para>
    /// <para>
    /// <b>출발 모드의 [취소]는 창만 닫는 게 아니라 출발 자체를 취소한다(2026-09-17 기획 확정).</b>
    /// 파티원이 갈 생각이 없는데 파티장이 30초를 그대로 기다리게 두지 않기 위함이다. 파티는 유지된다.
    /// </para>
    /// <para>
    /// <b>답 없이 닫히면(ESC) 두 모드 모두 [아니오]로 친다.</b> 같은 창에서 ESC와 [취소]가 다른 뜻이면
    /// 헷갈리고, 답하지 않고 창만 치운 파티원을 상대가 제한 시간 끝까지 기다리게 된다.
    /// </para>
    /// <para>
    /// <b>초대자 줄은 레벨과 클래스를 함께 적는다.</b> 카드에서는 클래스를 아이콘으로 두어 이름표가
    /// 필요 없었지만, 여기는 한 줄짜리 글이라 이름이 있어야 그 줄이 제 몫을 한다.
    /// </para>
    /// <para>
    /// <b>파티 수락이 아니라 "이 던전에 같이 들어가기" 수락이다.</b> 그래서 던전 이름과 난이도가
    /// 반드시 보여야 한다 — 그것 없이는 무엇에 동의하는지 알 수 없다.
    /// </para>
    /// </remarks>
    public class PartyInviteAcceptPopup : BasePopup
    {
        private enum PromptMode
        {
            Invite,
            Depart,
        }

        [Header("데이터원")]
        [Tooltip("파티 상태를 물어볼 곳. 결성창·슬롯 바와 같은 오브젝트를 물린다.")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        [Header("표시")]
        [Tooltip("누가 불렀는지 / 누가 출발을 걸었는지. 형식은 모드별 문구를 쓴다.")]
        [SerializeField] private TMP_Text messageText;
        [Tooltip("상대의 레벨과 클래스를 한 줄로. 아래 형식과 이름표를 쓴다.")]
        [SerializeField] private TMP_Text inviterMetaText;

        [Tooltip("{0}=레벨, {1}=클래스 이름.")]
        [SerializeField] private string metaFormat = "Lv.{0} {1}";

        [Tooltip("클래스 이름. 인덱스는 CharacterSaveData.characterType과 같다(카드의 classIcons와 같은 순서).")]
        [SerializeField] private string[] classNames = { "검사", "거너" };
        [SerializeField] private TMP_Text dungeonNameText;
        [SerializeField] private TMP_Text difficultyText;
        [SerializeField] private CountdownView countdown;

        [Header("응답")]
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;
        [Tooltip("비우면 버튼 자식에서 찾는다. 모드마다 라벨이 바뀐다.")]
        [SerializeField] private TMP_Text acceptLabel;
        [Tooltip("비우면 버튼 자식에서 찾는다. 모드마다 라벨이 바뀐다.")]
        [SerializeField] private TMP_Text declineLabel;

        [Header("문구 · 초대")]
        [Tooltip("{0}에 초대자 닉네임이 들어간다.")]
        [SerializeField] private string messageFormat = "{0} 님이 파티에 초대했습니다";
        [SerializeField] private string countdownTitle = "응답 제한";
        [SerializeField] private string inviteAcceptText = "수락";
        [SerializeField] private string inviteDeclineText = "거절";

        [Header("문구 · 출발")]
        [Tooltip("{0}에 파티장 닉네임이 들어간다.")]
        [SerializeField] private string departMessageFormat = "{0} 님이 던전 출발을 시작했습니다";
        [SerializeField] private string departCountdownTitle = "출발까지";
        [SerializeField] private string departAcceptText = "입장";
        [SerializeField] private string departDeclineText = "취소";

        private IPartySource source;

        // 열릴 때 국면으로 정한 모드. 열려 있는 동안 바뀌지 않는다 — 국면이 벗어나면 창이 닫히기 때문이다.
        private PromptMode mode;

        // 이미 답했는지(예/아니오). 답 없이 닫힐 때만 [아니오]로 치기 위해, 그리고 한 번만 답하기 위해 둔다.
        // ★ 예 클릭 → CloseSelf 시점엔 아직 서버 왕복 전이라 Phase가 그대로다. 이 가드가 없으면
        //   OnHide가 "아직 그 국면인데 답 안 함"으로 오인해 수락 직후 거절(취소)을 덧쏜다.
        private bool answered;

        // 초대 모드의 응답 타이머는 로컬로 센다 — 받는 사람 혼자 보는 값이라 서버 동기가 필요 없고,
        // 게이지가 매 프레임 매끄럽게 흐른다. 시작값은 네트워크가 준 남은 시간(RemainingSeconds),
        // 없으면 전체 창(PhaseDuration)에서 시작한다.
        // 출발 모드는 이 값을 쓰지 않는다 — 파티장 화면과 같은 숫자가 보여야 해서 소스를 매 프레임 읽는다.
        private float timerRemaining;
        private float timerTotal;

        private PartyPhase ExpectedPhase => mode == PromptMode.Depart ? PartyPhase.Departing : PartyPhase.Invited;

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

            if (acceptButton != null)
            {
                acceptButton.onClick.AddListener(OnAcceptClicked);
                if (acceptLabel == null) acceptLabel = acceptButton.GetComponentInChildren<TMP_Text>(true);
            }

            if (declineButton != null)
            {
                declineButton.onClick.AddListener(OnDeclineClicked);
                if (declineLabel == null) declineLabel = declineButton.GetComponentInChildren<TMP_Text>(true);
            }
        }

        protected override void OnShow()
        {
            answered = false;
            mode = source != null && source.Phase == PartyPhase.Departing ? PromptMode.Depart : PromptMode.Invite;

            if (source != null) source.OnChanged += OnSourceChanged;

            bool depart = mode == PromptMode.Depart;
            if (countdown != null) countdown.SetTitle(depart ? departCountdownTitle : countdownTitle);
            if (acceptLabel != null) acceptLabel.text = depart ? departAcceptText : inviteAcceptText;
            if (declineLabel != null) declineLabel.text = depart ? departDeclineText : inviteDeclineText;

            // 로컬 타이머 초기화(초대 모드만 쓴다). 네트워크가 준 남은 시간을 우선 쓰되, 없으면(0) 전체 창부터 센다.
            timerTotal = source != null ? source.PhaseDuration : 0f;
            timerRemaining = source != null ? source.RemainingSeconds : 0f;
            if (timerRemaining <= 0.01f) timerRemaining = timerTotal;
            if (timerTotal <= 0.01f) timerTotal = timerRemaining;

            Redraw();
        }

        protected override void OnHide()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;

            // 답 없이 닫혔고(ESC·back) 국면이 아직 살아 있으면 [아니오]로 친다 — 안 그러면 상대가
            // 제한 시간 끝까지 기다린다. 국면이 이미 벗어났으면 결판난 일(성립·취소·만료)이라 보내면 안 된다
            // (예 직후 닫힘도 이 검사와 answered 가드로 걸러진다).
            if (!answered && source != null && source.Phase == ExpectedPhase)
            {
                answered = true;
                SendDecline();
            }
        }

        private void OnDestroy()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        // 남은 시간은 여기서 직접 읽는다. 데이터원이 매 프레임 알림을 쏘면 다른 화면까지 다시 그려진다.
        private void Update()
        {
            if (source == null || source.Phase != ExpectedPhase) return;

            if (mode == PromptMode.Depart)
            {
                // 출발 만료는 서버가 판정한다(파티 유지 · 출발만 취소). 국면이 바뀌면 OnSourceChanged가 닫으므로
                // 여기서는 그리기만 한다 — 로컬에서 먼저 취소를 쏘면 서버 시계와 어긋난 판정이 된다.
                countdown?.Set(source.RemainingSeconds, source.PhaseDuration);
                return;
            }

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
            // 시간이 다 되거나 상대가 취소하면 답할 대상이 없다. 남겨 두면 먹통인 창이 된다.
            if (source == null || source.Phase != ExpectedPhase)
            {
                CloseSelf();
                return;
            }

            Redraw();
        }

        private void Redraw()
        {
            if (source == null) return;

            // 초대 모드는 부른 사람, 출발 모드는 파티장(= 파티원 입장에서 상대)을 보여준다.
            PartyMemberInfo other = mode == PromptMode.Depart ? source.Partner : source.Inviter;
            if (messageText != null)
            {
                string format = mode == PromptMode.Depart ? departMessageFormat : messageFormat;
                messageText.text = string.Format(format, other != null ? other.Nickname : string.Empty);
            }

            if (inviterMetaText != null) inviterMetaText.text = DescribeMeta(other);

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
            if (source != null)
            {
                if (mode == PromptMode.Depart) source.ConfirmDepart();
                else source.AcceptInvite();
            }

            CloseSelf();
        }

        private void OnDeclineClicked()
        {
            answered = true;
            SendDecline();
            CloseSelf();
        }

        // [아니오]가 모드마다 보내는 요청. 출발 모드는 창만 닫는 게 아니라 출발 자체를 취소한다(파티는 유지).
        private void SendDecline()
        {
            if (source == null) return;

            if (mode == PromptMode.Depart) source.CancelDepart();
            else source.DeclineInvite();
        }

        // ESC(BasePopup.CanCloseByBack)와 같은 길로 닫는다. 직접 gameObject를 끄면
        // UIManager의 열린 팝업 목록에 죽은 항목이 남는다.
        private void CloseSelf()
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<PartyInviteAcceptPopup>();
        }
    }
}
