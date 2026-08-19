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
        }

        private void Awake()
        {
            self = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>월드 좌표에서 불꽃을 터뜨린다.</summary>
        /// <param name="worldPosition">터질 지점(보통 부서지는 오브젝트의 <c>transform.position</c>)</param>
        public void Play(Vector3 worldPosition) => PlayLocal(WorldToLocal(worldPosition));

        /// <summary>이 레이어 기준 좌표에서 불꽃을 터뜨린다.</summary>
        /// <param name="localPosition">터질 지점(anchoredPosition 기준)</param>
        public void PlayLocal(Vector2 localPosition)
        {
            if (count <= 0) return;
            if (self == null) self = (RectTransform)transform;

            for (int i = 0; i < count; i++)
            {
                Spark spark = Take();

                // 방향은 완전 무작위로 뿌린다. 등간격으로 나누면 바퀴살처럼 읽힌다.
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(speedMin, speedMax);

                spark.Position = localPosition;
                spark.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                spark.Life = Random.Range(lifeMin, Mathf.Max(lifeMin, lifeMax));
                spark.Age = 0f;

                spark.Rect.gameObject.SetActive(true);
                Apply(spark);
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

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
                spark.Position += spark.Velocity * dt;

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

            spark.Rect.sizeDelta = new Vector2(Mathf.Max(minLength, speed * lengthPerSpeed), thickness);

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
