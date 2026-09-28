using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 로그인 화면 양옆에 장식용 홀로그램 정보창(<see cref="HoloInfoWindow"/>)을 무작위 크기·간격으로 띄운다.
    /// 로그인 성공 순간 떠 있는 창을 모두 걷고 더 띄우지 않는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>가운데는 비운다.</b> 기준 사각형(로그인 창)의 좌우 범위를 이 레이어 좌표로 옮겨 여백을 더한 세로 띠를
    /// 금지 구역으로 두고, 그 바깥 왼쪽·오른쪽 영역에만 놓는다. 게이지도 로그인 창 폭 안에 있어 함께 비워진다.
    /// 창끼리는 겹치지 않게 몇 번 자리를 다시 뽑고, 끝내 자리가 없으면 이번 차례는 건너뛴다.
    /// </para>
    /// <para>
    /// <b>로그인 버튼을 누른 뒤(인증 중)에는 새로 띄우지 않는다</b> — 게이지 연출로 시선이 모여야 할 때라서.
    /// 승인 연출이 시작되면(<see cref="LoginGaugeView.GrantStarted"/>) 떠 있는 창을 빠르게 꺼 버린다.
    /// 인증에 실패하면 다시 띄우기 시작한다.
    /// </para>
    /// 이 컴포넌트는 화면 전체를 덮는(Stretch) 빈 RectTransform 레이어에 붙인다. 창은 그 아래에 코드로 만들어 풀링한다.
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class HoloInfoSpawner : MonoBehaviour
    {
        [Header("배치")]
        [Tooltip("가운데 금지 구역의 기준(로그인 창). 이 사각형의 좌우 폭 + 여백 안에는 창을 만들지 않는다.")]
        [SerializeField] private RectTransform centerExclusion;

        [Tooltip("금지 구역 양옆으로 더 비울 여백(px).")]
        [SerializeField, Min(0f)] private float exclusionMargin = 70f;

        [Tooltip("화면 가장자리에서 띄울 여백(px).")]
        [SerializeField, Min(0f)] private float edgeMargin = 40f;

        [Tooltip("창끼리 떨어질 최소 간격(px).")]
        [SerializeField, Min(0f)] private float spacing = 16f;

        [Header("크기 (무작위 범위)")]
        [SerializeField] private Vector2 minSize = new Vector2(220f, 110f);
        [SerializeField] private Vector2 maxSize = new Vector2(420f, 280f);

        [Tooltip("본문 글자 크기 범위. 창마다 무작위라 크고 작은 데이터 창이 섞인다.")]
        [SerializeField] private Vector2 bodyFontSizeRange = new Vector2(12f, 16f);

        [Header("타이밍")]
        [Tooltip("다음 창까지의 간격(초) 범위.")]
        [SerializeField] private Vector2 spawnInterval = new Vector2(0.9f, 2.4f);

        [Tooltip("켜진 뒤 첫 창까지의 시간(초).")]
        [SerializeField, Min(0f)] private float firstDelay = 0.6f;

        [Tooltip("동시에 떠 있을 수 있는 최대 개수.")]
        [SerializeField, Min(1)] private int maxWindows = 4;

        [Tooltip("타이핑 속도(글자/초) 범위.")]
        [SerializeField] private Vector2 charsPerSecond = new Vector2(160f, 320f);

        [Tooltip("다 쓴 뒤 머무는 시간(초) 범위.")]
        [SerializeField] private Vector2 holdSeconds = new Vector2(1.4f, 3.2f);

        [Header("로그인 연동")]
        [Tooltip("로그인 게이지. 인증 중엔 새 창을 막고, 승인 순간 모든 창을 걷는다.")]
        [SerializeField] private LoginGaugeView gauge;

        [Tooltip("승인 순간 창이 꺼지는 속도 배율(1 = 평소 꺼짐, 클수록 빠름).")]
        [SerializeField, Min(0.1f)] private float dismissSpeed = 2.5f;

        [Header("모양")]
        [Tooltip("창 글자 폰트. 비우면 TMP 기본 폰트.")]
        [SerializeField] private TMP_FontAsset font;

        [Tooltip("창 그래픽 머티리얼(선택). 가산 합성 UI 머티리얼(ProjectS/UI Additive)을 주면 더 빛처럼 보인다.")]
        [SerializeField] private Material windowMaterial;

        private RectTransform layer;
        private readonly List<HoloInfoWindow> pool = new();
        private readonly List<HoloInfoWindow> active = new();
        private float nextSpawnTime;
        private bool isStopped;

        private void Awake()
        {
            layer = (RectTransform)transform;
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }

        private void OnEnable()
        {
            isStopped = false;
            nextSpawnTime = Time.unscaledTime + firstDelay;
            if (gauge != null) gauge.GrantStarted += OnGrantStarted;
        }

        private void OnDisable()
        {
            if (gauge != null) gauge.GrantStarted -= OnGrantStarted;
        }

        private void Update()
        {
            if (isStopped) return;

            // 인증 중에는 새로 띄우지 않고, 풀린 뒤 곧바로 몰려나오지 않게 다음 차례를 계속 민다.
            if (gauge != null && gauge.IsUnlocking)
            {
                nextSpawnTime = Time.unscaledTime + firstDelay;
                return;
            }

            if (Time.unscaledTime < nextSpawnTime) return;
            nextSpawnTime = Time.unscaledTime + Random.Range(spawnInterval.x, spawnInterval.y);

            if (active.Count >= maxWindows) return;
            if (TryPickArea(out Rect area)) Spawn(area);
        }

        /// <summary>
        /// 떠 있는 창을 모두 빠르게 끄고 더 띄우지 않는다. 로그인 성공 시 자동으로 불린다.
        /// </summary>
        public void DismissAll()
        {
            isStopped = true;

            // 콜백에서 active가 줄어들므로 사본을 돈다.
            foreach (HoloInfoWindow window in active.ToArray())
                window.Dismiss(dismissSpeed);
        }

        private void OnGrantStarted() => DismissAll();

        private void Spawn(Rect area)
        {
            HoloInfoWindow window = pool.Count > 0 ? PopPool() : HoloInfoWindow.Create(layer, font, windowMaterial);
            active.Add(window);
            window.Play(
                area,
                Random.Range(bodyFontSizeRange.x, bodyFontSizeRange.y),
                Random.Range(charsPerSecond.x, charsPerSecond.y),
                Random.Range(holdSeconds.x, holdSeconds.y),
                OnWindowFinished);
        }

        private HoloInfoWindow PopPool()
        {
            HoloInfoWindow window = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return window;
        }

        private void OnWindowFinished(HoloInfoWindow window)
        {
            active.Remove(window);
            pool.Add(window);
        }

        // 왼쪽·오른쪽 빈 영역 중 하나(넓은 쪽일수록 자주)를 골라, 크기·위치를 뽑고 기존 창과 겹치지 않는 자리를 찾는다.
        private bool TryPickArea(out Rect area)
        {
            area = default;

            Rect bounds = layer.rect;
            float yMin = bounds.yMin + edgeMargin;
            float yMax = bounds.yMax - edgeMargin;

            GetExclusionX(out float excludeMin, out float excludeMax);
            float leftMin = bounds.xMin + edgeMargin;
            float leftMax = excludeMin - exclusionMargin;
            float rightMin = excludeMax + exclusionMargin;
            float rightMax = bounds.xMax - edgeMargin;

            float leftWidth = Mathf.Max(0f, leftMax - leftMin);
            float rightWidth = Mathf.Max(0f, rightMax - rightMin);
            if (leftWidth < minSize.x && rightWidth < minSize.x) return false;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                bool useLeft = rightWidth < minSize.x
                    || (leftWidth >= minSize.x && Random.value < leftWidth / (leftWidth + rightWidth));
                float sideMin = useLeft ? leftMin : rightMin;
                float sideWidth = useLeft ? leftWidth : rightWidth;

                float w = Mathf.Min(Random.Range(minSize.x, maxSize.x), sideWidth);
                float h = Mathf.Min(Random.Range(minSize.y, maxSize.y), yMax - yMin);
                if (h < minSize.y) return false;

                float x = Random.Range(sideMin, sideMin + sideWidth - w);
                float y = Random.Range(yMin, yMax - h);
                var candidate = new Rect(x, y, w, h);

                if (!Overlaps(candidate))
                {
                    area = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool Overlaps(Rect candidate)
        {
            Rect padded = new Rect(candidate.x - spacing, candidate.y - spacing,
                                   candidate.width + spacing * 2f, candidate.height + spacing * 2f);
            foreach (HoloInfoWindow window in active)
                if (padded.Overlaps(window.Area)) return true;
            return false;
        }

        // 금지 구역의 좌우 끝을 이 레이어 좌표로 옮긴다. 기준이 없으면 화면 가운데 1/3을 비운다.
        private void GetExclusionX(out float min, out float max)
        {
            if (centerExclusion == null)
            {
                Rect r = layer.rect;
                min = r.xMin + r.width / 3f;
                max = r.xMax - r.width / 3f;
                return;
            }

            var corners = new Vector3[4];
            centerExclusion.GetWorldCorners(corners);
            min = float.MaxValue;
            max = float.MinValue;
            foreach (Vector3 corner in corners)
            {
                float x = layer.InverseTransformPoint(corner).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
        }
    }
}
