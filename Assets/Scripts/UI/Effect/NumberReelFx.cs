using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 슬롯머신처럼 숫자가 아래로 흘러내리다 감속하고, 목표 숫자를 살짝 지나쳤다가 되돌아와 맞춰지는 연출.
    /// 레벨업 알림의 레벨 숫자에 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>구조</b>: 이 컴포넌트를 <b>창(뷰포트) 역할의 오브젝트</b>에 붙인다. 자식 TMP 하나를 원본으로 삼아
    /// 필요한 개수만큼 복제해 세로로 세운 뒤, 통째로 밀어 올리고 내린다.
    /// 원본을 복제하므로 폰트·크기·색을 인스펙터에서 맞춰 두면 릴 전체가 그 모양을 따른다.
    /// <see cref="RectMask2D"/>가 없으면 붙인다 — 없으면 릴이 창 밖까지 다 보인다.
    /// </para>
    /// <para>
    /// <b>왜 TMP 하나의 text만 갈아끼우지 않는가</b>: 그러면 숫자가 제자리에서 바뀔 뿐 흘러내리지 않는다.
    /// "아래로 내려간다"는 요구는 실제로 세로 이동이 있어야 성립하므로 릴을 만든다.
    /// </para>
    /// <para>
    /// <b>낮은 레벨 처리</b>: 목표가 3인데 12칸을 돌리면 -9부터 세게 된다. 그래서 표시값을
    /// 1~<see cref="wrapMax"/> 범위로 감아 돌린다(예: 99 다음이 1). 슬롯머신처럼 보이면서
    /// 어떤 레벨에서도 시작 숫자가 이상해지지 않는다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class NumberReelFx : MonoBehaviour
    {
        [Header("원본")]
        [Tooltip("복제할 숫자 라벨. 비워두면 자식에서 첫 TMP를 찾는다.")]
        [SerializeField] private TMP_Text template;

        [Tooltip("한 칸 높이(픽셀). 0이면 원본 라벨의 높이를 쓴다.")]
        [SerializeField, Min(0f)] private float cellHeight;

        [Header("회전")]
        [Tooltip("목표까지 지나칠 숫자 칸 수. 클수록 오래 돈다.")]
        [SerializeField, Min(1)] private int spinCells = 14;

        [Tooltip("도는 시간(초).")]
        [SerializeField, Min(0.05f)] private float spinDuration = 0.9f;

        [Tooltip("도는 동안의 감속 곡선. 끝이 완만할수록 슬롯머신처럼 멎는다.")]
        [SerializeField]
        private AnimationCurve spinCurve = new(new Keyframe(0f, 0f, 2.4f, 2.4f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("되돌아오기")]
        [Tooltip("목표를 지나치는 정도(칸). 0이면 그냥 멎는다.")]
        [SerializeField, Min(0f)] private float overshootCells = 0.55f;

        [Tooltip("지나친 뒤 목표로 되돌아오는 시간(초).")]
        [SerializeField, Min(0f)] private float settleDuration = 0.45f;

        [Tooltip("되돌아오면서 목표를 넘나드는 횟수. 0이면 튕김 없이 눌러앉는다. 1 근처가 기계식 릴 느낌.")]
        [SerializeField, Min(0f)] private float settleOscillations = 1.1f;

        [Tooltip("진동이 잦아드는 속도. 클수록 빨리 멎어 묵직하게 느껴지고, 작을수록 오래 흔들린다.")]
        [SerializeField, Min(0.1f)] private float settleDamping = 4.5f;

        [Header("표시")]
        [Tooltip("표시값을 1~이 값 범위로 감아 돌린다. 0이면 감지 않는다(음수도 그대로 표시).")]
        [SerializeField, Min(0)] private int wrapMax = 99;

        [Tooltip("시간을 unscaled로 센다. 레벨업은 연출로 timeScale이 낮아진 중에도 뜬다.")]
        [SerializeField] private bool useUnscaledTime = true;

        private RectTransform self;
        private TMP_Text[] labels;
        private int[] shown;          // 라벨별 마지막 표시값. 같은 값이면 TMP 갱신을 건너뛴다.
        private float cell;
        private int target;
        private Coroutine routine;

        /// <summary>연출이 끝나고 최종적으로 맞춰진 숫자.</summary>
        public int Value => target;

        /// <summary>지금 돌고 있는지.</summary>
        public bool IsSpinning => routine != null;

        private void Awake()
        {
            self = (RectTransform)transform;

            if (template == null) template = GetComponentInChildren<TMP_Text>(true);
            if (template == null)
            {
                Debug.LogError($"{name}: 복제할 TMP 라벨이 없습니다. 자식에 텍스트를 하나 두세요.", this);
                enabled = false;
                return;
            }

            // 릴이 창 밖으로 넘치면 위아래 숫자가 다 보인다. 마스크가 없으면 붙여 준다.
            if (GetComponent<RectMask2D>() == null) gameObject.AddComponent<RectMask2D>();

            cell = cellHeight > 0f ? cellHeight : template.rectTransform.rect.height;
            if (cell <= 0f) cell = self.rect.height;
            if (cell <= 0f) cell = 64f;   // 레이아웃이 아직 안 잡힌 경우의 최후 보루

            BuildLabels();
        }

        /// <summary>
        /// 릴을 돌려 <paramref name="value"/>에 맞춘다. 이미 돌고 있으면 새 목표로 처음부터 다시 돈다.
        /// </summary>
        /// <param name="value">최종적으로 보여줄 숫자</param>
        public void Play(int value)
        {
            target = value;

            if (!isActiveAndEnabled)
            {
                // 꺼진 상태에서는 코루틴이 돌지 않는다. 조용히 사라지지 않게 값만이라도 맞춰 둔다.
                Apply(0f);
                return;
            }

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(SpinRoutine());
        }

        /// <summary>연출 없이 숫자를 즉시 맞춘다(초기 표시·리셋용).</summary>
        /// <param name="value">보여줄 숫자</param>
        public void SetImmediate(int value)
        {
            target = value;

            if (routine != null) StopCoroutine(routine);
            routine = null;

            Apply(0f);
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsSpinning이 영영 true가 된다.
            routine = null;
        }

        private IEnumerator SpinRoutine()
        {
            float start = -spinCells;
            float peak = overshootCells;

            float elapsed = 0f;
            while (elapsed < spinDuration)
            {
                elapsed += Delta;
                float t = Mathf.Clamp01(elapsed / spinDuration);
                Apply(Mathf.LerpUnclamped(start, peak, spinCurve.Evaluate(t)));
                yield return null;
            }
            Apply(peak);

            // 지나친 만큼 되돌아와 목표에 정확히 앉는다. 이 구간이 "넘어갔다 맞춰지는" 느낌을 만든다.
            // 매끄럽게 미끄러져 들어오면 가벼워 보여서, 감쇠 진동으로 반동을 한두 번 남긴다.
            if (settleDuration > 0f && !Mathf.Approximately(peak, 0f))
            {
                elapsed = 0f;
                while (elapsed < settleDuration)
                {
                    elapsed += Delta;
                    float t = Mathf.Clamp01(elapsed / settleDuration);
                    Apply(peak * Damped(t));
                    yield return null;
                }
            }

            Apply(0f);
            routine = null;
        }

        private float Delta => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        /// <summary>
        /// 감쇠 진동. t=0에서 1이고 t=1에서는 사실상 0이라, 지나친 위치에서 목표로 끌려오는 궤적이 된다.
        /// 코사인이 부호를 바꾸는 지점마다 목표를 넘나들어 반동이 생기고, 지수항이 그 폭을 빠르게 줄인다.
        /// <see cref="settleOscillations"/>가 0이면 코사인이 1로 고정되어 튕김 없이 눌러앉는다.
        /// </summary>
        /// <param name="t">되돌아오기 구간의 진행도(0~1)</param>
        /// <returns>지나친 양에 곱할 배율</returns>
        private float Damped(float t)
            => Mathf.Exp(-settleDamping * t) * Mathf.Cos(Mathf.PI * 2f * settleOscillations * t);

        /// <summary>
        /// 릴 위치를 반영한다. <paramref name="position"/>은 칸 단위이며 0이 "목표가 정중앙".
        /// 값이 커질수록 라벨이 아래로 내려가고 표시 숫자는 커진다.
        /// </summary>
        private void Apply(float position)
        {
            if (labels == null) return;

            int half = labels.Length / 2;
            int center = Mathf.RoundToInt(position);

            for (int i = 0; i < labels.Length; i++)
            {
                int slot = center + i - half;

                // y = (slot - position) * cell → position이 커지면 y가 작아진다 = 아래로 흐른다.
                labels[i].rectTransform.anchoredPosition = new Vector2(0f, (slot - position) * cell);

                int value = Wrap(target + slot);
                if (shown[i] == value) continue;

                shown[i] = value;
                labels[i].SetText("{0}", value);
            }
        }

        private int Wrap(int value)
        {
            if (wrapMax <= 0) return value;

            // 1..wrapMax 로 감는다. C#의 %는 음수에서 음수를 내므로 한 번 더 더해 보정한다.
            int span = wrapMax;
            return ((value - 1) % span + span) % span + 1;
        }

        private void BuildLabels()
        {
            // 창 높이를 덮을 만큼 + 지나치는 구간 여유. 최소 3칸은 있어야 위아래가 자연스럽다.
            float viewport = self.rect.height > 0f ? self.rect.height : cell;
            int count = Mathf.Max(3, Mathf.CeilToInt(viewport / cell) + 2 + Mathf.CeilToInt(overshootCells));

            labels = new TMP_Text[count];
            shown = new int[count];

            // 앵커를 바꾸기 전에 실제 폭을 재 둔다.
            // 원본이 부모를 꽉 채우는 stretch 앵커면 sizeDelta.x가 0인데, 그 0을 중앙 앵커에 그대로 쓰면
            // 폭 0짜리 라벨이 된다. 폭 0인 TMP는 글자마다 줄을 바꿔 숫자가 세로로 쌓인다.
            float width = template.rectTransform.rect.width;
            if (width <= 0f) width = self.rect.width;
            if (width <= 0f) width = cell;

            for (int i = 0; i < count; i++)
            {
                TMP_Text label = i == 0 ? template : Instantiate(template, template.transform.parent);
                label.name = $"Cell_{i}";
                label.gameObject.SetActive(true);

                // 칸보다 긴 숫자가 들어와도 줄바꿈으로 세로로 흐르지 않게 막는다.
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.alignment = TextAlignmentOptions.Center;

                RectTransform rt = label.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(width, cell);

                labels[i] = label;
                shown[i] = int.MinValue;   // 첫 Apply에서 반드시 한 번 쓰이도록
            }
        }
    }
}
