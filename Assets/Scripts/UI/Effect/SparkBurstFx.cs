using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 전기 스파크가 한 점에서 터져 나가는 연출. 가는 불꽃이 진행 방향으로 늘어진 채 튀어 나가
    /// 중력을 받아 떨어지며, 흰빛에서 주황으로 식다가 사그라든다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="HexFragment"/>로는 스파크가 안 된다.</b> 그쪽은 정사각 육각형이 무작위로 회전하며
    /// 등속에 가깝게 날아가는 <b>잔해</b>다. 스파크는 (1) 진행 방향으로 늘어난 가는 선이고,
    /// (2) 중력에 끌려 포물선을 그리며, (3) 색이 식고, (4) 수명·속도 편차가 크다. 넷 다 다르다.
    /// </para>
    /// <para>
    /// <b>길이를 속도에서 뽑는다.</b> 시간 곡선으로 정하면 모두 같은 리듬으로 자랐다 줄어 장식처럼 보인다.
    /// 속도에 매어 두면 빠른 것은 길게, 느려진 것은 짧게 그려져 저절로 제각각이 되고
    /// 감속·낙하와 함께 꼬리가 사그라든다.
    /// </para>
    /// <para>
    /// <b>스프라이트도 프리팹도 쓰지 않는다.</b> 가는 흰 선은 스프라이트 없는 <see cref="Image"/>
    /// 사각형 그대로가 곧 선이다. 풀도 이 컴포넌트가 자식으로 직접 들고 있어,
    /// 별도 스포너 오브젝트나 프리팹 에셋 없이 컴포넌트만 붙이면 동작한다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 연출 중 timeScale이 낮아져도 불꽃은 같은 속도로 튀어야 한다.</para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class SparkBurstFx : MonoBehaviour
    {
        [Header("양")]
        [Tooltip("한 번에 튀는 불꽃 수.")]
        [SerializeField, Min(0)] private int count = 22;

        [Header("속도")]
        [Tooltip("튀어 나가는 속도의 최소·최대(px/초). 폭이 넓어야 멀리 가는 것과 바로 죽는 것이 섞인다.")]
        [SerializeField] private float speedMin = 520f;

        [SerializeField] private float speedMax = 1450f;

        [Tooltip("초당 감속 비율.")]
        [SerializeField, Min(0f)] private float drag = 1.6f;

        [Tooltip("아래로 끌리는 가속도(px/초²). 이게 있어야 곧게 날지 않고 포물선을 그린다.")]
        [SerializeField] private float gravity = 2400f;

        [Header("모양")]
        [Tooltip("속도 1px/초당 꼬리 길이(px). 클수록 길게 늘어진다.")]
        [SerializeField, Min(0f)] private float lengthPerSpeed = 0.05f;

        [Tooltip("꼬리 두께(px). 전기 불꽃은 아주 가늘다.")]
        [SerializeField, Min(0.5f)] private float thickness = 3f;

        [Tooltip("아무리 느려져도 유지할 최소 길이(px).")]
        [SerializeField, Min(0f)] private float minLength = 4f;

        [Header("색·수명")]
        [Tooltip("갓 튀었을 때의 색. 전기 불꽃은 흰빛에 가깝다.")]
        [SerializeField] private Color hotColor = new(1f, 0.97f, 0.88f, 1f);

        [Tooltip("식었을 때의 색.")]
        [SerializeField] private Color coolColor = new(1f, 0.45f, 0.12f, 1f);

        [Tooltip("수명의 최소·최대(초). 편차가 커야 지지직거리는 인상이 난다.")]
        [SerializeField, Min(0.02f)] private float lifeMin = 0.16f;

        [SerializeField, Min(0.02f)] private float lifeMax = 0.5f;

        [Tooltip("깜박이는 정도(0~1). 전기 불꽃은 밝기가 일정하지 않다.")]
        [SerializeField, Range(0f, 1f)] private float flicker = 0.45f;


        // 한 프레임에 인정할 최대 시간(초). 에디터 멈칫·로딩 히치 뒤에는 unscaledDeltaTime이
        // 통째로 튀어(1초 이상) 들어오는데, 그대로 쓰면 연출이 재생되는 대신 한 프레임에
        // 전부 소모돼 건너뛴 것처럼 보인다. 히치 때는 느려질지언정 사라지지는 않게 잘라낸다.
        private const float MaxStep = 0.05f;

        private RectTransform self;
        private Canvas canvas;
        private readonly List<Spark> pool = new();

        // 불꽃 하나의 상태. 클래스 하나에 몰아넣어 풀 관리를 단순하게 유지한다.
        private class Spark
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float Age;

            // 태어난 뒤 실제로 이동한 거리(px). 꼬리 길이의 상한으로 쓴다.
            public float Travelled;
        }

        private void Awake() => EnsureRefs();

        // Awake에 기대지 않고 필요할 때마다 확인한다.
        // 에디터에서 플레이 없이 ContextMenu로 시험 발사할 때는 Awake가 돌지 않아,
        // self/canvas가 null인 채로 좌표 변환에 들어가면 그대로 터진다.
        private void EnsureRefs()
        {
            if (self == null) self = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>월드 좌표에서 불꽃을 터뜨린다.</summary>
        /// <param name="worldPosition">터질 지점(보통 부서지는 오브젝트의 <c>transform.position</c>)</param>
        public void Play(Vector3 worldPosition) => PlayLocal(WorldToLocal(worldPosition));

        /// <summary>이 레이어 기준 좌표에서 불꽃을 사방으로 터뜨린다.</summary>
        /// <param name="localPosition">터질 지점(anchoredPosition 기준)</param>
        public void PlayLocal(Vector2 localPosition)
            => EmitLocal(localPosition, count, Vector2.right, 180f);

        /// <summary>
        /// 월드 좌표에서 <b>방향을 지정해</b> 불꽃을 뿜는다.
        /// 용접 불똥처럼 한쪽으로 쏟아져야 할 때 쓴다(사방으로 터지는 <see cref="Play"/>와 구분).
        /// </summary>
        /// <param name="worldPosition">뿜어져 나올 지점</param>
        /// <param name="amount">이번에 낼 개수</param>
        /// <param name="direction">중심 방향</param>
        /// <param name="spreadDeg">중심 방향 기준 좌우 흔들림(도). 180이면 사방과 같아진다.</param>
        /// <param name="speedScale">속도 배율. 약하게 튀길 때 1보다 작게 준다.</param>
        public void Emit(Vector3 worldPosition, int amount, Vector2 direction,
                         float spreadDeg, float speedScale = 1f)
            => EmitLocal(WorldToLocal(worldPosition), amount, direction, spreadDeg, speedScale);

        /// <summary>
        /// 월드 좌표를 이 레이어의 로컬 좌표로 옮긴다.
        /// 방향까지 직접 계산해야 하는 쪽(<see cref="GaugeHeatSparks"/>)이 같은 좌표계에서 재려고 쓴다.
        /// </summary>
        /// <param name="world">월드 좌표</param>
        /// <returns>이 레이어 기준 좌표</returns>
        public Vector2 ToLocal(Vector3 world)
        {
            EnsureRefs();
            return WorldToLocal(world);
        }

        /// <summary>이 레이어 기준 좌표에서 방향을 지정해 불꽃을 뿜는다.</summary>
        /// <param name="localPosition">뿜어져 나올 지점(anchoredPosition 기준)</param>
        /// <param name="amount">이번에 낼 개수</param>
        /// <param name="direction">중심 방향</param>
        /// <param name="spreadDeg">중심 방향 기준 좌우 흔들림(도). 180이면 사방과 같아진다.</param>
        /// <param name="speedScale">속도 배율</param>
        public void EmitLocal(Vector2 localPosition, int amount, Vector2 direction,
                              float spreadDeg, float speedScale = 1f)
        {
            if (amount <= 0) return;
            EnsureRefs();

            // spreadDeg가 180이면 아래 Random.Range가 정확히 한 바퀴를 덮으므로,
            // 방향 없이 사방으로 뿌리던 기존 동작과 결과가 같다(기존 사용처가 그대로 유지되는 이유).
            float baseAngle = direction.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.y, direction.x)
                : 0f;
            float spread = spreadDeg * Mathf.Deg2Rad;

            for (int i = 0; i < amount; i++)
            {
                Spark spark = Take();

                float angle = baseAngle + Random.Range(-spread, spread);
                float speed = Random.Range(speedMin, speedMax) * speedScale;

                spark.Position = localPosition;
                spark.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                spark.Life = Random.Range(lifeMin, Mathf.Max(lifeMin, lifeMax));
                spark.Age = 0f;
                spark.Travelled = 0f;

                spark.Rect.gameObject.SetActive(true);
                Apply(spark);
            }
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);

            for (int i = 0; i < pool.Count; i++)
            {
                Spark spark = pool[i];
                if (!spark.Rect.gameObject.activeSelf) continue;

                spark.Age += dt;
                if (spark.Age >= spark.Life)
                {
                    spark.Rect.gameObject.SetActive(false);
                    continue;
                }

                spark.Velocity += Vector2.down * (gravity * dt);
                spark.Velocity *= Mathf.Clamp01(1f - drag * dt);

                Vector2 delta = spark.Velocity * dt;
                spark.Position += delta;
                spark.Travelled += delta.magnitude;

                Apply(spark);
            }
        }

        private void Apply(Spark spark)
        {
            float t = spark.Age / spark.Life;
            float speed = spark.Velocity.magnitude;

            spark.Rect.anchoredPosition = spark.Position;

            // 속도가 0에 가까우면 방향을 알 수 없다. 마지막 각도를 유지한다.
            if (speed > 0.01f)
            {
                float angle = Mathf.Atan2(spark.Velocity.y, spark.Velocity.x) * Mathf.Rad2Deg;
                spark.Rect.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            // ★ 꼬리는 "지나온 자취"다. 지나온 거리보다 길게 그리면 안 된다.
            //   길이를 속도만으로 정하면 갓 태어난 불꽃이 첫 프레임부터 최대 길이의 꼬리를 달고 나오는데,
            //   pivot이 앞쪽 끝이라 그 꼬리가 진행 방향 <b>반대쪽</b>으로 뻗는다.
            //   한 점에서 연속으로 뿜을 때는 그 뒤꼬리들이 전부 겹쳐 반대편에 밝은 쐐기로 박힌다.
            // 상한은 "지나온 거리"지만, 태어난 첫 프레임은 그게 0이라 길이도 0이 된다.
            // 최소한 한 프레임분 이동거리는 인정해준다 — 안 그러면 갓 태어난 불꽃이 한 프레임 동안
            // 완전히 사라지고, Update가 돌지 않는 에디트 모드에서는 아예 보이지 않는다.
            float travelled = Mathf.Max(spark.Travelled, speed * Mathf.Clamp(Time.unscaledDeltaTime, 0.008f, MaxStep));
            float length = Mathf.Min(Mathf.Max(minLength, speed * lengthPerSpeed), travelled);
            spark.Rect.sizeDelta = new Vector2(length, thickness);

            Color c = Color.Lerp(hotColor, coolColor, t);
            c.a = (1f - t) * Mathf.Lerp(1f, Random.value, flicker);
            spark.Image.color = c;
        }

        private Spark Take()
        {
            foreach (Spark spark in pool)
            {
                if (!spark.Rect.gameObject.activeSelf) return spark;
            }

            GameObject go = new("Spark", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(self, false);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            // 꼬리가 뒤로 끌리도록 앞쪽 끝을 축으로 삼는다.
            rt.pivot = new Vector2(1f, 0.5f);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;

            Spark created = new() { Rect = rt, Image = image };
            pool.Add(created);
            return created;
        }

        private Vector2 WorldToLocal(Vector3 world)
        {
            // Screen Space - Overlay 캔버스는 카메라를 null로 넘겨야 변환이 맞는다.
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(self, screen, cam, out Vector2 local);
            return local;
        }
    }
}
