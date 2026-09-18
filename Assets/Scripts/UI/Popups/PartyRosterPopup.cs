using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티 결성창(docs/PARTY_WINDOW_UI.md §8). 마을에서 <c>Tab</c>으로 연다.
    /// 한 창이 두 화면을 갈아 끼운다 — 파티 없음 · 파티 결성(출발 대기 포함).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>초대 수락은 여기서 하지 않는다(2026-09-07).</b> 한때 이 창이 그 역할까지 했는데,
    /// 로스터 카드가 초상화 크기로 커지면서 "수락할래?" 하나 물으려고 카드 둘·던전 줄·카운트다운을
    /// 전부 보여주는 꼴이 됐다. 받는 쪽 화면은 <see cref="PartyInviteAcceptPopup"/>으로 떼어냈고,
    /// 이 창은 <see cref="PartyPhase.Invited"/>를 '파티 없음'과 같게 취급한다 — 그 국면엔 아직 파티가 없다.
    /// </para>
    /// <para>
    /// <b>출발 제한 시간은 이 창에 그리지 않는다(2026-09-17 TH).</b> 파티원은 출발 응답 팝업
    /// (<see cref="PartyInviteAcceptPopup"/>의 출발 모드)에서 남은 시간을 보고 입장/취소를 고른다.
    /// 같은 숫자를 두 창에 그리면 겹쳐 뜰 때 중복이고, 결성창 레이아웃만 무거워진다.
    /// </para>
    /// <para>
    /// <b>파티가 없어도 창은 열린다.</b> 아무 반응이 없으면 키가 먹은 것인지 창이 없는 것인지 알 수 없다.
    /// 대신 기능을 전부 잠그고 다음에 할 것을 한 줄로 안내한다(§8).
    /// </para>
    /// <para>
    /// <b>출발 제한 시간이 지나도 파티는 유지된다</b>(2026-09-07). 출발만 취소되고 화면은 '파티 결성'으로 돌아오며,
    /// 파티장은 다시 [던전 입장]을 눌러 새로 걸 수 있다.
    /// </para>
    /// </remarks>
    public class PartyRosterPopup : BasePopup
    {
        [Header("데이터원")]
        [Tooltip("파티 상태를 물어볼 곳. 슬롯 바와 같은 오브젝트를 물려야 한다(던전 창 밖에 두는 것).")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        [Header("공통")]
        [SerializeField] private Button closeButton;
        [Tooltip("가려는 던전 줄. 표시 전용이라 파티가 없을 때는 숨긴다.")]
        [SerializeField] private GameObject dungeonRow;
        [SerializeField] private TMP_Text dungeonNameText;
        [SerializeField] private TMP_Text difficultyText;

        [Header("로스터")]
        [SerializeField] private PartySlotView selfSlot;
        [SerializeField] private PartySlotView partnerSlot;

        [Header("파티 없음")]
        [Tooltip("기능이 전부 잠긴 상태에서 다음에 할 것을 알려 주는 한 줄.")]
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private string emptyMessage = "던전 입구에서 파티를 만들 수 있습니다";

        [Header("파티 결성 · 출발")]
        [SerializeField] private GameObject formedRoot;
        [SerializeField] private Button leaveButton;
        [Tooltip("파티장은 출발 걸기/취소, 파티원은 즉시 입장. 상황에 따라 라벨이 바뀐다.")]
        [SerializeField] private Button departButton;
        [SerializeField] private TMP_Text departLabel;
        [Tooltip("파티원이 아직 못 누를 때 이유를 적어 두는 자리.")]
        [SerializeField] private TMP_Text departHintText;

        [Header("문구")]
        [SerializeField] private string departLabelStart = "던전 입장";
        [SerializeField] private string departLabelCancel = "출발 취소";
        [SerializeField] private string departLabelConfirm = "던전 입장";
        [SerializeField] private string departHintWaiting = "파티장이 출발을 시작하면 활성화됩니다";

        private IPartySource source;

        protected override void OnInit()
        {
            source = partySourceBehaviour as IPartySource;

            // 슬롯을 비워 두면 등록된 파티 소스를 자동으로 받아온다(창마다 크로스 오브젝트로 끌어다 꽂지 않게).
            if (source == null && partySourceBehaviour == null) source = PartySourceProvider.Current;

            if (source == null)
            {
                Debug.LogError(partySourceBehaviour == null
                    ? "[PartyRosterPopup] partySourceBehaviour가 비어 있고 등록된 파티 소스도 없다 — 창이 영영 '파티 없음'으로 남는다."
                    : $"[PartyRosterPopup] {partySourceBehaviour.GetType().Name}은 IPartySource를 구현하지 않는다.", this);
            }

            Debug.Log($"[진단][PartyRosterPopup] partySource={(source as MonoBehaviour != null ? $"{((MonoBehaviour)source).name}#{((MonoBehaviour)source).GetInstanceID()}" : "null")} (NetworkPartySource OnEnable의 #ID와 같아야 함)", this);

            if (closeButton != null) closeButton.onClick.AddListener(CloseSelf);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeaveClicked);
            if (departButton != null) departButton.onClick.AddListener(OnDepartClicked);
        }

        protected override void OnShow()
        {
            if (source != null) source.OnChanged += Redraw;
            Redraw();
        }

        protected override void OnHide()
        {
            if (source != null) source.OnChanged -= Redraw;
        }

        private void OnDestroy()
        {
            if (source != null) source.OnChanged -= Redraw;
        }

        private void Redraw()
        {
            if (source == null)
            {
                ShowPhase(PartyPhase.None);
                return;
            }

            ShowPhase(source.Phase);

            // 던전 줄은 파티가 맺어져야 의미가 생긴다. 그 전에는 갈 곳이 정해지지 않았다.
            bool hasDestination = source.Partner != null;
            if (dungeonRow != null) dungeonRow.SetActive(hasDestination);
            if (dungeonNameText != null) dungeonNameText.text = source.DungeonName;
            if (difficultyText != null) difficultyText.text = source.DifficultyLabel;

            DrawRoster();
            DrawButtons();
        }

        private void ShowPhase(PartyPhase phase)
        {
            // Invited는 아직 파티가 없는 국면이라 '파티 없음'과 같이 다룬다.
            // 그 화면은 PartyInviteAcceptPopup이 따로 띄운다.
            bool formed = phase is PartyPhase.Formed or PartyPhase.Departing;

            if (emptyRoot != null) emptyRoot.SetActive(!formed);
            if (formedRoot != null) formedRoot.SetActive(formed);

            if (emptyText != null) emptyText.text = emptyMessage;
        }

        private void DrawRoster()
        {
            PartyMemberInfo other = source.Partner;
            bool inParty = other != null;

            if (selfSlot != null)
            {
                selfSlot.SetMember(source.Self, isLeader: source.IsLeader && inParty, interactable: false);
            }

            if (partnerSlot == null) return;

            if (other != null)
            {
                partnerSlot.SetMember(other, !source.IsLeader, interactable: false);
                return;
            }

            partnerSlot.SetEmpty(interactable: false);
        }

        private void DrawButtons()
        {
            bool departing = source.Phase == PartyPhase.Departing;
            bool formed = source.Phase is PartyPhase.Formed or PartyPhase.Departing;

            if (leaveButton != null) leaveButton.interactable = formed;

            if (departButton == null) return;

            if (source.IsLeader)
            {
                // 파티장: 출발을 걸고, 도는 동안에는 취소할 수 있다.
                departButton.interactable = formed;
                if (departLabel != null) departLabel.text = departing ? departLabelCancel : departLabelStart;
                if (departHintText != null) departHintText.gameObject.SetActive(false);
                return;
            }

            // 파티원: 파티장이 출발을 걸기 전까지는 누를 수 없고, 그 이유를 적어 둔다.
            departButton.interactable = departing;
            if (departLabel != null) departLabel.text = departLabelConfirm;
            if (departHintText != null)
            {
                departHintText.gameObject.SetActive(formed && !departing);
                departHintText.text = departHintWaiting;
            }
        }

        // ── 입력 ────────────────────────────────────────────────

        private void OnLeaveClicked()
        {
            if (source == null) return;

            // 나가기는 되돌리려면 다시 초대를 받아야 하므로 한 번 확인을 받는다.
            if (ConfirmDialog.Instance != null) ConfirmDialog.Instance.Show("파티에서 나갈까요?", source.RequestLeave);
            else source.RequestLeave();
        }

        private void OnDepartClicked()
        {
            if (source == null) return;

            if (source.IsLeader)
            {
                if (source.Phase == PartyPhase.Departing) source.CancelDepart();
                else source.RequestDepart();
                return;
            }

            source.ConfirmDepart();
        }

        // ESC(BasePopup.CanCloseByBack)와 같은 길로 닫는다. 직접 gameObject를 끄면
        // UIManager의 열린 팝업 목록에 죽은 항목이 남는다.
        private void CloseSelf()
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<PartyRosterPopup>();
        }
    }
}
