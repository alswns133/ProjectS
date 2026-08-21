using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스(레이드 포함) HP 바 오버레이 뷰. 화면 상단중앙에 떠서 보스 이름·HP 수치·남은 줄 수(X N)·
    /// 그로기 게이지를 그린다. <see cref="BossHpPresenter"/>가 이벤트를 받아 이 뷰의 메서드를 호출한다.
    ///
    /// 로아식 다세그먼트 모델: 총 HP를 <c>SegmentCount</c>줄로 나눠(한 줄 = MaxHp/SegmentCount) 남은 줄 수를 "X N"으로,
    /// 현재 줄 안의 채움 비율로 바를 그린다. 줄 수에 따라 바 색이 순환하고(<see cref="segmentPalette"/>),
    /// 방금 깎인 구간은 밝은 트레일(지연 바)로 잠깐 남았다가 스르륵 따라 빠진다.
    ///
    /// <b>표시/숨김은 barRoot 자식만 토글한다.</b> 이 뷰(+프레젠터)가 붙은 루트는 이벤트를 받으려 항상 활성이어야 하므로,
    /// 실제 바 계층만 켜고 끈다(오버레이 알림과 같은 결).
    /// </summary>
    public class BossHpView : MonoBehaviour
    {
        [Header("표시 토글")]
        // 바 계층 전체. 보스 등장 시 켜고 퇴장 시 끈다(루트는 이벤트 수신용으로 항상 활성).
        [SerializeField] private GameObject barRoot;

        [Header("텍스트")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI hpValueText;
        [SerializeField] private TextMeshProUGUI segmentCountText;

        [Header("HP 바")]
        // 맨 뒤 배경. 다음 줄 색으로 칠해져, 현재 줄(hpFill)이 깎인 만큼 뒤에서 드러난다(로아 다단 바).
        // 마지막 줄(X1)·단일 바에서는 뒤에 줄이 없어 emptyBehindColor(어두운 색)로 칠한다.
        [SerializeField] private Image hpBackground;
        // 현재 줄 채움을 즉시 그리는 앞면. fillAmount로 비율을 그린다(Filled 타입).
        [SerializeField] private Image hpFill;
        // 방금 깎인 구간을 잠깐 남기는 지연 바(현재 줄 색을 밝게). hpFill과 배경 사이에 깔린다.
        [SerializeField] private Image hpTrail;

        [Header("그로기")]
        [SerializeField] private Image groggyFill;
        // 특수 패턴(레이드 기믹)으로 그로기가 잠겼을 때 켜는 자물쇠 표시.
        [SerializeField] private GameObject groggyLockIcon;

        [Header("색상 규칙")]
        // 남은 줄 수에 따라 순환하는 바 색. index = (남은 줄 수 - 1) % 길이. 비우면 singleBarColor를 쓴다.
        // 색·순서·개수는 기획 튜닝값이라 인스펙터에서 자유롭게 바꾼다(로아식 색 순환의 근사).
        [SerializeField]
        private Color[] segmentPalette =
        {
            new Color(0.62f, 0.20f, 0.85f, 1f),   // 보라
            new Color(0.95f, 0.78f, 0.20f, 1f),   // 노랑
            new Color(0.95f, 0.45f, 0.15f, 1f),   // 주황
            new Color(0.85f, 0.18f, 0.18f, 1f),   // 빨강
        };
        // 세그먼트가 없을 때(SegmentCount=0, 단일 바)의 바 색.
        [SerializeField] private Color singleBarColor = new Color(0.85f, 0.18f, 0.18f, 1f);
        // 마지막 줄(X1) 뒤·단일 바 뒤에 칠할 색. 더 이상 드러날 다음 줄이 없을 때의 어두운 배경.
        [SerializeField] private Color emptyBehindColor = new Color(0.05f, 0.05f, 0.07f, 0.85f);
        // 트레일(지연 바)을 현재 줄 색에서 얼마나 밝게 뽑을지(0=그대로, 1=흰색). "방금 깎인" 밝은 띠로 읽히게.
        [SerializeField, Range(0f, 1f)] private float trailBrightness = 0.35f;

        [Header("트레일 연출")]
        // 트레일이 현재 fill까지 따라 내려오는 속도(fillAmount 단위/초). 클수록 빨리 붙는다.
        [SerializeField, Min(0.01f)] private float trailLerpSpeed = 1.5f;
        // 깎인 직후 트레일이 머무는 시간(초). 이 시간이 지나야 따라 내려오기 시작한다.
        [SerializeField, Min(0f)] private float trailHoldSeconds = 0.15f;

        [Header("드레인 연출")]
        // 바가 목표 HP로 따라가는 속도(0~1). 매 순간 남은 거리(현재 표시값↔목표)의 이 비율만큼 좁힌다(프레임률 보정).
        // 작을수록 천천히 밀리고 1이면 즉시. 남은 거리 기준이라 보스 크기·줄 수와 무관하게 같은 감으로 보인다.
        // 0.1~0.2 정도가 "줄이 밀리는" 느낌. (하한 0.1로 막아 얼어붙지 않게 한다.)
        [SerializeField, Range(0.1f, 1f)] private float drainSpeed = 0.12f;

        [Header("표기 형식")]
        [SerializeField] private string segmentCountFormat = "X {0}";
        [SerializeField] private string hpValueFormat = "{0}/{1}";

        // 목표값(SetHp가 넣는 실제 HP). 화면 표시는 이 값으로 러프하게 수렴한다.
        private int targetHp;
        private int targetMax;
        private int targetSegmentCount;
        // 화면에 그려지는 HP. 목표(targetHp)를 향해 매 프레임 밀려 내려간다. 바·수치·줄 수를 이 값으로 그린다.
        private float displayHp;
        // 등장 직후 첫 SetHp는 드레인 없이 즉시 맞춘다(등장부터 깎이는 연출이 되지 않게).
        private bool snapNext = true;
        // SetHp가 한 번이라도 불렸는지. Update가 데이터 없이 그리지 않게 막는다.
        private bool hasData;

        // 현재 줄 채움(그린 값). Render가 displayHp에서 계산한다.
        private float fillValue = 1f;
        // 트레일(지연값). 현재 fill보다 뒤처져 "방금 깎인" 밝은 띠를 만든다.
        private float trailValue = 1f;
        // 트레일 홀드 타이머. 깎인 시점부터 흐르고, trailHoldSeconds를 넘어야 트레일이 움직인다.
        private float trailHoldTimer;

        /// <summary>보스 등장 시 바를 켜고 이름을 세팅한다. 게이지는 가득 찬 상태로 초기화한다.</summary>
        /// <param name="bossName">표시할 보스 이름.</param>
        public void Show(string bossName)
        {
            if (nameText != null) nameText.text = bossName;

            snapNext = true;      // 등장 첫 값은 드레인 없이 즉시 맞춘다
            fillValue = 1f;
            trailValue = 1f;
            trailHoldTimer = 0f;
            if (hpFill != null) hpFill.fillAmount = 1f;
            if (hpTrail != null) hpTrail.fillAmount = 1f;   // 색은 첫 SetHp가 현재 줄 색에서 뽑아 칠한다

            if (barRoot != null) barRoot.SetActive(true);
        }

        /// <summary>보스 퇴장(사망·이탈) 시 바를 숨긴다.</summary>
        public void Hide()
        {
            if (barRoot != null) barRoot.SetActive(false);
        }

        /// <summary>
        /// 목표 HP를 갱신한다. 실제 바·수치·줄 수는 이 목표로 러프하게(드레인) 수렴한다(Update). 즉시 꽂지 않는 이유는
        /// "딱딱 끊기지 않고 줄이 밀려 깎이는" 연출을 위해서다. 비율이 아니라 원본 수치를 받는 이유는 표기·줄 계산에 원본이 필요해서다.
        /// </summary>
        /// <param name="cur">현재 HP(목표).</param>
        /// <param name="max">최대 HP.</param>
        /// <param name="segmentCount">풀 HP일 때 표시할 줄 수. 0이면 세그먼트 없이 단일 바로 그린다.</param>
        public void SetHp(int cur, int max, int segmentCount)
        {
            targetHp = cur;
            targetMax = max;
            targetSegmentCount = segmentCount;

            // 등장 직후 첫 값은 즉시 맞춘다(그 뒤 피해부터 밀려 깎인다).
            if (snapNext)
            {
                displayHp = cur;
                snapNext = false;
            }

            hasData = true;
        }

        // 현재 displayHp로 바·배경·트레일·수치·줄 수를 그린다. Update가 매 프레임 호출한다.
        private void Render()
        {
            float cur = Mathf.Max(0f, displayHp);
            int max = targetMax;
            int segmentCount = targetSegmentCount;

            float fraction;
            int segments;
            Color color;

            if (segmentCount <= 0 || max <= 0)
            {
                // 단일 바: 전체 비율을 그대로 그리고 줄 수 표기는 숨긴다.
                fraction = max > 0 ? Mathf.Clamp01(cur / max) : 0f;
                segments = -1;
                color = singleBarColor;
            }
            else
            {
                // 한 줄당 HP는 총 HP를 줄 수로 나눠 계산한다(기획은 "몇 줄"만 넣는다).
                float hpPerSegment = (float)max / segmentCount;

                // 남은 줄 수(부분 줄 포함) = ceil(cur / 줄당HP). 현재 줄 채움 = 그 줄 안에서의 비율.
                segments = Mathf.CeilToInt(cur / hpPerSegment);
                if (segments < 1 && cur > 0f) segments = 1;
                float lower = (segments - 1) * hpPerSegment;
                fraction = Mathf.Clamp01((cur - lower) / hpPerSegment);

                if (cur <= 0f) { segments = 0; fraction = 0f; }

                color = PaletteColor(segments);
            }

            // 줄이 넘어가 채움이 다시 차오르면(fraction↑) 트레일도 같이 올리고, 깎이면(fraction↓) 홀드를 다시 시작한다.
            if (fraction > trailValue) trailValue = fraction;
            else if (fraction < fillValue) trailHoldTimer = 0f;
            fillValue = fraction;
            if (trailValue < fillValue) trailValue = fillValue;

            // 뒤에서 드러날 '다음 줄' 색. 마지막 줄(X1)·단일 바는 뒤에 줄이 없어 어두운 배경으로 칠한다.
            Color nextColor = (segmentCount > 0 && segments >= 2) ? PaletteColor(segments - 1) : emptyBehindColor;
            if (hpBackground != null) hpBackground.color = nextColor;

            if (hpFill != null)
            {
                hpFill.fillAmount = fillValue;
                hpFill.color = color;
            }
            if (hpTrail != null)
            {
                // 트레일은 현재 줄 색을 밝게 뽑아 "방금 깎인" 띠로 보이게 한다(배경의 다음 색이 드러나기 직전 구간).
                hpTrail.color = Color.Lerp(color, Color.white, trailBrightness);
                hpTrail.fillAmount = trailValue;
            }

            if (hpValueText != null)
                hpValueText.text = string.Format(hpValueFormat, Mathf.CeilToInt(cur), Mathf.Max(max, 0));

            if (segmentCountText != null)
            {
                bool showCount = segments >= 0;
                segmentCountText.gameObject.SetActive(showCount);
                if (showCount) segmentCountText.text = string.Format(segmentCountFormat, segments);
            }
        }

        /// <summary>그로기 게이지와 잠금(자물쇠) 표시 갱신.</summary>
        /// <param name="ratio">남은 그로기 비율(0~1).</param>
        /// <param name="locked">특수 패턴으로 잠겼는지 여부.</param>
        public void SetGroggy(float ratio, bool locked)
        {
            if (groggyFill != null) groggyFill.fillAmount = Mathf.Clamp01(ratio);
            if (groggyLockIcon != null) groggyLockIcon.SetActive(locked);
        }

        private void Update()
        {
            if (barRoot == null || !barRoot.activeSelf || !hasData) return;

            // HP를 목표로 러프하게 밀어 깎는다. 매 순간 남은 거리의 drainSpeed 비율만큼 좁힌다(프레임률 보정).
            // (줄 넘어감 스냅은 fraction 계산에서 자연히 생긴다 — 한 줄을 다 비우면 다음 줄이 꽉 찬 채로 이어진다.)
            if (!Mathf.Approximately(displayHp, targetHp))
            {
                if (drainSpeed >= 1f)
                {
                    displayHp = targetHp;   // 1이면 즉시(연출 끄기)
                }
                else
                {
                    // 60fps에서 한 프레임당 남은 거리의 drainSpeed만큼 좁히도록 지수 보정한다.
                    float t = 1f - Mathf.Pow(1f - drainSpeed, Time.deltaTime * 60f);
                    displayHp = Mathf.Lerp(displayHp, targetHp, t);

                    // 지수 접근은 끝에서 무한히 기어가므로, 충분히 가까우면 스냅해 마무리한다.
                    if (Mathf.Abs(displayHp - targetHp) < 0.5f) displayHp = targetHp;
                }
            }

            // 트레일은 홀드 시간을 넘긴 뒤에야 현재 fill까지 스르륵 따라 내려온다(방금 깎인 구간을 잠깐 남김).
            if (trailValue > fillValue)
            {
                if (trailHoldTimer < trailHoldSeconds)
                    trailHoldTimer += Time.deltaTime;
                else
                    trailValue = Mathf.MoveTowards(trailValue, fillValue, trailLerpSpeed * Time.deltaTime);
            }

            Render();
        }

        // 인스펙터 segmentPalette가 비어 있을 때(생성 툴로 만든 컴포넌트는 배열 초기값이 직렬화 안 될 수 있다)
        // 쓰는 코드 기본 팔레트. 이게 있어야 인스펙터를 안 채워도 줄수별 색이 순환한다.
        private static readonly Color[] DefaultPalette =
        {
            new Color(0.62f, 0.20f, 0.85f, 1f),   // 보라
            new Color(0.95f, 0.78f, 0.20f, 1f),   // 노랑
            new Color(0.95f, 0.45f, 0.15f, 1f),   // 주황
            new Color(0.85f, 0.18f, 0.18f, 1f),   // 빨강
        };

        // 남은 줄 수로 순환 색을 고른다. 인스펙터 팔레트가 비면 코드 기본 팔레트로 순환한다(단색으로 죽지 않게).
        private Color PaletteColor(int segments)
        {
            Color[] palette = (segmentPalette != null && segmentPalette.Length > 0)
                ? segmentPalette
                : DefaultPalette;

            int count = palette.Length;
            int idx = ((segments - 1) % count + count) % count;   // 음수·0도 안전하게 감싼다
            return palette[idx];
        }
    }
}
