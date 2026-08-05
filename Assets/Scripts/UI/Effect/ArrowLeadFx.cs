using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 화살표 강조 잔상 연출(전진형). 잔상이 화살표 <b>본체에서 태어나 가리키는 방향으로 나아가며</b>
    /// 페이드아웃한다. "저쪽으로 가라"처럼 방향을 밀어 주는 인상이 필요할 때 쓴다.
    ///
    /// <see cref="ArrowTrailFx"/>와 짝이며 차이는 잔상이 흐르는 방향뿐이다. 그쪽은 본체 뒤에서 태어나
    /// 본체로 빨려 들어와(주목시키는 인상), 이쪽은 본체에서 앞으로 뻗어 나간다(밀어 주는 인상).
    /// 한 오브젝트에 둘을 같이 붙이면 잔상이 서로 반대로 흘러 지저분해지므로 하나만 쓴다.
    ///
    /// 잔상은 매 프레임 스폰하지 않고 <see cref="Awake"/>에서 필요한 개수만 한 번 복제해 위상(phase)만 돌린다.
    /// 무한 루프 연출이라 개수가 변하지 않는데, 풀에서 꺼내 쓰면 캔버스가 계속 리빌드되기 때문이다.
    /// 개수는 <see cref="travelTime"/>과 <see cref="interval"/>에서 자동으로 나온다 —
    /// 인스펙터에서 "몇 개"가 아니라 "얼마나 자주"로 조절한다.
    ///
    /// 배치: 화살표 본체(<see cref="source"/>)의 <b>부모</b> RectTransform에 붙인다(예: ArrowRoot).
    /// 잔상은 본체 rect 바깥까지 나가므로, 이 오브젝트가 RectMask2D·Mask 안에 있으면 잔상이 잘린다.
    /// 전진형은 나가는 쪽이 잘리면 연출이 통째로 안 보이므로 특히 주의한다.
    /// 대화 중 timeScale이 0이 될 수 있어 unscaled 시간을 쓴다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ArrowLeadFx : MonoBehaviour
    {
        [Header("원본")]
        [Tooltip("복제할 화살표 본체. 비워두면 자식에서 첫 Graphic(Image·TMP_Text 모두 가능)을 찾는다.")]
        [SerializeField] private Graphic source;

        [Header("잔상")]
        [Tooltip("화살표가 가리키는 방향(로컬 기준). 잔상은 본체에서 출발해 이쪽으로 나아간다.")]
        [SerializeField] private Vector2 direction = Vector2.right;

        [Tooltip("본체에서 잔상이 사라지는 지점까지의 거리(px).")]
        [SerializeField, Min(0f)] private float distance = 60f;

        [Tooltip("잔상 하나가 본체에서 끝점까지 가는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.01f)] private float travelTime = 0.5f;

        [Tooltip("다음 잔상이 출발하기까지의 간격(초). 이동 시간보다 크면 잔상 사이에 쉬는 구간이 생긴다.\n" +
                 "필요한 잔상 개수는 이동 시간 / 간격으로 자동 계산한다.")]
        [SerializeField, Min(0.01f)] private float interval = 0.7f;

        [Tooltip("위상 대비 알파(0=본체, 1=끝점). 출발 직후를 0으로 두어야 본체와 겹쳐 두꺼워 보이지 않는다.")]
        [SerializeField] private AnimationCurve alphaOverPhase =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));

        [Tooltip("위상 대비 크기 배율. 나아가면서 작아지면 멀어지며 흩어지는 느낌이 난다.")]
        [SerializeField] private AnimationCurve scaleOverPhase = AnimationCurve.Linear(0f, 1f, 1f, 0.75f);

        [Header("색")]
        [Tooltip("켜면 본체 색을 그대로 쓰고, 끄면 아래 tint를 쓴다.")]
        [SerializeField] private bool useSourceColor = true;

        [Tooltip("useSourceColor가 꺼졌을 때 잔상 색. 알파는 알파 커브에 곱해진다.")]
        [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.6f);

        // 복제된 잔상들. 위치·색만 매 프레임 갱신하므로 컴포넌트를 미리 잡아둔다.
        private RectTransform[] ghostRects;
        private Graphic[] ghostGraphics;

        private RectTransform sourceRect;
        private Vector3 baseScale;       // 본체 스케일. 좌우 반전(음수 스케일) 화살표를 살리기 위해 곱해 쓴다.
        private float time;

        /// <summary>
        /// 잔상 하나가 출발해 다시 자기 차례가 올 때까지의 시간. 잔상 개수만큼 간격이 쌓인 값이다.
        /// 캐싱하지 않는 이유는 <see cref="distance"/>와 같다 — 플레이 중 인스펙터 조절이 바로 먹히게 하기 위함.
        /// </summary>
        private float Cycle => interval * (ghostRects != null ? ghostRects.Length : 1);

        /// <summary>
        /// 잔상 흐름을 처음부터 다시 시작한다. 화살표가 다른 항목으로 점프할 때 호출한다.
        /// 안 부르면 이동 프레임에 잔상이 화면을 가로지르며 끌려간 것처럼 보인다.
        /// </summary>
        public void Restart()
        {
            time = 0f;
            Apply();
        }

        private void Awake()
        {
            if (source == null) source = GetComponentInChildren<Graphic>();

            if (source == null)
            {
                Debug.LogWarning($"{name}: 복제할 화살표 Graphic이 없어 잔상 연출을 끈다.", this);
                enabled = false;
                return;
            }

            // 자기 자신을 복제하면 이 컴포넌트까지 딸려가 잔상이 또 잔상을 만든다.
            if (source.gameObject == gameObject)
            {
                Debug.LogWarning($"{name}: source는 자식 오브젝트여야 한다(ArrowRoot > Arrow 구조).", this);
                enabled = false;
                return;
            }

            CreateGhosts();
            Apply();
        }

        private void OnEnable() => Restart();

        private void Update()
        {
            // 대화창은 timeScale 0에서도 돌 수 있으므로 unscaled.
            // 한 주기마다 되감아 오래 켜둬도 float 정밀도가 떨어지지 않게 한다.
            time = Mathf.Repeat(time + Time.unscaledDeltaTime, Cycle);
            Apply();
        }

        private void CreateGhosts()
        {
            sourceRect = (RectTransform)source.transform;
            baseScale = sourceRect.localScale;

            // 앞선 잔상이 끝점에 닿기 전에 다음 잔상이 출발해야 하는 만큼만 만든다.
            // 간격이 이동 시간보다 길면 1개로 충분하고, 남는 시간이 잔상 사이의 쉬는 구간이 된다.
            int count = Mathf.Max(1, Mathf.CeilToInt(travelTime / interval));

            ghostRects = new RectTransform[count];
            ghostGraphics = new Graphic[count];

            int sourceIndex = sourceRect.GetSiblingIndex();

            for (int i = 0; i < ghostRects.Length; i++)
            {
                GameObject clone = Instantiate(source.gameObject, transform);
                clone.name = $"{source.name}_Lead{i}";

                RectTransform rect = (RectTransform)clone.transform;
                // 본체보다 앞 형제로 두어 잔상이 화살표 아래에 깔리게 한다.
                // 전진형이라 출발 지점에서 본체와 겹치는데, 위에 깔리면 본체 윤곽이 흐려진다.
                rect.SetSiblingIndex(sourceIndex);

                Graphic graphic = clone.GetComponent<Graphic>();
                graphic.raycastTarget = false;   // 잔상이 선택 버튼 클릭을 먹지 않게.

                ghostRects[i] = rect;
                ghostGraphics[i] = graphic;
            }
        }

        private void Apply()
        {
            if (ghostRects == null) return;

            Color baseColor = useSourceColor ? source.color : tint;

            // 경로는 매 프레임 현재 값으로 다시 잡는다. 캐싱해두면 플레이 중 distance·direction을 조절해도
            // 화면이 반응하지 않아 연출 값을 눈으로 맞출 수 없다. 화살표 본체가 움직이는 경우도 여기서 따라간다.
            //
            // ★ ArrowTrailFx와 다른 유일한 지점: 본체가 끝점이 아니라 '출발점'이다.
            //   그쪽은 (본체 - 방향*거리) → 본체, 이쪽은 본체 → (본체 + 방향*거리).
            Vector2 startPosition = sourceRect.anchoredPosition;
            Vector2 endPosition = startPosition + direction.normalized * distance;
            float cycle = Cycle;

            for (int i = 0; i < ghostRects.Length; i++)
            {
                // 잔상마다 interval씩 출발을 늦춰 줄지어 흐르게 한다.
                float elapsed = Mathf.Repeat(time + i * interval, cycle);
                float phase = elapsed / travelTime;

                // 끝점에 닿은 잔상은 다음 차례까지 숨어서 기다린다. 이 쉬는 구간이 잔상 사이의 빈 간격이다.
                bool visible = phase <= 1f;
                if (ghostGraphics[i].enabled != visible) ghostGraphics[i].enabled = visible;
                if (!visible) continue;

                ghostRects[i].anchoredPosition = Vector2.Lerp(startPosition, endPosition, phase);
                ghostRects[i].localScale = baseScale * scaleOverPhase.Evaluate(phase);

                Color c = baseColor;
                c.a = baseColor.a * alphaOverPhase.Evaluate(phase);
                ghostGraphics[i].color = c;
            }
        }

        private void OnValidate()
        {
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
        }
    }
}
