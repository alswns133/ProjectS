using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
    /// 시간은 unscaled로 센다. 보스 등장은 히트스톱·컷신으로 timeScale이 낮아진 순간에 겹치기 쉽다.
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

        [Tooltip("커튼이 이만큼 진행할 때까지 위험 표시가 줄어들어 사라진다(0~1).")]
        [SerializeField, Range(0.05f, 1f)] private float iconShrinkRatio = 0.45f;

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

        [Tooltip("사라지는 데 걸리는 시간(초).")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

        private CanvasGroup group;
        private Coroutine routine;

        // 커튼은 띠를, 슬램은 BOSS를 제자리에서 옮긴다. 원래 자리를 기억해 두지 않으면
        // 두 번째 재생부터 옮겨진 자리를 기준으로 삼아 조금씩 밀려난다.
        private Vector2[] bandHome;
        private Vector2 bossHome;

        /// <summary>지금 재생 중인지.</summary>
        public bool IsPlaying => routine != null;

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
        /// 보스 등장 연출을 처음부터 재생한다. 이미 재생 중이면 중단하고 다시 시작한다.
        /// </summary>
        /// <param name="bossName">BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다.</param>
        public void Play(string bossName)
        {
            if (bossNameText != null)
            {
                bossNameText.text = bossName;
                bossNameText.gameObject.SetActive(!string.IsNullOrWhiteSpace(bossName));
            }

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"{name}: 컴포넌트가 비활성이라 보스 등장 연출을 재생하지 못했다.", this);
                return;
            }

            if (group == null) group = GetComponent<CanvasGroup>();
            if (iconGlitch == null && warningIcon != null) iconGlitch = warningIcon.GetComponent<GlitchImageFx>();

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>재생 중인 연출을 즉시 끝낸다. 씬 전환·보스 즉사처럼 화면이 통째로 바뀔 때 호출한다.</summary>
        public void Dismiss()
        {
            // 커튼이 본 흐름과 나란히 도는 별도 코루틴이라 routine 하나만 멈추면 계속 띠를 밀어낸다.
            // 이 오브젝트에서 도는 코루틴은 전부 이 연출의 것이므로 통째로 멈춰도 안전하다.
            StopAllCoroutines();
            routine = null;

            if (group != null) group.alpha = 0f;
            gameObject.SetActive(false);
        }

        private IEnumerator PlayRoutine()
        {
            SetWarningActive(true);

            if (bossRoot != null)
            {
                // 지난 재생이 흔들리는 도중에 끊겼으면 어긋난 자리가 남아 있다. 매번 제자리에서 시작한다.
                bossRoot.anchoredPosition = bossHome;
                bossRoot.localScale = Vector3.one;
                bossRoot.gameObject.SetActive(false);
            }

            yield return Fade(0f, 1f, warningFadeIn);

            // 1. 빠르게 두어 번 깜박인 뒤 켜진 채로 머문다.
            //    같은 간격으로 계속 깜박이면 신호등처럼 읽힌다. 짧게 튄 뒤 멎어야
            //    "경고가 들어왔다"가 되고, 머무는 동안 긴장이 쌓인다.
            for (int i = 0; i < fastBlinkCount; i++)
            {
                SetIconVisible(true);
                yield return Wait(fastBlinkOnSeconds);

                SetIconVisible(false);
                yield return Wait(fastBlinkOffSeconds);
            }

            SetIconVisible(true);
            yield return Wait(iconHoldSeconds);

            // 2. 경고가 커튼처럼 위아래로 쓸려 나간다.
            //    본 흐름과 나란히 돌려서, 띠가 아직 빠져나가는 중에 BOSS가 박히게 한다.
            //    다 걷힌 뒤에 박으면 두 동작이 순서대로 재생되는 별개 연출로 보인다.
            StartCoroutine(SweepCurtain());
            yield return Wait(curtainDuration * slamTriggerRatio);

            // 3. BOSS가 박힌다
            yield return Wait(slamExtraDelay);
            yield return Slam();

            yield return Wait(holdSeconds);
            yield return Fade(1f, 0f, fadeOutSeconds);

            group.alpha = 0f;
            gameObject.SetActive(false);
            routine = null;
        }

        /// <summary>
        /// BOSS가 위에서 내려와 제자리에 꽂힌다. 하강 → 정지 → 흔들림 순서다.
        /// </summary>
        /// <remarks>
        /// 크기만 줄이면 "멀리서 다가온다"라 부드럽게 읽힌다. 도장은 <b>아래로</b> 내려와야 하므로
        /// 높이와 크기를 같은 곡선으로 함께 줄인다. 착지 뒤에는 반동 대신 <b>완전한 정지</b>를 두는데,
        /// 움직이던 것이 뚝 끊기는 그 정적이 충돌을 인지시킨다. 흔들림은 그 뒤에 오는 여파다.
        /// </remarks>
        private IEnumerator Slam()
        {
            if (bossRoot == null) yield break;

            bossRoot.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < slamDuration)
            {
                elapsed += Delta;
                float t = Mathf.Clamp01(elapsed / slamDuration);
                float e = slamCurve.Evaluate(t);

                bossRoot.localScale = Vector3.one * Mathf.LerpUnclamped(slamStartScale, 1f, e);
                bossRoot.anchoredPosition = bossHome + new Vector2(0f, Mathf.LerpUnclamped(slamDropDistance, 0f, e));
                yield return null;
            }

            // 정확히 제자리에서 멎는다. 반동을 주면 도장이 아니라 튕기는 물건으로 읽힌다.
            bossRoot.localScale = Vector3.one;
            bossRoot.anchoredPosition = bossHome;

            // 히트스톱. 여기서 아무것도 움직이지 않아야 앞의 속도가 충돌로 환산된다.
            yield return Wait(impactHoldSeconds);

            yield return ImpactShake();
        }

        /// <summary>찍힌 여파로 BOSS가 잠깐 흔들린다. 세로로 더 크게 흔들어 내려찍은 방향을 남긴다.</summary>
        private IEnumerator ImpactShake()
        {
            if (impactShakeStrength <= 0f || impactShakeDuration <= 0f) yield break;

            float elapsed = 0f;
            while (elapsed < impactShakeDuration)
            {
                elapsed += Delta;

                // 제곱으로 잦아들게 해 첫 순간이 가장 크고 빠르게 멎는다.
                float k = 1f - Mathf.Clamp01(elapsed / impactShakeDuration);
                float amp = impactShakeStrength * k * k;

                bossRoot.anchoredPosition = bossHome + new Vector2(
                    Random.Range(-amp, amp) * 0.45f,
                    Random.Range(-amp, amp));
                yield return null;
            }

            bossRoot.anchoredPosition = bossHome;
        }

        /// <summary>
        /// 경고 띠가 위아래로 갈라지며 화면 밖으로 쓸려 나가고, 그 사이 위험 표시는 줄어들어 사라진다.
        /// 다 빠져나가면 경고 묶음을 통째로 끈다.
        /// </summary>
        /// <remarks>
        /// 띠를 그냥 <c>SetActive(false)</c>로 지우면 흐르던 것이 한 프레임에 툭 없어져
        /// 다음 단계와 이어지지 않는다. 화면 밖으로 밀어내면 "걷혔다"가 되고,
        /// 열린 자리로 BOSS가 들어오는 인과가 생긴다.
        /// </remarks>
        private IEnumerator SweepCurtain()
        {
            Vector3 iconScale = warningIcon != null ? warningIcon.rectTransform.localScale : Vector3.one;

            float elapsed = 0f;
            while (elapsed < curtainDuration)
            {
                elapsed += Delta;
                float t = Mathf.Clamp01(elapsed / curtainDuration);
                float eased = curtainCurve.Evaluate(t);

                MoveBands(eased);

                // 아이콘은 커튼보다 빨리 사라진다. 띠만 나가고 가운데가 남으면 화면이 어수선하다.
                if (warningIcon != null)
                {
                    float shrink = 1f - Mathf.Clamp01(t / iconShrinkRatio);
                    warningIcon.rectTransform.localScale = iconScale * shrink;
                }

                yield return null;
            }

            MoveBands(1f);
            SetWarningActive(false);
        }

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

        private void SetWarningActive(bool active)
        {
            if (warningRoot != null) warningRoot.SetActive(active);
            if (!active) return;

            // 커튼이 옮겨 놓은 띠와 줄여 놓은 아이콘을 제자리로 돌린다.
            // 이걸 빠뜨리면 두 번째 재생부터 경고가 화면 밖에서 시작한다.
            MoveBands(0f);
            if (warningIcon != null) warningIcon.rectTransform.localScale = Vector3.one;

            SetIconVisible(true);
        }

        // Graphic.enabled가 아니라 오브젝트를 껐다 켠다. 실제 위험 표시는 테두리·느낌표처럼
        // 이미지 여러 장으로 조합되기 쉬운데, 컴포넌트만 끄면 자식이 그대로 남아 반쪽만 깜박인다.
        private void SetIconVisible(bool visible)
        {
            if (warningIcon == null) return;

            warningIcon.gameObject.SetActive(visible);

            // 켜지는 순간마다 글리치를 크게 튀긴다. 깜박임과 지지직이 같은 박자로 맞아야
            // 신호가 들어오면서 화면이 흔들리는 것처럼 읽힌다.
            if (visible && iconGlitch != null) iconGlitch.Pulse();
        }

        private float Delta => Time.unscaledDeltaTime;

        private IEnumerator Wait(float seconds)
        {
            float remain = seconds;
            while (remain > 0f)
            {
                remain -= Delta;
                yield return null;
            }
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                group.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Delta;
                group.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            group.alpha = to;
        }
    }
}
