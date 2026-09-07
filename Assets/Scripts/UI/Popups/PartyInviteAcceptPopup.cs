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

        protected override void OnInit()
        {
            source = partySourceBehaviour as IPartySource;
            if (source == null)
            {
                Debug.LogError(partySourceBehaviour == null
                    ? "[PartyInviteAcceptPopup] partySourceBehaviour가 비어 있다 — 초대를 받을 수 없다."
                    : $"[PartyInviteAcceptPopup] {partySourceBehaviour.GetType().Name}은 IPartySource를 구현하지 않는다.", this);
            }

            if (acceptButton != null) acceptButton.onClick.AddListener(OnAcceptClicked);
            if (declineButton != null) declineButton.onClick.AddListener(OnDeclineClicked);
        }

        protected override void OnShow()
        {
            if (source != null) source.OnChanged += OnSourceChanged;
            if (countdown != null) countdown.SetTitle(countdownTitle);

            Redraw();
        }

        protected override void OnHide()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        private void OnDestroy()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        // 남은 시간은 여기서 직접 읽는다. 데이터원이 매 프레임 알림을 쏘면 다른 화면까지 다시 그려진다.
        private void Update()
        {
            if (source == null || source.Phase != PartyPhase.Invited) return;

            countdown?.Set(source.RemainingSeconds, source.PhaseDuration);
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
            source?.AcceptInvite();
            CloseSelf();
        }

        private void OnDeclineClicked()
        {
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
