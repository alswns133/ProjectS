using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using ProjectS.Data;
using ProjectS.Debugging;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 퀘스트 트래커(QuestList). 진행 중인 퀘스트마다 카드 한 장(<see cref="QuestTrackerEntry"/>)을
    /// Content에 생성하고, 카드의 어느 영역을 눌렀는지에 따라 다르게 반응한다.
    ///   - <b>제목 영역</b> → 고정(핀) 토글. 고정한 퀘스트는 트래커를 접어도 축소 형태로 남는다(최대 <see cref="maxPinned"/>개).
    ///   - <b>내용 영역</b> → 상세 팝업(<see cref="QuestDetailPopup"/>) + 카드 선택 연출.
    ///
    /// 책임을 셋으로 나눠 둔다 — 레이아웃 규칙이 퀘스트 로직 안으로 새어 들어가지 않게 하기 위한 분리다.
    ///   - 창의 크기·스크롤·접기: <see cref="ExpandableScrollList"/>
    ///   - 카드 한 장의 표시와 선택 연출: <see cref="QuestTrackerEntry"/>
    ///   - 그 둘을 언제 부를지(퀘스트 이벤트·고정·선택·팝업): 이 클래스
    ///
    /// 여닫기와 카드 클릭은 <b>마우스 모드에서만</b> 허용한다. 커서가 잠긴 TPS 조작 중에는 화면 중앙
    /// 레이캐스트가 UI에 걸려 의도치 않게 눌릴 수 있어서다(<see cref="PlayerEvents.OnCursorModeChanged"/>).
    /// </summary>
    public class QuestTrackerHud : MonoBehaviour
    {
        /// <summary>인스펙터에 노출하기 위한 구체 이벤트 타입(UnityEvent&lt;T&gt;는 그대로는 직렬화되지 않는다).</summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("목록")]
        [Tooltip("창 본체. 높이 계산·스크롤·접기를 맡는다.")]
        [SerializeField] private ExpandableScrollList window;

        [Tooltip("카드가 쌓일 Content(ScrollRect의 Content). Vertical Layout Group + Content Size Fitter가 붙어 있어야 한다.")]
        [FormerlySerializedAs("container")]
        [SerializeField] private Transform content;

        [Tooltip("퀘스트 한 개를 표시할 카드 프리팹(QuestTrackerEntry 포함).")]
        [FormerlySerializedAs("entryPrefab")]
        [SerializeField] private QuestTrackerEntry cardPrefab;

        [Header("단축키")]
        [Tooltip("트래커를 펼치면서 마우스 모드로 함께 전환하는 키. 다시 누르면 접고 커서를 잠근다.")]
        [SerializeField] private InputAction toggleAction =
            new InputAction("QuestTrackerToggle", InputActionType.Button, "<Keyboard>/j");

        [Header("고정(핀)")]
        [Tooltip("트래커를 접어도 남길 수 있는 퀘스트 최대 개수. 넘으면 가장 오래 고정된 것이 자동으로 풀린다.")]
        [SerializeField] private int maxPinned = 3;

        [Header("상세 팝업")]
        [Tooltip("내용 영역을 눌렀을 때 열 팝업. 같은 캔버스 아래 아무 곳에나 두면 된다(이 클래스가 직접 열고 닫는다).")]
        [SerializeField] private QuestDetailPopup detailPopup;

        [Header("여닫기")]
        [Tooltip("접힘/펼침이 바뀔 때 발행(true=접힘). 사운드 등 추가 연출에 연결(화살표 회전은 창이 처리).")]
        [SerializeField] private BoolEvent onCollapsedChanged = new BoolEvent();

        // 진행 중 퀘스트 → 그 카드. 진행/완료 때 해당 카드를 찾고 지우는 데 쓴다.
        private readonly Dictionary<QuestData, QuestTrackerEntry> cards = new();

        // 고정된 퀘스트를 '고정한 순서대로' 담는다. 가득 찼을 때 무엇을 풀지 정하려면 순서가 필요하다.
        private readonly List<QuestData> pinned = new();

        // 수락한 순서. Dictionary는 순회 순서를 보장하지 않으므로, 정렬·요약의 기준 순서는 이 리스트가 갖는다.
        private readonly List<QuestData> order = new();

        private QuestTrackerEntry selected;
        private bool isMouseMode;
        private Player player;

        /// <summary>목록이 접혀 있는지.</summary>
        public bool IsCollapsed => window != null && window.IsCollapsed;

        private void OnEnable()
        {
            QuestEvents.OnQuestAccepted += OnAccepted;
            QuestEvents.OnQuestProgressUpdated += OnProgress;
            QuestEvents.OnQuestCompleted += OnCompleted;
            PlayerEvents.OnCursorModeChanged += OnCursorModeChanged;
            if (window != null) window.CollapsedChanged += OnWindowCollapsedChanged;

            if (detailPopup != null) detailPopup.Closed += OnPopupClosed;

            toggleAction.Enable();
            toggleAction.started += OnToggleShortcut;

            // 이 트래커는 씬마다 새로 생성되는 씬 오브젝트라, 켜질 때 이미 진행 중인 퀘스트를 다시 그려 넣는다.
            // 카드를 이벤트(수락/진행/완료)로만 만들어서, 이전 씬에서 수락한 퀘스트는 이 복원이 없으면
            // 씬 전환 후 목록에서 사라진다(이벤트가 다시 오지 않기 때문). 커서/접힘 상태를 맞추기 전에 카드를 채운다.
            RebuildActiveQuests();

            // Player가 커서 상태를 알리기 전이므로 실제 커서로 초기 상태를 잡는다.
            ApplyMouseMode(Cursor.lockState != CursorLockMode.Locked);
            // 창의 startCollapsed는 이 구독보다 먼저 처리될 수 있어 초기 표시는 직접 맞춘다.
            ApplyCollapsedView();
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestAccepted -= OnAccepted;
            QuestEvents.OnQuestProgressUpdated -= OnProgress;
            QuestEvents.OnQuestCompleted -= OnCompleted;
            PlayerEvents.OnCursorModeChanged -= OnCursorModeChanged;
            if (window != null) window.CollapsedChanged -= OnWindowCollapsedChanged;

            // UIManager는 씬을 넘어 살아남으므로, 이 씬이 내려갈 때 등록을 반드시 빼야 한다.
            // 안 그러면 popupMap이 파괴된 팝업을 계속 들고 있게 된다.
            if (detailPopup != null)
            {
                detailPopup.Closed -= OnPopupClosed;
                UIManager.Instance?.UnregisterPopup(detailPopup);
            }

            toggleAction.started -= OnToggleShortcut;
            toggleAction.Disable();
        }

        // ---------- 단축키 ----------

        // 한 번에 '창 펼침 + 마우스 모드'를 함께 처리한다. 둘을 따로 하면 Alt → 트래커 클릭 → 카드 클릭으로
        // 상세를 보기까지 3스텝이 걸려, 저널을 없앤 이 설계의 이점이 사라진다.
        private void OnToggleShortcut(InputAction.CallbackContext _)
        {
            bool opening = IsCollapsed;

            // 커서를 먼저 바꿔야 ApplyMouseMode가 돌아 카드가 눌리는 상태로 펼쳐진다.
            if (player == null) player = FindAnyObjectByType<Player>();
            if (player != null) player.SetCursorMode(opening);

            SetCollapsed(!opening);
        }

        // ---------- 마우스 모드 ----------

        private void OnCursorModeChanged(bool mouseMode) => ApplyMouseMode(mouseMode);

        // 마우스 모드가 꺼지면 조작을 잠그고, 열려 있던 상세 팝업과 카드 선택을 되돌린다.
        // (선택된 카드가 접힌 채로 남아 TPS 조작 중에 이상한 모양으로 굳는 것을 막는다.)
        // 고정 상태는 표시 설정이므로 건드리지 않는다 — 접힌 트래커에 계속 남아야 한다.
        private void ApplyMouseMode(bool mouseMode)
        {
            isMouseMode = mouseMode;

            if (window != null) window.SetInteractable(mouseMode);
            foreach (var card in cards.Values) card.SetInteractable(mouseMode);

            if (!mouseMode) CloseDetail();
        }

        // ---------- 여닫기 ----------

        /// <summary>접힘/펼침을 토글한다. 여닫기 버튼의 OnClick에 연결한다.</summary>
        public void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

        /// <summary>
        /// 접힘 상태를 지정한다. 접으면 고정한 퀘스트만 축소 형태(제목+목표 개수)로 남고 나머지는 숨는다.
        /// 카드 데이터는 그대로라 펼치면 전부 복원된다.
        /// </summary>
        /// <param name="value">true=접힘, false=펼침</param>
        public void SetCollapsed(bool value)
        {
            if (window != null) window.SetCollapsed(value);
        }

        // 창이 접힘 상태를 바꿀 때마다 호출된다. 창의 접기 버튼은 ExpandableScrollList에 직접 물려 있으므로,
        // 표시 갱신을 SetCollapsed가 아니라 이 이벤트에 걸어야 버튼으로 접었을 때도 빠지지 않는다.
        private void OnWindowCollapsedChanged(bool collapsed)
        {
            // 접으면 선택 연출을 걸어 둘 카드가 사라지므로 상세 팝업도 함께 닫는다.
            if (collapsed) CloseDetail();

            ApplyCollapsedView();
            onCollapsedChanged?.Invoke(collapsed);
        }

        // 접힘 여부·고정·완료로 카드마다 '보일지 / 축소할지 / +N을 달지'를 정한다.
        //
        // 펼침: 전부 보이고 전부 전체 형태.
        // 접힘: 맨 위에 완료 퀘스트 요약 한 줄(나머지는 +N), 그 아래에 고정 퀘스트.
        //       완료 퀘스트를 여러 줄로 늘어놓으면 그만큼 고정 퀘스트가 아래로 밀려 나가므로 한 줄로 접는다.
        private void ApplyCollapsedView()
        {
            if (!IsCollapsed)
            {
                foreach (var card in cards.Values)
                {
                    card.gameObject.SetActive(true);
                    card.SetCompact(false);
                    card.SetExtraCount(0);
                }

                if (window != null) window.Refresh();
                return;
            }

            // 완료(반납 대기) 퀘스트 중 맨 앞 하나만 대표로 남기고 개수를 센다.
            QuestData headline = null;
            int completedCount = 0;
            foreach (var quest in order)
            {
                if (!quest.IsReadyToTurnIn) continue;

                completedCount++;
                if (headline == null) headline = quest;
            }

            int shownPinned = 0;
            foreach (var quest in order)
            {
                if (!cards.TryGetValue(quest, out QuestTrackerEntry card)) continue;

                bool visible;
                int extra = 0;

                if (quest.IsReadyToTurnIn)
                {
                    // 완료 퀘스트는 고정 여부와 무관하게 요약 줄로만 다룬다(고정 슬롯을 먹지 않는다).
                    visible = quest == headline;
                    extra = completedCount - 1;
                }
                else
                {
                    visible = pinned.Contains(quest) && shownPinned < maxPinned;
                    if (visible) shownPinned++;
                }

                card.gameObject.SetActive(visible);
                if (!visible) continue;

                // 비활성 상태에서는 레이아웃 계산이 안 되므로, 켜 준 다음에 형태를 바꾼다.
                card.SetCompact(true);
                card.SetExtraCount(extra);
            }

            if (window != null) window.Refresh();
        }

        // 완료(반납 대기) 퀘스트를 목록 맨 위로 올린다. 그룹 안에서는 수락 순서를 유지해,
        // 목표를 채울 때마다 남은 퀘스트들의 자리가 덩달아 뒤섞이지 않게 한다.
        private void ApplySortOrder()
        {
            int index = 0;

            foreach (var quest in order)
            {
                if (quest.IsReadyToTurnIn && cards.TryGetValue(quest, out QuestTrackerEntry card))
                    card.transform.SetSiblingIndex(index++);
            }

            foreach (var quest in order)
            {
                if (!quest.IsReadyToTurnIn && cards.TryGetValue(quest, out QuestTrackerEntry card))
                    card.transform.SetSiblingIndex(index++);
            }
        }

        // ---------- 고정(핀) ----------

        // 제목 영역 클릭: 고정을 토글한다. 이미 최대 개수면 가장 오래 고정된 것을 자동으로 푼다.
        // "먼저 하나 푸세요"라고 막으면 플레이어가 무엇을 풀어야 할지 몰라 흐름이 끊기기 때문이다.
        private void OnTitleClicked(QuestTrackerEntry card)
        {
            if (!isMouseMode) return;

            // 접힌 상태에서는 고정을 건드리지 않고 트래커를 펼치기만 한다.
            // 접힘 상태에 남아 있는 카드는 곧 고정된 카드이므로, 여기서 토글을 허용하면
            // 누르자마자 그 카드가 목록에서 사라져 무엇을 눌렀는지 알 수 없게 된다.
            if (IsCollapsed)
            {
                SetCollapsed(false);
                return;
            }

            if (!TryFindQuest(card, out QuestData quest)) return;

            if (pinned.Remove(quest))
            {
                quest.IsPinned = false;   // QuestData에 저장 → 씬 전환 후에도 유지
                card.SetPinned(false);
            }
            else
            {
                if (pinned.Count >= maxPinned)
                {
                    QuestData oldest = pinned[0];
                    pinned.RemoveAt(0);
                    oldest.IsPinned = false;   // 자동으로 풀리는 것도 데이터에 반영
                    if (cards.TryGetValue(oldest, out QuestTrackerEntry oldCard)) oldCard.SetPinned(false);
                }

                quest.IsPinned = true;
                pinned.Add(quest);
                card.SetPinned(true);
            }

            // 접힌 상태에서 고정을 바꿨다면 지금 바로 표시가 달라져야 한다.
            ApplyCollapsedView();
        }

        // ---------- 카드 선택과 상세 팝업 ----------

        // 내용 영역 클릭: 같은 카드를 다시 누르면 닫고, 다른 카드를 누르면 이전 선택을 되돌린 뒤 새로 연다.
        private void OnDetailClicked(QuestTrackerEntry card)
        {
            if (!isMouseMode) return;

            if (selected == card)
            {
                CloseDetail();
                return;
            }

            if (selected != null) selected.SetSelected(false);

            selected = card;
            card.SetSelected(true);

            if (detailPopup == null || !TryFindQuest(card, out QuestData quest)) return;

            UIManager manager = UIManager.Instance;
            if (manager == null)
            {
                DevLog.Warning("[QuestTracker] UIManager가 없어 상세 팝업을 열 수 없습니다. " +
                               "Bootstrap에서 시작하거나, 이 씬에 UIManager 오브젝트를 추가하세요.");
                return;
            }

            // 등록은 멱등이라 열 때마다 불러도 안전하다. Awake 순서에 기대지 않기 위해 여기서 확인한다
            // (UIManager가 이 씬에 있으면 QuestTrackerHud.OnEnable보다 늦게 깨어날 수 있다).
            manager.RegisterPopup(detailPopup);
            // 내용을 먼저 채우고 연다 — 첫 프레임에 빈 팝업이 보이지 않게 하기 위함.
            detailPopup.Setup(quest, card.Visual);

            // 이미 열려 있는데 다시 ShowPopup을 부르면 UIManager의 활성 목록에 중복으로 쌓인다.
            // 카드만 갈아탈 때는 내용만 바꾸면 된다.
            if (!detailPopup.IsVisible) manager.ShowPopup<QuestDetailPopup>();
        }

        // 팝업이 어떤 경로로든 닫혔을 때(X 버튼·Esc·바깥 클릭) 카드 선택만 되돌린다.
        // 여기서 다시 닫기를 요청하면 UIManager.Hide → OnHide → Closed로 되돌아와 무한 재귀가 된다.
        private void OnPopupClosed() => DeselectCard();

        // 선택 해제 + 팝업 닫기. 카드가 사라지거나 마우스 모드가 꺼질 때처럼 이쪽에서 먼저 닫는 경우에 쓴다.
        private void CloseDetail()
        {
            DeselectCard();

            if (detailPopup != null && detailPopup.IsVisible)
                UIManager.Instance?.ClosePopup<QuestDetailPopup>();
        }

        // 접혀 있던 카드를 원래 모습으로 되돌린다. 팝업 상태는 건드리지 않는다.
        private void DeselectCard()
        {
            if (selected == null) return;

            selected.SetSelected(false);
            selected = null;
        }

        private bool TryFindQuest(QuestTrackerEntry card, out QuestData quest)
        {
            foreach (var pair in cards)
            {
                if (pair.Value != card) continue;

                quest = pair.Key;
                return true;
            }

            quest = null;
            return false;
        }

        // ---------- 목록 갱신 ----------

        // 켜질 때(주로 씬 전환으로 새로 생성될 때) QuestManager의 현재 진행 중인 퀘스트를 다시 그려 넣는다.
        // OnAccepted를 그대로 재사용한다 — 진행도·완료 체크를 퀘스트의 현재 상태에서 다시 읽어 만들고,
        // 중복 카드 생성은 OnAccepted 내부의 cards.ContainsKey 가드가 막는다.
        private void RebuildActiveQuests()
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;   // 부트스트랩 없이 직접 씬 테스트 등: 매니저가 없으면 복원 생략

            foreach (QuestData quest in manager.ActiveQuests)
            {
                OnAccepted(quest);

                // 핀 복원: 핀은 QuestData(씬을 넘어 유지)에 저장되므로 새 트래커에서도 되살린다.
                // maxPinned를 넘는 잉여 핀은 데이터에서도 풀어, 표시와 저장 상태가 어긋나지 않게 한다.
                if (!quest.IsPinned) continue;

                if (pinned.Count < maxPinned && cards.TryGetValue(quest, out QuestTrackerEntry card))
                {
                    pinned.Add(quest);
                    card.SetPinned(true);
                }
                else
                {
                    quest.IsPinned = false;
                }
            }

            // 카드를 한꺼번에 만든 직후엔 각 카드의 슬롯 높이(LayoutElement.preferredHeight)가 아직 0이라,
            // Content 높이가 0으로 계산돼 스크롤 뷰포트에 잘려 '활성인데 안 보이는' 상태가 된다.
            // (평소엔 수락 후 OnProgress가 높이를 재줘서 보였다.) 카드마다 높이를 확정하고
            // — 비활성 카드는 MeasureNaturalHeight 내부의 activeInHierarchy 가드로 무시된다 —
            // 창 높이 재계산은 SetDirty로 프레임 끝(LateUpdate)에 한 번 돌게 예약한다(레이아웃이 안정된 뒤).
            foreach (QuestTrackerEntry card in cards.Values)
                card.MeasureNaturalHeight();

            if (window != null) window.SetDirty();

            // 패널이 막 켜진 이 프레임엔 TMP 메시·레이아웃이 아직 준비 전이라 위 측정이 0을 돌려줄 수 있다.
            // 그러면 카드 슬롯 높이가 0으로 남아 Content 높이가 0이 되고, 카드가 스크롤 마스크에 잘려
            // '활성인데 안 보이는' 상태가 된다. 다음 프레임에 다시 재고 창을 갱신해 확실히 높이를 잡는다.
            if (isActiveAndEnabled) StartCoroutine(RemeasureNextFrame());
        }

        // 다음 프레임(=레이아웃/TMP 준비 완료)에 카드 높이를 다시 재고 창을 갱신한다.
        // 씬 전환처럼 '켜지는 프레임에 카드를 한꺼번에 만드는' 경우의 0 높이 문제를 해결하기 위함이다.
        private IEnumerator RemeasureNextFrame()
        {
            yield return null;

            foreach (QuestTrackerEntry card in cards.Values)
                card.MeasureNaturalHeight();

            if (window != null) window.Refresh();
        }

        // 퀘스트 수락 시: 카드를 하나 만들어 목록에 추가한다.
        private void OnAccepted(QuestData quest)
        {
            if (cardPrefab == null || content == null) return;
            if (cards.ContainsKey(quest)) return;   // 중복 생성 방지

            QuestTrackerEntry card = Instantiate(cardPrefab, content);
            card.SetTitle(quest.Title);
            card.SetProgress(BuildProgress(quest));
            card.SetObjectiveCount(BuildObjectiveCount(quest));
            // 수락 직후는 보통 미완료라 체크는 꺼진다(목표 0개 같은 예외 대비해 실제 상태로 초기화).
            card.SetQuestCompletedCheck(quest.IsReadyToTurnIn);
            card.SetPinned(false);
            card.SetInteractable(isMouseMode);

            card.TitleClicked += OnTitleClicked;
            card.DetailClicked += OnDetailClicked;
            card.HeightChanged += OnCardHeightChanged;
            cards.Add(quest, card);
            order.Add(quest);

            ApplySortOrder();
            // 접힌 상태에서 새 퀘스트를 받으면 고정 전이므로 숨겨진 채로 시작한다.
            ApplyCollapsedView();
        }

        // 목표 카운트가 바뀔 때: 해당 카드의 진행 내용과 완료 체크를 갱신한다.
        private void OnProgress(QuestData quest, int cur, int max)
        {
            if (!cards.TryGetValue(quest, out QuestTrackerEntry card)) return;

            card.SetProgress(BuildProgress(quest));
            card.SetObjectiveCount(BuildObjectiveCount(quest));
            // 모든 목표를 채워 '반납 대기(완료가능)'가 되면 체크를 켠다. 실제 반납되면 OnCompleted가 카드를 지운다.
            card.SetQuestCompletedCheck(quest.IsReadyToTurnIn);

            // 이 진행으로 완료가 됐을 수 있다 → 맨 위로 올리고, 접힘 요약 줄(+N)도 다시 계산한다.
            ApplySortOrder();
            ApplyCollapsedView();

            // 진행도 문자열의 줄 수가 바뀌면 카드 높이도 바뀌므로 다시 잰다.
            card.MeasureNaturalHeight();
            if (window != null) window.Refresh();
        }

        // 반납(완료) 시: 그 카드를 목록에서 제거한다.
        private void OnCompleted(QuestData quest)
        {
            if (!cards.TryGetValue(quest, out QuestTrackerEntry card)) return;

            cards.Remove(quest);
            pinned.Remove(quest);
            order.Remove(quest);

            if (card != null)
            {
                if (selected == card) CloseDetail();

                card.TitleClicked -= OnTitleClicked;
                card.DetailClicked -= OnDetailClicked;
                card.HeightChanged -= OnCardHeightChanged;

                // Destroy는 프레임 끝에 처리돼, 그대로 두면 사라진 카드의 자리가 한 프레임 남는다.
                // 비활성 자식은 Layout Group이 즉시 무시하므로 먼저 끄고 지운다.
                card.gameObject.SetActive(false);
                Destroy(card.gameObject);
            }

            // 완료 줄이 하나 빠졌으니 남은 완료 퀘스트의 +N도 다시 계산한다.
            ApplyCollapsedView();
        }

        // 선택 연출로 슬롯 높이가 매 프레임 바뀌는 동안 호출된다.
        // 즉시 Refresh가 아니라 예약이라, 카드 여러 장이 동시에 움직여도 리빌드는 프레임당 1회로 합쳐진다.
        private void OnCardHeightChanged()
        {
            if (window != null) window.SetDirty();
        }

        // 제목 줄에 붙는 요약. 축소 형태에서 유일하게 남는 진행 정보라 한 줄로 짧게 만든다.
        // 예) 목표 1개 "1/3", 목표 2개 "1/3 · 0/2"
        private static string BuildObjectiveCount(QuestData quest)
        {
            var sb = new StringBuilder();
            foreach (var objective in quest.Objectives)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(objective.CurrentCount).Append('/').Append(objective.Target.RequiredCount);
            }

            return sb.ToString();
        }

        // 내용 영역에 들어갈 본문. 목표 카운트는 제목 줄(BuildObjectiveCount)이 이미 보여주므로
        // 여기는 "무엇을 하라"는 한 문장만 둔다. 목표별로 줄을 쌓으면 목표가 늘어나는 만큼 카드가 자라
        // 카드 한 장이 트래커를 다 차지하게 된다.
        //
        // TrackerText가 비어 있으면 Description을 잘라서 쓴다. 기존 JSON을 한 번에 고치지 않고도
        // 넘어가되, 잘린 티(…)가 나야 데이터를 채워야 한다는 걸 눈으로 알 수 있다.
        private static string BuildProgress(QuestData quest)
        {
            QuestTable definition = quest.Definition;

            if (!string.IsNullOrEmpty(definition.TrackerText)) return definition.TrackerText;

            string description = definition.Description;
            if (string.IsNullOrEmpty(description)) return string.Empty;

            return description.Length <= QuestTable.MaxTrackerTextLength
                ? description
                : description.Substring(0, QuestTable.MaxTrackerTextLength) + "…";
        }
    }
}
