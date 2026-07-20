using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// HUD 메뉴 아이콘들을 애로우 버튼으로 접었다 펼치는 접이식 툴바.
    /// HorizontalLayoutGroup의 Child Scale Width가 켜진 상태에서 아이콘 스케일을
    /// 순차적으로 줄이고 늘려, 아이콘이 애로우 쪽으로 빨려 들어가는 연출을 만든다.
    /// 애로우는 같은 진행값으로 180도 회전하며 펼침/접힘 상태를 표시한다.
    /// </summary>
    public class FoldableToolbar : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private Button arrowButton;
        [SerializeField] private RectTransform arrow;

        // 펼칠 때 애로우에서 가까운 아이콘부터 나타나도록, 가까운 순서대로 연결한다.
        // 접을 때는 자동으로 반대(먼 아이콘부터) 순서가 된다.
        [SerializeField] private RectTransform[] icons;

        [Header("타이밍")]
        [SerializeField] private float iconDuration = 0.15f;   // 아이콘 하나가 커지거나 작아지는 시간
        [SerializeField] private float stagger = 0.05f;        // 아이콘 간 시작 시차
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // 애로우 스프라이트가 그려진 방향에 따라 인스펙터에서 조정한다.
        [Header("화살표")]
        [SerializeField] private float expandedArrowY = 0f;
        [SerializeField] private float foldedArrowY = 180f;

        private float totalDuration;
        private float time;          // 0 = 완전히 접힘, totalDuration = 완전히 펼침
        private Coroutine routine;

        /// <summary>현재 툴바가 펼쳐진 상태(또는 펼쳐지는 중)인지 여부.</summary>
        public bool IsExpanded { get; private set; } = true;

        private void Awake()
        {
            totalDuration = Mathf.Max(0.01f, stagger * (icons.Length - 1) + iconDuration);

            // 씬에 배치된 기본 상태가 펼침이므로 진행값도 끝 지점에서 시작한다.
            time = totalDuration;
            Apply();
        }

        private void OnEnable() => arrowButton.onClick.AddListener(Toggle);
        private void OnDisable() => arrowButton.onClick.RemoveListener(Toggle);

        /// <summary>
        /// 툴바를 접거나 펼친다. 연출 진행 중에 다시 호출해도
        /// 현재 지점에서 그대로 역재생되므로 연타에 안전하다.
        /// </summary>
        public void Toggle()
        {
            IsExpanded = !IsExpanded;

            if (routine != null)
                StopCoroutine(routine);

            routine = StartCoroutine(AnimateRoutine(IsExpanded ? totalDuration : 0f));
        }

        private IEnumerator AnimateRoutine(float target)
        {
            while (!Mathf.Approximately(time, target))
            {
                // 일시정지(timeScale 0) 중에도 HUD 조작은 가능해야 하므로 unscaled를 쓴다.
                time = Mathf.MoveTowards(time, target, Time.unscaledDeltaTime);
                Apply();
                yield return null;
            }

            time = target;
            Apply();
            routine = null;
        }

        // 진행값(time) 하나에서 아이콘 스케일과 애로우 회전을 모두 계산한다.
        // 상태가 값 하나로 결정되므로 어느 지점에서 끊고 역재생해도 연출이 어긋나지 않는다.
        private void Apply()
        {
            for (int i = 0; i < icons.Length; i++)
            {
                float local = Mathf.Clamp01((time - stagger * i) / iconDuration);
                icons[i].localScale = Vector3.one * ease.Evaluate(local);
            }

            float t = ease.Evaluate(time / totalDuration);
            arrow.localRotation = Quaternion.Euler(0f, Mathf.Lerp(foldedArrowY, expandedArrowY, t), 0f);
        }
    }
}
