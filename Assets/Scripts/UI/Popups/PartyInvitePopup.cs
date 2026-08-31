using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 입장 창의 빈 파티 슬롯을 눌렀을 때 뜨는 초대 목록 팝업(docs/PARTY_WINDOW_UI.md §3·§6).
    /// 접속 중 / 최근 두 탭, 닉네임 검색, 정렬, 단일 선택, 초대 요청까지를 담당한다.
    ///
    /// <para>
    /// 목록의 출처는 <see cref="IPartyMemberSource"/> 하나뿐이라 <b>네트워크를 모른다.</b>
    /// 지금은 <see cref="DummyPartyMemberSource"/>를 물려 UI만으로 끝까지 만들 수 있다.
    /// </para>
    /// <para>
    /// <b>초대를 실제로 보내지는 않는다.</b> <see cref="OnInviteRequested"/>로 대상만 알리고
    /// 대기 상태로 잠근다. 성공/실패는 바깥에서 <see cref="EndInviting"/>로 알려 준다 —
    /// 검증은 어차피 서버가 다시 하므로 UI가 결과를 지어내면 안 된다.
    /// </para>
    /// </summary>
    public class PartyInvitePopup : BasePopup
    {
        /// <summary>초대 버튼을 눌렀다. 인자는 선택된 상대. 발행 직후 팝업은 대기 상태로 잠긴다.</summary>
        public event Action<PartyMemberInfo> OnInviteRequested;

        /// <summary>⑥ 초대 수신 토글을 바꿨다. 캐릭터 세이브에 저장하는 것은 바깥의 몫이다.</summary>
        public event Action<bool> OnAcceptInvitesChanged;

        [Header("① 닫기")]
        [SerializeField] private Button closeButton;

        [Header("데이터원")]
        [Tooltip("목록을 물어볼 곳. 네트워크가 붙기 전에는 DummyPartyMemberSource를 끼운다.")]
        [SerializeField] private MonoBehaviour memberSourceBehaviour;

        [Header("② ③ 탭")]
        [SerializeField] private Toggle onlineTab;
        [SerializeField] private Toggle recentTab;

        [Header("⑤ 검색 · 새로고침")]
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private Button refreshButton;

        [Header("④ 목록")]
        [Tooltip("카드가 쌓이는 Content. 가이드 행은 여기 넣지 않는다(목록이 비어도 남아야 하므로).")]
        [SerializeField] private RectTransform listRoot;
        [SerializeField] private PartyPlayerCard cardPrefab;
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("가이드 행의 '플레이어' 버튼. 누르면 닉네임 정렬 방향이 뒤집힌다.")]
        [SerializeField] private Button sortButton;
        [SerializeField] private TMP_Text sortLabel;

        [Header("빈 상태")]
        [Tooltip("목록이 비었을 때만 켜지는 안내. 첫 응답 전에는 켜지 않는다.")]
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private TMP_Text emptyText;

        [Header("⑥ 초대 수신")]
        [SerializeField] private Button acceptToggleButton;
        [SerializeField] private TMP_Text acceptToggleLabel;

        [Header("⑦ 초대")]
        [SerializeField] private Button inviteButton;
        [SerializeField] private TMP_Text inviteButtonLabel;

        // 화면에 만들어 둔 카드들. 목록이 줄면 남는 카드는 꺼 두고 재사용한다.
        private readonly List<PartyPlayerCard> cards = new();

        // 정렬이 끝난 "표시 순서". 갱신이 와도 이 순서는 유지하고, 새 사람만 뒤에 붙인다
        // — 매 갱신마다 다시 정렬하면 누르려던 카드가 발밑에서 움직인다(문서 §6).
        private readonly List<PartyMemberInfo> ordered = new();

        // 검색어까지 적용해 실제로 그릴 목록.
        private readonly List<PartyMemberInfo> visible = new();

        private IPartyMemberSource source;
        private bool showingRecent;
        private bool ascending = true;
        private bool inviting;

        // 첫 응답 전에는 빈 상태 문구를 띄우지 않는다. 안 그러면 열 때마다
        // "플레이어가 없습니다"가 한 번 번쩍였다가 목록이 채워진다.
        private bool hasData;

        // 선택은 인덱스가 아니라 Id로 들고 있는다. 갱신으로 순서가 바뀌어도 같은 사람을 가리키기 위해서다.
        private string selectedId;

        /// <summary>현재 선택된 상대. 아무도 안 골랐으면 null.</summary>
        public PartyMemberInfo Selected => FindVisible(selectedId);

        protected override void OnInit()
        {
            source = memberSourceBehaviour as IPartyMemberSource;
            if (source == null && memberSourceBehaviour != null)
            {
                Debug.LogError($"[PartyInvitePopup] {memberSourceBehaviour.GetType().Name}은 IPartyMemberSource를 구현하지 않는다.", this);
            }

            if (onlineTab != null) onlineTab.onValueChanged.AddListener(OnOnlineTabChanged);
            if (recentTab != null) recentTab.onValueChanged.AddListener(OnRecentTabChanged);
            if (searchInput != null) searchInput.onValueChanged.AddListener(OnSearchChanged);
            if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
            if (sortButton != null) sortButton.onClick.AddListener(OnSortClicked);
            if (acceptToggleButton != null) acceptToggleButton.onClick.AddListener(OnAcceptToggleClicked);
            if (inviteButton != null) inviteButton.onClick.AddListener(OnInviteClicked);
            if (closeButton != null) closeButton.onClick.AddListener(CloseSelf);
        }

        // ESC(BasePopup.CanCloseByBack)와 같은 길로 닫는다. 직접 gameObject를 끄면
        // UIManager의 열린 팝업 목록에 죽은 항목이 남는다.
        private void CloseSelf()
        {
            if (UIManager.Instance != null) UIManager.Instance.ClosePopup<PartyInvitePopup>();
        }

        protected override void OnShow()
        {
            // 닫았다 열면 전부 초기화한다(문서 §6). 탭만 되돌리고 검색어를 남기면
            // "접속 중 탭인데 걸러진 목록"이라는 어긋난 상태가 된다.
            showingRecent = false;
            ascending = true;
            inviting = false;
            selectedId = null;
            hasData = false;

            if (onlineTab != null) onlineTab.SetIsOnWithoutNotify(true);
            if (recentTab != null) recentTab.SetIsOnWithoutNotify(false);
            if (searchInput != null) searchInput.SetTextWithoutNotify(string.Empty);

            if (source != null) source.OnChanged += OnSourceChanged;

            UpdateSortLabel();
            Resort();
            ScrollToTop();
        }

        protected override void OnHide()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        private void OnDestroy()
        {
            if (source != null) source.OnChanged -= OnSourceChanged;
        }

        /// <summary>
        /// 초대 응답이 끝났음을 알려 대기 상태를 푼다. 서버가 거절·실패를 돌려줬을 때도 부른다.
        /// </summary>
        /// <param name="keepSelection">실패라 같은 상대를 다시 고른 채로 두고 싶으면 true</param>
        public void EndInviting(bool keepSelection = false)
        {
            inviting = false;
            if (!keepSelection) selectedId = null;

            Redraw();
        }

        /// <summary>⑥ 토글의 표시를 바깥 상태(캐릭터 세이브)에 맞춘다.</summary>
        /// <param name="accepting">초대를 받는 상태인지</param>
        public void SetAcceptInvites(bool accepting)
        {
            if (acceptToggleLabel != null)
            {
                acceptToggleLabel.text = accepting ? "파티 초대 허용" : "파티 초대 거부";
            }
        }

        // ── 입력 ────────────────────────────────────────────────

        private void OnOnlineTabChanged(bool on)
        {
            if (!on || !showingRecent) return;

            showingRecent = false;
            SwitchTab();
        }

        private void OnRecentTabChanged(bool on)
        {
            if (!on || showingRecent) return;

            showingRecent = true;
            SwitchTab();
        }

        private void SwitchTab()
        {
            // 탭을 바꾸면 정렬을 다시 계산하고 맨 위로 올린다(문서 §6).
            selectedId = null;
            Resort();
            ScrollToTop();
        }

        // 검색어가 바뀔 때마다 선택을 푼다. 걸러져 화면에서 사라진 사람에게 초대가 나가는 것을 막기 위함이다
        // — "보이는 것 = 선택 가능한 것 = 초대 대상" 원칙(문서 §6).
        private void OnSearchChanged(string _)
        {
            selectedId = null;
            Redraw();
        }

        private void OnRefreshClicked()
        {
            source?.Refresh();

            // Refresh가 OnChanged를 발행하지만, 새로고침은 정렬을 다시 계산해야 하는 네 시점 중 하나라
            // 순서 유지가 기본인 OnSourceChanged와 달리 여기서 직접 다시 정렬한다.
            Resort();
            ScrollToTop();
        }

        private void OnSortClicked()
        {
            ascending = !ascending;
            UpdateSortLabel();
            Resort();
            ScrollToTop();
        }

        private void OnAcceptToggleClicked()
        {
            bool nowAccepting = acceptToggleLabel == null || acceptToggleLabel.text != "파티 초대 허용";
            SetAcceptInvites(nowAccepting);
            OnAcceptInvitesChanged?.Invoke(nowAccepting);
        }

        private void OnInviteClicked()
        {
            PartyMemberInfo target = Selected;
            if (inviting || target == null || !target.CanInvite) return;

            inviting = true;
            Redraw();
            OnInviteRequested?.Invoke(target);
        }

        private void OnCardClicked(int index)
        {
            if (inviting || index < 0 || index >= visible.Count) return;

            PartyMemberInfo member = visible[index];

            // 초대할 수 없는 카드는 선택되지 않고, 대신 그 카드가 진동한다.
            // 버튼을 흔들면 여러 명 중 누구 때문에 막혔는지 알 수 없어 카드 쪽을 흔든다(문서 §5).
            if (!member.CanInvite)
            {
                if (index < cards.Count) cards[index].PlayRejectShake();
                return;
            }

            // 단일 선택 — 같은 카드를 다시 누르면 선택이 풀린다.
            selectedId = selectedId == member.Id ? null : member.Id;
            Redraw();
        }

        private void OnSourceChanged()
        {
            // 갱신은 상태만 반영하고 순서는 건드리지 않는다. 정렬은 네 시점에서만 다시 한다(문서 §6).
            SyncKeepingOrder();
            Redraw();
        }

        // ── 목록 만들기 ─────────────────────────────────────────

        private IReadOnlyList<PartyMemberInfo> Latest()
        {
            if (source == null) return Array.Empty<PartyMemberInfo>();

            return showingRecent ? source.GetRecentMembers() : source.GetOnlineMembers();
        }

        /// <summary>정렬을 처음부터 다시 계산한다. 창 열기·탭 전환·새로고침·정렬 화살표에서만 부른다.</summary>
        private void Resort()
        {
            ordered.Clear();
            ordered.AddRange(Latest());
            hasData = source != null && source.IsReady;

            // 1차 그룹(초대 가능 → 불가)은 고정이고, 뒤집히는 것은 2차 닉네임 키뿐이다.
            // 그룹까지 뒤집히면 회색 카드가 맨 위로 올라와 목록이 쓸모없어진다.
            ordered.Sort((a, b) =>
            {
                if (a.CanInvite != b.CanInvite) return a.CanInvite ? -1 : 1;

                int byName = string.Compare(a.Nickname, b.Nickname, StringComparison.CurrentCulture);
                return ascending ? byName : -byName;
            });

            Redraw();
        }

        /// <summary>
        /// 순서는 그대로 두고 내용만 최신으로 맞춘다. 사라진 사람은 빼고, 새로 들어온 사람은 <b>맨 뒤에</b> 붙인다.
        /// </summary>
        private void SyncKeepingOrder()
        {
            IReadOnlyList<PartyMemberInfo> latest = Latest();
            hasData = source != null && source.IsReady;

            // 기존 자리에 최신 정보를 덮어쓰고, 최신 목록에 없는 줄은 제거한다.
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                PartyMemberInfo found = FindById(latest, ordered[i].Id);
                if (found == null) ordered.RemoveAt(i);
                else ordered[i] = found;
            }

            // 새로 들어온 사람만 뒤에 붙인다. 여기서 다시 정렬하면 카드가 발밑에서 움직인다.
            for (int i = 0; i < latest.Count; i++)
            {
                if (FindById(ordered, latest[i].Id) == null) ordered.Add(latest[i]);
            }
        }

        private void Redraw()
        {
            BuildVisible();

            // 걸러졌거나 목록에서 사라진 사람은 선택을 푼다. 카드가 없는데 ⑦만 살아 있으면
            // 보이지 않는 사람에게 초대가 나간다(문서 §6).
            if (selectedId != null && FindVisible(selectedId) == null) selectedId = null;

            EnsureCards(visible.Count);

            for (int i = 0; i < cards.Count; i++)
            {
                bool used = i < visible.Count;
                cards[i].gameObject.SetActive(used);
                if (!used) continue;

                cards[i].SetMember(i, visible[i]);
                cards[i].SetSelected(visible[i].Id == selectedId);
            }

            UpdateEmptyState();
            UpdateInviteButton();
        }

        private void BuildVisible()
        {
            visible.Clear();

            string keyword = searchInput != null ? searchInput.text : null;
            bool filtering = !string.IsNullOrWhiteSpace(keyword);

            for (int i = 0; i < ordered.Count; i++)
            {
                if (filtering &&
                    ordered[i].Nickname.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }

                visible.Add(ordered[i]);
            }
        }

        private void EnsureCards(int count)
        {
            if (cardPrefab == null || listRoot == null) return;

            while (cards.Count < count)
            {
                PartyPlayerCard card = Instantiate(cardPrefab, listRoot);
                card.OnClicked += OnCardClicked;
                cards.Add(card);
            }
        }

        private void UpdateEmptyState()
        {
            // 첫 응답 전에는 아무것도 띄우지 않는다(로딩과 빈 상태를 구분).
            bool show = hasData && visible.Count == 0;

            if (emptyRoot != null) emptyRoot.SetActive(show);
            if (!show || emptyText == null) return;

            string keyword = searchInput != null ? searchInput.text : null;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                emptyText.text = showingRecent
                    ? $"'{keyword}'와 일치하는 플레이어가 없습니다"
                    : $"'{keyword}'와 일치하는 플레이어가 없습니다\n접속 중인 플레이어만 검색됩니다";
            }
            else
            {
                emptyText.text = showingRecent
                    ? "함께 던전을 클리어한 기록이 없습니다"
                    : "접속 중인 다른 플레이어가 없습니다";
            }
        }

        private void UpdateInviteButton()
        {
            if (inviteButton != null) inviteButton.interactable = !inviting && Selected != null;
            if (inviteButtonLabel != null) inviteButtonLabel.text = inviting ? "초대 중…" : "파티 초대";
        }

        private void UpdateSortLabel()
        {
            if (sortLabel != null) sortLabel.text = ascending ? "플레이어 ▲" : "플레이어 ▼";
        }

        private void ScrollToTop()
        {
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
        }

        private PartyMemberInfo FindVisible(string id) => FindById(visible, id);

        private static PartyMemberInfo FindById(IReadOnlyList<PartyMemberInfo> list, string id)
        {
            if (id == null) return null;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == id) return list[i];
            }

            return null;
        }
    }
}
