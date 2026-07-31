using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 내용에 맞춰 세로로 늘어나다가 한계 높이에 닿으면 스크롤로 바뀌는 목록 창. 접기/펼치기를 함께 제공한다.
    /// 퀘스트 트래커처럼 "항목 수가 0개일 수도, 10개일 수도 있는" HUD 목록을 위한 것이다.
    ///
    /// Unity의 ContentSizeFitter에는 최대 높이 개념이 없어서, 그냥 붙이면 항목이 늘어나는 만큼
    /// 창이 화면 밖까지 자란다. 그래서 Content의 선호 높이를 직접 재서 maxHeight로 clamp한 값을
    /// 스크롤 영역의 LayoutElement에 써 넣는다. 한계 아래면 창이 내용에 딱 맞고(스크롤 없음),
    /// 한계를 넘으면 창은 고정되고 그때부터 스크롤이 열린다.
    ///
    /// 배치: 창 루트(VerticalLayoutGroup + ContentSizeFitter)에 붙이고 scrollRect/scrollAreaLayout/content를 연결한다.
    /// 항목을 넣거나 뺀 뒤에는 반드시 <see cref="Refresh"/>를 호출한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ExpandableScrollList : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("스크롤을 담당하는 ScrollRect. 접을 때 이 오브젝트를 통째로 끈다.")]
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("ScrollRect가 붙은 오브젝트의 LayoutElement. 여기 preferredHeight를 계산해 써 넣는다.")]
        [SerializeField] private LayoutElement scrollAreaLayout;

        [Tooltip("항목이 쌓이는 Content(Vertical Layout Group + Content Size Fitter).")]
        [SerializeField] private RectTransform content;

        [Header("높이")]
        [Tooltip("이 높이를 넘으면 창이 더 자라지 않고 스크롤이 열린다.")]
        [SerializeField] private float maxHeight = 400f;

        [Tooltip("항목이 0개여도 유지할 최소 높이. 0이면 헤더만 남는다.")]
        [SerializeField] private float minHeight = 0f;

        [Header("접기")]
        [Tooltip("접기/펼치기 버튼(없으면 코드로만 조작).")]
        [SerializeField] private Button foldButton;

        [Tooltip("접힘 상태를 나타낼 화살표. 접으면 collapsedArrowZ만큼 회전한다.")]
        [SerializeField] private RectTransform foldArrow;

        [SerializeField] private float expandedArrowZ = 0f;
        [SerializeField] private float collapsedArrowZ = -90f;

        [Tooltip("시작 시 접힌 상태로 둘지.")]
        [SerializeField] private bool startCollapsed = false;

        private RectTransform self;
        private bool dirty;

        /// <summary>
        /// 접힘 상태가 바뀔 때 발행(true=접힘). 구독자가 이 시점에 항목의 표시 형태를 정한다.
        /// </summary>
        public event Action<bool> CollapsedChanged;

        /// <summary>목록이 접혀 있는지.</summary>
        public bool IsCollapsed { get; private set; }

        /// <summary>현재 내용이 한계 높이를 넘어 스크롤이 열린 상태인지.</summary>
        public bool IsScrollable { get; private set; }

        private void Awake()
        {
            self = (RectTransform)transform;
        }

        private void LateUpdate()
        {
            if (!dirty) return;

            dirty = false;
            Refresh();
        }

        private void OnEnable()
        {
            if (foldButton != null) foldButton.onClick.AddListener(ToggleCollapsed);
            SetCollapsed(startCollapsed);
        }

        private void OnDisable()
        {
            if (foldButton != null) foldButton.onClick.RemoveListener(ToggleCollapsed);
        }

        /// <summary>접힘/펼침을 토글한다. 접기 버튼에 자동으로 연결된다.</summary>
        public void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

        /// <summary>
        /// 접기 버튼과 스크롤 조작의 활성 여부를 지정한다.
        /// 마우스 모드가 아닐 때 꺼서, 커서가 잠긴 상태의 화면 중앙 레이캐스트로 눌리는 것을 막는다.
        /// </summary>
        /// <param name="value">true=조작 가능</param>
        public void SetInteractable(bool value)
        {
            if (foldButton != null) foldButton.interactable = value;
            if (scrollRect != null) scrollRect.enabled = value;
        }

        /// <summary>
        /// 접힘 상태를 지정하고 <see cref="CollapsedChanged"/>를 발행한다.
        ///
        /// <b>이 컴포넌트는 항목을 직접 숨기지 않는다.</b> 접었을 때 무엇을 남길지는 목록마다 다르기 때문이다
        /// (퀘스트 트래커는 고정한 퀘스트만 축소 형태로 남긴다). 소비자가 이벤트를 받아 항목 표시를 정하면,
        /// 그 결과로 줄어든 Content 높이를 이 창이 다시 재서 따라간다.
        /// 전부 숨기는 단순한 목록이라면 이벤트에 항목 루트의 SetActive를 연결하면 된다.
        /// </summary>
        /// <param name="value">true=접힘, false=펼침</param>
        public void SetCollapsed(bool value)
        {
            IsCollapsed = value;

            if (foldArrow != null)
                foldArrow.localRotation = Quaternion.Euler(0f, 0f, value ? collapsedArrowZ : expandedArrowZ);

            CollapsedChanged?.Invoke(value);
            Refresh();
        }

        /// <summary>
        /// 이번 프레임 끝에 <see cref="Refresh"/>를 한 번만 실행하도록 예약한다.
        /// 카드 접힘 연출처럼 매 프레임 높이가 바뀌는 경우, 카드마다 즉시 Refresh를 부르면
        /// 한 프레임에 강제 리빌드가 몇 번씩 돌아 낭비가 크다. 몇 번을 불러도 LateUpdate에서 1회로 합쳐진다.
        /// </summary>
        public void SetDirty() => dirty = true;

        /// <summary>
        /// 항목이 추가·삭제되거나 항목 내용(줄 수)이 바뀐 뒤 호출한다. 창 높이를 다시 계산한다.
        /// 호출하지 않으면 항목만 바뀌고 창 크기는 예전 상태로 남는다.
        /// </summary>
        public void Refresh()
        {
            if (content == null || scrollAreaLayout == null) return;

            if (IsCollapsed)
            {
                RebuildSelf();
                return;
            }

            // Content는 ScrollRect 안에 있어(부모에 Layout Group이 없다) 창 본체와는 별개의 레이아웃 루트로 갱신된다.
            // 두 루트의 갱신 순서는 보장되지 않으므로 '내용 먼저, 창 나중'을 여기서 직접 강제한다.
            // 이 순서가 어긋나면 창 높이가 한 프레임 늦게 따라와 항목이 늘 때마다 크기가 튄다.
            if (content.gameObject.activeInHierarchy)
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            float wanted = LayoutUtility.GetPreferredHeight(content);
            IsScrollable = wanted > maxHeight;
            scrollAreaLayout.preferredHeight = Mathf.Clamp(wanted, minHeight, maxHeight);

            if (scrollRect != null)
            {
                // 내용이 한계보다 짧으면 스크롤을 잠근다. 안 그러면 드래그로 목록이 위아래로 밀린다.
                scrollRect.vertical = IsScrollable;
                if (!IsScrollable) scrollRect.verticalNormalizedPosition = 1f;
            }

            RebuildSelf();
        }

        private void RebuildSelf()
        {
            if (self == null) self = (RectTransform)transform;
            if (gameObject.activeInHierarchy) LayoutRebuilder.ForceRebuildLayoutImmediate(self);
        }
    }
}
