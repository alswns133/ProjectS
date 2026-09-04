using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 불에서 떠오른 잉걸불이 부력을 받아 하늘거리며 올라가다 식어 사그라드는 연출.
    /// 불 가림막(<see cref="FireCurtainFx"/>)이 타며 걷힐 때, 타는 경계에서 계속 떠오른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="SparkBurstFx"/>로는 잉걸불이 안 된다.</b> 그쪽은 전기 스파크다 —
    /// (1) 아래로 강하게 끌리고, (2) 진행 방향으로 늘어난 가는 선이며, (3) 0.2초 안팎에 죽는다.
    /// 불똥은 정반대로 (1) 열에 밀려 <b>위로</b> 오르고, (2) 꼬리 없는 점이며, (3) 몇 초를 산다.
    /// 넷 다 반대라 같은 클래스의 파라미터로는 맞출 수 없다.
    /// </para>
    /// <para>
    /// <b>하늘거림은 좌우 흔들림(sway)이 만든다.</b> 그냥 위로 띄우면 불꽃놀이 잔해처럼 곧게 오른다.
    /// 입자마다 다른 위상·주기의 사인파를 가로 속도에 얹어야 상승 기류에 실려 흔들리는 것으로 읽힌다.
    /// 위상이 같으면 전부 한 몸으로 좌우로 쓸려 커튼처럼 보이므로 <b>반드시 입자별로</b> 준다.
    /// </para>
    /// <para>
    /// <b>알파가 명멸한다.</b> 잉걸불은 밝기가 일정하지 않다. 수명 곡선만으로 흐려지면
    /// 조용히 페이드아웃하는 점이 되어 '타고 있는 것'으로 안 읽힌다.
    /// </para>
    /// <para>
    /// <b>스프라이트도 프리팹도 필수가 아니다</b>(<see cref="SparkBurstFx"/>와 같은 방침).
    /// 비워 두면 스프라이트 없는 작은 <see cref="Image"/> 사각형이 그대로 점이 된다.
    /// 부드러운 원형 점을 쓰고 싶으면 <see cref="emberSprite"/>에 물린다.
    /// 풀도 이 컴포넌트가 자식으로 직접 들고 있어 별도 스포너 오브젝트가 필요 없다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 연출 중 timeScale이 낮아져도 불똥은 같은 속도로 떠올라야 한다.</para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class EmberDriftFx : MonoBehaviour
    {
        [Header("모양")]
        [Tooltip("불똥 하나의 스프라이트. 비우면 사각형 그대로 그린다. 부드러운 원형 점을 쓰면 더 낫다.")]
        [SerializeField] private Sprite emberSprite;

        [Tooltip("불똥 크기의 최소·최대(px). 크기 편차가 있어야 원근이 생긴다.")]
        [SerializeField, Min(0.5f)] private float sizeMin = 2.5f;

        [SerializeField, Min(0.5f)] private float sizeMax = 6f;

        [Tooltip("수명이 다할수록 줄어드는 정도(0~1). 1이면 완전히 점이 되어 사라진다.")]
        [SerializeField, Range(0f, 1f)] private float shrink = 0.6f;

        [Header("상승")]
        [Tooltip("갓 떠오를 때의 위쪽 속도 최소·최대(px/초).")]
        [SerializeField] private float riseSpeedMin = 60f;

        [SerializeField] private float riseSpeedMax = 180f;

        [Tooltip("계속 위로 미는 부력 가속도(px/초²). 열기가 식지 않는 한 불똥은 계속 밀려 오른다.")]
        [SerializeField] private float buoyancy = 55f;

        [Tooltip("초당 감속 비율. 높이면 금방 떠오름을 멈추고 제자리에서 하늘거린다.")]
        [SerializeField, Min(0f)] private float drag = 0.55f;

        [Header("하늘거림")]
        [Tooltip("좌우로 흔들리는 폭(px/초). 0이면 곧게 올라가 불꽃놀이 잔해처럼 보인다.")]
        [SerializeField, Min(0f)] private float swayStrength = 70f;

        [Tooltip("좌우로 흔들리는 주기(초당 왕복 수)의 최소·최대. 입자마다 다르게 뽑아 한 몸으로 쓸리지 않게 한다.")]
        [SerializeField, Min(0f)] private float swayFrequencyMin = 0.6f;

        [SerializeField, Min(0f)] private float swayFrequencyMax = 1.8f;

        [Header("색·수명")]
        [Tooltip("갓 떠올랐을 때의 색. 불 가림막의 Hot Color와 맞춘다.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color hotColor = new(1f, 0.78f, 0.3f, 1f);

        [Tooltip("식어 가는 색. 여기까지 식은 뒤 사그라든다.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color coolColor = new(0.85f, 0.18f, 0.03f, 1f);

        [Tooltip("수명의 최소·최대(초). 스파크와 달리 길게 살아야 떠오르는 과정이 보인다.")]
        [SerializeField, Min(0.1f)] private float lifeMin = 1.4f;

        [SerializeField, Min(0.1f)] private float lifeMax = 3.2f;

        [Tooltip("밝기 명멸(0~1). 잉걸불은 밝기가 일정하지 않다. 0이면 조용히 페이드아웃하는 점이 된다.")]
        [SerializeField, Range(0f, 1f)] private float flicker = 0.5f;

        [Tooltip("수명의 앞 몇 %를 밝아지는 데 쓸지(0~1). 갑자기 켜지면 튀어나온 것으로 보인다.")]
        [SerializeField, Range(0f, 0.5f)] private float fadeInRatio = 0.12f;

        [Header("한도")]
        [Tooltip("동시에 살아 있을 수 있는 불똥 수. 풀 크기의 상한이기도 하다.")]
        [SerializeField, Min(1)] private int maxAlive = 220;

        // 한 프레임에 인정할 최대 시간(초). 에디터 멈칫·로딩 히치 뒤에는 unscaledDeltaTime이
        // 통째로 튀어 들어오는데, 그대로 쓰면 불똥이 한 프레임에 화면 밖으로 날아간다.
        private const float MaxStep = 0.05f;

        private RectTransform self;
        private Canvas canvas;
        private readonly List<Ember> pool = new();

        // 불똥 하나의 상태. 클래스 하나에 몰아넣어 풀 관리를 단순하게 유지한다.
        private class Ember
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float Age;
            public float Size;

            // 입자별 흔들림. 위상과 주기가 제각각이어야 한 몸으로 쓸리지 않는다.
            public float SwayPhase;
            public float SwayFrequency;
            public float SwayScale;

            // 명멸 위상. 이것까지 같으면 전부 동시에 깜박여 신호등처럼 보인다.
            public float FlickerPhase;
        }

        private void Awake() => EnsureRefs();

        // Awake에 기대지 않고 필요할 때마다 확인한다(SparkBurstFx와 같은 이유 —
        // 에디터에서 플레이 없이 시험 발사할 때는 Awake가 돌지 않는다).
        private void EnsureRefs()
        {
            if (self == null) self = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>월드 좌표에서 불똥을 띄운다.</summary>
        /// <param name="worldPosition">불똥이 떠오를 지점</param>
        /// <param name="amount">이번에 낼 개수</param>
        public void Emit(Vector3 worldPosition, int amount) => EmitLocal(ToLocal(worldPosition), amount);

        /// <summary>이 레이어 기준 좌표에서 불똥을 띄운다.</summary>
        /// <param name="localPosition">떠오를 지점(anchoredPosition 기준)</param>
        /// <param name="amount">이번에 낼 개수</param>
        public void EmitLocal(Vector2 localPosition, int amount)
        {
            if (amount <= 0) return;
            EnsureRefs();

            for (int i = 0; i < amount; i++) Spawn(localPosition);
        }

        /// <summary>
        /// 원 둘레를 따라 불똥을 띄운다. 타들어가는 경계선처럼 <b>선에서</b> 피어오를 때 쓴다
        /// (<see cref="FireCurtainFx"/>가 걷히는 동안 매 프레임 부른다).
        /// </summary>
        /// <param name="centerLocal">원의 중심(anchoredPosition 기준)</param>
        /// <param name="radius">반지름(px)</param>
        /// <param name="amount">이번에 낼 개수</param>
        /// <param name="jitter">반지름 방향으로 흩뿌릴 폭(px). 0이면 정확히 원 위에 줄지어 티가 난다.</param>
        public void EmitRing(Vector2 centerLocal, float radius, int amount, float jitter = 24f)
        {
            if (amount <= 0) return;
            EnsureRefs();

            for (int i = 0; i < amount; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float r = radius + Random.Range(-jitter, jitter);
                Spawn(centerLocal + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r);
            }
        }

        /// <summary>월드 좌표를 이 레이어의 로컬 좌표로 옮긴다.</summary>
        /// <param name="world">월드 좌표</param>
        /// <returns>이 레이어 기준 좌표(anchoredPosition)</returns>
        public Vector2 ToLocal(Vector3 world)
        {
            EnsureRefs();

            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(self, screen, cam, out Vector2 local);
            return local;
        }

        /// <summary>살아 있는 불똥을 전부 즉시 치운다. 씬 전환·연출 중단에서 부른다.</summary>
        public void Clear()
        {
            for (int i = 0; i < pool.Count; i++) pool[i].Rect.gameObject.SetActive(false);
        }

        private void Spawn(Vector2 localPosition)
        {
            Ember ember = Take();
            if (ember == null) return;   // 한도에 걸리면 이번 것은 포기한다(오래된 것을 지우면 깜박여 티가 난다)

            ember.Position = localPosition;

            // 위로 뜨되 좌우로 조금 벌어져 나온다. 전부 수직으로 출발하면 분수처럼 보인다.
            ember.Velocity = new Vector2(
                Random.Range(-swayStrength, swayStrength) * 0.4f,
                Random.Range(riseSpeedMin, Mathf.Max(riseSpeedMin, riseSpeedMax)));

            ember.Life = Random.Range(lifeMin, Mathf.Max(lifeMin, lifeMax));
            ember.Age = 0f;
            ember.Size = Random.Range(sizeMin, Mathf.Max(sizeMin, sizeMax));

            ember.SwayPhase = Random.Range(0f, Mathf.PI * 2f);
            ember.SwayFrequency = Random.Range(swayFrequencyMin, Mathf.Max(swayFrequencyMin, swayFrequencyMax));
            ember.SwayScale = Random.Range(0.4f, 1f);
            ember.FlickerPhase = Random.Range(0f, 100f);

            ember.Rect.gameObject.SetActive(true);
            Apply(ember);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            float now = Time.unscaledTime;

            for (int i = 0; i < pool.Count; i++)
            {
                Ember ember = pool[i];
                if (!ember.Rect.gameObject.activeSelf) continue;

                ember.Age += dt;
                if (ember.Age >= ember.Life)
                {
                    ember.Rect.gameObject.SetActive(false);
                    continue;
                }

                // 부력은 계속 위로 민다. 스파크의 중력과 부호만 다른 게 아니라,
                // 감속(drag)과 균형을 이뤄 '떠오르다 멈춰 하늘거리는' 종단 속도를 만든다.
                ember.Velocity += Vector2.up * (buoyancy * dt);
                ember.Velocity *= Mathf.Clamp01(1f - drag * dt);

                // 가로 흔들림은 속도에 누적하지 않고 위치에 직접 얹는다.
                // 속도에 더하면 흔들림이 쌓여 한쪽으로 흘러가 버린다.
                float sway = Mathf.Sin((now + ember.SwayPhase) * ember.SwayFrequency * Mathf.PI * 2f)
                             * swayStrength * ember.SwayScale;

                ember.Position += (ember.Velocity + new Vector2(sway, 0f)) * dt;
                Apply(ember);
            }
        }

        private void Apply(Ember ember)
        {
            float t = Mathf.Clamp01(ember.Age / ember.Life);

            ember.Rect.anchoredPosition = ember.Position;

            float size = ember.Size * Mathf.Lerp(1f, 1f - shrink, t);
            ember.Rect.sizeDelta = new Vector2(size, size);

            // 밝기: 짧게 밝아졌다 길게 사그라든다. 여기에 명멸을 곱해 일정하지 않게 만든다.
            float fadeIn = fadeInRatio > 0f ? Mathf.Clamp01(t / fadeInRatio) : 1f;
            float fadeOut = 1f - Mathf.Clamp01((t - fadeInRatio) / Mathf.Max(0.001f, 1f - fadeInRatio));
            float alpha = fadeIn * fadeOut * fadeOut;   // 제곱으로 꼬리를 길게 남긴다

            if (flicker > 0f)
            {
                float f = Mathf.PerlinNoise(ember.FlickerPhase, Time.unscaledTime * 6f);
                alpha *= Mathf.Lerp(1f, f, flicker);
            }

            Color color = Color.Lerp(hotColor, coolColor, t);
            color.a = alpha;
            ember.Image.color = color;
        }

        /// <summary>꺼져 있는 불똥을 재사용하고, 없으면 한도 안에서 새로 만든다.</summary>
        private Ember Take()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].Rect.gameObject.activeSelf) return pool[i];
            }

            if (pool.Count >= maxAlive) return null;

            GameObject go = new("Ember", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(self, false);

            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            Image image = go.GetComponent<Image>();
            image.sprite = emberSprite;
            image.raycastTarget = false;

            Ember ember = new() { Rect = rect, Image = image };
            pool.Add(ember);
            return ember;
        }
    }
}
