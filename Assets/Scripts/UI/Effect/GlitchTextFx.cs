using System.Collections;
using System.Text;
using UnityEngine;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 사이버펑크풍 글리치 텍스트 연출. 켜지는 순간 글자를 알아볼 수 없게 부순 뒤,
    /// 1~2초에 걸쳐 지지직이 잦아들며 최종 문구가 드러난다.
    /// 드러나는 과정에서 영어 → 한자 → … 순으로 표기가 바뀌다 마지막에 한글로 확정된다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 연출은 두 축이 동시에 간다.
    /// <list type="number">
    /// <item><b>비주얼</b> — 셰이더 <c>ProjectS/UI Glitch Text</c>의 <c>_Glitch</c>를 1 → <see cref="settledGlitch"/>로
    ///       떨어뜨린다. 0으로 완전히 끄지 않고 0.1쯤 남기면 정지 화면이 아니라 "신호가 아직 불안정한" 인상이 남는다.</item>
    /// <item><b>글자</b> — 처음에는 아무 의미 없는 기호 난장(<see cref="scrambleCharset"/>)이고,
    ///       글리치가 잦아들수록 <see cref="cycleTexts"/>의 표기를 번갈아 보여주다 <see cref="finalText"/>로 멈춘다.</item>
    /// </list>
    /// </para>
    /// <para>
    /// 글자 뒤섞기를 셰이더가 아니라 코드에서 하는 이유: 셰이더는 이미 그려진 글리프를 일그러뜨릴 뿐
    /// "다른 글자로 바꾸는" 것은 못 한다. 알아볼 수 없는 혼란은 <b>모양 왜곡 + 문자 교체</b>가 겹쳐야 나온다.
    /// </para>
    /// <para>
    /// 머티리얼은 <see cref="TMP_Text.fontMaterial"/>(인스턴스)을 쓴다. fontSharedMaterial을 만지면
    /// 같은 폰트를 쓰는 화면의 모든 텍스트가 함께 글리치된다.
    /// </para>
    /// <para>
    /// 준비물: TMP 텍스트의 Font Material을 복제해 셰이더를 <c>ProjectS/UI Glitch Text</c>로 바꾼 뒤
    /// 그 머티리얼을 이 텍스트에 물린다. 셰이더가 없으면 경고만 남기고 글자 연출만 동작한다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(TMP_Text))]
    public class GlitchTextFx : MonoBehaviour
    {
        private const string ShaderName = "ProjectS/UI Glitch Text";
        private static readonly int GlitchID = Shader.PropertyToID("_Glitch");

        [Header("셰이더")]
        [Tooltip("ProjectS/UI Glitch Text 셰이더. 비워두면 이름으로 찾지만, 빌드에 포함되도록 직접 넣는 것을 권장한다.")]
        [SerializeField] private Shader glitchShader;

        [Header("문구")]
        [Tooltip("연출이 끝나고 남는 최종 문구. 보통 한글.")]
        [SerializeField] private string finalText = "사망";

        [Tooltip("최종 문구 직전에 번갈아 스쳐 지나갈 표기들. 위에서부터 순서대로 나타난다.")]
        [SerializeField] private string[] cycleTexts = { "DEAD", "死亡", "DEAD", "死亡" };

        [Tooltip("혼란 구간에 쓰일 글자 풀. 의미를 읽을 수 없는 기호가 섞일수록 어지러워 보인다.")]
        [SerializeField, TextArea]
        private string scrambleCharset = "アイウエオカキクケコサシスセソタチツテトナニヌネノ" +
                                         "死亡終焉滅絶断裂壊崩零虚無闇" +
                                         "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789" +
                                         "#$%&*+-/<=>?@[]^_{|}~";

        [Header("타이밍")]
        [Tooltip("글리치가 1에서 정착값까지 떨어지는 데 걸리는 시간(초). 기획 요청은 1~2초.")]
        [SerializeField, Min(0.05f)] private float settleDuration = 1.6f;

        [Tooltip("연출이 끝난 뒤 남길 글리치 값. 0이면 완전히 멈추고, 0.1쯤이면 미세하게 계속 떨린다.")]
        [SerializeField, Range(0f, 1f)] private float settledGlitch = 0.1f;

        [Tooltip("글리치 감쇠 곡선(0=시작, 1=끝). 뒤로 갈수록 급히 떨어지면 '탁 잡히는' 인상이 난다.")]
        [SerializeField] private AnimationCurve settleCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("혼란 구간에서 글자를 갈아치우는 간격(초). 짧을수록 정신없다.")]
        [SerializeField, Min(0.01f)] private float scrambleInterval = 0.04f;

        [Tooltip("전체 시간 중 혼란(기호 난장)이 차지하는 비율. 나머지 구간에서 표기가 번갈아 나타난다.")]
        [SerializeField, Range(0f, 1f)] private float chaosRatio = 0.55f;

        [Header("동작")]
        [Tooltip("켜질 때 자동으로 재생한다. 팝업처럼 SetActive로 등장하는 UI에 맞춘 기본값.")]
        [SerializeField] private bool playOnEnable = true;

        private TMP_Text label;
        private Material material;
        private Coroutine routine;

        /// <summary>현재 글리치 세기(0~1). 다른 연출과 맞추고 싶을 때 읽는다.</summary>
        public float Glitch { get; private set; }

        private void Awake()
        {
            label = GetComponent<TMP_Text>();

            // fontMaterial은 접근하는 순간 이 텍스트 전용 인스턴스가 만들어진다.
            // 그래서 여기서 셰이더를 바꿔도 같은 폰트를 쓰는 다른 텍스트는 영향받지 않는다
            // (fontSharedMaterial을 만지면 화면의 모든 텍스트가 함께 글리치된다).
            material = label.fontMaterial;
            if (material == null) return;

            ApplyGlitchShader();

            if (!material.HasProperty(GlitchID))
            {
                Debug.LogWarning(
                    $"{name}: _Glitch 속성이 없어 비주얼 글리치는 생략하고 글자 연출만 재생한다. " +
                    "glitchShader 슬롯에 'ProjectS/UI Glitch Text'를 넣으세요.", this);
                material = null;
            }
        }

        /// <remarks>
        /// 폰트 머티리얼을 손으로 복제해 셰이더를 바꾸는 과정을 없애기 위해 코드에서 갈아끼운다.
        /// TMP가 UGUI 패키지로 통합되면서 인스펙터의 'Create Material Preset'이 사라져,
        /// 서브에셋 머티리얼을 복제하는 절차가 번거로워졌기 때문이다.
        ///
        /// 폰트 아틀라스(_MainTex)와 색은 원래 머티리얼에 이미 들어 있고 셰이더만 교체하는 것이라
        /// 별도 배선이 필요 없다. 인스턴스에만 적용되므로 에셋은 그대로 남는다.
        /// </remarks>
        private void ApplyGlitchShader()
        {
            Shader target = glitchShader != null ? glitchShader : Shader.Find(ShaderName);

            if (target == null)
            {
                Debug.LogWarning($"{name}: '{ShaderName}' 셰이더를 찾지 못했다. " +
                                 "glitchShader 슬롯에 직접 넣어 주세요(빌드 포함 보장에도 필요).", this);
                return;
            }

            if (material.shader != target) material.shader = target;
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            // 코루틴은 오브젝트가 꺼지면 어차피 멈추지만, 다시 켰을 때 이전 진행 상태가 남지 않게
            // 참조를 비우고 최종 표시로 되돌려 둔다(꺼진 순간의 깨진 글자가 다음 등장에 한 프레임 보인다).
            routine = null;
            ApplyGlitch(settledGlitch);
            if (label != null) label.text = finalText;
        }

        /// <summary>연출을 처음부터 재생한다. 이미 재생 중이면 다시 시작한다.</summary>
        public void Play()
        {
            if (!isActiveAndEnabled) return;

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>연출을 건너뛰고 최종 상태로 즉시 확정한다.</summary>
        public void Skip()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            ApplyGlitch(settledGlitch);
            label.text = finalText;
        }

        private IEnumerator PlayRoutine()
        {
            // 사망 연출은 timeScale이 0이거나 낮아진 상태에서 돌 수 있어 unscaled로 센다.
            float elapsed = 0f;
            float nextScramble = 0f;

            ApplyGlitch(1f);
            label.text = BuildScramble(finalText.Length);

            while (elapsed < settleDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / settleDuration);

                // 곡선은 1 → 0을 그리고, 거기에 정착값을 섞어 최종적으로 settledGlitch에서 멈춘다.
                ApplyGlitch(Mathf.Lerp(settledGlitch, 1f, Mathf.Clamp01(settleCurve.Evaluate(t))));

                if (elapsed >= nextScramble)
                {
                    nextScramble = elapsed + scrambleInterval;
                    label.text = ResolveText(t);
                }

                yield return null;
            }

            ApplyGlitch(settledGlitch);
            label.text = finalText;
            routine = null;
        }

        // 진행도에 따라 무엇을 보여줄지 고른다.
        // 앞구간은 기호 난장, 뒷구간은 cycleTexts를 순서대로 훑고, 끝나면 finalText.
        private string ResolveText(float t)
        {
            if (t < chaosRatio) return BuildScramble(finalText.Length);

            if (cycleTexts == null || cycleTexts.Length == 0) return finalText;

            // 남은 구간을 표기 개수로 균등 분할한다. 뒤로 갈수록 같은 표기가 오래 머물러
            // "점점 잡혀 간다"는 인상이 나도록 진행도를 제곱해 앞쪽을 빠르게 지나가게 했다.
            float local = Mathf.Clamp01((t - chaosRatio) / Mathf.Max(0.0001f, 1f - chaosRatio));
            int index = Mathf.Clamp(Mathf.FloorToInt(local * local * cycleTexts.Length), 0, cycleTexts.Length - 1);

            string candidate = cycleTexts[index];
            return string.IsNullOrEmpty(candidate) ? finalText : candidate;
        }

        private string BuildScramble(int length)
        {
            if (string.IsNullOrEmpty(scrambleCharset)) return finalText;

            int count = Mathf.Max(1, length);
            StringBuilder sb = new StringBuilder(count);

            for (int i = 0; i < count; i++)
                sb.Append(scrambleCharset[Random.Range(0, scrambleCharset.Length)]);

            return sb.ToString();
        }

        private void ApplyGlitch(float value)
        {
            Glitch = Mathf.Clamp01(value);
            if (material != null) material.SetFloat(GlitchID, Glitch);
        }

        private void OnValidate()
        {
            // 인스펙터에서 만지는 동안에도 정착값이 곧바로 보이게 한다(연출 값 맞추기용).
            if (!Application.isPlaying && material != null) ApplyGlitch(settledGlitch);
        }
    }
}
