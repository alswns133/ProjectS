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
    /// <para>
    /// <b>모양을 정하는 셰이더 값은 머티리얼이 아니라 이 컴포넌트에서 조절한다</b>(2026-08-05 TH).
    /// 머티리얼이 재생 중에만 만들어지는 인스턴스라, 값을 머티리얼에 두면 <b>재생 전에는 손댈 방법이 없고
    /// 재생 중에 맞춘 값은 정지하면 사라졌다.</b> 그래서 값을 직렬화되는 필드로 끌어올리고,
    /// <see cref="previewInEditor"/>로 재생하지 않아도 결과를 보며 맞출 수 있게 했다.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public class GlitchTextFx : MonoBehaviour
    {
        private const string ShaderName = "ProjectS/UI Glitch Text";

        private static readonly int GlitchID = Shader.PropertyToID("_Glitch");
        private static readonly int CellSizeID = Shader.PropertyToID("_CellSize");
        private static readonly int ScatterID = Shader.PropertyToID("_Scatter");
        private static readonly int CellOffsetID = Shader.PropertyToID("_CellOffset");
        private static readonly int RgbSplitID = Shader.PropertyToID("_RgbSplit");
        private static readonly int FlickerSpeedID = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int ScanlineID = Shader.PropertyToID("_Scanline");
        private static readonly int SoftnessID = Shader.PropertyToID("_Softness");

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

        [Header("모양 (머티리얼 값)")]
        [Tooltip("파편 한 조각의 크기(px). 작을수록 잘게 부서진다.")]
        [SerializeField, Min(1f)] private float cellSize = 9f;

        [Tooltip("글리치가 1일 때 꺼지는 셀의 비율. 높을수록 파편이 드문드문해진다.")]
        [SerializeField, Range(0f, 1f)] private float scatter = 0.85f;

        [Tooltip("살아남은 셀이 어긋나는 정도. 폰트 아틀라스 UV 기준이라 0.01을 넘기면 옆 칸의 다른 글자를 끌어온다.")]
        [SerializeField, Range(0f, 0.01f)] private float cellOffset = 0.006f;

        [Tooltip("색수차. 위와 같은 이유로 아주 작은 값만 쓴다.")]
        [SerializeField, Range(0f, 0.01f)] private float rgbSplit = 0.0022f;

        [Tooltip("초당 파편 재배치 횟수. 높을수록 정신없다.")]
        [SerializeField, Min(0f)] private float flickerSpeed = 18f;

        [Tooltip("가로 주사선 농도.")]
        [SerializeField, Range(0f, 1f)] private float scanline = 0.25f;

        [Tooltip("글자 가장자리 부드러움.")]
        [SerializeField, Range(0f, 1f)] private float softness = 0.15f;

        [Header("동작")]
        [Tooltip("켜질 때 자동으로 재생한다. 팝업처럼 SetActive로 등장하는 UI에 맞춘 기본값.")]
        [SerializeField] private bool playOnEnable = true;

        [Header("에디터 미리보기")]
        [Tooltip("재생하지 않아도 씬/게임 뷰에 글리치를 적용해, 위 값을 눈으로 보며 맞출 수 있게 한다.\n" +
                 "끄면 예전처럼 재생 중에만 적용된다(텍스트는 원래 폰트 머티리얼로 그려진다).")]
        [SerializeField] private bool previewInEditor = true;

        [Tooltip("미리보기로 보여줄 글리치 세기. 연출의 어느 지점을 볼지 고르는 값일 뿐, 재생에는 영향이 없다.\n" +
                 "1=부서진 순간, 0=완전히 잡힌 글자.")]
        [SerializeField, Range(0f, 1f)] private float previewGlitch = 1f;

        private TMP_Text label;
        private Material material;
        private Coroutine routine;

        // 셰이더/머티리얼 경고는 한 번만 남긴다. 에디터 미리보기는 매 프레임 돌아서
        // 안 그러면 콘솔이 같은 경고로 뒤덮인다.
        private bool warned;

        /// <summary>현재 글리치 세기(0~1). 다른 연출과 맞추고 싶을 때 읽는다.</summary>
        public float Glitch { get; private set; }

        /// <remarks>
        /// 여기서는 참조만 잡고 머티리얼은 만들지 않는다. <b>같은 GameObject에 붙은 컴포넌트끼리는
        /// <c>Awake</c> 순서가 보장되지 않아</b>, TMP보다 먼저 깨어나면 아직 준비되지 않은 폰트 머티리얼을
        /// 집게 된다(그러면 글자 연출만 돌고 비주얼 글리치가 통째로 빠진다).
        /// 머티리얼은 모든 Awake가 끝난 뒤인 <see cref="Play"/> 시점에 만든다.
        /// </remarks>
        private void Awake()
        {
            label = GetComponent<TMP_Text>();
        }

        /// <summary>
        /// 글리치용 머티리얼 인스턴스를 준비한다. 여러 번 불러도 안전하다.
        /// </summary>
        /// <remarks>
        /// fontMaterial은 접근하는 순간 이 텍스트 전용 인스턴스가 만들어진다. 그래서 여기서 셰이더를
        /// 바꿔도 같은 폰트를 쓰는 다른 텍스트는 영향받지 않는다
        /// (fontSharedMaterial을 만지면 화면의 모든 텍스트가 함께 글리치된다).
        ///
        /// 에디터 미리보기로 만든 인스턴스에는 <see cref="HideFlags.DontSave"/>를 건다.
        /// 안 걸면 이 머티리얼이 <b>씬 파일 안에 통째로 저장되어</b> 텍스트를 만질 때마다 씬 diff가 생긴다
        /// (씬 충돌을 늘리지 않기 위한 처리다). 저장되지 않으므로 씬을 다시 열면 TMP가 폰트 기본
        /// 머티리얼로 되돌아가고, 이 컴포넌트가 곧바로 다시 인스턴스를 만든다.
        /// </remarks>
        /// <returns>글리치 값을 쓸 수 있는 머티리얼이 준비됐으면 true</returns>
        private bool EnsureMaterial()
        {
            if (material != null) return true;

            // 한 번 실패한 조건은 인스펙터를 다시 만지기 전까지 바뀌지 않는다. 매 프레임 재시도하면
            // 에디터에서 Shader.Find만 계속 돈다(OnValidate가 warned를 풀어 다시 시도하게 한다).
            if (warned) return false;

            if (label == null) label = GetComponent<TMP_Text>();
            if (label == null) return false;

            // 미리보기 인스턴스는 저장되지 않으므로(DontSave), 미리보기를 켠 채 저장한 씬은
            // 텍스트의 머티리얼 참조가 비어 있는 상태로 로드된다. TMP도 자기 Awake에서 폰트 기본값으로
            // 복구하지만 그 Awake가 우리보다 먼저 돈다는 보장이 없어, 비어 있으면 여기서 먼저 되살린다.
            // 이걸 빼면 null에서 인스턴스를 만들려다 실패해 글리치가 조용히 사라진다.
            if (label.fontSharedMaterial == null && label.font != null)
                label.fontSharedMaterial = label.font.material;

            Material instance = label.fontMaterial;
            if (instance == null) return false;

            material = instance;
            ApplyGlitchShader();

            if (!material.HasProperty(GlitchID))
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning(
                        $"{name}: _Glitch 속성이 없어 비주얼 글리치는 생략하고 글자 연출만 재생한다. " +
                        "glitchShader 슬롯에 'ProjectS/UI Glitch Text'를 넣으세요.", this);
                }

                material = null;
                return false;
            }

            if (!Application.isPlaying) material.hideFlags = HideFlags.DontSave;

            ApplyLook();
            return true;
        }

        /// <summary>인스펙터에서 조절한 모양 값을 머티리얼에 반영한다.</summary>
        private void ApplyLook()
        {
            if (material == null) return;

            material.SetFloat(CellSizeID, cellSize);
            material.SetFloat(ScatterID, scatter);
            material.SetFloat(CellOffsetID, cellOffset);
            material.SetFloat(RgbSplitID, rgbSplit);
            material.SetFloat(FlickerSpeedID, flickerSpeed);
            material.SetFloat(ScanlineID, scanline);
            material.SetFloat(SoftnessID, softness);
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
                // 에디터 미리보기는 매 프레임 재시도하므로, 경고를 막지 않으면 콘솔이 뒤덮인다.
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning($"{name}: '{ShaderName}' 셰이더를 찾지 못했다. " +
                                     "glitchShader 슬롯에 직접 넣어 주세요(빌드 포함 보장에도 필요).", this);
                }

                return;
            }

            if (material.shader != target) material.shader = target;
        }

        private void OnEnable()
        {
            // 에디터에서는 코루틴이 돌지 않는다. 미리보기는 Update가 맡는다.
            if (!Application.isPlaying) return;

            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            // 에디터에서는 아무것도 하지 않는다. 도메인 리로드 때도 이 콜백이 불리는데,
            // 여기서 label.text를 건드리면 디자이너가 씬에 적어 둔 문구가 finalText로 덮어써진다.
            if (!Application.isPlaying) return;

            // 코루틴은 오브젝트가 꺼지면 어차피 멈추지만, 다시 켰을 때 이전 진행 상태가 남지 않게
            // 참조를 비우고 최종 표시로 되돌려 둔다(꺼진 순간의 깨진 글자가 다음 등장에 한 프레임 보인다).
            routine = null;
            ApplyGlitch(settledGlitch);
            if (label != null) label.text = finalText;
        }

        /// <remarks>
        /// 에디터 전용 미리보기 루프. 인스펙터에서 값을 드래그하는 동안 결과가 바로 보이게 한다
        /// (에디터에서는 코루틴이 돌지 않아 연출 재생 대신 <see cref="previewGlitch"/> 한 지점을 고정해 보여준다).
        /// 머티리얼 준비를 OnValidate가 아니라 여기서 하는 이유: OnValidate는 에셋 임포트·씬 로드 중에도
        /// 불리는데 그 타이밍에 오브젝트를 만드는 것은 권장되지 않는다.
        /// </remarks>
        private void Update()
        {
            if (Application.isPlaying) return;

            if (!previewInEditor)
            {
                RestoreEditorPreview();
                return;
            }

            if (!EnsureMaterial()) return;

            ApplyLook();
            ApplyGlitch(previewGlitch);
        }

        /// <summary>
        /// 에디터 미리보기를 끄고 텍스트를 원래 폰트 머티리얼로 되돌린다.
        /// </summary>
        /// <remarks>
        /// 미리보기용 인스턴스는 저장되지 않으므로(DontSave), 켜 둔 채 씬을 저장하면 텍스트의 머티리얼
        /// 참조가 <b>빈 값으로 기록된다</b>. 그 씬을 재생하면 머티리얼이 없는 상태에서 시작해
        /// 글리치가 조용히 빠지므로, 저장 직전에 <c>GlitchTextFxPreviewGuard</c>(에디터)가 이 메서드를
        /// 불러 정상 참조를 써 넣는다. 저장이 끝나면 미리보기는 다음 <see cref="Update"/>에서 되살아난다.
        ///
        /// public인 이유는 그 에디터 훅이 다른 어셈블리(Assembly-CSharp-Editor)에 있어서다.
        /// </remarks>
        public void RestoreEditorPreview()
        {
            if (material == null) return;

            material = null;
            warned = false;

            if (label == null) label = GetComponent<TMP_Text>();
            if (label != null && label.font != null) label.fontSharedMaterial = label.font.material;
        }

        /// <summary>연출을 처음부터 재생한다. 이미 재생 중이면 다시 시작한다.</summary>
        /// <remarks>에디터(비재생)에서는 코루틴이 돌지 않으므로 미리보기 값만 반영하고 끝낸다.</remarks>
        public void Play()
        {
            if (!isActiveAndEnabled) return;

            if (!Application.isPlaying)
            {
                if (EnsureMaterial()) ApplyGlitch(previewGlitch);
                return;
            }

            // 머티리얼은 여기서 확보한다. OnEnable은 그 오브젝트의 모든 Awake가 끝난 뒤에 불리므로
            // TMP가 먼저 준비를 마쳤음이 보장된다(Awake에서 잡으면 순서에 따라 실패한다).
            EnsureMaterial();

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>연출을 건너뛰고 최종 상태로 즉시 확정한다.</summary>
        public void Skip()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            ApplyGlitch(settledGlitch);
            if (label != null) label.text = finalText;
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

        /// <remarks>
        /// 이미 머티리얼이 준비된 상태에서만 값을 밀어 넣는다. 재생 중에 인스펙터를 만져도 곧바로 반영되게
        /// 하기 위한 것이고, 에디터에서 머티리얼을 처음 만드는 일은 <see cref="Update"/>가 맡는다.
        /// </remarks>
        private void OnValidate()
        {
            // 인스펙터를 만졌다는 것은 조건이 바뀌었을 수 있다는 뜻이다(셰이더를 지금 막 넣었다든가).
            // 경고 억제를 풀어 다음 프레임에 다시 시도하게 한다.
            warned = false;

            if (material == null) return;

            ApplyLook();
            if (!Application.isPlaying) ApplyGlitch(previewGlitch);
        }
    }
}
