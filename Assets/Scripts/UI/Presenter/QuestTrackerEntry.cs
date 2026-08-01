using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 추적 목록(QuestList)의 카드 한 장. <see cref="QuestTrackerHud"/>가 퀘스트마다 하나씩 생성한다.
    ///
    /// 카드는 <b>제목 영역</b>과 <b>내용 영역</b>으로 나뉘고, 각각 누를 때 하는 일이 다르다.
    ///   - 제목 영역: 고정(핀) 토글. 고정하면 제목 배경이 오렌지로 바뀌고, 트래커를 접어도 남는다.
    ///   - 내용 영역: 상세 팝업 열기. 이때 카드가 세로로 접히면서 옆으로 튀어나오는 선택 연출이 걸린다.
    /// 접힌 트래커에 남는 축소 형태는 별도 프리팹이 아니라 <see cref="SetCompact"/>로 내용 영역만 끈 모습이다
    /// — 같은 카드를 재사용해야 고정/해제할 때 오브젝트가 갈아끼워지지 않고 제자리에서 바뀐다.
    ///
    /// <b>슬롯(이 컴포넌트가 붙은 루트)과 비주얼(자식)을 분리한 구조다.</b> Vertical Layout Group은
    /// 자식의 위치·크기를 매 리빌드마다 덮어쓰므로, 카드를 그 자리에서 움직이거나 키우는 연출은
    /// 레이아웃에게 즉시 되돌려진다. 그래서 목록에 배치되는 것은 내용이 없는 슬롯뿐이고,
    /// 실제로 보이는 카드(visual)는 그 슬롯 안에서 레이아웃의 통제를 받지 않는 자유 오브젝트로 둔다.
    ///   - 슬롯: LayoutElement.preferredHeight만 갖는다 → 이 값을 줄이면 아래 카드들이 위로 흐른다.
    ///   - 비주얼: Content Size Fitter로 자기 높이를 스스로 잰다 → 그 값이 슬롯의 기준 높이가 된다.
    ///
    /// 선택 연출은 진행값 하나(0=평상시, 1=선택됨)로 모든 값을 계산한다. 연출 도중에 다시 눌러
    /// 역재생해도 상태가 어긋나지 않게 하기 위함이다(<c>FoldableToolbar</c>와 같은 방식).
    /// </summary>
    [RequireComponent(typeof(LayoutElement))]
    public class QuestTrackerEntry : MonoBehaviour
    {
        // 제목 강조색. TMP 리치 텍스트 태그로 감싸므로 titleText의 Rich Text가 켜져 있어야 한다(기본값 켜짐).
        private const string TitleColorHex = "#E5D64B";

        [Header("비주얼")]
        [Tooltip("실제로 보이는 카드 본체. 슬롯의 자식이며 레이아웃 통제를 받지 않는다(Content Size Fitter로 자기 높이를 잰다).")]
        [SerializeField] private RectTransform visual;

        [Tooltip("선택 시 흐리게 만들기 위한 CanvasGroup. visual에 붙인다.")]
        [SerializeField] private CanvasGroup visualGroup;

        [Tooltip("visual의 Content Size Fitter. 선택 연출이 카드 높이를 몰고 갈 동안 잠시 꺼야 한다(비우면 Awake에서 찾는다).")]
        [SerializeField] private ContentSizeFitter visualFitter;

        [Header("제목 영역 — 누르면 고정")]
        [Tooltip("제목 영역 전체를 덮는 버튼.")]
        [SerializeField] private Button titleButton;

        [Tooltip("고정 여부에 따라 색이 바뀌는 제목 배경.")]
        [SerializeField] private Image titleBackground;

        [Tooltip("평소(고정 안 됨) 색.")]
        [SerializeField] private Color unpinnedColor = new Color(0.11f, 0.36f, 0.62f);

        [Tooltip("고정됐을 때 색.")]
        [SerializeField] private Color pinnedColor = new Color(0.93f, 0.53f, 0.13f);

        [Tooltip("모든 목표를 채워 반납할 수 있을 때 색. 고정 색보다 우선한다.")]
        [SerializeField] private Color completedColor = new Color(0.24f, 0.70f, 0.32f);

        [SerializeField] private TMP_Text titleText;            // 퀘스트 제목
        [SerializeField] private TMP_Text objectiveCountText;   // 목표 개수("1/3"). 축소 형태에서도 남는다
        [SerializeField] private Toggle completedToggle;        // 완료 표시. 플레이어가 누르는 용도가 아니다

        [Tooltip("접힘 상태에서 완료 퀘스트를 한 줄로 요약할 때 나머지 개수를 보여줄 텍스트(\"+2\").")]
        [SerializeField] private TMP_Text extraCountText;

        [Header("내용 영역 — 누르면 상세 팝업")]
        [Tooltip("내용 영역 루트. 축소 형태에서는 이 오브젝트만 끄면 카드가 제목 줄만 남는다.")]
        [SerializeField] private GameObject detailArea;

        [Tooltip("내용 영역을 덮는 버튼.")]
        [SerializeField] private Button detailButton;

        [SerializeField] private TMP_Text progressText;         // 진행 내용(줄 수만큼 카드가 늘어난다)

        [Tooltip("진행 내용의 LayoutElement. 두 줄 상한을 여기 preferredHeight로 써 넣는다.")]
        [SerializeField] private LayoutElement progressLayout;

        [Tooltip("진행 내용이 차지할 수 있는 최대 줄 수. 넘치면 …으로 잘린다.")]
        [SerializeField] private int maxProgressLines = 2;

        [Header("선택 연출")]
        [Tooltip("선택됐을 때 슬롯이 줄어들 높이. 카드가 세로로 접히는 정도를 정한다.")]
        [SerializeField] private float selectedSlotHeight = 34f;

        [Tooltip("선택됐을 때 카드가 튀어나올 방향·거리(로컬 좌표). 팝업이 왼쪽에 뜨므로 보통 x는 음수.")]
        [SerializeField] private Vector2 selectedOffset = new Vector2(-95f, 0f);

        // 1보다 크게 두면 카드가 아래 이웃 위로 겹치는데, 계층 순서는 이제 정렬(완료 퀘스트 최상단)이
        // 소유하므로 겹친 카드를 위로 끌어올릴 수 없다. 그리는 순서까지 바꾸려면 비주얼에
        // Canvas(Override Sorting) + GraphicRaycaster를 붙여 sortingOrder를 올려야 한다.
        [Tooltip("선택됐을 때 카드 배율. 1보다 작으면 물러나는 느낌, 크면 튀어나오는 느낌(겹침 주의).")]
        [SerializeField] private float selectedScale = 0.85f;

        [Tooltip("선택됐을 때 카드 투명도.")]
        [SerializeField] private float selectedAlpha = 0.75f;

        [SerializeField] private float duration = 0.18f;
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("완료 분해 연출")]
        [Tooltip("카드가 파편으로 흩어지는 연출. 비워 두면 슬롯만 줄어들고 분해는 생략된다.")]
        [SerializeField] private QuestCardDisintegrateFx disintegrateFx;

        [Tooltip("분해가 시작되고 나서 슬롯이 줄기 시작할 때까지의 지연(초). 소거가 끝나기 전에 겹쳐야 매끄럽다.")]
        [SerializeField] private float collapseDelay = 0.30f;

        [Tooltip("슬롯 높이가 0까지 줄어드는 시간(초). 아래 카드들이 자리를 메우는 속도다.")]
        [SerializeField] private float collapseDuration = 0.20f;

        private LayoutElement slot;
        private float animProgress;    // 0 = 평상시, 1 = 완전히 선택됨
        private float target;
        private float naturalHeight;   // 비주얼이 스스로 잰 높이(내용에 따라 변한다)
        private Vector2 basePosition;  // 평상시 비주얼 위치(프리팹에서 0이 아닐 수 있다)
        private bool interactable = true;
        private bool disintegrating;   // 분해 중에는 슬롯 높이의 주인이 연출로 넘어간다

        /// <summary>제목 영역이 눌렸을 때 발행. 고정 토글에 쓴다.</summary>
        public event Action<QuestTrackerEntry> TitleClicked;

        /// <summary>내용 영역이 눌렸을 때 발행. 상세 팝업 열기에 쓴다.</summary>
        public event Action<QuestTrackerEntry> DetailClicked;

        /// <summary>슬롯 높이가 바뀌었음을 알린다. <see cref="QuestTrackerHud"/>가 창 갱신을 예약한다.</summary>
        public event Action HeightChanged;

        /// <summary>고정(핀)된 상태인지. 고정된 카드만 접힌 트래커에 남는다.</summary>
        public bool IsPinned { get; private set; }

        /// <summary>모든 목표를 채워 반납할 수 있는 상태인지. 제목이 녹색이 되고 목록 맨 위로 올라간다.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>축소(제목 줄만) 형태인지.</summary>
        public bool IsCompact { get; private set; }

        /// <summary>현재 선택(접힘+튀어나옴) 상태인지.</summary>
        public bool IsSelected { get; private set; }

        /// <summary>연결선을 그릴 기준이 되는 카드 본체의 RectTransform. 상세 팝업이 참조한다.</summary>
        public RectTransform Visual => visual;

        private void Awake()
        {
            slot = GetComponent<LayoutElement>();

            if (visual != null)
            {
                basePosition = visual.anchoredPosition;
                // 인스펙터에 연결하지 않은 기존 프리팹도 그대로 동작하게 여기서 찾아 둔다.
                if (visualFitter == null) visualFitter = visual.GetComponent<ContentSizeFitter>();
            }

            // 상태 표시 전용이라 클릭을 막는다. 눌러서 꺼버리면 표시가 실제 퀘스트 상태와 어긋난다.
            // (Toggle의 Transition을 None으로 둬야 비활성 색으로 어두워지지 않는다.)
            if (completedToggle != null) completedToggle.interactable = false;

            // 상한을 넘긴 문장은 잘린 티(…)가 나야 한다. 그냥 두면 세 번째 줄이 카드 밖으로 나가
            // 마스크에 글자 중간이 뭉텅 잘린다.
            if (progressText != null) progressText.overflowMode = TextOverflowModes.Ellipsis;

            ApplyTitleColor();
        }

        private void OnEnable()
        {
            if (titleButton != null) titleButton.onClick.AddListener(OnTitleClicked);
            if (detailButton != null) detailButton.onClick.AddListener(OnDetailClicked);
        }

        private void OnDisable()
        {
            if (titleButton != null) titleButton.onClick.RemoveListener(OnTitleClicked);
            if (detailButton != null) detailButton.onClick.RemoveListener(OnDetailClicked);
        }

        // ---------- 내용 ----------

        /// <summary>제목을 설정한다(카드 생성 시 1회). 지정 강조색으로 표시한다.</summary>
        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = $"<color={TitleColorHex}>{title}</color>";
        }

        /// <summary>진행 내용 텍스트를 갱신한다(목표 카운트가 바뀔 때마다).</summary>
        public void SetProgress(string text)
        {
            if (progressText != null) progressText.text = text;
        }

        /// <summary>제목 줄에 표시할 목표 개수를 갱신한다. 축소 형태에서 유일하게 남는 진행 정보다.</summary>
        /// <param name="text">"1/3" 같은 요약 문자열</param>
        public void SetObjectiveCount(string text)
        {
            if (objectiveCountText != null) objectiveCountText.text = text;
        }

        /// <summary>
        /// 완료(반납 대기) 여부를 토글과 제목 색에 반영한다.
        /// onValueChanged를 태우지 않는다 — 표시 갱신이 다른 로직을 건드리지 않게 하기 위함이다.
        /// </summary>
        /// <param name="check">true=완료 표시 켬</param>
        public void SetQuestCompletedCheck(bool check)
        {
            IsCompleted = check;

            if (completedToggle != null) completedToggle.SetIsOnWithoutNotify(check);
            ApplyTitleColor();
        }

        /// <summary>
        /// 제목 옆 "+N"을 지정한다. 접힘 상태에서 완료 퀘스트를 한 줄로 요약할 때, 화면에 못 실은 나머지 개수를 알린다.
        /// 0 이하면 숨긴다.
        /// </summary>
        /// <param name="count">숨겨진 나머지 개수</param>
        public void SetExtraCount(int count)
        {
            if (extraCountText == null) return;

            bool show = count > 0;
            extraCountText.gameObject.SetActive(show);
            if (show) extraCountText.text = $"+{count}";
        }

        /// <summary>
        /// 비주얼이 스스로 잰 높이를 슬롯의 기준 높이로 삼는다. 내용(줄 수)이나 축소 여부가 바뀐 뒤 호출한다.
        /// 이 값을 갱신하지 않으면 텍스트가 늘어도 슬롯은 예전 높이를 유지해 카드가 겹쳐 보인다.
        /// </summary>
        public void MeasureNaturalHeight()
        {
            if (disintegrating) return;
            if (visual == null || !visual.gameObject.activeInHierarchy) return;

            // 폭이 정해져야 TMP가 몇 줄이 되는지 알 수 있으므로, 먼저 한 번 세우고 상한을 적용한 뒤 다시 세운다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(visual);
            ClampProgressLines();
            LayoutRebuilder.ForceRebuildLayoutImmediate(visual);

            naturalHeight = LayoutUtility.GetPreferredHeight(visual);
            Apply();
        }

        // 진행 내용의 높이를 최대 줄 수로 자른다. LayoutElement는 Graphic(우선순위 0)보다 우선순위가 높아
        // 여기 써 넣은 값이 TMP가 보고하는 높이를 이긴다.
        //
        // 데이터 쪽에서 이미 TrackerText 길이를 막고 있으므로(QuestTable.Validate) 평소에는 걸릴 일이 없다.
        // 이건 폰트·해상도·언어가 바뀌어 예상보다 줄이 늘어났을 때를 위한 안전장치다.
        private void ClampProgressLines()
        {
            if (progressText == null || progressLayout == null) return;
            if (maxProgressLines < 1) return;

            TMP_FontAsset font = progressText.font;
            if (font == null || font.faceInfo.pointSize <= 0f) return;

            float lineHeight = font.faceInfo.lineHeight / font.faceInfo.pointSize * progressText.fontSize;
            float cap = maxProgressLines * lineHeight;

            progressLayout.preferredHeight = Mathf.Min(progressText.preferredHeight, cap);
        }

        /// <summary>클릭 가능 여부. 마우스 모드가 아닐 때 꺼서 오조작을 막는다.</summary>
        /// <param name="value">true=클릭 가능</param>
        public void SetInteractable(bool value)
        {
            interactable = value;
            ApplyInteractable();
        }

        // ---------- 고정 / 축소 ----------

        /// <summary>고정 상태를 지정한다. 제목 배경색이 바뀌고, 접힌 트래커에 남을지가 결정된다.</summary>
        /// <param name="value">true=고정</param>
        public void SetPinned(bool value)
        {
            IsPinned = value;
            ApplyTitleColor();
        }

        /// <summary>
        /// 축소(제목 줄만) 형태로 전환한다. 내용 영역을 끄면 Content Size Fitter가 카드 높이를 알아서 줄인다.
        /// 축소 상태에서는 내용 영역이 없으므로 상세 팝업도 열 수 없다.
        /// </summary>
        /// <param name="compact">true=축소</param>
        public void SetCompact(bool compact)
        {
            IsCompact = compact;

            if (detailArea != null) detailArea.SetActive(!compact);
            ApplyInteractable();
            MeasureNaturalHeight();
        }

        // ---------- 선택 연출 ----------

        /// <summary>
        /// 선택 상태를 지정한다. 슬롯이 <see cref="selectedSlotHeight"/>까지 줄어들면서
        /// 카드 본체는 커지고 옆으로 밀려 나온다. 연출 중 다시 호출해도 그 지점에서 역재생된다.
        /// </summary>
        /// <param name="value">true=선택(접힘+튀어나옴)</param>
        /// <param name="instant">true면 연출 없이 즉시 반영(목록을 다시 그릴 때 사용)</param>
        public void SetSelected(bool value, bool instant = false)
        {
            IsSelected = value;
            target = value ? 1f : 0f;

            if (instant)
            {
                animProgress = target;
                Apply();
            }
        }

        // ---------- 완료 분해 연출 ----------

        /// <summary>
        /// 카드를 파편으로 흩뜨리며 없앤다. 분해가 진행되는 동안 슬롯 높이를 0까지 줄여
        /// 아래 카드들이 자리를 메우게 한다. 연출이 끝나면 onDone을 부르므로,
        /// 호출부는 그때 카드를 파괴하고 목록을 재정렬하면 된다.
        /// </summary>
        /// <param name="onDone">연출 종료 콜백(카드는 아직 살아 있는 상태로 호출된다)</param>
        public void PlayDisintegrate(Action onDone)
        {
            if (disintegrating) return;

            disintegrating = true;
            SetInteractable(false);
            if (disintegrateFx != null) disintegrateFx.Play();

            StartCoroutine(DisintegrateRoutine(onDone));
        }

        private IEnumerator DisintegrateRoutine(Action onDone)
        {
            float fxDuration = disintegrateFx != null ? disintegrateFx.Duration : 0f;
            float total = Mathf.Max(collapseDelay + collapseDuration, fxDuration);
            float startHeight = slot != null ? slot.preferredHeight : 0f;
            float elapsed = 0f;

            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01((elapsed - collapseDelay) / Mathf.Max(0.01f, collapseDuration));
                if (slot != null) slot.preferredHeight = Mathf.Lerp(startHeight, 0f, ease.Evaluate(t));
                HeightChanged?.Invoke();

                yield return null;
            }

            if (slot != null) slot.preferredHeight = 0f;
            HeightChanged?.Invoke();

            onDone?.Invoke();
        }

        // 인스펙터 우클릭으로 재생해 감각을 확인한다(플레이 모드에서만 의미가 있다).
        [ContextMenu("분해 연출 테스트")]
        private void TestDisintegrate()
        {
            disintegrating = false;
            if (disintegrateFx != null) disintegrateFx.ResetVisual();
            PlayDisintegrate(null);
        }

        private void Update()
        {
            if (disintegrating) return;
            if (Mathf.Approximately(animProgress, target)) return;

            // 일시정지(timeScale 0) 중에도 HUD 조작은 가능해야 하므로 unscaled를 쓴다.
            float step = duration <= 0f ? 1f : Time.unscaledDeltaTime / duration;
            animProgress = Mathf.MoveTowards(animProgress, target, step);
            Apply();
        }

        // 진행값 하나에서 슬롯 높이·배율·위치·투명도를 모두 계산한다.
        // 상태가 값 하나로 결정되므로 어느 지점에서 끊고 역재생해도 연출이 어긋나지 않는다.
        private void Apply()
        {
            // 분해 중에는 슬롯 높이를 연출이 몰고 간다. 여기서 같이 쓰면 두 값이 매 프레임 서로를 덮어쓴다.
            if (disintegrating) return;

            float t = ease.Evaluate(animProgress);
            float height = Mathf.Lerp(naturalHeight, selectedSlotHeight, t);

            if (slot != null) slot.preferredHeight = height;

            if (visual != null)
            {
                // 슬롯만 줄이면 아래 카드는 올라오지만 카드 자체는 그대로라 요약글이 계속 보인다.
                // 보이는 카드도 같이 줄여야 visual의 RectMask2D가 내용 영역을 잘라내 '접히는' 그림이 된다.
                // Content Size Fitter를 켜 둔 채로는 매 프레임 내용 높이로 되돌리므로 연출 중에는 꺼야 한다.
                if (visualFitter != null) visualFitter.enabled = t <= 0f;
                if (t > 0f) visual.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

                visual.localScale = Vector3.one * Mathf.Lerp(1f, selectedScale, t);
                visual.anchoredPosition = Vector2.Lerp(basePosition, basePosition + selectedOffset, t);
            }

            if (visualGroup != null) visualGroup.alpha = Mathf.Lerp(1f, selectedAlpha, t);

            // 슬롯 높이가 바뀌었으니 창 전체 높이도 다시 재야 한다.
            // 즉시가 아니라 예약이라, 카드 여러 장이 동시에 움직여도 리빌드는 프레임당 1회로 합쳐진다.
            HeightChanged?.Invoke();
        }

        // 완료 > 고정 > 기본 순으로 색이 결정된다. 완료를 위에 두는 이유는
        // "반납하러 가라"가 "이건 추적 중이다"보다 플레이어가 지금 해야 할 행동에 가깝기 때문이다.
        private void ApplyTitleColor()
        {
            if (titleBackground == null) return;

            titleBackground.color = IsCompleted ? completedColor
                                  : IsPinned    ? pinnedColor
                                                : unpinnedColor;
        }

        // 축소 상태에서는 내용 영역 자체가 꺼져 있으므로 상세 버튼도 함께 잠근다.
        private void ApplyInteractable()
        {
            if (titleButton != null) titleButton.interactable = interactable;
            if (detailButton != null) detailButton.interactable = interactable && !IsCompact;
        }

        private void OnTitleClicked() => TitleClicked?.Invoke(this);
        private void OnDetailClicked() => DetailClicked?.Invoke(this);
    }
}
