using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 사이버펑크 버튼 연출의 구동부. 셰이더 <c>ProjectS/UI Cyber Button</c>(바탕)과
    /// <c>ProjectS/UI Cyber Button Text</c>(라벨)에 버튼마다 달라야 하는 값을 넣어 준다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>셰이더만 붙여도 돌아간다.</b> 두 셰이더는 스스로 버스트를 돌기 때문에 이 컴포넌트가 없어도
    /// 지지직거린다. 이 컴포넌트가 하는 일은 셰이더가 <b>스스로 알 수 없는 세 가지</b>다.
    /// <list type="number">
    /// <item><b>rect 크기</b>(<c>_RectBounds</c>) — UI 셰이더는 자기 rect 크기를 받지 못한다.
    ///       이 값이 없으면 9-Sliced 버튼에서 찢김과 테두리 글로우가 아홉 조각 경계마다 끊긴다.</item>
    /// <item><b>박자 분산</b>(<c>_Phase</c>) — 셰이더는 <c>_Time</c>을 공유하므로, 값이 같으면
    ///       화면의 모든 버튼이 한 순간에 같이 튄다. 버튼마다 다른 위상을 뽑아 흩어 놓는다.</item>
    /// <item><b>입력 반응</b>(<c>_Hover</c>·<c>_Glitch</c>) — 마우스를 올리거나 누른 순간의 반응.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>머티리얼은 반드시 인스턴스를 뜬다.</b> 셰이더 프로퍼티는 머티리얼 전역이라 원본 에셋을 직접
    /// 만지면 같은 머티리얼을 쓰는 다른 버튼까지 같은 rect·같은 박자로 물들고, 에디터에서는 에셋이
    /// 영구히 변한다(<see cref="GlitchImageFx"/>와 같은 방침).
    /// 대신 이 버튼은 캔버스 배칭에서 떨어져 나와 드로우콜을 하나 더 쓴다 — 화면을 가득 채우는
    /// 리스트 항목이 아니라 <b>몇 개 안 되는 주요 버튼</b>에 붙이는 것을 전제로 한 맞바꿈이다.
    /// </para>
    /// <para>
    /// <b>모양 값을 어디서 만지는지는 바탕과 라벨이 다르다</b>(2026-09-10 TH).
    /// <list type="bullet">
    /// <item><b>바탕</b> — .mat 에셋을 연다. 재생 전에도 편집할 수 있으니 컴포넌트로 끌어올릴 이유가 없다.</item>
    /// <item><b>라벨</b> — 이 컴포넌트의 "라벨 모양" 항목에서 만진다. 라벨 머티리얼은
    ///       <see cref="TMP_Text.fontMaterial"/>, 즉 재생 중에만 존재하는 인스턴스라
    ///       <b>재생 전에는 손댈 방법이 없고 재생 중에 맞춘 값은 정지하면 사라진다.</b>
    ///       그래서 값을 직렬화되는 필드로 끌어올렸다 — <see cref="GlitchTextFx"/>가 같은 이유로 택한 방식이다.</item>
    /// </list>
    /// </para>
    /// <para>시간은 unscaled로 센다. 일시정지(<c>timeScale = 0</c>) 중에도 메뉴 버튼은 반응해야 한다.</para>
    /// </remarks>
    [RequireComponent(typeof(Graphic))]
    public class CyberButtonFx : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private static readonly int GlitchID = Shader.PropertyToID("_Glitch");
        private static readonly int HoverID = Shader.PropertyToID("_Hover");
        private static readonly int PhaseID = Shader.PropertyToID("_Phase");
        private static readonly int RectBoundsID = Shader.PropertyToID("_RectBounds");

        private static readonly int GlitchIdleID = Shader.PropertyToID("_GlitchIdle");
        private static readonly int BurstStrengthID = Shader.PropertyToID("_BurstStrength");
        private static readonly int BurstIntervalID = Shader.PropertyToID("_BurstInterval");
        private static readonly int BurstTimeID = Shader.PropertyToID("_BurstTime");
        private static readonly int FlickerSpeedID = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int SliceHeightID = Shader.PropertyToID("_SliceHeight");
        private static readonly int SliceAmountID = Shader.PropertyToID("_SliceAmount");
        private static readonly int SliceOffsetID = Shader.PropertyToID("_SliceOffset");
        private static readonly int DropoutID = Shader.PropertyToID("_Dropout");
        private static readonly int ShakeID = Shader.PropertyToID("_Shake");
        private static readonly int SoftnessID = Shader.PropertyToID("_Softness");
        private static readonly int WobbleID = Shader.PropertyToID("_Wobble");
        private static readonly int WobbleSpeedID = Shader.PropertyToID("_WobbleSpeed");
        private static readonly int WobbleScaleID = Shader.PropertyToID("_WobbleScale");
        private static readonly int GlowID = Shader.PropertyToID("_Glow");
        private static readonly int GlowWidthID = Shader.PropertyToID("_GlowWidth");
        private static readonly int GlowColorID = Shader.PropertyToID("_GlowColor");
        private static readonly int HoverGlowID = Shader.PropertyToID("_HoverGlow");
        private static readonly int HoverGlitchID = Shader.PropertyToID("_HoverGlitch");

        [Header("대상")]
        [Tooltip("버튼 바탕. 비워두면 이 오브젝트의 Graphic을 쓴다. " +
                 "머티리얼에 ProjectS/UI Cyber Button 셰이더가 물려 있어야 한다.")]
        [SerializeField] private Graphic background;

        [Tooltip("버튼 라벨. 비워두면 자식에서 찾는다. 없으면 바탕만 연출한다.")]
        [SerializeField] private TMP_Text label;

        [Tooltip("라벨 머티리얼에 강제로 물릴 셰이더(ProjectS/UI Cyber Button Text). " +
                 "비워두면 라벨의 현재 머티리얼을 그대로 쓴다. TMP 머티리얼 프리셋을 따로 만들지 않고 " +
                 "쓰려면 여기에 넣는다 — 폰트 인스턴스에만 적용되므로 다른 텍스트에는 영향이 없다.")]
        [SerializeField] private Shader labelShader;

        [Header("반응")]
        [Tooltip("마우스를 올렸을 때 _Hover가 도달하는 값. 0이면 반응하지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float hoverAmount = 1f;

        [Tooltip("_Hover가 목표값까지 가는 데 걸리는 시간(초). 0이면 즉시 붙는다.")]
        [SerializeField, Min(0f)] private float hoverFade = 0.12f;

        [Tooltip("눌린 순간 터뜨릴 글리치 세기.")]
        [SerializeField, Range(0f, 1f)] private float pressGlitch = 1f;

        [Tooltip("눌린 글리치가 잦아드는 데 걸리는 시간(초).")]
        [SerializeField, Min(0f)] private float pressDecay = 0.22f;

        [Header("박자")]
        [Tooltip("버튼마다 다른 위상을 무작위로 뽑는다. 끄면 아래 고정값을 쓴다 — " +
                 "여러 버튼을 일부러 같은 박자로 튀게 하고 싶을 때만 끈다.")]
        [SerializeField] private bool randomPhase = true;

        [SerializeField, Range(0f, 30f)] private float fixedPhase = 0f;

        // ── 라벨 모양 ─────────────────────────────────────────────────────────
        // 여기 있는 값들은 원래 머티리얼에 있어야 할 것들이다. 그런데 라벨 머티리얼은
        // TMP_Text.fontMaterial — 재생 중에만 만들어지는 인스턴스라, 값을 머티리얼에 두면
        // 재생 전에는 손댈 방법이 없고 재생 중에 맞춘 값은 정지하면 사라진다.
        // 그래서 직렬화되는 필드로 끌어올렸다(GlitchTextFx가 2026-08-05에 같은 이유로 택한 방식).
        // 바탕 머티리얼은 편집 가능한 .mat 에셋이라 이렇게 하지 않는다 — 색·글로우는 .mat에서 만진다.
        [Header("라벨 모양 (인스펙터에서 조절)")]
        [Tooltip("아래 값들을 라벨 머티리얼에 밀어 넣을지. 끄면 라벨의 머티리얼 프리셋 값을 그대로 쓴다. " +
                 "TMP 머티리얼 프리셋을 제대로 만들어 둔 라벨에서만 끈다.")]
        [SerializeField] private bool applyLabelStyle = true;

        [Tooltip("글자 전체가 덜컥 흔들리는 폭(px). 0이면 제자리에서 찢기기만 한다. " +
                 "가장 먼저 낮춰 볼 값이다 — 여기가 크면 글자가 계속 떠는 것처럼 보인다.")]
        [SerializeField, Range(0f, 12f)] private float labelShake = 0.8f;

        [Tooltip("평상시 글리치 세기. 라벨은 읽혀야 하므로 낮게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float labelGlitchIdle = 0.08f;

        [Tooltip("이따금 크게 튈 때의 세기.")]
        [SerializeField, Range(0f, 1f)] private float labelBurstStrength = 0.75f;

        [Tooltip("버스트와 버스트 사이의 대기 시간(초).")]
        [SerializeField, Range(0f, 20f)] private float labelBurstInterval = 2.6f;

        [Tooltip("한 번 튀어 있는 시간(초).")]
        [SerializeField, Range(0.01f, 2f)] private float labelBurstTime = 0.14f;

        [Tooltip("초당 재배치 횟수. 높을수록 정신없다.")]
        [SerializeField, Min(0f)] private float labelFlickerSpeed = 18f;

        [Header("라벨 — 블록 찢김")]
        [Tooltip("블록 하나의 높이(px). 글자 높이의 1/4~1/6쯤.")]
        [SerializeField, Min(1f)] private float labelSliceHeight = 12f;

        [Tooltip("글리치 1일 때 밀려나는 블록의 비율.")]
        [SerializeField, Range(0f, 1f)] private float labelSliceAmount = 0.55f;

        [Tooltip("밀리는 최대 거리(px). 대부분의 블록은 이보다 훨씬 조금 밀린다. " +
                 "16을 넘기면 폰트 아틀라스에서 옆 칸의 다른 글자를 끌어온다.")]
        [SerializeField, Range(0f, 16f)] private float labelSliceOffset = 7f;

        [Tooltip("블록이 통째로 사라지는 비율.")]
        [SerializeField, Range(0f, 1f)] private float labelDropout = 0.12f;

        [Header("라벨 — 흔들림")]
        [Tooltip("굽이치는 가로 흔들림의 폭(px). 블록 찢김이 '뚝뚝 끊기는 어긋남'이라면 이쪽은 " +
                 "'연속적으로 굽이치는 흔들림'이다. 글리치가 0일 때도 절반쯤 남는다.")]
        [SerializeField, Range(0f, 12f)] private float labelWobble = 1.2f;

        [SerializeField, Min(0f)] private float labelWobbleSpeed = 3.4f;

        [Tooltip("세로로 몇 번 굽이치는가. 크면 잔물결, 작으면 글자 전체가 통째로 휘청인다.")]
        [SerializeField, Range(0.01f, 1f)] private float labelWobbleScale = 0.16f;

        [Tooltip("글자 가장자리 부드러움.")]
        [SerializeField, Range(0f, 1f)] private float labelSoftness = 0.12f;

        [Header("라벨 — 글로우")]
        [Tooltip("기본값이 흰색인 것은 의도다. 글자에 없던 색을 더하지 않고 번짐만 준다.")]
        [SerializeField, ColorUsage(true, true)]
        private Color labelGlowColor = Color.white;

        [SerializeField, Range(0f, 4f)] private float labelGlow = 0.5f;

        [Tooltip("번지는 폭(SDF 거리 단위). 폰트 아틀라스의 패딩을 넘으면 잘린다.")]
        [SerializeField, Range(0.01f, 0.4f)] private float labelGlowWidth = 0.16f;

        [Header("라벨 — 마우스 반응")]
        [SerializeField, Range(0f, 4f)] private float labelHoverGlow = 1.2f;

        [SerializeField, Range(0f, 1f)] private float labelHoverGlitch = 0.3f;

        private Selectable selectable;
        private RectTransform rect;

        private Material backgroundMaterial;
        private Material labelMaterial;

        private float hover;
        private float hoverTarget;
        private float glitch;

        private void Awake()
        {
            if (background == null) background = GetComponent<Graphic>();
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);

            selectable = GetComponent<Selectable>();
            rect = transform as RectTransform;

            backgroundMaterial = InstanceOf(background);
            labelMaterial = InstanceOfLabel(label);

            if (backgroundMaterial == null && labelMaterial == null)
            {
                Debug.LogWarning($"{name}: 사이버 버튼 셰이더가 물린 머티리얼이 없습니다. " +
                                 "바탕에는 'ProjectS/UI Cyber Button', 라벨에는 " +
                                 "'ProjectS/UI Cyber Button Text' 머티리얼을 물려 주세요.", this);
                enabled = false;
                return;
            }

            float phase = randomPhase ? Random.Range(0f, 30f) : fixedPhase;
            SetFloat(PhaseID, phase);

            ApplyLabelStyle();
            PushRectBounds();
            Apply();
        }

        /// <summary>
        /// 재생 중 인스펙터를 만지면 바로 반영된다. 재생 전에는 라벨 머티리얼 인스턴스가 아직
        /// 없으므로 아무 일도 하지 않는다 — 라벨 모양은 플레이 모드에서 맞춘다.
        /// </summary>
        /// <remarks>
        /// 플레이 모드에서 맞춘 값은 정지하면 되돌아간다(Unity 공통 동작). 마음에 드는 값을 찾았다면
        /// 정지하기 전에 컴포넌트 우클릭 → Copy Component, 정지 후 Paste Component Values로 남긴다.
        /// </remarks>
        private void OnValidate()
        {
            ApplyLabelStyle();
        }

        private void OnDestroy()
        {
            // 바탕 머티리얼은 Awake에서 우리가 뜬 것이라 우리가 치운다(누수 방지).
            // 라벨 쪽 fontMaterial은 TMP가 만들고 TMP가 치운다 — 여기서 Destroy하면
            // 텍스트가 자기 머티리얼을 잃고 분홍색으로 남는다.
            if (backgroundMaterial != null) Destroy(backgroundMaterial);
        }

        private void OnDisable()
        {
            // 꺼졌다 켜질 때 마우스가 떠난 것을 못 받고 hover가 켜진 채 굳는 것을 막는다.
            hover = 0f;
            hoverTarget = 0f;
            glitch = 0f;
            Apply();
        }

        /// <summary>
        /// rect 크기가 바뀌면(레이아웃 갱신·해상도 변경) 셰이더가 쓰는 값도 따라가야 한다.
        /// 빠지면 찢김 폭과 글로우 두께가 옛 크기 기준으로 남아 어긋난다.
        /// </summary>
        private void OnRectTransformDimensionsChange()
        {
            PushRectBounds();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            hover = hoverFade <= 0f
                ? hoverTarget
                : Mathf.MoveTowards(hover, hoverTarget, dt / hoverFade);

            if (glitch > 0f)
            {
                glitch = pressDecay <= 0f ? 0f : Mathf.MoveTowards(glitch, 0f, dt / pressDecay);
            }

            Apply();
        }

        /// <summary>
        /// 지금 즉시 한 번 크게 튀게 한다. 클릭이 아닌 다른 사건(잠금 해제 실패, 알림 등)에
        /// 맞춰 버튼을 흔들고 싶을 때 밖에서 호출한다.
        /// </summary>
        public void Pulse()
        {
            glitch = pressGlitch;
            Apply();
        }

        /// <inheritdoc/>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsInteractable()) return;
            hoverTarget = hoverAmount;
        }

        /// <inheritdoc/>
        public void OnPointerExit(PointerEventData eventData)
        {
            hoverTarget = 0f;
        }

        /// <inheritdoc/>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable()) return;
            Pulse();
        }

        /// <inheritdoc/>
        public void OnPointerUp(PointerEventData eventData)
        {
            // 눌린 채로 밖으로 끌고 나간 경우를 대비해 목표를 다시 잡는다.
            if (!IsInteractable()) hoverTarget = 0f;
        }

        private bool IsInteractable()
        {
            return selectable == null || selectable.IsInteractable();
        }

        // 상호작용이 막힌 버튼은 반응도 자발적인 글리치도 죽여, 눌리는 버튼과 눈에 띄게 구분한다.
        private void Apply()
        {
            bool on = IsInteractable();
            SetFloat(HoverID, on ? hover : 0f);
            SetFloat(GlitchID, on ? glitch : 0f);
        }

        private void PushRectBounds()
        {
            if (rect == null) return;

            Rect r = rect.rect;
            Vector4 bounds = new Vector4(r.xMin, r.yMin, r.width, r.height);

            if (backgroundMaterial != null && backgroundMaterial.HasProperty(RectBoundsID))
                backgroundMaterial.SetVector(RectBoundsID, bounds);
        }

        // 라벨 머티리얼에만 밀어 넣는다. 바탕은 편집 가능한 .mat 에셋이라 여기서 덮어쓰면
        // .mat에서 맞춰 둔 값이 재생하는 순간 사라진다.
        private void ApplyLabelStyle()
        {
            if (!applyLabelStyle || labelMaterial == null) return;

            SetLabelFloat(ShakeID, labelShake);
            SetLabelFloat(GlitchIdleID, labelGlitchIdle);
            SetLabelFloat(BurstStrengthID, labelBurstStrength);
            SetLabelFloat(BurstIntervalID, labelBurstInterval);
            SetLabelFloat(BurstTimeID, labelBurstTime);
            SetLabelFloat(FlickerSpeedID, labelFlickerSpeed);

            SetLabelFloat(SliceHeightID, labelSliceHeight);
            SetLabelFloat(SliceAmountID, labelSliceAmount);
            SetLabelFloat(SliceOffsetID, labelSliceOffset);
            SetLabelFloat(DropoutID, labelDropout);

            SetLabelFloat(WobbleID, labelWobble);
            SetLabelFloat(WobbleSpeedID, labelWobbleSpeed);
            SetLabelFloat(WobbleScaleID, labelWobbleScale);
            SetLabelFloat(SoftnessID, labelSoftness);

            SetLabelColor(GlowColorID, labelGlowColor);
            SetLabelFloat(GlowID, labelGlow);
            SetLabelFloat(GlowWidthID, labelGlowWidth);

            SetLabelFloat(HoverGlowID, labelHoverGlow);
            SetLabelFloat(HoverGlitchID, labelHoverGlitch);
        }

        private void SetLabelFloat(int id, float value)
        {
            if (labelMaterial.HasProperty(id)) labelMaterial.SetFloat(id, value);
        }

        private void SetLabelColor(int id, Color value)
        {
            if (labelMaterial.HasProperty(id)) labelMaterial.SetColor(id, value);
        }

        private void SetFloat(int id, float value)
        {
            if (backgroundMaterial != null && backgroundMaterial.HasProperty(id))
                backgroundMaterial.SetFloat(id, value);

            if (labelMaterial != null && labelMaterial.HasProperty(id))
                labelMaterial.SetFloat(id, value);
        }

        // 셰이더 프로퍼티가 없는 머티리얼은 인스턴스를 뜨지 않는다. 엉뚱한 머티리얼을 복제해
        // 배칭만 깨뜨리는 일을 막기 위함이다(반환이 null이면 그 대상은 연출에서 빠진다).
        private static Material InstanceOf(Graphic target)
        {
            if (target == null || target.material == null) return null;
            if (!target.material.HasProperty(GlitchID)) return null;

            Material instanced = new Material(target.material);
            target.material = instanced;
            return instanced;
        }

        private Material InstanceOfLabel(TMP_Text target)
        {
            if (target == null) return null;

            // fontMaterial은 접근하는 순간 TMP가 인스턴스를 떠 준다.
            // fontSharedMaterial을 만지면 같은 폰트를 쓰는 화면의 모든 글자가 함께 떨린다.
            Material instanced = target.fontMaterial;
            if (instanced == null) return null;

            if (labelShader != null && instanced.shader != labelShader)
                instanced.shader = labelShader;

            return instanced.HasProperty(GlitchID) ? instanced : null;
        }
    }
}
