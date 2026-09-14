using ProjectS.Managers;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 등장 연출(HUD 기획서 6절 · UI_DG_BossText · UI_DG_Warning).
    /// 경고 배너가 흐르며 위험 표시가 깜박이다가, 경고가 위아래로 걷히면서 그 자리에 "BOSS"가 박힌다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>흐름 → 깜박임 → 걷힘 → 충돌</b> 순서다. 사망 팝업의 글리치 연출(<see cref="GlitchTextFx"/>)을
    /// 통째로 재사용하지 않는 것은 그 시그니처가 뚜렷해서 플레이어가 "죽었을 때 그것"으로 읽기 때문이다.
    /// 사망은 실패, 보스 등장은 긴장·기대라 감정 방향이 반대다.
    /// (위험 표시의 지지직은 <see cref="GlitchImageFx"/>로 결이 다른 지속 상태를 쓴다.)
    /// </para>
    /// <para>
    /// <b>걷힘과 충돌은 겹쳐서 재생한다.</b> 띠가 다 빠져나간 뒤에 BOSS를 박으면 두 동작이
    /// 순서대로 재생되는 별개 연출로 보인다. 아직 걷히는 중에 박혀야 "열린 자리로 들어왔다"가 된다.
    /// </para>
    /// <para>
    /// <b>표시 참조는 전부 선택 사항이다.</b> 비어 있는 단계는 건너뛰고 나머지만 재생한다 —
    /// 아트가 붙기 전에도 타이밍을 검증할 수 있게 했다(<c>DeathPopup</c>·<c>LevelUpNotice</c>와 같은 방침).
    /// </para>
    /// <para>
    /// <b>재생 경로는 둘이고, 화면은 하나의 함수가 그린다.</b> 레이드 등장 컷신에서는 Timeline의
    /// <see cref="BossIntroTrack"/>에 꽂아 클립 시간으로 구동하고(불 가림막과 같은 방식),
    /// 테스트·단독 호출은 <see cref="Play"/>가 코루틴으로 구동한다. 두 경로 모두 "시각 t의 화면"을
    /// <see cref="Sample"/>로 그리므로 타이밍이 어긋나지 않는다. 보스 오브젝트를 참조하지 않는다 —
    /// 이름은 클립(또는 호출부)이 넘기므로 보스 스폰·등장 신호와 무관하게 돈다.
    /// </para>
    /// <para>
    /// <b>왜 코루틴이 아니라 시각 샘플링인가.</b> 코루틴은 제 시간으로 흘러가 타임라인을 스크럽·되감아도
    /// 화면이 따라오지 않는다. 등장 컷신은 카메라·보스 모션과 프레임 단위로 맞춰야 해서, 헤드를 끌면
    /// 연출이 같이 움직여야 한다(<see cref="FireCurtainClip"/>과 같은 이유).
    /// </para>
    /// <para>
    /// 코루틴 경로의 시간은 unscaled로 센다. 보스 등장은 히트스톱·컷신으로 timeScale이 낮아진 순간에 겹치기 쉽다.
    /// 타임라인 경로는 디렉터의 시계를 따른다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class BossIntroFx : MonoBehaviour
    {
        [Header("경고 단계")]
        [Tooltip("배너·위험 표시를 담은 묶음. 파괴 단계에서 통째로 숨긴다.")]
        [SerializeField] private GameObject warningRoot;

        [Tooltip("위아래 경고 띠. 흐르는 방향은 각 ScrollingBand의 speed 부호로 정한다.")]
        [SerializeField] private ScrollingBand[] bands;

        [Tooltip("가운데 위험 표시. 깜박일 대상.")]
        [SerializeField] private Graphic warningIcon;

        [Tooltip("메인 플레어 전에 주위에서 하나씩 반짝이는 작은 빛들. 배열 순서가 곧 터지는 순서다. " +
                 "몇 개를 터뜨릴지는 보스의 강함에 맞춰 클립의 Spark Count(또는 Play 인자)로 정한다 — 모바일 게임의 별 등급처럼. " +
                 "★ 메인 플레어와 마찬가지로 경고 묶음(Warning Root) 밖에 둔다.")]
        [SerializeField] private Graphic[] sparkFlares;

        [Tooltip("주위 빛이 하나씩 터지는 간격(초). 마지막 빛이 터지고 이만큼 뒤에 메인 플레어가 터진다.")]
        [SerializeField, Min(0.01f)] private float sparkInterval = 0.12f;

        [Tooltip("주위 빛 하나가 켜져 있는 시간(초). 크기 변화는 메인 플레어의 Start/End Scale·곡선을 함께 쓴다.")]
        [SerializeField, Min(0f)] private float sparkSeconds = 0.08f;

        [Tooltip("주위 빛이 켜지는 순간의 회전(도)을 빛마다 ±이 범위에서 랜덤하게 준다. 0이면 배치한 각도 그대로. " +
                 "재생할 때마다 새로 뽑고, 에디터에서 타임라인을 스크럽할 때는 같은 각도로 고정된다.")]
        [SerializeField, Range(0f, 180f)] private float sparkRandomAngle = 15f;

        [Tooltip("주위 빛이 켜져 있는 동안 도는 각도(도). 음수면 반대 방향. 0이면 돌지 않는다. " +
                 "크기와 같은 곡선을 따라 처음에 빠르게 돌고 끝에서 멎는다 — 일정하게 돌면 반짝임이 아니라 회전하는 물체로 보인다.")]
        [SerializeField] private float sparkSpinDegrees = 45f;

        [Tooltip("경고가 뜨기 직전 번쩍이는 플레어(Image). 비우면 플레어 없이 곧바로 경고가 나타난다. " +
                 "★ 경고 묶음(Warning Root) 밖, 이 오브젝트 바로 아래에 둔다 — 플레어 동안 경고 묶음은 꺼져 있어 안에 두면 안 보인다.")]
        [SerializeField] private Graphic warningFlare;

        [Tooltip("플레어가 켜져 있는 시간(초). 경고가 언제 나타나는지는 아래 Warning Delay After Flare가 정한다. " +
                 "한 프레임(60fps 약 0.017초)보다 짧아도 플레이 중에는 최소 한 프레임은 보인다.")]
        [SerializeField, Min(0f)] private float flareSeconds = 0.01f;

        [Tooltip("플레어가 켜진 뒤 경고(아이콘·띠)가 나타나기까지의 시간(초). " +
                 "Flare Seconds보다 작으면 플레어가 번쩍이는 도중에 경고가 페이드 없이 한 번에 나타난다 — 빛에 가려 튀어나오는 순간이 안 보여 '빛 속에서 등장'으로 읽힌다. " +
                 "Flare Seconds 이상이면 플레어가 꺼진 뒤 Warning Fade In만큼 서서히 나타난다.")]
        [SerializeField, Min(0f)] private float warningDelayAfterFlare = 0.02f;

        [Tooltip("플레어가 켜지는 순간의 크기 배율(플레어 오브젝트에 설정한 크기 기준).")]
        [SerializeField, Min(0f)] private float flareStartScale = 0.8f;

        [Tooltip("플레어가 꺼지기 직전의 크기 배율. 시작보다 커야 빛이 터져 나오는 것으로 읽힌다.")]
        [SerializeField, Min(0f)] private float flareEndScale = 1f;

        [Tooltip("플레어가 커지는 진행 곡선. 앞이 가파를수록 한 번에 터져 나온다 — 선형이면 부풀어 오르는 것으로 보인다.")]
        [SerializeField]
        private AnimationCurve flareScaleCurve = new(
            new Keyframe(0f, 0f, 2.6f, 2.6f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("경고가 나타나는 시간(초).")]
        [SerializeField, Min(0f)] private float warningFadeIn = 0.2f;

        [Tooltip("위험 표시가 빠르게 깜박이는 횟수. 이 뒤에 한 번 켜진 채로 머문다.")]
        [SerializeField, Min(0)] private int fastBlinkCount = 2;

        [Tooltip("빠른 깜박임에서 켜져 있는 시간(초).")]
        [SerializeField, Min(0f)] private float fastBlinkOnSeconds = 0.07f;

        [Tooltip("빠른 깜박임에서 꺼져 있는 시간(초).")]
        [SerializeField, Min(0f)] private float fastBlinkOffSeconds = 0.07f;

        [Tooltip("깜박임이 끝난 뒤 위험 표시가 켜진 채로 머무는 시간(초).")]
        [SerializeField, Min(0f)] private float iconHoldSeconds = 0.5f;

        [Tooltip("위험 표시의 글리치. 비워두면 아이콘에서 찾는다. 깜박여 켜질 때마다 크게 튀게 한다.")]
        [SerializeField] private GlitchImageFx iconGlitch;

        [Header("경고 걷힘(커튼)")]
        [Tooltip("경고 띠가 화면 밖으로 쓸려 나가는 시간(초).")]
        [SerializeField, Min(0.02f)] private float curtainDuration = 0.34f;

        [Tooltip("띠가 밀려나는 거리(px). 화면 밖까지 완전히 빠져나갈 만큼 줘야 한다.")]
        [SerializeField, Min(0f)] private float curtainDistance = 640f;

        [Tooltip("밀려나는 진행 곡선. 뒤로 갈수록 가팔라야 붙잡혔다 뜯겨 나가는 느낌이 난다.")]
        [SerializeField]
        private AnimationCurve curtainCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.6f, 2.6f));

        [Tooltip("커튼이 이만큼 진행했을 때 BOSS가 박히기 시작한다(0~1). " +
                 "겹쳐야 두 동작이 하나로 읽힌다. 1이면 커튼이 끝난 뒤에 박힌다.")]
        [SerializeField, Range(0f, 1f)] private float slamTriggerRatio = 0.5f;

        [Header("위험 표시 고조 (등장 → 슬램 착지)")]
        [Tooltip("아이콘이 나타난 순간부터 BOSS가 착지하는 순간까지 커지는 최종 배율. 1이면 커지지 않는다. " +
                 "착지 순간의 터짐은 이 크기에서 시작한다.")]
        [SerializeField, Min(0f)] private float iconBuildUpScale = 1.3f;

        [Tooltip("아이콘이 나타날 때의 떨림 폭(px). 0이면 처음엔 가만히 있다가 점점 떨기 시작한다.")]
        [SerializeField, Min(0f)] private float iconShakeStart;

        [Tooltip("착지 직전의 떨림 폭(px). 커질수록 슬램 직전의 긴장이 세진다.")]
        [SerializeField, Min(0f)] private float iconShakeEnd = 8f;

        [Tooltip("크기·떨림이 고조되는 진행 곡선. 뒤로 갈수록 가팔라야 '점점 차오르다 터진다'가 된다 — " +
                 "선형이면 처음부터 같은 속도로 커져 긴장이 쌓이는 느낌이 약하다.")]
        [SerializeField]
        private AnimationCurve iconBuildUpCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.2f, 2.2f));

        [Header("위험 표시 터짐")]
        [Tooltip("터지면서 부푸는 최대 배율. 1이면 부풀지 않고 사라지기만 한다.")]
        [SerializeField, Min(1f)] private float iconPopScale = 1.4f;

        [Tooltip("부풀었다 사라지는 데 걸리는 시간(초). 길면 터진 게 아니라 커졌다 없어진 것으로 보인다.")]
        [SerializeField, Min(0.01f)] private float iconPopDuration = 0.08f;

        [Tooltip("터질 때 튀는 전기 스파크. 비워두면 자식에서 찾는다. 없으면 스파크 없이 터지기만 한다. " +
                 "양·속도·색은 SparkBurstFx 쪽에서 조절한다.")]
        [SerializeField] private SparkBurstFx sparkBurst;

        [Tooltip("커튼이 slamTriggerRatio에 도달한 뒤 BOSS가 박히기까지 더 기다릴 시간(초). " +
                 "0이면 그 지점에서 곧바로 박힌다.")]
        [FormerlySerializedAs("shatterToSlamDelay")]
        [SerializeField, Min(0f)] private float slamExtraDelay;

        [Header("슬램 단계")]
        [Tooltip("BOSS 텍스트 묶음. 슬램에서 크기를 흔든다.")]
        [SerializeField] private RectTransform bossRoot;

        [Tooltip("보스 이름 라벨. 비워두면 이름을 표시하지 않는다.")]
        [SerializeField] private TMP_Text bossNameText;

        [Tooltip("박히기 시작할 때의 크기 배율. 클수록 멀리서 날아와 꽂히는 느낌이 난다.")]
        [SerializeField, Min(1f)] private float slamStartScale = 3.2f;

        [Tooltip("내려찍기 시작 높이(px). 이만큼 위에서 제자리로 떨어진다. " +
                 "크기만 줄면 '멀리서 다가온다'가 되어 도장으로 안 읽힌다.")]
        [SerializeField] private float slamDropDistance = 260f;

        [Tooltip("박히는 데 걸리는 시간(초). 길면 내려앉는 것처럼 보인다.")]
        [SerializeField, Min(0.02f)] private float slamDuration = 0.09f;

        [Tooltip("크기·높이가 줄어드는 진행 곡선. 앞이 평평하고 끝이 가파를수록 떠 있다 한 번에 꽂힌다.")]
        [SerializeField]
        private AnimationCurve slamCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 3.2f, 3.2f));

        [Tooltip("찍힌 직후 모든 것이 완전히 멈춰 있는 시간(초). " +
                 "움직임이 뚝 끊기는 이 정적이 충돌을 인지시킨다. 0이면 곧바로 흔들린다.")]
        [SerializeField, Min(0f)] private float impactHoldSeconds = 0.04f;

        [Tooltip("찍힌 순간 아래로 박히는 폭(px). 0이면 흔들리지 않는다. 착지 첫 프레임에 이 폭으로 한 번에 내려박힌 뒤 튕기며 멎는다.")]
        [SerializeField, Min(0f)] private float impactShakeStrength = 14f;

        [Tooltip("진동이 완전히 멎기까지의 최대 시간(초). 실제로 보이는 길이는 대부분 Damping이 정한다 — 이 값은 끝에서 확실히 0에 닿게 하는 상한이다.")]
        [SerializeField, Min(0f)] private float impactShakeDuration = 0.25f;

        [Tooltip("초당 튕기는 횟수(Hz). 쿵 하고 박히는 느낌은 14~20. 낮추면(8 이하) 느리게 출렁여 말랑하게 보인다.")]
        [SerializeField, Min(0.1f)] private float impactShakeFrequency = 16f;

        [Tooltip("진동이 죽는 속도. 클수록 첫 충격 한 번만 남고 바로 멎어 '쿵'이 되고, 작을수록 여러 번 출렁여 젤리처럼 보인다. " +
                 "18이면 약 0.05초 만에 폭이 1/3로 줄어 한두 번 튕기고 멎는다.")]
        [SerializeField, Min(0f)] private float impactShakeDamping = 18f;

        [Tooltip("가로 진동의 비율(세로 폭 대비). 내려찍은 충격이라 세로가 주가 되어야 한다. 0이면 세로로만 진동한다.")]
        [SerializeField, Range(0f, 1f)] private float impactShakeHorizontal = 0.12f;

        [Tooltip("진동하며 눌렸다 펴지는 정도. 아래로 박힐 때 납작하고 넓어졌다가 튀어 오를 때 되돌아온다. " +
                 "단단한 글자가 찌그러지면 말랑한 물체(젤리)로 읽히므로 '쿵'에는 0을 권장한다. 0.02 정도면 충격만 살짝 실린다.")]
        [SerializeField, Range(0f, 0.3f)] private float impactSquash;

        [Header("마무리")]
        [Tooltip("BOSS가 박힌 뒤 화면에 머무는 시간(초).")]
        [SerializeField, Min(0f)] private float holdSeconds = 1.4f;

        [Tooltip("BOSS 텍스트가 재처럼 타들어가며 사라지는 연출. 비워두면 자식에서 찾는다. " +
                 "없으면 아래 시간만큼 그냥 페이드아웃한다.")]
        [SerializeField] private AshDissolveFx ashDissolve;

        [Tooltip("사라지는 데 걸리는 시간(초). 재 연출을 쓰면 그쪽 duration이 대신 쓰인다.")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

        private CanvasGroup group;
        private Coroutine routine;

        // 커튼은 띠를, 슬램은 BOSS를 제자리에서 옮긴다. 원래 자리를 기억해 두지 않으면
        // 두 번째 재생부터 옮겨진 자리를 기준으로 삼아 조금씩 밀려난다.
        private Vector2[] bandHome;
        private Vector2 bossHome;
        private Vector2 iconHome;   // 고조 단계의 떨림이 아이콘을 옮기므로 제자리를 기억한다

        // ── 샘플링 세션(코루틴 재생 1회 또는 타임라인 클립 1회) ──
        // 세션 시작 시점의 상태를 쥐고 있다가 끝날 때 그대로 되돌린다. 하드코딩한 "기본 상태"로 돌리면
        // 에디터 미리보기가 씬에 저장된 값을 바꿔 놓아 씬이 더러워진다.
        private bool sampling;
        private float lastSampleTime;
        private bool ashTouched;

        // 이번 세션에 플레어를 한 프레임이라도 그렸는지. 플레어가 한 프레임보다 짧으면 샘플 시각이 그 구간을
        // 건너뛰어 아예 안 보이는데, 그걸 막으려고 "아직 못 그렸으면 이번 프레임에 한 번 그린다"의 기준으로 쓴다.
        private bool flareShown;

        // 주위 빛마다 한 프레임이라도 그렸는지(비트 i = sparkFlares[i]). 용도는 flareShown과 같다.
        private int sparksShownMask;

        // 이번 세션의 주위 빛 시작 각도를 뽑는 씨앗. 빛마다 이 값과 인덱스로 각도를 결정적으로 뽑는다(BeginSampling에서 정함).
        private int sparkAngleSeed;

        // 이번 재생에서 터뜨릴 주위 빛 개수. -1이면 배열 전부. 클립/Play가 재생 시작에 정한다(SetSparkCount).
        private int sparkCountOverride = -1;
        private RestState rest;

        // 찍힌 뒤 흔들림을 뽑는 박자(Hz). 원래 매 프레임 난수였던 떨림을 시각에서 결정적으로 뽑아,
        // 스크럽해도 같은 모양이 나오게 한다.
        private const float ShakeRate = 60f;

        /// <summary>세션 시작 시점의 표시 상태. <see cref="EndSampling"/>이 이 값으로 되돌린다.</summary>
        private struct RestState
        {
            public bool RootActive;
            public float Alpha;
            public bool WarningActive;
            public bool IconActive;
            public Color IconColor;
            public Vector3 IconScale;
            public bool FlareActive;
            public Vector3 FlareScale;
            public bool[] SparkActive;
            public Vector3[] SparkScale;
            public Quaternion[] SparkRotation;
            public bool BossActive;
            public Vector3 BossScale;
        }

        /// <summary>연출 안의 각 단계가 시작·끝나는 시각(초). 인스펙터 값에서 매번 계산한다.</summary>
        private struct Timings
        {
            public int SparkCount;
            public float FlareStart;
            public float FlareEnd;
            public float WarningStart;
            public float FadeIn;
            public float BlinkStart;
            public float HoldStart;
            public float CurtainStart;
            public float CurtainEnd;
            public float SlamStart;
            public float Land;
            public float ShakeStart;
            public float ShakeEnd;
            public float OutStart;
            public float OutDuration;
            public float End;
        }

        /// <summary>지금 코루틴으로 재생 중인지. 타임라인 구동은 포함하지 않는다.</summary>
        public bool IsPlaying => routine != null;

        /// <summary>
        /// 연출 전체 길이(초). 경고 등장부터 BOSS가 다 타 사라질 때까지다.
        /// 타임라인 클립은 이 길이 이상이어야 끝까지 재생된다.
        /// </summary>
        public float Duration
        {
            get
            {
                ResolveOptionalRefs();
                return BuildTimings().End;
            }
        }

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();

            // 연출은 입력을 받지 않는다. 켜 두면 화면을 덮는 동안 아래 HUD 클릭을 삼킨다.
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            CaptureBandHome();
            if (bossRoot != null) bossHome = bossRoot.anchoredPosition;
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsPlaying이 영영 true가 된다.
            routine = null;
        }

        /// <summary>
        /// 보스 등장 연출을 처음부터 재생한다(코루틴 경로). 이미 재생 중이면 중단하고 다시 시작한다.
        /// 레이드 컷신은 이 메서드가 아니라 <see cref="BossIntroTrack"/>으로 재생한다.
        /// </summary>
        /// <param name="bossName">BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다.</param>
        public void Play(string bossName) => Play(bossName, -1);

        /// <summary>
        /// 보스 등장 연출을 처음부터 재생한다(코루틴 경로). 주위 빛 개수(보스 강함)를 함께 지정한다.
        /// </summary>
        /// <param name="bossName">BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다.</param>
        /// <param name="sparkCount">메인 플레어 전에 터뜨릴 주위 빛 개수. -1이면 배열 전부.</param>
        public void Play(string bossName, int sparkCount)
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            // 끊긴 지난 재생의 흔적을 먼저 되돌린다. 오브젝트를 켠 뒤에 되돌리면, 지난 세션이 꺼진 상태에서
            // 시작했을 때 여기서 다시 꺼져 방금 시작한 코루틴이 죽는다.
            EndSampling();

            SetBossName(bossName);
            SetSparkCount(sparkCount);

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"{name}: 컴포넌트가 비활성이라 보스 등장 연출을 재생하지 못했다.", this);
                return;
            }

            // TODO(sound): 보스 등장 연출음 — 등장 스팅/전용 BGM 전환. SoundManager.Instance.PlaySFX(<보스 등장 SFX>) 또는 PlayBgm(<보스 BGM>);
            //   커튼 슬램(slamTriggerRatio) 타이밍에 맞춰 임팩트음을 Sample의 착지 교차 지점에서 따로 낼 수도 있다.
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>재생 중인 연출을 즉시 끝낸다. 씬 전환·보스 즉사처럼 화면이 통째로 바뀔 때 호출한다.</summary>
        public void Dismiss()
        {
            StopAllCoroutines();
            routine = null;

            EndSampling();
            if (group != null) group.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// BOSS 아래 이름 줄을 채운다. 비우면 줄을 숨긴다. 타임라인 클립은 재생 시작에 한 번 부른다.
        /// </summary>
        /// <param name="bossName">표시할 이름.</param>
        public void SetBossName(string bossName)
        {
            if (bossNameText == null) return;

            string text = bossName ?? string.Empty;
            if (bossNameText.text != text) bossNameText.text = text;
            SetActive(bossNameText.gameObject, !string.IsNullOrWhiteSpace(text));
        }

        /// <summary>
        /// 메인 플레어 전에 터뜨릴 주위 빛 개수를 정한다. 보스의 강함을 별 등급처럼 보여 주는 손잡이다.
        /// 뒤 단계 시각이 모두 이 개수만큼 밀리므로 <see cref="Sample"/> 전에 부른다(타임라인 클립은 재생 시작에 한 번).
        /// </summary>
        /// <param name="count">터뜨릴 개수. 음수면 배열 전부, 배열보다 크면 배열 길이로 자른다.</param>
        public void SetSparkCount(int count)
        {
            sparkCountOverride = count;
        }

        // 실제로 터뜨릴 주위 빛 개수. 비트마스크로 한 프레임 보장을 세므로 31개로 묶는다.
        private int ActiveSparkCount
        {
            get
            {
                int length = sparkFlares != null ? Mathf.Min(sparkFlares.Length, 31) : 0;
                return sparkCountOverride < 0 ? length : Mathf.Min(sparkCountOverride, length);
            }
        }

        /// <summary>
        /// 연출 시작으로부터 <paramref name="time"/>초 지점의 화면을 그린다. 앞뒤 어느 시각이든 바로 그 모습이 된다.
        /// </summary>
        /// <param name="time">연출 시작 기준 시각(초). 0 미만이나 <see cref="Duration"/> 이상이면 숨긴다.</param>
        /// <remarks>
        /// 첫 호출이 샘플링 세션을 열고 그 순간의 상태를 기억한다. 세션은 <see cref="EndSampling"/>으로 닫아야
        /// 원래 상태로 돌아간다. 한 순간에 튀는 효과(위험 표시 글리치·착지 스파크)는 되감을 수 없으므로
        /// <b>플레이 모드에서 시간이 앞으로 흘러 그 시각을 지날 때만</b> 터뜨린다.
        /// </remarks>
        public void Sample(float time)
        {
            if (!sampling) BeginSampling();

            float prev = lastSampleTime;
            lastSampleTime = time;
            bool emitEvents = Application.isPlaying && time > prev;

            Timings tm = BuildTimings();

            if (time < 0f || time >= tm.End)
            {
                if (group != null) group.alpha = 0f;
                SetActive(gameObject, false);
                return;
            }

            // 먼저 켜야 한다. 플레이 모드에서 처음 켜질 때 Awake가 알파를 0으로 덮으므로, 알파는 그 뒤에 쓴다.
            SetActive(gameObject, true);
            if (group == null) group = GetComponent<CanvasGroup>();

            bool sparkOn = SampleSparks(time, prev, emitEvents, in tm);
            bool flareOn = SampleFlare(time, prev, emitEvents, in tm);
            if (flareOn && time >= tm.FlareEnd)
            {
                // 한 프레임보다 짧은 플레어를 이번 프레임에 억지로 그렸다. 지나쳐 버린 뒤쪽 시각(깜박임·글리치 등)은
                // 다음 프레임이 교차로 잡도록 기준 시각을 플레어 끝 직전으로 되돌린다.
                lastSampleTime = Mathf.Min(time, tm.FlareEnd - 0.0001f);
            }

            SampleGroupAlpha(time, flareOn || sparkOn, in tm);
            // 경고가 아직 안 나올 때. 플레어 뒤에 나오는 설정에서 한 프레임짜리 플레어를 억지로 그리는 프레임도 포함한다
            // (그 프레임에 경고까지 같이 뜨면 "플레어 → 경고" 순서가 무너진다).
            bool beforeWarning = time < tm.WarningStart || (flareOn && tm.WarningStart >= tm.FlareEnd);

            SampleWarning(time, beforeWarning, in tm);
            SampleIcon(time, prev, emitEvents, beforeWarning, in tm);
            SampleBoss(time, in tm);
            SampleAsh(time, in tm);

            // 착지 프레임. 부딪힌 바로 그 순간에 스파크가 튀어야 텍스트가 부순 것으로 읽힌다.
            if (emitEvents && sparkBurst != null && warningIcon != null && Crossed(prev, time, tm.Land))
                sparkBurst.Play(warningIcon.rectTransform.position);
        }

        /// <summary>
        /// 샘플링 세션을 닫고 세션 시작 시점의 상태로 되돌린다. 열린 세션이 없으면 아무것도 하지 않는다.
        /// 타임라인 클립이 범위를 벗어나거나 그래프가 사라질 때 부른다 — 빠지면 연출이 중간에 잘렸을 때
        /// 경고 띠가 밀려난 채, BOSS가 박힌 채 화면에 남는다.
        /// </summary>
        public void EndSampling()
        {
            if (!sampling) return;
            sampling = false;

            MoveBands(0f);

            if (bossRoot != null)
            {
                bossRoot.anchoredPosition = bossHome;
                bossRoot.localScale = rest.BossScale;
                SetActive(bossRoot.gameObject, rest.BossActive);
            }

            if (warningIcon != null)
            {
                warningIcon.rectTransform.localScale = rest.IconScale;
                warningIcon.rectTransform.anchoredPosition = iconHome;
                warningIcon.color = rest.IconColor;
                SetActive(warningIcon.gameObject, rest.IconActive);
            }

            SetActive(warningRoot, rest.WarningActive);
            if (warningFlare != null)
            {
                warningFlare.rectTransform.localScale = rest.FlareScale;
                SetActive(warningFlare.gameObject, rest.FlareActive);
            }

            if (sparkFlares != null && rest.SparkActive != null)
            {
                for (int i = 0; i < sparkFlares.Length && i < rest.SparkActive.Length; i++)
                {
                    if (sparkFlares[i] == null) continue;
                    sparkFlares[i].rectTransform.localScale = rest.SparkScale[i];
                    sparkFlares[i].rectTransform.localRotation = rest.SparkRotation[i];
                    SetActive(sparkFlares[i].gameObject, rest.SparkActive[i]);
                }
            }

            if (ashDissolve != null && ashTouched) ashDissolve.ResetDissolve();
            ashTouched = false;

            if (group != null) group.alpha = rest.Alpha;
            SetActive(gameObject, rest.RootActive);
        }

        /// <summary>
        /// 타임라인 에디터 미리보기가 건드리는 속성을 등록한다. <see cref="BossIntroTrack"/>이 부른다.
        /// </summary>
        /// <param name="driver">미리보기가 끝나면 등록된 속성을 원래 값으로 되돌리는 수집기.</param>
        /// <remarks>
        /// 여기에 없는 속성을 <see cref="Sample"/>이 바꾸면, 헤드를 끌어 본 것만으로 그 값이 씬에 저장된다
        /// (띠가 밀려난 위치, 꺼진 오브젝트가 그대로 커밋되는 사고). 그리는 대상을 늘리면 여기도 함께 늘린다.
        /// </remarks>
        public void CollectDrivenProperties(IPropertyCollector driver)
        {
            if (driver == null) return;

            driver.AddFromName(gameObject, "m_IsActive");
            AddComponent(driver, GetComponent<CanvasGroup>());

            if (warningRoot != null) driver.AddFromName(warningRoot, "m_IsActive");
            if (warningFlare != null)
            {
                driver.AddFromName(warningFlare.gameObject, "m_IsActive");
                AddComponent(driver, warningFlare.rectTransform);
            }

            if (sparkFlares != null)
            {
                foreach (Graphic spark in sparkFlares)
                {
                    if (spark == null) continue;
                    driver.AddFromName(spark.gameObject, "m_IsActive");
                    AddComponent(driver, spark.rectTransform);
                }
            }

            if (bands != null)
            {
                foreach (ScrollingBand band in bands)
                {
                    if (band != null) AddComponent(driver, band.transform);
                }
            }

            if (warningIcon != null)
            {
                driver.AddFromName(warningIcon.gameObject, "m_IsActive");
                AddComponent(driver, warningIcon.rectTransform);
                AddComponent(driver, warningIcon);
            }

            if (bossRoot != null)
            {
                driver.AddFromName(bossRoot.gameObject, "m_IsActive");
                AddComponent(driver, bossRoot);
            }

            if (bossNameText != null)
            {
                driver.AddFromName(bossNameText.gameObject, "m_IsActive");
                AddComponent(driver, bossNameText);
            }
        }

        private IEnumerator PlayRoutine()
        {
            float end = Duration;
            float elapsed = 0f;

            while (elapsed < end)
            {
                Sample(elapsed);
                yield return null;
                elapsed += Delta;
            }

            // 다 탄 뒤의 모습은 "아무것도 없음"이다. 상태를 되돌린 뒤 그룹째 내린다.
            routine = null;
            EndSampling();
            group.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void BeginSampling()
        {
            sampling = true;
            lastSampleTime = -1f;
            ashTouched = false;
            flareShown = false;
            sparksShownMask = 0;

            // 플레이 중엔 재생마다 각도를 새로 뽑고, 에디터 스크럽 중엔 고정해 헤드를 끌 때마다 각도가 튀지 않게 한다.
            sparkAngleSeed = Application.isPlaying ? Random.Range(1, int.MaxValue) : 0;

            if (group == null) group = GetComponent<CanvasGroup>();
            ResolveOptionalRefs();
            WarnIfFlareInsideWarning();

            // 세션마다 제자리를 다시 뜬다. 지난 세션은 EndSampling이 제자리로 돌려 두었으므로 항상 같은 값이다.
            CaptureBandHome();
            if (bossRoot != null) bossHome = bossRoot.anchoredPosition;
            if (warningIcon != null) iconHome = warningIcon.rectTransform.anchoredPosition;

            rest = new RestState
            {
                RootActive = gameObject.activeSelf,
                Alpha = group != null ? group.alpha : 0f,
                WarningActive = warningRoot != null && warningRoot.activeSelf,
                IconActive = warningIcon != null && warningIcon.gameObject.activeSelf,
                IconColor = warningIcon != null ? warningIcon.color : Color.white,
                IconScale = warningIcon != null ? warningIcon.rectTransform.localScale : Vector3.one,
                FlareActive = warningFlare != null && warningFlare.gameObject.activeSelf,
                FlareScale = warningFlare != null ? warningFlare.rectTransform.localScale : Vector3.one,
                SparkActive = CaptureSparkActive(),
                SparkScale = CaptureSparkScale(),
                SparkRotation = CaptureSparkRotation(),
                BossActive = bossRoot != null && bossRoot.gameObject.activeSelf,
                BossScale = bossRoot != null ? bossRoot.localScale : Vector3.one,
            };
        }

        // 플레어를 경고 묶음 안에 두면, 플레어가 켜지는 동안 묶음이 꺼져 있어 아무것도 안 보인다.
        // 화면만 봐서는 "플레어가 안 나온다"로만 보여 원인을 찾기 어려우니 짚어 준다.
        private void WarnIfFlareInsideWarning()
        {
            if (warningRoot == null) return;

            WarnIfInsideWarning(warningFlare, "Warning Flare");
            if (sparkFlares == null) return;
            foreach (Graphic spark in sparkFlares) WarnIfInsideWarning(spark, "Spark Flares");
        }

        private void WarnIfInsideWarning(Graphic flare, string slot)
        {
            if (flare == null || !flare.transform.IsChildOf(warningRoot.transform)) return;

            Debug.LogWarning($"{name}: {slot} '{flare.name}'가 경고 묶음 '{warningRoot.name}' 안에 있어 " +
                             "플레어 동안 함께 꺼져 보이지 않습니다. 경고 묶음 밖(이 오브젝트 바로 아래)으로 옮기세요.", flare);
        }

        private bool[] CaptureSparkActive()
        {
            if (sparkFlares == null) return null;

            var active = new bool[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                active[i] = sparkFlares[i] != null && sparkFlares[i].gameObject.activeSelf;
            return active;
        }

        private Quaternion[] CaptureSparkRotation()
        {
            if (sparkFlares == null) return null;

            var rotation = new Quaternion[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                rotation[i] = sparkFlares[i] != null ? sparkFlares[i].rectTransform.localRotation : Quaternion.identity;
            return rotation;
        }

        private Vector3[] CaptureSparkScale()
        {
            if (sparkFlares == null) return null;

            var scale = new Vector3[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                scale[i] = sparkFlares[i] != null ? sparkFlares[i].rectTransform.localScale : Vector3.one;
            return scale;
        }

        private void ResolveOptionalRefs()
        {
            if (iconGlitch == null && warningIcon != null) iconGlitch = warningIcon.GetComponent<GlitchImageFx>();
            if (sparkBurst == null) sparkBurst = GetComponentInChildren<SparkBurstFx>(true);
            if (ashDissolve == null) ashDissolve = GetComponentInChildren<AshDissolveFx>(true);
        }

        /// <summary>
        /// 인스펙터 값에서 단계별 시각을 계산한다. 순서는 <b>(플레어) → 흐름 → 깜박임 → 걷힘(과 겹쳐) 충돌 → 머묾 → 타들어감</b>이다.
        /// </summary>
        /// <remarks>
        /// 슬램 시작은 커튼 끝이 아니라 커튼 진행 도중(<see cref="slamTriggerRatio"/>)에 둔다.
        /// 다 걷힌 뒤에 박으면 두 동작이 순서대로 재생되는 별개 연출로 보인다.
        /// </remarks>
        private Timings BuildTimings()
        {
            Timings t = default;
            // 플레어가 없으면 둘 다 0 — 이하 시각이 플레어 추가 전과 같다.
            // 주위 빛이 하나씩 터지고, 마지막 빛에서 한 간격 뒤에 메인 플레어가 터진다. 주위 빛이 없으면 0.
            t.SparkCount = ActiveSparkCount;
            t.FlareStart = t.SparkCount * sparkInterval;
            t.FlareEnd = t.FlareStart + (warningFlare != null ? flareSeconds : 0f);
            t.WarningStart = t.FlareStart + (warningFlare != null ? warningDelayAfterFlare : 0f);

            // 플레어가 번쩍이는 도중에 나타나면 페이드를 건너뛴다. 빛이 등장 순간을 가려 주므로 서서히 밝아질 이유가 없고,
            // 페이드를 그대로 두면 같은 그룹의 플레어까지 흐려진다.
            t.FadeIn = t.WarningStart < t.FlareEnd ? 0f : warningFadeIn;
            t.BlinkStart = t.WarningStart + t.FadeIn;
            t.HoldStart = t.BlinkStart + fastBlinkCount * (fastBlinkOnSeconds + fastBlinkOffSeconds);
            t.CurtainStart = t.HoldStart + iconHoldSeconds;
            t.CurtainEnd = t.CurtainStart + curtainDuration;
            t.SlamStart = t.CurtainStart + curtainDuration * slamTriggerRatio + slamExtraDelay;
            t.Land = t.SlamStart + slamDuration;
            t.ShakeStart = t.Land + impactHoldSeconds;
            t.ShakeEnd = t.ShakeStart + ((impactShakeStrength > 0f || impactSquash > 0f) && impactShakeDuration > 0f ? impactShakeDuration : 0f);
            t.OutStart = t.ShakeEnd + holdSeconds;
            t.OutDuration = ashDissolve != null ? ashDissolve.Duration : fadeOutSeconds;
            t.End = t.OutStart + t.OutDuration;
            return t;
        }

        /// <summary>
        /// 경고 직전의 플레어. 켜져 있어야 하는 프레임이면 true를 돌려주고, 그동안 경고·아이콘은 숨긴다.
        /// </summary>
        /// <remarks>
        /// 페이드 없이 한 번에 켜졌다 꺼진다. 서서히 밝아지면 "반짝"이 아니라 "켜졌다"로 읽혀,
        /// 경고가 터져 들어오는 인상이 약해진다. 대신 켜져 있는 동안 작게 시작해 커진다 — 크기가 그대로면
        /// 켜졌다 꺼지는 깜빡임으로만 보이고, 커져야 빛이 터져 나오는 것으로 읽힌다.
        /// </remarks>
        private bool SampleFlare(float time, float prev, bool emitEvents, in Timings tm)
        {
            if (warningFlare == null) return false;

            bool shown = flareShown;
            // 메인 플레어는 돌리지 않는다(가로로 긴 모양이라 돌면 화면을 가르는 선이 기울어 보인다).
            bool on = SampleBurst(warningFlare, rest.FlareScale, tm.FlareStart, tm.FlareEnd, time, prev, emitEvents, ref shown);
            flareShown = shown;
            return on;
        }

        /// <summary>
        /// 메인 플레어 전에 주위 빛을 배열 순서대로 하나씩 터뜨린다. 하나라도 켜져 있으면 true.
        /// </summary>
        /// <remarks>
        /// 한꺼번에 켜지 않고 간격을 두고 차례로 터뜨린다 — 모바일 게임의 별 등급처럼 "하나, 둘, 셋…" 세어지는 박자가
        /// 곧 보스의 강함으로 읽힌다. 개수(<see cref="SetSparkCount"/>)를 넘는 빛은 이번 재생에서 꺼 둔다.
        /// </remarks>
        private bool SampleSparks(float time, float prev, bool emitEvents, in Timings tm)
        {
            if (sparkFlares == null) return false;

            bool anyOn = false;
            for (int i = 0; i < sparkFlares.Length; i++)
            {
                Graphic spark = sparkFlares[i];
                if (spark == null) continue;

                if (i >= tm.SparkCount)
                {
                    SetActive(spark.gameObject, false);
                    continue;
                }

                float start = i * sparkInterval;
                int bit = 1 << i;
                bool shown = (sparksShownMask & bit) != 0;
                Vector3 baseScale = rest.SparkScale != null && i < rest.SparkScale.Length ? rest.SparkScale[i] : Vector3.one;
                Quaternion baseRotation = rest.SparkRotation != null && i < rest.SparkRotation.Length ? rest.SparkRotation[i] : Quaternion.identity;

                // 빛마다 다른 시작 각도. 씨앗+인덱스로 뽑아 한 재생 안에서는 프레임마다 같은 값이 나온다.
                float startAngle = (Hash01(sparkAngleSeed + i * 7919) * 2f - 1f) * sparkRandomAngle;

                if (SampleBurst(spark, baseScale, start, start + sparkSeconds, time, prev, emitEvents, ref shown))
                {
                    anyOn = true;
                    ApplySparkRotation(spark, baseRotation, startAngle, start, start + sparkSeconds, time);
                }

                if (shown) sparksShownMask |= bit;
            }

            return anyOn;
        }

        /// <summary>
        /// 켜져 있는 주위 빛을 시작 각도에서 <see cref="sparkSpinDegrees"/>만큼 돌린다. 크기와 같은 곡선을 따른다.
        /// </summary>
        /// <remarks>
        /// 회전은 배치해 둔 각도(세션 시작 값)에 더한다. 레이아웃에서 빛마다 기울여 둔 각도를 덮어쓰지 않게 하기 위함이다.
        /// </remarks>
        private void ApplySparkRotation(Graphic spark, Quaternion baseRotation, float startAngle, float start, float end, float time)
        {
            float length = end - start;
            float k = length > 0f ? Mathf.Clamp01((time - start) / length) : 1f;
            float e = flareScaleCurve != null && flareScaleCurve.length > 0 ? flareScaleCurve.Evaluate(k) : k;

            spark.rectTransform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, startAngle + sparkSpinDegrees * e);
        }

        /// <summary>
        /// 빛 하나를 [<paramref name="start"/>, <paramref name="end"/>) 동안 켜고, 그사이 작게 시작해 커지게 한다.
        /// 메인 플레어와 주위 빛이 같은 규칙으로 터진다.
        /// </summary>
        /// <param name="shown">이번 세션에 한 프레임이라도 그렸는지. 한 프레임보다 짧은 빛을 건너뛰지 않기 위한 기준.</param>
        /// <returns>이번 프레임에 켜져 있으면 true.</returns>
        private bool SampleBurst(Graphic burst, Vector3 baseScale, float start, float end,
                                 float time, float prev, bool emitEvents, ref bool shown)
        {
            bool on = time >= start && time < end;

            // 빛이 한 프레임보다 짧으면 두 샘플 사이에 끼어 한 번도 안 그려질 수 있다.
            // 플레이 중 그 구간을 방금 건너뛰었다면 이번 프레임에 한 번은 그린다(스크럽 중에는 보이는 그대로 둔다).
            if (!on && emitEvents && !shown && time >= start && prev < end) on = true;
            if (on) shown = true;

            if (on)
            {
                // 크기는 오브젝트에 잡아 둔 크기(세션 시작 값)에 곱한다. 레이아웃에서 맞춘 비율을 덮어쓰지 않게.
                // 억지로 그린 한 프레임(구간을 이미 지난 시각)은 끝 크기로 보인다.
                float length = end - start;
                float k = length > 0f ? Mathf.Clamp01((time - start) / length) : 1f;
                float e = flareScaleCurve != null && flareScaleCurve.length > 0 ? flareScaleCurve.Evaluate(k) : k;
                burst.rectTransform.localScale = baseScale * Mathf.LerpUnclamped(flareStartScale, flareEndScale, e);
            }

            SetActive(burst.gameObject, on);
            return on;
        }

        /// <summary>그룹 알파. 경고가 나타나는 페이드인과, 재 연출이 없을 때의 페이드아웃만 담당한다.</summary>
        private void SampleGroupAlpha(float time, bool flareOn, in Timings tm)
        {
            // 플레어는 같은 그룹 안에 있으므로 번쩍이는 동안은 1이어야 한다. 경고의 페이드인은 경고가 나타나는 시각부터 센다
            // (플레어 도중에 나타나는 설정이면 FadeIn이 0이라 페이드 자체가 없다).
            float alpha = 1f;
            if (!flareOn && tm.FadeIn > 0f && time < tm.WarningStart + tm.FadeIn)
                alpha = Mathf.Clamp01((time - tm.WarningStart) / tm.FadeIn);

            // 재 연출을 쓰면 텍스트가 직접 타들어가므로 그룹은 1로 둔다.
            // 둘을 겹치면 재가 뜨자마자 같이 흐려져 날리는 게 안 보인다.
            if (ashDissolve == null && time >= tm.OutStart && tm.OutDuration > 0f)
                alpha = 1f - Mathf.Clamp01((time - tm.OutStart) / tm.OutDuration);

            group.alpha = alpha;
        }

        /// <summary>
        /// 경고 띠가 위아래로 갈라지며 화면 밖으로 쓸려 나가고, 다 빠져나가면 경고 묶음을 통째로 끈다.
        /// </summary>
        /// <remarks>
        /// 띠를 그냥 <c>SetActive(false)</c>로 지우면 흐르던 것이 한 프레임에 툭 없어져
        /// 다음 단계와 이어지지 않는다. 화면 밖으로 밀어내면 "걷혔다"가 되고,
        /// 열린 자리로 BOSS가 들어오는 인과가 생긴다.
        /// </remarks>
        private void SampleWarning(float time, bool beforeWarning, in Timings tm)
        {
            SetActive(warningRoot, !beforeWarning && time < tm.CurtainEnd);

            float progress = time < tm.CurtainStart
                ? 0f
                : curtainCurve.Evaluate(Mathf.Clamp01((time - tm.CurtainStart) / Mathf.Max(0.0001f, curtainDuration)));
            MoveBands(progress);
        }

        /// <summary>
        /// 위험 표시: 빠르게 두어 번 깜박인 뒤 켜진 채 머물면서 점점 커지고 거세게 떨다가, BOSS가 착지하는 순간 부풀었다 터져 사라진다.
        /// </summary>
        /// <remarks>
        /// 같은 간격으로 계속 깜박이면 신호등처럼 읽힌다. 짧게 튄 뒤 멎어야 "경고가 들어왔다"가 되고,
        /// 머무는 동안 긴장이 쌓인다. 경고가 길어지면 가만히 있는 아이콘이 정적으로 보이므로, 착지까지 크기와 떨림을
        /// 함께 키워 "차오르다 터진다"로 만든다. 서서히 줄여 없애면 "조용히 물러났다"가 되므로, 부풀렸다 한 번에 터뜨려
        /// BOSS가 그 자리를 밀어내고 들어온 것처럼 읽히게 한다.
        /// </remarks>
        private void SampleIcon(float time, float prev, bool emitEvents, bool beforeWarning, in Timings tm)
        {
            if (warningIcon == null) return;

            // 고조 진행도: 아이콘이 나타난 순간 0 → 착지 순간 1. 곡선을 거쳐 뒤로 갈수록 빠르게 차오른다.
            float buildSpan = tm.Land - tm.WarningStart;
            float buildRaw = buildSpan > 0f ? Mathf.Clamp01((time - tm.WarningStart) / buildSpan) : 1f;
            float build = iconBuildUpCurve != null && iconBuildUpCurve.length > 0 ? iconBuildUpCurve.Evaluate(buildRaw) : buildRaw;
            float grown = Mathf.LerpUnclamped(1f, iconBuildUpScale, build);

            bool visible;
            float scale = grown;
            float alpha = 1f;
            Vector2 shake = Vector2.zero;

            if (beforeWarning)
            {
                // 플레어가 먼저 번쩍이고, 아이콘은 Warning Delay After Flare 뒤에 나타난다.
                visible = false;
            }
            else if (time < tm.Land)
            {
                visible = time >= tm.HoldStart || IsBlinkOn(time, in tm);

                // 떨림도 크기와 같은 진행도로 거세진다. 시각에서 결정적으로 뽑아 스크럽해도 같은 모양이 나온다.
                float amp = Mathf.LerpUnclamped(iconShakeStart, iconShakeEnd, build);
                if (amp > 0f)
                {
                    int step = Mathf.FloorToInt(time * ShakeRate);
                    shake.x = (Hash01(step * 2 + 101) * 2f - 1f) * amp;
                    shake.y = (Hash01(step * 2 + 102) * 2f - 1f) * amp;
                }
            }
            else if (iconPopDuration > 0f && time < tm.Land + iconPopDuration)
            {
                // 터짐은 다 커진 크기에서 시작한다. 1에서 다시 시작하면 착지 순간 아이콘이 툭 줄어들어 보인다.
                float k = (time - tm.Land) / iconPopDuration;
                visible = true;
                scale = grown * Mathf.Lerp(1f, iconPopScale, k);
                alpha = 1f - k;
            }
            else
            {
                visible = false;
            }

            warningIcon.rectTransform.localScale = Vector3.one * scale;
            warningIcon.rectTransform.anchoredPosition = iconHome + shake;

            Color c = warningIcon.color;
            c.a = alpha;
            warningIcon.color = c;

            SetActive(warningIcon.gameObject, visible);

            // 켜지는 순간마다 글리치를 크게 튀긴다. 깜박임과 지지직이 같은 박자로 맞아야
            // 신호가 들어오면서 화면이 흔들리는 것처럼 읽힌다.
            if (visible && emitEvents && iconGlitch != null && CrossedIconOnEdge(prev, time, in tm))
                iconGlitch.Pulse();
        }

        /// <summary>
        /// BOSS가 위에서 내려와 제자리에 꽂힌다. 하강 → 정지 → 진동 순서다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 크기만 줄이면 "멀리서 다가온다"라 부드럽게 읽힌다. 도장은 <b>아래로</b> 내려와야 하므로
        /// 높이와 크기를 같은 곡선으로 함께 줄인다. 착지 뒤에는 반동 대신 <b>완전한 정지</b>를 두는데,
        /// 움직이던 것이 뚝 끊기는 그 정적이 충돌을 인지시킨다. 진동은 그 뒤에 오는 여파다.
        /// </para>
        /// <para>
        /// <b>진동은 난수 떨림이 아니라 잦아드는 파형이다.</b> 매 프레임 난수로 흔들면 방향이 제멋대로 튀어
        /// "정신없이 떨린다"로 읽힌다.
        /// </para>
        /// <para>
        /// <b>"쿵"과 "젤리"를 가르는 것은 첫 순간과 감쇠다.</b> 사인(0에서 출발)으로 흔들면 박힌 뒤 폭이 서서히 차올라
        /// 말랑하게 눌리는 것처럼 보이고, 감쇠가 느리면 여러 번 출렁여 젤리가 된다. 단단한 것이 떨어지면 <b>닿는 순간이
        /// 가장 세고</b> 곧바로 죽는다. 그래서 코사인(최대에서 출발)으로 착지 첫 프레임에 아래로 박히게 하고,
        /// 지수 감쇠(<see cref="impactShakeDamping"/>)로 한두 번 튕기고 멎게 한다.
        /// </para>
        /// </remarks>
        private void SampleBoss(float time, in Timings tm)
        {
            if (bossRoot == null) return;

            bool active = time >= tm.SlamStart;
            SetActive(bossRoot.gameObject, active);

            Vector3 scale = Vector3.one;
            Vector2 offset = Vector2.zero;

            if (active && time < tm.Land)
            {
                float e = slamCurve.Evaluate(Mathf.Clamp01((time - tm.SlamStart) / Mathf.Max(0.0001f, slamDuration)));
                scale = Vector3.one * Mathf.LerpUnclamped(slamStartScale, 1f, e);
                offset.y = Mathf.LerpUnclamped(slamDropDistance, 0f, e);
            }
            else if (time >= tm.ShakeStart && time < tm.ShakeEnd)
            {
                float t = time - tm.ShakeStart;

                // 지수로 빠르게 죽인다(쿵의 핵심). 끝에서 정확히 0에 닿도록 남은 시간 비율을 한 번 더 곱한다.
                float fade = 1f - Mathf.Clamp01(t / impactShakeDuration);
                float decay = Mathf.Exp(-impactShakeDamping * t) * fade;

                // 코사인으로 최대에서 출발한다 — 착지 첫 프레임에 아래로 가장 세게 박히고, 그다음 튕겨 오른다.
                float wave = Mathf.Cos(2f * Mathf.PI * impactShakeFrequency * t);

                offset.y = -impactShakeStrength * decay * wave;

                // 가로는 박자를 어긋낸 작은 흔들림. 세로와 같은 박자면 대각선으로만 움직여 기계적으로 보인다.
                offset.x = impactShakeStrength * impactShakeHorizontal * decay *
                           Mathf.Sin(2f * Mathf.PI * impactShakeFrequency * 1.37f * t);

                // 아래로 눌릴 때(wave > 0) 납작하고 넓게, 튀어 오를 때 반대로. 부피가 유지되는 것처럼 가로·세로를 반대로 준다.
                float squash = impactSquash * decay * wave;
                scale = new Vector3(1f + squash, 1f - squash, 1f);
            }

            bossRoot.localScale = scale;
            bossRoot.anchoredPosition = bossHome + offset;
        }

        /// <summary>
        /// 머묾이 끝나면 BOSS 텍스트가 재처럼 타들어간다. 이 시점에 화면에 남은 것은 BOSS 텍스트뿐이다.
        /// </summary>
        private void SampleAsh(float time, in Timings tm)
        {
            if (ashDissolve == null) return;

            if (time >= tm.OutStart)
            {
                ashDissolve.SetProgress(tm.OutDuration > 0f ? (time - tm.OutStart) / tm.OutDuration : 1f);
                ashTouched = true;
            }
            else if (ashTouched)
            {
                // 되감아 머묾 이전으로 돌아왔다 — 탄 자국을 한 번만 지운다.
                ashDissolve.ResetDissolve();
                ashTouched = false;
            }
        }

        private bool IsBlinkOn(float time, in Timings tm)
        {
            float local = time - tm.BlinkStart;
            if (local < 0f) return true;   // 페이드인 동안은 켜진 채로 나타난다

            float period = fastBlinkOnSeconds + fastBlinkOffSeconds;
            if (period <= 0f) return true;

            return local % period < fastBlinkOnSeconds;
        }

        // 위험 표시가 "켜지는" 시각들: 경고 등장, 빠른 깜박임의 매 켜짐, 머묾 시작.
        private bool CrossedIconOnEdge(float prev, float now, in Timings tm)
        {
            if (Crossed(prev, now, tm.WarningStart) || Crossed(prev, now, tm.HoldStart)) return true;

            float period = fastBlinkOnSeconds + fastBlinkOffSeconds;
            for (int i = 0; i < fastBlinkCount; i++)
            {
                if (Crossed(prev, now, tm.BlinkStart + i * period)) return true;
            }

            return false;
        }

        private static bool Crossed(float prev, float now, float edge) => prev < edge && edge <= now;

        /// <summary>띠를 각자 화면 바깥 방향으로 <paramref name="progress"/>만큼 밀어낸다.</summary>
        private void MoveBands(float progress)
        {
            if (bands == null || bandHome == null) return;

            for (int i = 0; i < bands.Length && i < bandHome.Length; i++)
            {
                if (bands[i] == null) continue;

                // 화면 중앙보다 위에 있으면 위로, 아래면 아래로 나간다.
                float outward = bandHome[i].y >= 0f ? 1f : -1f;

                RectTransform rt = (RectTransform)bands[i].transform;
                rt.anchoredPosition = bandHome[i] + new Vector2(0f, outward * curtainDistance * progress);
            }
        }

        private void CaptureBandHome()
        {
            if (bands == null)
            {
                bandHome = null;
                return;
            }

            bandHome = new Vector2[bands.Length];
            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i] != null) bandHome[i] = ((RectTransform)bands[i].transform).anchoredPosition;
            }
        }

        // 상태가 실제로 바뀔 때만 토글한다. 같은 값으로 매 프레임 SetActive를 부르면 하위 OnEnable/OnDisable이 튄다.
        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static void AddComponent(IPropertyCollector driver, Component component)
        {
            if (component != null) driver.AddFromComponent(component.gameObject, component);
        }

        /// <summary>정수 하나에서 0~1 의사난수를 뽑는다(PCG 해시). 같은 시각이면 항상 같은 흔들림이 나온다.</summary>
        private static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x = (x >> 22) ^ x;
                return x / (float)uint.MaxValue;
            }
        }

        private float Delta => Time.unscaledDeltaTime;
    }
}
