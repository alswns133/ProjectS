using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 카드 한 장이 녹색 빛을 따라 육각 파편으로 분해되는 연출. 카드의 <b>보이는 부분만</b> 담당하고,
    /// 슬롯 높이를 줄여 아래 카드가 자리를 메우는 것은 <see cref="QuestTrackerEntry"/>가 맡는다.
    ///
    /// 셰이더 디졸브를 쓰지 않는다. 카드 안에 Image·TMP·Toggle이 섞여 있어 머티리얼을 전부 갈아끼워야 하고
    /// TMP는 자체 셰이더라 변형을 따로 만들어야 하기 때문이다. 대신 <see cref="RectMask2D"/>의 좌측 padding을
    /// 키워 왼쪽부터 잘라내고, 잘려 나간 자리에 파편을 뿌려 부서지는 것처럼 보이게 한다.
    /// 결과는 거의 같으면서 카드 내용이 무엇이든 그대로 동작한다.
    ///
    /// 방향은 "빛이 왼쪽으로 흐르고 카드가 왼쪽부터 그 흐름을 탄다"로 잡았다. 오른쪽부터 지우면
    /// 파편이 아직 멀쩡한 카드 위를 가로질러 지저분해진다. 상세 팝업 연결선도 카드 왼쪽으로 나가 방향이 맞는다.
    /// </summary>
    public class QuestCardDisintegrateFx : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("분해할 카드 본체.")]
        [SerializeField] private RectTransform visual;

        [Tooltip("카드 본체에 붙인 RectMask2D. 좌측 padding을 키워 왼쪽부터 지운다.")]
        [SerializeField] private RectMask2D visualMask;

        [Header("스윕 라이트")]
        [Tooltip("카드 왼쪽으로 흘러 나가는 빛. 오버레이에 복제해 쓰고 끝나면 파괴한다(비워도 동작).")]
        [SerializeField] private RectTransform sweepPrefab;

        [Tooltip("빛이 카드 왼쪽 끝에서 더 나아갈 거리(px).")]
        [SerializeField] private float sweepTravel = 160f;

        [SerializeField] private float sweepDuration = 0.35f;

        [Header("소거")]
        [Tooltip("빛이 먼저 나가고 카드가 뒤따라 지워지도록 두는 지연(초).")]
        [SerializeField] private float eraseDelay = 0.05f;

        [SerializeField] private float eraseDuration = 0.40f;

        [Header("파편")]
        [Tooltip("카드를 가로로 나눌 칸 수. 소거 경계가 한 칸을 지날 때마다 그 칸의 파편이 튀어나온다.")]
        [SerializeField] private int columns = 6;

        [Tooltip("카드를 세로로 나눌 칸 수.")]
        [SerializeField] private int rows = 3;

        [SerializeField] private float fragmentSize = 14f;
        [SerializeField] private Color fragmentColor = new Color(0.24f, 0.70f, 0.32f, 1f);
        [SerializeField] private float fragmentSpeed = 220f;

        [Tooltip("파편마다 속도를 흔드는 폭. 0이면 전부 같은 속도로 나가 줄지어 보인다.")]
        [SerializeField] private float fragmentSpeedJitter = 90f;

        [Tooltip("위아래로 흩어지는 폭(px/초).")]
        [SerializeField] private float fragmentSpreadY = 70f;

        [SerializeField] private float fragmentAngularSpeed = 180f;
        [SerializeField] private float fragmentLifetime = 0.5f;

        private HexFragmentSpawner spawner;
        private Coroutine routine;

        /// <summary>카드가 완전히 지워지기까지 걸리는 시간(초). 호출부가 후속 처리 타이밍을 맞추는 데 쓴다.</summary>
        public float Duration => eraseDelay + eraseDuration;

        /// <summary>
        /// 연출을 처음부터 재생한다. 이미 재생 중이면 끊고 다시 시작한다.
        /// </summary>
        /// <param name="reverse">
        /// false=분해(카드가 왼쪽부터 지워짐), true=등장(지워진 카드가 빛을 따라 다시 그려짐).
        /// 마스크 진행 방향과 파편이 튀는 순서만 뒤집으면 되므로 같은 루틴을 공유한다.
        /// </param>
        public void Play(bool reverse = false)
        {
            if (visual == null) return;

            if (routine != null) StopCoroutine(routine);

            // 등장은 '완전히 지워진 상태'에서 시작해야 한다. 분해는 그 반대.
            if (visualMask != null)
                visualMask.padding = reverse ? new Vector4(visual.rect.width, 0f, 0f, 0f) : Vector4.zero;

            routine = StartCoroutine(PlayRoutine(reverse));
        }

        /// <summary>소거를 되돌려 카드를 원래대로 보이게 한다. 재생 전과 테스트 반복에 쓴다.</summary>
        public void ResetVisual()
        {
            if (visualMask != null) visualMask.padding = Vector4.zero;
        }

        private IEnumerator PlayRoutine(bool reverse)
        {
            // 오버레이는 씬에 하나만 두므로 처음 한 번만 찾는다.
            if (spawner == null) spawner = FindAnyObjectByType<HexFragmentSpawner>();

            Rect area = visual.rect;
            float columnWidth = area.width / Mathf.Max(1, columns);
            // 분해는 왼쪽 칸부터, 등장은 오른쪽 칸부터 순서대로 파편이 튄다(경계가 지나는 방향이 반대).
            int nextColumn = reverse ? columns - 1 : 0;
            int step = reverse ? -1 : 1;

            RectTransform sweep = SpawnSweep(area);
            Graphic sweepGraphic = sweep != null ? sweep.GetComponent<Graphic>() : null;
            Color sweepColor = sweepGraphic != null ? sweepGraphic.color : Color.white;
            Vector2 sweepStart = sweep != null ? sweep.anchoredPosition : Vector2.zero;

            float total = Duration;
            float elapsed = 0f;

            while (elapsed < total)
            {
                elapsed += Time.unscaledDeltaTime;

                if (sweep != null)
                {
                    float st = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, sweepDuration));
                    sweep.anchoredPosition = sweepStart + Vector2.left * (sweepTravel * st);

                    if (sweepGraphic != null)
                    {
                        Color c = sweepColor;
                        c.a = sweepColor.a * (1f - st);
                        sweepGraphic.color = c;
                    }
                }

                float progress = Mathf.Clamp01((elapsed - eraseDelay) / Mathf.Max(0.01f, eraseDuration));
                // 분해는 0→1(왼쪽부터 지움), 등장은 1→0(지워진 상태에서 되돌림).
                float clip = reverse ? 1f - progress : progress;
                if (visualMask != null) visualMask.padding = new Vector4(clip * area.width, 0f, 0f, 0f);

                // 경계가 지나간 칸부터 순서대로 파편을 방출한다.
                float edgeX = area.xMin + clip * area.width;
                while (IsColumnPassed(nextColumn, columnWidth, area, edgeX, reverse))
                {
                    EmitColumn(nextColumn, columnWidth, area);
                    nextColumn += step;
                }

                yield return null;
            }

            // 프레임이 튀어 마지막 칸들을 건너뛰었어도 파편은 빠짐없이 나가야 한다.
            while (nextColumn >= 0 && nextColumn < columns)
            {
                EmitColumn(nextColumn, columnWidth, area);
                nextColumn += step;
            }

            if (visualMask != null)
                visualMask.padding = reverse ? Vector4.zero : new Vector4(area.width, 0f, 0f, 0f);
            if (sweep != null) Destroy(sweep.gameObject);

            routine = null;
        }

        // 경계가 이 칸의 중심을 지났는지. 분해는 경계가 오른쪽으로, 등장은 왼쪽으로 움직여 판정이 반대다.
        private bool IsColumnPassed(int column, float columnWidth, Rect area, float edgeX, bool reverse)
        {
            if (column < 0 || column >= columns) return false;

            float center = area.xMin + (column + 0.5f) * columnWidth;
            return reverse ? center >= edgeX : center <= edgeX;
        }

        // 한 칸(세로 한 줄)의 파편을 카드 위치에 맞춰 오버레이에 뿌린다.
        private void EmitColumn(int column, float columnWidth, Rect area)
        {
            if (spawner == null) return;

            float x = area.xMin + (column + 0.5f) * columnWidth;
            float rowHeight = area.height / Mathf.Max(1, rows);

            for (int i = 0; i < rows; i++)
            {
                float y = area.yMin + (i + 0.5f) * rowHeight;
                Vector2 position = spawner.WorldToOverlay(visual.TransformPoint(new Vector3(x, y, 0f)));

                Vector2 velocity = new Vector2(
                    -(fragmentSpeed + Random.Range(-fragmentSpeedJitter, fragmentSpeedJitter)),
                    Random.Range(-fragmentSpreadY, fragmentSpreadY));

                spawner.Spawn(position, fragmentSize, fragmentColor, velocity,
                    Random.Range(-fragmentAngularSpeed, fragmentAngularSpeed), fragmentLifetime);
            }
        }

        // 스윕 라이트를 카드 왼쪽 가장자리에 맞춰 오버레이에 띄운다(오버레이에 둬야 뷰포트에 안 잘린다).
        private RectTransform SpawnSweep(Rect area)
        {
            if (sweepPrefab == null || spawner == null) return null;

            RectTransform sweep = Instantiate(sweepPrefab, spawner.Overlay);
            sweep.gameObject.SetActive(true);
            sweep.SetAsLastSibling();
            sweep.sizeDelta = new Vector2(sweep.sizeDelta.x, area.height);
            sweep.anchoredPosition =
                spawner.WorldToOverlay(visual.TransformPoint(new Vector3(area.xMin, area.center.y, 0f)));

            return sweep;
        }
    }
}
