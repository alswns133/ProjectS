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

        [Tooltip("찍힌 뒤 흔들리는 폭(px). 0이면 흔들리지 않는다.")]
        [SerializeField, Min(0f)] private float impactShakeStrength = 22f;

        [Tooltip("흔들림이 잦아드는 시간(초).")]
        [SerializeField, Min(0f)] private float impactShakeDuration = 0.16f;

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

        // ── 샘플링 세션(코루틴 재생 1회 또는 타임라인 클립 1회) ──
        // 세션 시작 시점의 상태를 쥐고 있다가 끝날 때 그대로 되돌린다. 하드코딩한 "기본 상태"로 돌리면
        // 에디터 미리보기가 씬에 저장된 값을 바꿔 놓아 씬이 더러워진다.
        private bool sampling;
        private float lastSampleTime;
        private bool ashTouched;
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
            public bool BossActive;
            public Vector3 BossScale;
        }

        /// <summary>연출 안의 각 단계가 시작·끝나는 시각(초). 인스펙터 값에서 매번 계산한다.</summary>
        private struct Timings
        {
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
        public void Play(string bossName)
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            // 끊긴 지난 재생의 흔적을 먼저 되돌린다. 오브젝트를 켠 뒤에 되돌리면, 지난 세션이 꺼진 상태에서
            // 시작했을 때 여기서 다시 꺼져 방금 시작한 코루틴이 죽는다.
            EndSampling();

            SetBossName(bossName);

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

            SampleGroupAlpha(time, in tm);
            SampleWarning(time, in tm);
            SampleIcon(time, prev, emitEvents, in tm);
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
                warningIcon.color = rest.IconColor;
                SetActive(warningIcon.gameObject, rest.IconActive);
            }

            SetActive(warningRoot, rest.WarningActive);

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

            if (group == null) group = GetComponent<CanvasGroup>();
            ResolveOptionalRefs();

            // 세션마다 제자리를 다시 뜬다. 지난 세션은 EndSampling이 제자리로 돌려 두었으므로 항상 같은 값이다.
            CaptureBandHome();
            if (bossRoot != null) bossHome = bossRoot.anchoredPosition;

            rest = new RestState
            {
                RootActive = gameObject.activeSelf,
                Alpha = group != null ? group.alpha : 0f,
                WarningActive = warningRoot != null && warningRoot.activeSelf,
                IconActive = warningIcon != null && warningIcon.gameObject.activeSelf,
                IconColor = warningIcon != null ? warningIcon.color : Color.white,
                IconScale = warningIcon != null ? warningIcon.rectTransform.localScale : Vector3.one,
                BossActive = bossRoot != null && bossRoot.gameObject.activeSelf,
                BossScale = bossRoot != null ? bossRoot.localScale : Vector3.one,
            };
        }

        private void ResolveOptionalRefs()
        {
            if (iconGlitch == null && warningIcon != null) iconGlitch = warningIcon.GetComponent<GlitchImageFx>();
            if (sparkBurst == null) sparkBurst = GetComponentInChildren<SparkBurstFx>(true);
            if (ashDissolve == null) ashDissolve = GetComponentInChildren<AshDissolveFx>(true);
        }

        /// <summary>
        /// 인스펙터 값에서 단계별 시각을 계산한다. 순서는 <b>흐름 → 깜박임 → 걷힘(과 겹쳐) 충돌 → 머묾 → 타들어감</b>이다.
        /// </summary>
        /// <remarks>
        /// 슬램 시작은 커튼 끝이 아니라 커튼 진행 도중(<see cref="slamTriggerRatio"/>)에 둔다.
        /// 다 걷힌 뒤에 박으면 두 동작이 순서대로 재생되는 별개 연출로 보인다.
        /// </remarks>
        private Timings BuildTimings()
        {
            Timings t = default;
            t.FadeIn = warningFadeIn;
            t.BlinkStart = warningFadeIn;
            t.HoldStart = t.BlinkStart + fastBlinkCount * (fastBlinkOnSeconds + fastBlinkOffSeconds);
            t.CurtainStart = t.HoldStart + iconHoldSeconds;
            t.CurtainEnd = t.CurtainStart + curtainDuration;
            t.SlamStart = t.CurtainStart + curtainDuration * slamTriggerRatio + slamExtraDelay;
            t.Land = t.SlamStart + slamDuration;
            t.ShakeStart = t.Land + impactHoldSeconds;
            t.ShakeEnd = t.ShakeStart + (impactShakeStrength > 0f && impactShakeDuration > 0f ? impactShakeDuration : 0f);
            t.OutStart = t.ShakeEnd + holdSeconds;
            t.OutDuration = ashDissolve != null ? ashDissolve.Duration : fadeOutSeconds;
            t.End = t.OutStart + t.OutDuration;
            return t;
        }

        /// <summary>그룹 알파. 경고가 나타나는 페이드인과, 재 연출이 없을 때의 페이드아웃만 담당한다.</summary>
        private void SampleGroupAlpha(float time, in Timings tm)
        {
            float alpha = 1f;
            if (tm.FadeIn > 0f && time < tm.FadeIn) alpha = time / tm.FadeIn;

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
        private void SampleWarning(float time, in Timings tm)
        {
            SetActive(warningRoot, time < tm.CurtainEnd);

            float progress = time < tm.CurtainStart
                ? 0f
                : curtainCurve.Evaluate(Mathf.Clamp01((time - tm.CurtainStart) / Mathf.Max(0.0001f, curtainDuration)));
            MoveBands(progress);
        }

        /// <summary>
        /// 위험 표시: 빠르게 두어 번 깜박인 뒤 켜진 채 머물고, BOSS가 착지하는 순간 부풀었다 터져 사라진다.
        /// </summary>
        /// <remarks>
        /// 같은 간격으로 계속 깜박이면 신호등처럼 읽힌다. 짧게 튄 뒤 멎어야 "경고가 들어왔다"가 되고,
        /// 머무는 동안 긴장이 쌓인다. 서서히 줄여 없애면 "조용히 물러났다"가 되므로, 부풀렸다 한 번에 터뜨려
        /// BOSS가 그 자리를 밀어내고 들어온 것처럼 읽히게 한다.
        /// </remarks>
        private void SampleIcon(float time, float prev, bool emitEvents, in Timings tm)
        {
            if (warningIcon == null) return;

            bool visible;
            float scale = 1f;
            float alpha = 1f;

            if (time < tm.HoldStart)
            {
                visible = IsBlinkOn(time, in tm);
            }
            else if (time < tm.Land)
            {
                visible = true;
            }
            else if (iconPopDuration > 0f && time < tm.Land + iconPopDuration)
            {
                float k = (time - tm.Land) / iconPopDuration;
                visible = true;
                scale = Mathf.Lerp(1f, iconPopScale, k);
                alpha = 1f - k;
            }
            else
            {
                visible = false;
            }

            warningIcon.rectTransform.localScale = Vector3.one * scale;

            Color c = warningIcon.color;
            c.a = alpha;
            warningIcon.color = c;

            SetActive(warningIcon.gameObject, visible);

            // 켜지는 순간마다 글리치를 크게 튀긴다. 깜박임과 지지직이 같은 박자로 맞아야
            // 신호가 들어오면서 화면이 흔들리는 것처럼 읽힌다.
            if (emitEvents && iconGlitch != null && CrossedIconOnEdge(prev, time, in tm))
                iconGlitch.Pulse();
        }

        /// <summary>
        /// BOSS가 위에서 내려와 제자리에 꽂힌다. 하강 → 정지 → 흔들림 순서다.
        /// </summary>
        /// <remarks>
        /// 크기만 줄이면 "멀리서 다가온다"라 부드럽게 읽힌다. 도장은 <b>아래로</b> 내려와야 하므로
        /// 높이와 크기를 같은 곡선으로 함께 줄인다. 착지 뒤에는 반동 대신 <b>완전한 정지</b>를 두는데,
        /// 움직이던 것이 뚝 끊기는 그 정적이 충돌을 인지시킨다. 흔들림은 그 뒤에 오는 여파로,
        /// 세로로 더 크게 흔들어 내려찍은 방향을 남긴다.
        /// </remarks>
        private void SampleBoss(float time, in Timings tm)
        {
            if (bossRoot == null) return;

            bool active = time >= tm.SlamStart;
            SetActive(bossRoot.gameObject, active);

            float scale = 1f;
            Vector2 offset = Vector2.zero;

            if (active && time < tm.Land)
            {
                float e = slamCurve.Evaluate(Mathf.Clamp01((time - tm.SlamStart) / Mathf.Max(0.0001f, slamDuration)));
                scale = Mathf.LerpUnclamped(slamStartScale, 1f, e);
                offset.y = Mathf.LerpUnclamped(slamDropDistance, 0f, e);
            }
            else if (time >= tm.ShakeStart && time < tm.ShakeEnd)
            {
                // 제곱으로 잦아들게 해 첫 순간이 가장 크고 빠르게 멎는다.
                float k = 1f - Mathf.Clamp01((time - tm.ShakeStart) / impactShakeDuration);
                float amp = impactShakeStrength * k * k;

                int step = Mathf.FloorToInt(time * ShakeRate);
                offset.x = (Hash01(step * 2 + 1) * 2f - 1f) * amp * 0.45f;
                offset.y = (Hash01(step * 2 + 2) * 2f - 1f) * amp;
            }

            bossRoot.localScale = Vector3.one * scale;
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
            if (Crossed(prev, now, 0f) || Crossed(prev, now, tm.HoldStart)) return true;

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
