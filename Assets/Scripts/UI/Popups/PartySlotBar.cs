using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// <b>채워진 칸을 누르면 컨텍스트 메뉴가 뜬다</b>(<see cref="PartyContextMenu"/>). 카드가 커지면서
    /// 초상화를 누른 것만으로 곧장 내보내기가 실행되면 놀라기 때문이고, 나중에 귓속말·친구 추가 같은
    /// 항목이 붙을 자리를 미리 열어 두려는 것이기도 하다. 빈 칸은 할 수 있는 게 초대뿐이라
    /// 메뉴 없이 바로 목록을 연다.
    /// </para>
    /// <para>
    /// <b>메뉴에 올릴 항목이 하나도 없으면 칸을 아예 못 누르게 한다.</b> 눌러도 아무 일이 없는 칸은
    /// 고장으로 읽힌다. 항목 목록과 활성 여부를 같은 함수에서 만들어, 나중에 항목을 추가하면
    /// 활성 조건도 저절로 따라온다.
    /// </para>
    /// <para>
    /// <b>파티장 위임은 넣지 않았다(2026-08-31).</b> 던전·난이도가 초대 시점에 확정돼 해체까지 고정이라
    /// 파티장의 권한은 사실상 출발 방아쇠 하나뿐이고, 2인 파티에서는 다시 맺는 비용이 초대 한 번이라
    /// 위임으로 얻는 게 없다. 정원이 늘면 그때 다시 본다.
    /// </para>
    /// <para>
    /// <b>던전·난이도를 고르기 전에는 빈 칸을 잠근다.</b> 어디로 갈지 정해지지 않으면 초대받은 사람에게
    /// 보여줄 내용이 없기 때문이다. 던전 입장 창이 선택을 바꿀 때마다 <see cref="SetSelectionReady"/>를
    /// 불러 준다.
    /// </para>
    /// </remarks>
    public class PartySlotBar : MonoBehaviour
    {
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

            bool inParty = source.Partner != null;

            // 메뉴에 올릴 게 있을 때만 누를 수 있다. 눌러도 빈 메뉴가 뜨는 칸은 고장으로 읽힌다.
            if (selfSlot != null)
            {
                selfSlot.SetMember(source.Self, source.IsLeader && inParty,
                                   interactable: BuildMenu(forSelf: true).Count > 0);
            }

            if (partnerSlot == null) return;

            if (source.Partner != null)
            {
                partnerSlot.SetMember(source.Partner, !source.IsLeader,
                                      interactable: BuildMenu(forSelf: false).Count > 0);
                return;
            }

            // 초대 응답을 기다리는 동안에는 다시 누르지 못하게 잠근다.
            bool inviting = source.IsInviting;
            partnerSlot.SetEmpty(selectionReady && !inviting, inviting ? invitingLabel : emptyLabel);
        }

        private void OnSelfSlotClicked() => OpenMenu(forSelf: true);

        private void OnPartnerClicked()
        {
            // 빈 칸은 할 수 있는 게 초대뿐이라 메뉴를 거치지 않는다.
            if (source != null && source.Partner == null)
            {
                OpenInvitePopup();
                return;
            }

            OpenMenu(forSelf: false);
        }

        private void OpenMenu(bool forSelf)
        {
            if (PartyContextMenu.Instance == null)
            {
                Debug.LogWarning("[PartySlotBar] PartyContextMenu가 씬에 없어 메뉴를 열 수 없다.", this);
                return;
            }

            List<PartyContextMenu.Entry> items = BuildMenu(forSelf);
            if (items.Count == 0) return;

            PartyMemberInfo target = forSelf ? source.Self : source.Partner;
            PartyContextMenu.Instance.Show(target != null ? target.Nickname : string.Empty, CursorPosition(), items);
        }

        /// <summary>
        /// 그 칸에서 지금 할 수 있는 일들. 활성 여부 판정도 이 결과를 쓰므로,
        /// 항목을 추가하면 칸이 눌리게 되는 것까지 함께 따라온다.
        /// </summary>
        private List<PartyContextMenu.Entry> BuildMenu(bool forSelf)
        {
            List<PartyContextMenu.Entry> items = new();
            if (source == null || source.Partner == null) return items;

            if (forSelf)
            {
                items.Add(new PartyContextMenu.Entry("파티 나가기", () => source.RequestLeave(),
                                                     "파티에서 나갈까요?"));
                return items;
            }

            // 상대를 내보내는 건 파티장만 할 수 있다. 파티원은 자기 칸으로 나가면 되므로 막다른 길이 아니다.
            if (source.IsLeader)
            {
                items.Add(new PartyContextMenu.Entry("내보내기", () => source.RequestKick(),
                                                     $"{source.Partner.Nickname}을(를) 파티에서 내보낼까요?",
                                                     destructive: true));
            }

            return items;
        }

        // 커서가 없으면(패드 등) 화면 가운데에 띄운다.
        private static Vector2 CursorPosition()
        {
            Mouse mouse = Mouse.current;
            return mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
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
