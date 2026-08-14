using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 같은 문구가 끝없이 흘러가는 띠. 보스 등장 경고 배너처럼 "계속 흐르는 안내"에 쓴다.
    /// 문구를 필요한 만큼 이어 붙인 라벨 두 장을 나란히 두고 통째로 밀면서, 한 장 폭만큼 갈 때마다 되감는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>마스크를 쓰지 않는다.</b> 이 띠는 기울어진 채로 화면을 가로지르는데,
    /// <see cref="UnityEngine.UI.RectMask2D"/>는 화면 축에 정렬된 사각형으로 잘라내서 회전한 오브젝트에는
    /// 엉뚱하게 먹는다. 대신 <b>띠 폭보다 넉넉히 긴 라벨</b>을 만들어 어느 순간에도 띠 전체가 글자로 덮이게 한다.
    /// 되감기는 항상 덮인 구간 안에서 일어나므로 끊김이 보이지 않는다.
    /// </para>
    /// <para>
    /// 반복 문자열의 길이를 문구 한 단위(<see cref="word"/>+<see cref="separator"/>)의 정수배로 맞춘다.
    /// 정수배가 아니면 되감는 순간 글자 간격이 어긋나 이음매가 드러난다.
    /// </para>
    /// <para>
    /// 시간은 unscaled로 센다. 보스 등장 연출은 컷신·히트스톱으로 timeScale이 낮아진 중에도 돌아야 한다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class ScrollingBand : MonoBehaviour
    {
        [Header("문구")]
        [Tooltip("반복할 라벨 원본. 비워두면 자식에서 첫 TMP를 찾는다.")]
        [SerializeField] private TMP_Text template;

        [Tooltip("반복할 단어.")]
        [SerializeField] private string word = "warning";

        [Tooltip("단어 사이에 넣을 간격 문자열. 공백 수로 밀도를 조절한다.")]
        [SerializeField] private string separator = "     ";

        [Header("흐름")]
        [Tooltip("초당 이동 픽셀. 양수면 오른쪽, 음수면 왼쪽으로 흐른다.")]
        [SerializeField] private float speed = 160f;

        [Tooltip("시간을 unscaled로 센다. 연출 중 timeScale이 낮아져도 흐름이 늘어지지 않는다.")]
        [SerializeField] private bool useUnscaledTime = true;

        private RectTransform self;
        private RectTransform track;
        private float loopWidth;
        private float offset;

        /// <summary>초당 이동 픽셀. 양수면 오른쪽. 런타임에 방향·속도를 바꿀 때 쓴다.</summary>
        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        private void Awake()
        {
            self = (RectTransform)transform;

            if (template == null) template = GetComponentInChildren<TMP_Text>(true);
            if (template == null)
            {
                Debug.LogError($"{name}: 반복할 TMP 라벨이 없습니다. 자식에 텍스트를 하나 두세요.", this);
                enabled = false;
                return;
            }

            Build();
        }

        private void Update()
        {
            if (track == null || loopWidth <= 0f) return;

            float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            offset += speed * delta;

            // [-loopWidth, 0) 범위로 되감는다. 라벨 두 장이 [0, 2·loopWidth]를 덮으므로
            // 이 범위 안에서는 띠 전체가 항상 글자로 채워져 있다.
            offset -= Mathf.Floor(offset / loopWidth + 1f) * loopWidth;

            track.anchoredPosition = new Vector2(offset, 0f);
        }

        private void Build()
        {
            float bandWidth = self.rect.width;
            if (bandWidth <= 0f) bandWidth = 1920f;

            string unit = word + separator;

            // TMP는 문자열 <b>끝</b>의 공백을 폭 계산에서 버린다. "warning     "를 그대로 재면
            // 뒤 공백이 통째로 빠져 한 주기가 실제보다 짧게 나오고, 라벨 두 장이 그만큼 겹친다.
            // 그래서 "단어+간격+단어"에서 "단어"를 빼 한 주기의 실제 이동량을 구한다 —
            // 이러면 간격이 문자열 가운데에 놓여 폭 계산에 정상적으로 포함된다.
            template.textWrappingMode = TextWrappingModes.NoWrap;
            float wordWidth = template.GetPreferredValues(word).x;
            float unitAdvance = template.GetPreferredValues(word + separator + word).x - wordWidth;

            if (unitAdvance <= 0.01f)
            {
                Debug.LogError($"{name}: 문구 폭을 잴 수 없습니다. word가 비었거나 폰트가 없습니다.", this);
                enabled = false;
                return;
            }

            // 띠보다 한 단위 이상 길게 만들어야 되감기 지점이 화면 밖에 머문다.
            int repeat = Mathf.CeilToInt(bandWidth / unitAdvance) + 2;
            loopWidth = repeat * unitAdvance;

            System.Text.StringBuilder sb = new(unit.Length * repeat);
            for (int i = 0; i < repeat; i++) sb.Append(unit);
            string line = sb.ToString();

            track = new GameObject("Track", typeof(RectTransform)).GetComponent<RectTransform>();
            track.SetParent(self, false);
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(0f, 0.5f);
            track.pivot = new Vector2(0f, 0.5f);
            track.sizeDelta = new Vector2(loopWidth * 2f, self.rect.height);
            track.anchoredPosition = Vector2.zero;

            for (int i = 0; i < 2; i++)
            {
                TMP_Text label = i == 0 ? template : Instantiate(template, track);
                label.transform.SetParent(track, false);
                label.name = $"Line_{i}";
                label.gameObject.SetActive(true);

                label.text = line;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.alignment = TextAlignmentOptions.MidlineLeft;

                RectTransform rt = label.rectTransform;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(loopWidth, self.rect.height);
                rt.anchoredPosition = new Vector2(i * loopWidth, 0f);
            }

            offset = -loopWidth * 0.5f;
        }
    }
}
