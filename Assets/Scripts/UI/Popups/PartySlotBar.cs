using System;
using UnityEngine;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 입장 창 하단의 파티 슬롯 두 칸(docs/PARTY_WINDOW_UI.md §2).
    /// 왼쪽은 나, 오른쪽은 파티원이며, 빈 칸을 누르면 초대 목록 팝업을 연다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>초대 목록 팝업과의 연결을 이쪽이 쥔다.</b> 팝업은 "누구를 고를지"만 알고 파티를 모르며,
    /// 이 바가 팝업의 <see cref="PartyInvitePopup.OnInviteRequested"/>를 받아
    /// <see cref="IPartySource.RequestInvite"/>로 넘긴다. 덕분에 팝업은 던전 입장 창 밖(결성창 등)에서
    /// 열어도 그대로 쓸 수 있다.
    /// </para>
    /// <para>
    /// <b>던전·난이도를 고르기 전에는 빈 칸을 잠근다.</b> 어디로 갈지 정해지지 않으면 초대받은 사람에게
    /// 보여줄 내용이 없기 때문이다. 던전 입장 창이 선택을 바꿀 때마다 <see cref="SetSelectionReady"/>를
    /// 불러 준다.
    /// </para>
    /// </remarks>
    public class PartySlotBar : MonoBehaviour
    {
        /// <summary>파티원 칸을 눌렀다. 내보내기/나가기 확인창을 띄우는 것은 바깥의 몫이다.</summary>
        public event Action<PartyMemberInfo> OnPartnerSlotClicked;

        [Header("데이터원")]
        [Tooltip("파티 상태를 물어볼 곳. 네트워크가 붙기 전에는 DummyPartySource를 끼운다.")]
        [SerializeField] private MonoBehaviour partySourceBehaviour;

        [Header("칸")]
        [SerializeField] private PartySlotView selfSlot;
        [SerializeField] private PartySlotView partnerSlot;

        [Header("문구")]
        [SerializeField] private string emptyLabel = "＋ 파티원 초대";
        [SerializeField] private string invitingLabel = "초대 중…";

        private IPartySource source;
        private PartyInvitePopup invitePopup;

        // 던전·난이도가 골라졌는지. 던전 입장 창이 알려 준다.
        private bool selectionReady;

        private void Awake()
        {
            source = partySourceBehaviour as IPartySource;
            if (source == null)
            {
                Debug.LogError(partySourceBehaviour == null
                    ? "[PartySlotBar] partySourceBehaviour가 비어 있다 — 슬롯이 영영 비어 있게 된다."
                    : $"[PartySlotBar] {partySourceBehaviour.GetType().Name}은 IPartySource를 구현하지 않는다.", this);
            }
        }

        private void OnEnable()
        {
            if (selfSlot != null) selfSlot.OnClicked += OnSelfSlotClicked;
            if (partnerSlot != null) partnerSlot.OnClicked += OnPartnerClicked;
            if (source != null) source.OnChanged += Redraw;

            Redraw();
        }

        private void OnDisable()
        {
            if (selfSlot != null) selfSlot.OnClicked -= OnSelfSlotClicked;
            if (partnerSlot != null) partnerSlot.OnClicked -= OnPartnerClicked;
            if (source != null) source.OnChanged -= Redraw;

            // 팝업은 이 바보다 오래 살아 있다. 구독을 남기면 창을 닫았다 열 때마다 초대가 겹쳐 발행된다.
            UnbindPopup();
        }

        /// <summary>
        /// 던전·난이도가 골라졌는지 알려 준다. 던전 입장 창이 선택을 바꿀 때마다 부른다.
        /// </summary>
        /// <param name="ready">입장할 던전과 난이도가 모두 정해졌는지</param>
        public void SetSelectionReady(bool ready)
        {
            if (selectionReady == ready) return;

            selectionReady = ready;
            Redraw();
        }

        private void Redraw()
        {
            if (source == null) return;

            if (selfSlot != null) selfSlot.SetMember(source.Self, source.IsLeader && source.Partner != null, interactable: false);

            if (partnerSlot == null) return;

            if (source.Partner != null)
            {
                // 파티장이면 내보내기, 파티원이면 나가기 — 어느 쪽이든 누를 수 있다.
                partnerSlot.SetMember(source.Partner, !source.IsLeader, interactable: true);
                return;
            }

            // 초대 응답을 기다리는 동안에는 다시 누르지 못하게 잠근다.
            bool inviting = source.IsInviting;
            partnerSlot.SetEmpty(selectionReady && !inviting, inviting ? invitingLabel : emptyLabel);
        }

        // 내 칸은 누를 게 없다. 눌러도 아무 일이 없도록 두되, 나중에 내 정보 보기 같은 게 붙을 자리다.
        private void OnSelfSlotClicked() { }

        private void OnPartnerClicked()
        {
            if (source == null) return;

            if (source.Partner != null)
            {
                OnPartnerSlotClicked?.Invoke(source.Partner);
                return;
            }

            OpenInvitePopup();
        }

        private void OpenInvitePopup()
        {
            if (UIManager.Instance == null)
            {
                Debug.LogWarning("[PartySlotBar] UIManager가 없어 초대 목록을 열 수 없다.", this);
                return;
            }

            UIManager.Instance.ShowPopup<PartyInvitePopup>();

            // 인스턴스는 UIManager 아래에 있어 여기서 직접 참조를 들 수 없다. 열고 나서 받아 온다.
            PartyInvitePopup popup = UIManager.Instance.GetPopup<PartyInvitePopup>();
            if (popup == null || popup == invitePopup) return;

            UnbindPopup();
            invitePopup = popup;
            invitePopup.OnInviteRequested += OnInviteRequested;
        }

        private void OnInviteRequested(PartyMemberInfo target)
        {
            source?.RequestInvite(target);

            // 목록은 닫는다. 초대를 보낸 뒤에도 목록이 떠 있으면 이미 부른 사람을 또 고르게 된다.
            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<PartyInvitePopup>();

            Redraw();
        }

        private void UnbindPopup()
        {
            if (invitePopup == null) return;

            invitePopup.OnInviteRequested -= OnInviteRequested;
            invitePopup = null;
        }
    }
}
