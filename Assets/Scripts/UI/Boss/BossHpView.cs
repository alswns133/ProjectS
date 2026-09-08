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
    /// <b>바를 그리는 경로가 둘이다 (2026-09-07 TH 추가).</b>
    /// <list type="bullet">
    /// <item><b>트랙 경로</b> — <c>track</c>(홀로그램 세그먼트 셰이더를 물린 Image 한 장)을 지정하면
    /// 배경·채움·잔상·칸·주사선·리딩 엣지가 전부 그 한 장 안에서 합성된다. 목업 A안이 이쪽이다.</item>
    /// <item><b>기존 Image 경로</b> — track이 비어 있으면 hpBackground/hpFill/hpTrail 3장으로 그린다.</item>
    /// </list>
    /// 한 장으로 접는 이유는 드로우콜보다 정렬 쪽이 크다 — 채움과 잔상을 각각 Image로 두면 두 RectTransform이
    /// 따로 반올림되면서 저체력에서 1px씩 어긋난다. 그래도 기존 경로를 남겨 둔 것은 이 뷰가 이미 여러 씬에
    /// 얹혀 있어서, 트랙을 붙이지 않은 씬의 보스 바가 조용히 사라지지 않게 하기 위함이다.
    ///
    /// <b>다단 바는 트랙 경로에서도 그대로다.</b> 셰이더의 <c>_Fill</c>은 전체 HP가 아니라 <b>현재 줄 안의 채움</b>이고,
    /// 남은 줄 수·줄별 색 순환은 여기서 계산해 <c>_FillColor</c>/<c>_BehindColor</c>로 밀어 넣는다.
    /// 칸(<c>_Segments</c>, 기본 20)은 한 줄을 나눈 눈금이라 줄이 넘어가면 다시 20칸이 찬다.
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

        [Header("HP 바 — 홀로그램 트랙 (지정 시 우선)")]
        // 세그먼트 셰이더(ProjectS/UI Boss Segment Bar)를 물린 Image 한 장. 지정하면 아래 3장 대신
        // 이쪽으로만 그린다. Source Image는 None으로 둔다(셰이더가 칸을 절차적으로 그리고,
        // 스프라이트는 선택적 추가 마스크로만 쓰이므로 비우면 흰색이 들어와 영향이 없다).
        [SerializeField] private Image track;
        // 피격 백색 플래시가 사라지는 데 걸리는 시간(초). 목업 기준 0.09s.
        [SerializeField, Min(0.01f)] private float hitFlashSeconds = 0.09f;
        // 줄이 하나 넘어갈 때 터지는 글리치가 가라앉는 데 걸리는 시간(초).
        [SerializeField, Min(0.01f)] private float lineBreakGlitchSeconds = 0.42f;

        [Header("HP 바 — 기존 Image 경로 (트랙 미지정 시)")]
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
        // 타격 시점부터 트레일이 제자리에 머무는 시간(초). 이 시간이 지나야 따라 내려오기 시작한다.
        // 연타로 이 시간 안에 다시 맞으면 이전 트레일은 버리고 새로 선다(SetHp 참고).
        // ★ 이 시간 안에 fill(드레인)이 목표에 닿아야 "다 벌어진 띠가 잠깐 멈춰 있는" 그림이 나온다.
        //   drainSpeed가 낮아 드레인이 이 시간보다 오래 걸리면, 띠가 벌어지는 도중에 홀드가 끝나
        //   잔상이 제대로 보이기 전에 닫힌다.
        [SerializeField, Min(0f)] private float trailHoldSeconds = 0.2f;

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

        // 피격 백색 플래시 세기. 깎일 때 1로 올라가 hitFlashSeconds에 걸쳐 0으로 내려간다(트랙 경로 전용).
        private float hitFlash;
        // 줄 넘어감 글리치 세기. 남은 줄 수가 줄어든 프레임에 1로 올라간다(트랙 경로 전용).
        private float glitchGate;
        // 직전에 그린 남은 줄 수. "줄이 하나 넘어갔다"를 잡는 기준이라 -1은 아직 그린 적 없음을 뜻한다.
        private int lastSegments = -1;

        // track에 물린 머티리얼의 런타임 사본. 공유 머티리얼을 직접 만지면 에디터에서 .mat 에셋이 더럽혀지고,
        // 보스 바가 둘 이상 뜨는 순간 서로의 _Fill을 덮어쓴다. UI는 MaterialPropertyBlock을 못 쓰므로 사본이 정석.
        private Material trackMaterial;

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int GhostFillId = Shader.PropertyToID("_GhostFill");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int BehindColorId = Shader.PropertyToID("_BehindColor");
        private static readonly int GhostColorId = Shader.PropertyToID("_GhostColor");
        private static readonly int PixelWidthId = Shader.PropertyToID("_PixelWidth");
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int GlitchGateId = Shader.PropertyToID("_GlitchGate");

        private void Awake()
        {
            if (track == null) return;

            // track에 머티리얼을 안 물리면 Unity가 기본 UI 머티리얼을 돌려준다(null이 아니다).
            // 그대로 트랙 경로를 켜면 바가 흰 사각형으로 그려지고 원인을 찾기 어려우므로,
            // 세그먼트 셰이더인지(_Fill 유무) 확인하고 아니면 기존 Image 경로로 물러난다.
            Material source = track.material;
            if (source == null || !source.HasProperty(FillId))
            {
                Debug.LogWarning($"[BossHpView] {name}: track에 BossSegmentBar 머티리얼이 없다(_Fill 없음). " +
                                 "기존 Image 경로로 그린다.", this);
                return;
            }

            // Image.material은 에셋 자체를 돌려주므로 사본을 만들어 물린다(위 trackMaterial 주석 참고).
            trackMaterial = new Material(source);
            track.material = trackMaterial;
        }

        private void OnDestroy()
        {
            // 사본은 우리가 만들었으니 우리가 치운다. 안 치우면 씬을 옮길 때마다 머티리얼이 샌다.
            if (trackMaterial != null) Destroy(trackMaterial);
        }

        /// <summary>보스 등장 시 바를 켜고 이름을 세팅한다. 게이지는 가득 찬 상태로 초기화한다.</summary>
        /// <param name="bossName">표시할 보스 이름.</param>
        public void Show(string bossName)
        {
            if (nameText != null) nameText.text = bossName;

            snapNext = true;      // 등장 첫 값은 드레인 없이 즉시 맞춘다
            fillValue = 1f;
            trailValue = 1f;
            trailHoldTimer = 0f;

            // 등장 연출이 직전 보스의 플래시·글리치를 물려받지 않게 초기화한다.
            // lastSegments를 -1로 되돌리는 게 중요하다 — 안 그러면 이전 보스의 줄 수와 비교해
            // 등장하자마자 줄이 넘어간 것으로 오인하고 글리치가 터진다.
            hitFlash = 0f;
            glitchGate = 0f;
            lastSegments = -1;
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
            // 깎인 순간에만 반응한다. hasData 검사가 있어야 등장 직후 첫 값이나 회복·리셋에는
            // 반응하지 않는다(맞지도 않았는데 바가 번쩍이는 것을 막는다).
            if (hasData && cur < targetHp)
            {
                hitFlash = 1f;

                // 잔상은 "가장 최근 한 대"만 보여준다 — 새 타격이 오면 이전 잔상은 버리고,
                // 지금 그려져 있는 위치(fillValue)에서 잔상을 다시 세운 뒤 홀드를 처음부터 센다.
                // (2026-09-07 TH 수정) 누적시키지 않는 이유: 연타 중에는 잔상이 계속 위쪽에 붙박여
                // "직전 한 대가 얼마나 아팠나"가 안 읽히고, 띠 폭이 피해량이 아니라 연타 길이를 뜻하게 된다.
                //
                // 여기서 안 세우고 Render에 두면 안 된다. Render는 드레인이 도는 내내 "줄어드는 중"이라
                // 홀드가 매 프레임 되감겨, trailHoldSeconds가 "타격 후"가 아니라 "드레인이 멈춘 뒤"가 된다.
                trailValue = fillValue;
                trailHoldTimer = 0f;
            }

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

            // 줄이 넘어가 채움이 다시 차오르면(fraction↑) 트레일도 같이 끌어올린다. 안 올리면 다음 줄이
            // 꽉 찬 상태인데 잔상만 이전 줄의 낮은 위치에 남아 엉뚱한 띠가 보인다.
            //
            // 홀드 리셋(trailHoldTimer = 0)은 여기에 두지 않는다 — 드레인 중 매 프레임 참이 되어
            // 홀드가 되감기기 때문이다. 실제 타격 시점인 SetHp에서만 리셋한다. (2026-09-07 TH 수정)
            if (fraction > trailValue) trailValue = fraction;
            fillValue = fraction;
            if (trailValue < fillValue) trailValue = fillValue;

            // 줄이 하나 넘어간 순간(남은 줄 수 감소)에 글리치를 터뜨린다. 목업의 "신호 단절" 연출에 해당한다.
            // 여기서 잡는 이유는 SetHp가 아니라 화면에 그려지는 displayHp 기준이어야 연출과 바가 같은 프레임에 맞기 때문이다.
            if (lastSegments >= 0 && segments < lastSegments) glitchGate = 1f;
            lastSegments = segments;

            // 뒤에서 드러날 '다음 줄' 색. 마지막 줄(X1)·단일 바는 뒤에 줄이 없어 어두운 배경으로 칠한다.
            Color nextColor = (segmentCount > 0 && segments >= 2) ? PaletteColor(segments - 1) : emptyBehindColor;
            // 트레일/잔상은 현재 줄 색을 밝게 뽑아 "방금 깎인" 띠로 보이게 한다(다음 줄 색이 드러나기 직전 구간).
            Color ghostColor = Color.Lerp(color, Color.white, trailBrightness);

            if (trackMaterial != null)
            {
                // 트랙 한 장 경로: 배경·채움·잔상·칸이 전부 셰이더 안에서 합성된다.
                // _Fill은 전체 HP가 아니라 현재 줄 안의 채움이다(다단 바 유지).
                trackMaterial.SetFloat(FillId, fillValue);
                trackMaterial.SetFloat(GhostFillId, trailValue);
                trackMaterial.SetColor(FillColorId, color);
                trackMaterial.SetColor(BehindColorId, nextColor);

                // 잔상 농도(a)는 머티리얼에 저작된 값을 유지하고 색만 현재 줄에서 뽑는다.
                ghostColor.a = trackMaterial.GetColor(GhostColorId).a;
                trackMaterial.SetColor(GhostColorId, ghostColor);

                // px 단위 계산(리딩 엣지 폭·저체력 min-pixel clamp)이 해상도·앵커에 안 흔들리게 실제 폭을 넘긴다.
                // 안 넘기면 셰이더가 기본값 640px로 계산해 다른 폭에서 clamp가 과하거나 모자라게 걸린다.
                trackMaterial.SetFloat(PixelWidthId, track.rectTransform.rect.width);
                trackMaterial.SetFloat(HitFlashId, hitFlash);
                trackMaterial.SetFloat(GlitchGateId, glitchGate);
            }
            else
            {
                // 기존 Image 3장 경로(트랙을 붙이지 않은 씬).
                if (hpBackground != null) hpBackground.color = nextColor;

                if (hpFill != null)
                {
                    hpFill.fillAmount = fillValue;
                    hpFill.color = color;
                }
                if (hpTrail != null)
                {
                    hpTrail.color = ghostColor;
                    hpTrail.fillAmount = trailValue;
                }
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

            // 피격 플래시·줄 넘어감 글리치 감쇠. 트랙 경로에서만 그려지지만 상태는 항상 굴려
            // 도중에 트랙을 붙였다 떼도 값이 1에 붙박이지 않게 한다.
            if (hitFlash > 0f) hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime / hitFlashSeconds);
            if (glitchGate > 0f) glitchGate = Mathf.Max(0f, glitchGate - Time.deltaTime / lineBreakGlitchSeconds);

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
