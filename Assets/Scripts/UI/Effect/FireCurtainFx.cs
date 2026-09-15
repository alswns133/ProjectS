using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 등장 폭발이 화면을 삼켰다가, 계속 타며 머물다가, 중심부터 타서 걷히는 풀스크린 가림막
    /// (<c>ProjectS/UI Fire Curtain</c> 셰이더 구동). 걷히고 나면 포즈를 잡은 보스가 드러난다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 단색 Image + 디졸브가 아닌가.</b> 붉은 색면을 깔았다 지우면 "커튼이 걷혔다"로 읽혀
    /// 앞의 폭발과 인과가 끊긴다. 기획이 요구한 "폭발이 유지되는 듯한 가림"은 가림막 자체가
    /// <b>계속 타고 있는 불</b>이어야 성립한다 — 그래서 셰이더가 매 프레임 대류·명멸한다.
    /// </para>
    /// <para>
    /// <b>모양도 진행도 이 컴포넌트에서 조정한다.</b> 불의 색·결·명암·경계 같은 셰이더 값은 <see cref="look"/>에
    /// 모아 두고, 머티리얼 에셋은 셰이더와 UI 공통 값(스텐실 등)을 실어 나르는 틀로만 쓴다. 연출을 만지다가
    /// 머티리얼 에셋과 컴포넌트를 오가지 않게 하기 위함이다. 값은 에셋을 복사한 <b>인스턴스</b>에만 넣는다 —
    /// 에셋 자체에 쓰면 재생할 때마다 .mat 파일이 바뀌어 커밋에 섞이고, 다음 판이 탄 상태로 시작한다.
    /// 에디터에서는 매 프레임 인스턴스에 다시 넣으므로 재생 중에도 슬라이더를 움직이면 곧바로 화면에 반영된다.
    /// </para>
    /// <para>
    /// <b>에디터 미리보기</b>(<see cref="previewInEditor"/>). 타임라인을 돌리지 않아도 가림막을 원하는 진행 상태로
    /// 띄워 두고 이글거리는 채로 값을 조정할 수 있다. 재생(Play 모드)에서는 무시되므로 켜 둔 채 저장해도 안전하다.
    /// 켜 두면 타임라인 미리보기보다 우선한다 — 타임라인 창을 열어 두기만 해도 재생 헤드가 클립 안이면 값이 계속 들어와
    /// 미리보기가 먹지 않았기 때문이다. 타임라인으로 타이밍을 볼 때는 미리보기를 끈다.
    /// </para>
    /// <para>
    /// <b>Graphic.material에 직접 꽂지 않는다.</b> 그 필드는 씬에 직렬화되므로 에디터에서 인스턴스를 꽂으면
    /// 씬이 수정됨으로 표시되고, 저장하면 사라지는 머티리얼을 가리키게 된다. 대신 <see cref="IMaterialModifier"/>로
    /// 그릴 때만 인스턴스를 끼워 넣는다(UGUI의 Mask와 같은 방식).
    /// </para>
    /// <para>
    /// <b>중심은 화면 정중앙이 아니라 폭발이 터진 자리다.</b> 덮이기 시작하는 순간엔 실제 폭발 파티클이 아직
    /// 화면에 있으므로, 같은 자리에서 같은 색이 번지면 화면 캡처 없이도 "그 폭발이 화면을 삼켰다"로 이어져 보인다.
    /// 중심 좌표는 덮는 순간에 한 번만 떠서 고정한다 — 가려진 동안 카메라가 움직여도 불의 중심이 미끄러지지 않게.
    /// </para>
    /// <para>
    /// 시간은 unscaled로 센다. 보스 등장은 히트스톱·슬로우모션(<c>SlowMotionController</c>)과 겹치기 쉬운데,
    /// 그때 불까지 함께 느려지면 안 된다. 셰이더의 <c>_FxTime</c>도 같은 이유로 여기서 채운다.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class FireCurtainFx : MonoBehaviour, IMaterialModifier
    {
        [Header("대상")]
        [Tooltip("화면을 가득 채우는 Graphic(Image, 스프라이트 비워도 됨). 비우면 이 오브젝트에서 찾는다.")]
        [SerializeField] private Graphic target;

        [Tooltip("셰이더와 UI 공통 값(스텐실 등)을 실어 나르는 머티리얼 에셋. 불 모양 값은 에셋이 아니라 아래 '불 모양'에서 조정한다. " +
                 "실행 중에는 이 에셋을 복사한 인스턴스를 쓰므로 에셋 파일이 더러워지지 않는다.")]
        [SerializeField] private Material curtainMaterial;

        [Tooltip("머티리얼 에셋이 비었을 때만 쓰는 대체 셰이더. 이때도 불 모양 값은 아래 '불 모양'을 따른다.")]
        [SerializeField] private Shader curtainShader;

        [Header("불 모양 (셰이더 값)")]
        [Tooltip("UI Fire Curtain 셰이더의 모양 값 전부. 머티리얼 에셋 값보다 우선한다. " +
                 "덮임·걷힘·중심·시간처럼 코드가 매 프레임 채우는 값과 스텐실(마스크가 채움)은 여기 없다.")]
        [SerializeField] private CurtainLook look = new();

        // 머티리얼 에셋에 조정해 두었던 값을 컴포넌트로 한 번 옮겼는지. 이 필드가 생기기 전의 씬은 false로 읽히므로,
        // 처음 한 번 에셋 값을 가져와야 필드 기본값(셰이더 기본값)이 기존에 튜닝한 불 모양을 덮어쓰지 않는다.
        [SerializeField, HideInInspector] private bool lookImported;

        [Tooltip("폭발의 월드 좌표를 화면 좌표로 옮길 카메라. 비우면 Camera.main을 쓴다.")]
        [SerializeField] private Camera worldCamera;

        [Tooltip("폭발 지점. Signal로 PlayCover()를 부를 때와 에디터 미리보기에서 쓴다. 비우면 화면 중앙. " +
                 "Timeline 클립은 자기 Explosion Center를 쓴다.")]
        [SerializeField] private Transform centerTarget;

        [Header("덮임")]
        [Tooltip("폭발이 퍼져 화면을 다 덮는 데 걸리는 시간(초). 길면 번지는 것으로 보인다 — 폭발은 짧아야 한다.")]
        [SerializeField, Min(0.02f)] private float coverDuration = 0.28f;

        [Tooltip("덮이는 진행 곡선. 앞이 가파를수록 터져 나온 것처럼 읽힌다.")]
        [SerializeField]
        private AnimationCurve coverCurve = new(
            new Keyframe(0f, 0f, 3.4f, 3.4f), new Keyframe(1f, 1f, 0.2f, 0.2f));

        [Header("걷힘")]
        [Tooltip("다 타서 걷히는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.05f)] private float burnDuration = 0.85f;

        [Tooltip("타들어가는 진행 곡선. 값은 0에서 시작해 1로 끝나야 한다 " +
                 "— 시작값이 0이 아니면 첫 프레임부터 타 있는 상태라 과정 없이 뚫린다.")]
        [SerializeField]
        private AnimationCurve burnCurve = new(
            new Keyframe(0f, 0f, 0.8f, 0.8f), new Keyframe(1f, 1f, 2.2f, 2.2f));

        [Tooltip("켜면 걷힘(원형으로 뚫림)이 폭발 자리가 아니라 커튼 정중앙에서 시작한다. 덮임은 계속 폭발 자리를 따라간다. " +
                 "끄면 걷힘도 덮임과 같은 폭발 자리에서 뚫린다. Burn(월드 좌표)로 중심을 직접 넘기면 그 값이 이긴다.")]
        [SerializeField] private bool burnFromCurtainCenter = true;

        [Header("경계 여유")]
        [Tooltip("진행값을 1보다 얼마나 더 밀지. 경계가 노이즈로 일렁이므로 정확히 1에서 멈추면 " +
                 "모서리에 불이 남거나 덜 걷힌 자국이 남는다.")]
        [SerializeField, Range(0f, 0.6f)] private float overshoot = 0.3f;

        [Header("불똥")]
        [Tooltip("타는 경계에서 떠오를 잉걸불. 비우면 불똥 없이 걷히기만 한다. " +
                 "전기 스파크(SparkBurstFx)가 아니라 부력으로 하늘거리며 오르는 쪽이다.")]
        [SerializeField] private EmberDriftFx embers;

        [Tooltip("걷히는 동안 초당 띄울 불똥 수. 경계가 길어지는 후반에 더 많이 나도록 반지름에 비례시킨다.")]
        [SerializeField, Min(0f)] private float embersPerSecond = 70f;

        [Tooltip("불똥이 경계선에서 흩어지는 폭(px). 0이면 정확히 원 위에 줄지어 티가 난다.")]
        [SerializeField, Min(0f)] private float emberScatter = 30f;

        [Header("에디터 미리보기 (재생 중에는 무시)")]
        [Tooltip("켜면 타임라인을 돌리지 않아도 아래 진행 상태로 가림막을 띄워 둔다. Game 뷰에서 확인한다.")]
        [SerializeField] private bool previewInEditor;

        [Tooltip("미리보기 덮임. 1.3이면 화면을 완전히 덮은 유지 구간이다.")]
        [SerializeField, Range(0f, 1.3f)] private float previewCover = 1.3f;

        [Tooltip("미리보기 걷힘. 0.3~0.8 사이에서 불씨 띠·그을음 테두리를 확인한다.")]
        [SerializeField, Range(0f, 1.3f)] private float previewBurn;

        private RectTransform self;
        private Material material;
        private Coroutine routine;

        // 지금 화면에 넣은 진행값. 에디터가 에셋 값을 인스턴스로 다시 복사할 때 덮여 사라지므로 따로 들고 있다.
        private float currentCover;
        private float currentBurn;

        // 바깥(Timeline)이 진행값을 쥐고 있는지. 에디터 미리보기가 꺼져 있을 때 타임라인 값을 유지하는 데 쓴다.
        // SetProgress에서 켜고 Clear에서 끈다(클립 범위를 벗어나거나 미리보기가 끝나면 Clear가 불린다).
        private bool drivenExternally;

        // 덮는 순간에 뜬 화면 좌표. 카메라가 움직여도 불의 중심이 미끄러지지 않게 고정해 둔다.
        private Vector2 center = new(0.5f, 0.5f);

        // 불똥 방출의 소수점 나머지. 프레임마다 버리면 초당 개수가 프레임레이트에 끌려 들쭉날쭉해진다.
        private float emberCarry;

        // 지난 프레임의 걷힘 진행. Timeline이 되감을 때 불똥을 내지 않으려고 방향을 본다.
        private float lastBurn;

        // Burn(월드 좌표)로 호출부가 직접 넘긴 걷힘 중심. 있으면 burnFromCurtainCenter보다 우선하고, Clear에서 비운다.
        private Vector2? burnCenterOverride;

        // 이번 프레임의 렉트 크기·최대 반지름. UpdateGeometry가 채우고 불똥 방출이 그대로 쓴다.
        // 덮임과 걷힘은 중심이 다를 수 있어 반지름 정규화 값도 따로 든다.
        private Vector2 rectSize = new(1920f, 1080f);
        private float maxRadius = 1100f;
        private Vector2 burnCenter = new(0.5f, 0.5f);
        private float burnMaxRadius = 1100f;

        // 참조가 빠졌다는 경고를 한 번만 낸다. 에디터 미리보기는 매 틱 Prepare를 부르므로 그대로 두면 콘솔이 넘친다.
        private bool warnedMissing;

        // 폭발 중심이 화면 밖이었다는 경고를 한 번만 낸다(ToViewport 참고).
        private bool warnedOffscreen;

        private static readonly int CoverID = Shader.PropertyToID("_Cover");
        private static readonly int BurnID = Shader.PropertyToID("_Burn");
        private static readonly int CenterID = Shader.PropertyToID("_Center");
        private static readonly int RectSizeID = Shader.PropertyToID("_RectSize");
        private static readonly int MaxRadiusID = Shader.PropertyToID("_MaxRadius");
        private static readonly int BurnCenterID = Shader.PropertyToID("_BurnCenter");
        private static readonly int BurnMaxRadiusID = Shader.PropertyToID("_BurnMaxRadius");
        private static readonly int FxTimeID = Shader.PropertyToID("_FxTime");

        private static readonly int TintID = Shader.PropertyToID("_Color");
        private static readonly int BaseStrengthID = Shader.PropertyToID("_BaseStrength");
        private static readonly int SmokeColorID = Shader.PropertyToID("_SmokeColor");
        private static readonly int FireColorID = Shader.PropertyToID("_FireColor");
        private static readonly int HotColorID = Shader.PropertyToID("_HotColor");
        private static readonly int WhiteHotID = Shader.PropertyToID("_WhiteHot");
        private static readonly int SootColorID = Shader.PropertyToID("_SootColor");
        private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
        private static readonly int RidgeID = Shader.PropertyToID("_Ridge");
        private static readonly int BoilID = Shader.PropertyToID("_Boil");
        private static readonly int RiseSpeedID = Shader.PropertyToID("_RiseSpeed");
        private static readonly int RadialStretchID = Shader.PropertyToID("_RadialStretch");
        private static readonly int SwirlSpeedID = Shader.PropertyToID("_SwirlSpeed");
        private static readonly int ContrastID = Shader.PropertyToID("_Contrast");
        private static readonly int GammaID = Shader.PropertyToID("_Gamma");
        private static readonly int CoreBoostID = Shader.PropertyToID("_CoreBoost");
        private static readonly int FlickerID = Shader.PropertyToID("_Flicker");
        private static readonly int EdgeNoiseID = Shader.PropertyToID("_EdgeNoise");
        private static readonly int EdgeTongueID = Shader.PropertyToID("_EdgeTongue");
        private static readonly int EdgeSoftID = Shader.PropertyToID("_EdgeSoft");
        private static readonly int EmberWidthID = Shader.PropertyToID("_EmberWidth");
        private static readonly int SootWidthID = Shader.PropertyToID("_SootWidth");
        private static readonly int BurnSeedID = Shader.PropertyToID("_BurnSeed");

        /// <summary>
        /// UI Fire Curtain 셰이더의 모양 값 묶음. 필드 기본값은 셰이더 기본값과 같다.
        /// </summary>
        /// <remarks>
        /// 셰이더 Properties와 1:1이다. 셰이더에 모양 값을 추가하면 여기 필드, 위 ID, <see cref="ApplyLook"/>,
        /// <see cref="ImportLookFromMaterial"/> 네 곳을 함께 늘린다 — 빠지면 그 값만 머티리얼 에셋 값에 묶여 조정되지 않는다.
        /// </remarks>
        [Serializable]
        private class CurtainLook
        {
            [Header("바탕")]
            [Tooltip("전체 색조. 불 색 전체에 곱해진다.")]
            public Color tint = Color.white;

            [Tooltip("캡처한 폭발 프레임 등을 불 아래에 깔고 싶을 때만 올린다. " +
                     "Image가 _MainTex를 제 스프라이트(흰색)로 덮어쓰므로, 0이 아니면 화면이 하얗게 뜬다.")]
            [Range(0f, 1f)] public float baseStrength;

            [Header("색 램프 (차가운 쪽 → 뜨거운 쪽)")]
            [Tooltip("불꽃 사이의 어두운 연기.")]
            public Color smokeColor = new(0.09f, 0.045f, 0.035f, 1f);

            [Tooltip("중간 불. 폭발 파티클 색과 맞춘다.")]
            [ColorUsage(true, true)] public Color fireColor = new(1f, 0.24f, 0.05f, 1f);

            [Tooltip("뜨거운 호박색. HDR이라 Bloom이 집는다.")]
            [ColorUsage(true, true)] public Color hotColor = new(1f, 0.66f, 0.18f, 1f);

            [Tooltip("가장 뜨거운 심지. 아주 좁게 나와야 한다.")]
            [ColorUsage(true, true)] public Color whiteHot = new(1f, 0.95f, 0.82f, 1f);

            [Tooltip("타고 남은 그을음 테두리.")]
            public Color sootColor = new(0.05f, 0.03f, 0.03f, 1f);

            [Header("이글거림")]
            [Tooltip("불꽃 덩어리 크기. 클수록 잘다.")]
            public float noiseScale = 3.2f;

            [Tooltip("0이면 뭉게구름, 1이면 가늘게 뻗는 불꽃 혀.")]
            [Range(0f, 1f)] public float ridge = 0.75f;

            [Tooltip("층이 서로 말려 끓는 정도. 이글거림의 핵심.")]
            [Range(0f, 2f)] public float boil = 0.9f;

            [Tooltip("열기가 위로 오르는 기준 속도(옥타브마다 배수가 다르다).")]
            public float riseSpeed = 0.55f;

            [Tooltip("폭발이 밀어낸 방향으로 불꽃이 늘어나는 정도.")]
            [Range(0f, 3f)] public float radialStretch = 1.2f;

            [Tooltip("중심을 감고 도는 속도.")]
            public float swirlSpeed = 0.16f;

            [Header("명암")]
            [Tooltip("낮으면 균일한 색면(=밍밍), 높으면 불꽃과 연기가 갈린다.")]
            [Range(1f, 6f)] public float contrast = 3f;

            [Tooltip("크면 뜨거운 부분이 좁아져 심지가 또렷해진다.")]
            [Range(0.4f, 3f)] public float gamma = 1.35f;

            [Tooltip("중심이 더 뜨겁게 타는 정도.")]
            [Range(0f, 2f)] public float coreBoost = 0.7f;

            [Tooltip("전체 밝기 명멸.")]
            [Range(0f, 1f)] public float flicker = 0.22f;

            [Header("경계")]
            [Tooltip("경계가 일렁이는 폭. 0이면 원형 아이리스로 보인다.")]
            [Range(0f, 1f)] public float edgeNoise = 0.3f;

            [Tooltip("경계를 핥는 불꽃 혀의 잘기.")]
            public float edgeTongue = 5.5f;

            [Tooltip("경계 번짐. 크면 부드럽고 작으면 날카롭다.")]
            [Range(0.001f, 0.4f)] public float edgeSoftness = 0.045f;

            [Tooltip("타는 자리에 남는 불씨 띠 두께.")]
            [Range(0f, 0.4f)] public float emberWidth = 0.1f;

            [Tooltip("불씨 바깥의 그을음 띠 두께.")]
            [Range(0f, 0.4f)] public float sootWidth = 0.07f;

            [Tooltip("걷힘 경계를 덮임과 어긋나게 하는 오프셋.")]
            public float burnSeed = 17.3f;
        }

        /// <summary>지금 화면이 가려져 있거나 가려지는 중인지.</summary>
        public bool IsCovering => routine != null || currentCover > 0.001f;

        private void Awake()
        {
            self = (RectTransform)transform;
            if (target == null) target = GetComponent<Graphic>();

            // 가림막은 입력을 받지 않는다. 켜 두면 화면을 덮는 동안 아래 HUD 클릭을 삼킨다.
            if (target != null && target.raycastTarget) target.raycastTarget = false;

            Prepare();
            Clear();
        }

        private void OnEnable()
        {
            if (target != null) target.SetMaterialDirty();
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsCovering이 영영 true가 된다.
            routine = null;

            // 인스턴스를 끼워 넣던 것을 멈춘다. 다시 그리게 해야 Image가 원래 머티리얼로 돌아간다.
            if (target != null) target.SetMaterialDirty();
        }

        private void OnDestroy()
        {
            if (material == null) return;

            // 에디터(재생 아님)에서는 Destroy가 허용되지 않는다.
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                UpdateEditorPreview();
#endif
                return;
            }

            if (material == null) return;

#if UNITY_EDITOR
            // 재생 중에 머티리얼 에셋을 만져도 바로 보이게 한다. 빌드에는 들어가지 않는다.
            SyncFromTemplate();
#endif

            // 셰이더 내장 _Time은 timeScale을 타므로 히트스톱에 불까지 느려진다. 실제 시간을 직접 넣는다.
            material.SetFloat(FxTimeID, Time.unscaledTime);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Graphic이 그릴 머티리얼을 정할 때 부른다. 여기서 인스턴스를 돌려주므로 <c>Graphic.material</c>
        /// 필드(씬에 저장됨)는 건드리지 않는다.
        /// </remarks>
        public Material GetModifiedMaterial(Material baseMaterial)
        {
            return isActiveAndEnabled && material != null ? material : baseMaterial;
        }

        /// <summary>
        /// 폭발이 화면을 삼킨다. 끝나면 화면이 완전히 가려진 채 계속 탄다 —
        /// 걷어내려면 <see cref="Burn()"/>을 부른다.
        /// </summary>
        /// <param name="worldCenter">폭발이 터진 월드 좌표. 여기의 화면 위치에서 불이 자라난다.</param>
        public IEnumerator Cover(Vector3 worldCenter)
        {
            SetCenter(ToViewport(worldCenter));
            yield return CoverInternal();
        }

        /// <summary>화면 정중앙에서 덮는다. 폭발 위치를 특정할 수 없을 때만 쓴다.</summary>
        public IEnumerator Cover()
        {
            SetCenter(new Vector2(0.5f, 0.5f));
            yield return CoverInternal();
        }

        /// <summary>
        /// 가려진 화면이 타며 걷힌다. 시작점은 <see cref="burnFromCurtainCenter"/>를 따른다
        /// (켜져 있으면 커튼 정중앙, 꺼져 있으면 덮을 때 쓴 폭발 자리).
        /// </summary>
        public IEnumerator Burn()
        {
            yield return BurnInternal();
        }

        /// <summary>
        /// 가려진 화면이 <paramref name="worldCenter"/>의 화면 위치부터 타며 걷힌다.
        /// 보스 위치를 넘기면 "보스가 불을 밀어내고 드러난다"로 읽힌다. 걷힘 경계만 옮기고 불의 결은 그대로 둔다.
        /// </summary>
        /// <param name="worldCenter">걷힘이 시작될 월드 좌표.</param>
        public IEnumerator Burn(Vector3 worldCenter)
        {
            burnCenterOverride = ToViewport(worldCenter);
            UpdateGeometry();
            yield return BurnInternal();
        }

        /// <summary>
        /// 덮임 → 유지 → 걷힘을 한 번에 굴린다. 유지 구간에 보스가 자리를 잡고 카메라가 붙는다.
        /// </summary>
        /// <param name="worldCenter">폭발이 터진 월드 좌표.</param>
        /// <param name="holdSeconds">완전히 가려진 채로 머무는 시간(초).</param>
        public IEnumerator Play(Vector3 worldCenter, float holdSeconds)
        {
            yield return Cover(worldCenter);
            yield return Wait(holdSeconds);
            yield return Burn();
        }

        /// <summary>
        /// 진행 중인 연출을 즉시 끝내고 화면을 되돌린다.
        /// 씬 전환·보스 즉사처럼 화면이 통째로 바뀔 때, 그리고 Timeline 클립이 범위를 벗어날 때 호출된다.
        /// </summary>
        public void Clear()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            emberCarry = 0f;
            lastBurn = 0f;
            drivenExternally = false;
            burnCenterOverride = null;   // 이번 판에 넘겨받은 걷힘 중심이 다음 판까지 남지 않게 한다

            // 떠 있던 불똥까지 치운다. 남겨 두면 가림막만 사라지고 불똥이 허공에 떠 있다.
            if (embers != null) embers.Clear();

            if (!Prepare()) return;

            ApplyProgress(0f, 0f);
        }

        // ── Timeline·Signal 연동 ────────────────────────────────────────────

        /// <summary>
        /// 진행값을 <b>바깥에서 직접</b> 지정한다. Timeline 클립(<c>FireCurtainClip</c>)이 매 프레임 부른다.
        /// </summary>
        /// <param name="cover">덮임 진행. 0이면 투명, 1.3이면 화면을 완전히 덮는다.</param>
        /// <param name="burn">걷힘 진행. 0이면 그대로, 1.3이면 다 타서 사라진다.</param>
        /// <remarks>
        /// <para>
        /// 코루틴(<see cref="Cover(Vector3)"/>·<see cref="Burn()"/>)과 <b>둘 중 하나만</b> 쓴다.
        /// 코루틴은 제 시간으로 값을 밀고 이쪽은 호출부가 값을 쥐므로, 같이 굴리면 매 프레임 서로 덮어쓴다.
        /// Timeline이 연출을 소유하면 이 메서드만 쓴다 — 그래야 타임라인을 스크럽할 때 화면이 따라온다.
        /// </para>
        /// <para>
        /// 불똥도 여기서 낸다. 걷히는 중이면 경계 원 둘레에 계속 띄우고, 되감겨 <paramref name="burn"/>이
        /// 줄어들면 내지 않는다(되감을 때도 불똥이 나면 시간이 거꾸로 가는데 불만 늘어난다).
        /// </para>
        /// </remarks>
        public void SetProgress(float cover, float burn)
        {
            if (!Prepare()) return;

            // 에디터에서 미리보기를 켜 두었으면 사람이 고른 값이 이긴다. 타임라인 창을 열어 두기만 해도
            // 재생 헤드가 클립 안(기본 시작 0초)에 있으면 매 틱 값이 들어와, 미리보기 값이 먹지 않는 것처럼 보였다.
            if (!Application.isPlaying && previewInEditor) return;

            drivenExternally = true;
            UpdateGeometry();
            ApplyProgress(cover, burn);

            // 에디터에서 타임라인을 스크럽할 때는 재생 시간이 흐르지 않는다. 실제 시각으로 불을 굴린다.
            if (!Application.isPlaying) material.SetFloat(FxTimeID, Time.realtimeSinceStartup);

            if (burn > lastBurn + 0.00001f) EmitEmbers(burn, Time.unscaledDeltaTime);
            lastBurn = burn;
        }

        /// <summary>불이 자라날 중심을 월드 좌표로 지정한다. Timeline 클립이 시작할 때 한 번 부른다.</summary>
        /// <param name="worldCenter">폭발이 터진 월드 좌표</param>
        public void SetWorldCenter(Vector3 worldCenter) => SetCenter(ToViewport(worldCenter));

        /// <summary>
        /// 덮임을 코루틴으로 재생한다. Timeline <b>Signal Receiver</b>에서 부를 수 있는 void 진입점이다.
        /// </summary>
        /// <remarks>
        /// UnityEvent는 <c>IEnumerator</c> 반환 메서드를 목록에 띄우지 못한다. 그래서 Signal로 몰 때는
        /// 반드시 이 래퍼를 쓴다. 중심은 인스펙터의 <see cref="centerTarget"/>을 따르고, 비어 있으면 화면 중앙이다.
        /// </remarks>
        public void PlayCover()
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;

            if (centerTarget != null) SetWorldCenter(centerTarget.position);
            else SetCenter(new Vector2(0.5f, 0.5f));

            if (routine != null) StopCoroutine(routine);
            StartCoroutine(CoverInternal());
        }

        /// <summary>걷힘을 코루틴으로 재생한다. Signal Receiver용 void 진입점이다.</summary>
        public void PlayBurn()
        {
            if (!isActiveAndEnabled || !Application.isPlaying) return;

            if (routine != null) StopCoroutine(routine);
            StartCoroutine(BurnInternal());
        }

        private IEnumerator CoverInternal()
        {
            if (!Prepare()) yield break;

            // 지난 판이 걷히는 도중에 끊겼으면 탄 자국이 남아 있다. 매번 성한 상태에서 시작한다.
            ApplyProgress(currentCover, 0f);

            routine = StartCoroutine(Drive(burnPhase: false, coverCurve, coverDuration));
            yield return routine;
            routine = null;
        }

        private IEnumerator BurnInternal()
        {
            if (!Prepare()) yield break;

            emberCarry = 0f;

            routine = StartCoroutine(Drive(burnPhase: true, burnCurve, burnDuration));
            yield return routine;
            routine = null;

            // 다 걷혔으면 덮임도 함께 내려 둔다. 남겨 두면 다음 판이 이미 가려진 채로 시작한다.
            ApplyProgress(0f, 0f);
        }

        /// <summary>덮임 또는 걷힘 진행을 0에서 <see cref="overshoot"/>만큼 넘긴 지점까지 곡선대로 민다.</summary>
        /// <param name="burnPhase">true면 걷힘을, false면 덮임을 민다. 걷힘에서만 불똥을 띄운다.</param>
        /// <param name="curve">진행 곡선</param>
        /// <param name="duration">걸리는 시간(초)</param>
        private IEnumerator Drive(bool burnPhase, AnimationCurve curve, float duration)
        {
            float goal = 1f + overshoot;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float dt = Time.unscaledDeltaTime;
                elapsed += dt;
                UpdateGeometry();

                // 곡선이 잘못 잡혀도 범위를 벗어난 값이 셰이더로 새어 나가지 않게 막는다.
                float progress = Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(elapsed / duration))) * goal;

                if (burnPhase)
                {
                    ApplyProgress(currentCover, progress);
                    EmitEmbers(progress, dt);
                }
                else
                {
                    ApplyProgress(progress, currentBurn);
                }

                yield return null;
            }

            if (burnPhase) ApplyProgress(currentCover, goal);
            else ApplyProgress(goal, currentBurn);
        }

        /// <summary>진행값을 기억하고 머티리얼에 넣는다. 진행값은 반드시 여기로만 쓴다.</summary>
        private void ApplyProgress(float cover, float burn)
        {
            currentCover = cover;
            currentBurn = burn;

            if (material == null) return;

            material.SetFloat(CoverID, cover);
            material.SetFloat(BurnID, burn);
        }

        /// <summary>
        /// 지금 타고 있는 경계선(중심에서 <paramref name="progress"/>만큼 떨어진 원) 위에 불똥을 띄운다.
        /// </summary>
        /// <remarks>
        /// 한 점에서 터뜨리지 않고 <b>원 둘레를 따라</b> 뿌리는 것이 요점이다. 불똥은 타는 자리에서 나므로,
        /// 경계가 넓어질수록 나오는 자리도 넓어져야 한다. 그래서 개수를 반지름에 비례시킨다.
        /// <para>
        /// ★ 에디터(재생 아님)에서는 내지 않는다. 불똥은 풀에서 게임오브젝트를 만들어 쓰는데, 에디터에서는
        /// 불똥의 Update가 돌지 않아 움직이지도 사라지지도 않고 <b>씬에 오브젝트로 쌓여 저장된다.</b>
        /// </para>
        /// </remarks>
        private void EmitEmbers(float progress, float deltaTime)
        {
            if (!Application.isPlaying) return;
            if (embers == null || embersPerSecond <= 0f) return;

            // 불똥은 타는 경계에서 나므로 덮임이 아니라 걷힘의 중심·반지름을 쓴다.
            float radius = progress * burnMaxRadius;

            // 반지름 0에서는 낼 자리가 없다. 화면 절반쯤 왔을 때가 기준 개수가 되도록 정규화한다.
            float density = Mathf.Clamp01(radius / Mathf.Max(1f, burnMaxRadius * 0.5f));

            emberCarry += embersPerSecond * density * deltaTime;

            int amount = Mathf.FloorToInt(emberCarry);
            if (amount <= 0) return;

            emberCarry -= amount;

            Vector2 centerPx = new((burnCenter.x - 0.5f) * rectSize.x, (burnCenter.y - 0.5f) * rectSize.y);
            embers.EmitRing(centerPx, radius, amount, emberScatter);
        }

        /// <summary>
        /// 월드 좌표를 뷰포트(0~1)로 옮긴다. 카메라 뒤에 있으면 화면 중앙으로, 화면 밖이면 가장 가까운 가장자리로 당긴다.
        /// </summary>
        /// <remarks>
        /// <b>화면 밖 좌표를 그대로 쓰면 원이 사라진다.</b> 반지름은 중심에서 가장 먼 모서리까지로 정규화하는데,
        /// 중심이 화면 밖 멀리 있으면 그 거리가 화면보다 몇 배 커져 화면 안의 정규화 반지름이 거의 같은 값(예: 0.8~0.9)에
        /// 몰린다. 그러면 원의 호가 화면을 가로지르는 거의 평평한 띠가 되고, 경계 노이즈가 그 차이를 이겨
        /// 원형으로 퍼지는 대신 여기저기 뚫리는 디졸브처럼 보인다. 중심은 클립에 들어서는 순간 한 번만 뜨므로,
        /// 그 순간의 카메라 컷에서 폭발 지점이 화면 밖이면 그 판 전체가 이렇게 된다(되감아 뒤에서 들어오면
        /// 다른 컷에서 떠 멀쩡해 보이는 이유).
        /// </remarks>
        private Vector2 ToViewport(Vector3 worldCenter)
        {
            Camera cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return new Vector2(0.5f, 0.5f);

            Vector3 vp = cam.WorldToViewportPoint(worldCenter);

            // z가 음수면 카메라 뒤라 x/y가 반전된 쓰레기 값이다. 그대로 넘기면 불이 엉뚱한 데서 자란다.
            if (vp.z <= 0f)
            {
                WarnCenterOffscreen(cam, vp, "카메라 뒤");
                return new Vector2(0.5f, 0.5f);
            }

            if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f)
                WarnCenterOffscreen(cam, vp, "화면 밖");

            return new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
        }

        // 폭발 지점이 화면에 안 잡힌 채로 중심을 뜬 경우를 한 번 알린다. 원인은 코드가 아니라 "그 순간의 카메라 컷"이라
        // 화면만 봐서는 짐작하기 어렵다. 스크럽할 때마다 뜨면 소음이라 세션당 한 번만 낸다.
        private void WarnCenterOffscreen(Camera cam, Vector3 viewport, string reason)
        {
            if (warnedOffscreen) return;
            warnedOffscreen = true;

            Debug.LogWarning($"{name}: 폭발 중심이 {reason}(뷰포트 {viewport.x:0.00}, {viewport.y:0.00}, z {viewport.z:0.0})라 " +
                             $"화면 가장자리로 당겨 씁니다 — 카메라 '{cam.name}'. 불이 폭발 자리에서 자라게 하려면 " +
                             "커튼 클립 시작 시점의 카메라 컷에 폭발 지점(Explosion Center)이 보이도록 클립이나 컷을 옮기세요.", this);
        }

        private void SetCenter(Vector2 viewport)
        {
            center = viewport;
            UpdateGeometry();
        }

        /// <summary>
        /// 렉트 크기와, 덮임·걷힘 각 중심에서 가장 먼 모서리까지의 거리를 셰이더에 넣는다.
        /// </summary>
        /// <remarks>
        /// 이 거리로 반지름을 정규화하기 때문에 진행값 1이 곧 "화면 끝까지"가 된다.
        /// 중심이 화면 구석이면 반대편 모서리가 훨씬 머니, 고정값을 쓰면 덜 덮이거나 덜 걷힌다.
        /// 덮임(폭발 자리)과 걷힘(기본은 커튼 정중앙)은 중심이 달라 거리도 따로 잰다 — 하나로 쓰면
        /// 중앙에서 뚫리는 걷힘이 폭발 자리 기준 거리로 정규화돼 1에 닿기 전에 끝나거나 너무 늦게 끝난다.
        /// 해상도 변경에도 따라가야 해서 매 프레임 갱신한다(값 세팅뿐이라 비용은 무시할 수준).
        /// </remarks>
        private void UpdateGeometry()
        {
            if (material == null) return;
            if (self == null) self = (RectTransform)transform;

            Rect rect = self.rect;
            float w = Mathf.Max(1f, rect.width);
            float h = Mathf.Max(1f, rect.height);

            rectSize = new Vector2(w, h);
            maxRadius = FarthestCornerDistance(center, w, h);

            burnCenter = burnCenterOverride ?? (burnFromCurtainCenter ? new Vector2(0.5f, 0.5f) : center);
            burnMaxRadius = FarthestCornerDistance(burnCenter, w, h);

            material.SetVector(CenterID, new Vector4(center.x, center.y, 0f, 0f));
            material.SetVector(RectSizeID, new Vector4(w, h, 0f, 0f));
            material.SetFloat(MaxRadiusID, maxRadius);
            material.SetVector(BurnCenterID, new Vector4(burnCenter.x, burnCenter.y, 0f, 0f));
            material.SetFloat(BurnMaxRadiusID, burnMaxRadius);
        }

        // 중심에서 네 모서리까지 중 가장 먼 거리(px). 중심이 한쪽으로 치우칠수록 커진다.
        private static float FarthestCornerDistance(Vector2 viewportCenter, float width, float height)
        {
            float dx = Mathf.Max(viewportCenter.x, 1f - viewportCenter.x) * width;
            float dy = Mathf.Max(viewportCenter.y, 1f - viewportCenter.y) * height;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>머티리얼 인스턴스를 준비한다. 참조가 빠져 있으면 한 번 경고하고 조용히 물러난다.</summary>
        private bool Prepare()
        {
            if (material != null) return true;

            if (target == null) target = GetComponent<Graphic>();
            if (target == null)
            {
                WarnOnce("가림막을 그릴 Graphic이 없어 불 연출을 재생하지 못한다.");
                return false;
            }

            if (curtainMaterial != null)
            {
                material = new Material(curtainMaterial);
            }
            else if (curtainShader != null)
            {
                material = new Material(curtainShader);
            }
            else
            {
                WarnOnce("머티리얼 에셋과 셰이더가 모두 비어 불 연출을 재생하지 못한다.");
                return false;
            }

            // 인스턴스는 저장하지 않는다. 씬·에셋 어디에도 흔적을 남기지 않게 한다.
            material.name = $"{name} (Instance)";
            material.hideFlags = HideFlags.DontSave;

            // 에셋에서 복사한 모양 값을 컴포넌트 값으로 덮는다. 빌드에서는 여기서 한 번만 넣는다.
            ApplyLook();

            if (self == null) self = (RectTransform)transform;
            UpdateGeometry();
            ApplyProgress(currentCover, currentBurn);

            // IMaterialModifier가 새 인스턴스를 돌려주도록 다시 그리게 한다.
            target.SetMaterialDirty();
            return true;
        }

        /// <summary>
        /// <see cref="look"/>의 모양 값을 인스턴스 머티리얼에 넣는다. 에셋 복사(<c>SyncFromTemplate</c>) 뒤와
        /// 인스턴스를 만든 직후에 부른다 — 에셋 값보다 컴포넌트 값이 이기게 하기 위함이다.
        /// </summary>
        private void ApplyLook()
        {
            if (material == null || look == null) return;

            material.SetColor(TintID, look.tint);
            material.SetFloat(BaseStrengthID, look.baseStrength);

            material.SetColor(SmokeColorID, look.smokeColor);
            material.SetColor(FireColorID, look.fireColor);
            material.SetColor(HotColorID, look.hotColor);
            material.SetColor(WhiteHotID, look.whiteHot);
            material.SetColor(SootColorID, look.sootColor);

            material.SetFloat(NoiseScaleID, look.noiseScale);
            material.SetFloat(RidgeID, look.ridge);
            material.SetFloat(BoilID, look.boil);
            material.SetFloat(RiseSpeedID, look.riseSpeed);
            material.SetFloat(RadialStretchID, look.radialStretch);
            material.SetFloat(SwirlSpeedID, look.swirlSpeed);

            material.SetFloat(ContrastID, look.contrast);
            material.SetFloat(GammaID, look.gamma);
            material.SetFloat(CoreBoostID, look.coreBoost);
            material.SetFloat(FlickerID, look.flicker);

            material.SetFloat(EdgeNoiseID, look.edgeNoise);
            material.SetFloat(EdgeTongueID, look.edgeTongue);
            material.SetFloat(EdgeSoftID, look.edgeSoftness);
            material.SetFloat(EmberWidthID, look.emberWidth);
            material.SetFloat(SootWidthID, look.sootWidth);
            material.SetFloat(BurnSeedID, look.burnSeed);
        }

        private void WarnOnce(string message)
        {
            if (warnedMissing) return;

            warnedMissing = true;
            Debug.LogWarning($"{name}: {message}", this);
        }

        private IEnumerator Wait(float seconds)
        {
            float remain = seconds;
            while (remain > 0f)
            {
                remain -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 머티리얼 에셋 값을 인스턴스로 다시 복사하고, 이 컴포넌트가 쥔 모양·진행·중심 값을 다시 얹는다.
        /// </summary>
        /// <remarks>
        /// 복사하면 <c>_Cover</c>·<c>_Burn</c> 같은 진행값까지 에셋 값(0)으로 덮이므로, 반드시 뒤에서 되돌려 넣는다.
        /// 에디터 전용이다 — 빌드에서는 에셋이 실행 중에 바뀔 일이 없어 매 프레임 복사할 이유가 없다.
        /// </remarks>
        private void SyncFromTemplate()
        {
            if (material == null) return;

            // 에셋을 복사하면 모양 값까지 에셋 값으로 덮이므로, 복사 뒤에 컴포넌트 값을 반드시 다시 얹는다.
            if (curtainMaterial != null) material.CopyPropertiesFromMaterial(curtainMaterial);
            ApplyLook();
            UpdateGeometry();
            ApplyProgress(currentCover, currentBurn);
        }

        /// <summary>
        /// 에디터(재생 아님)에서 가림막을 띄워 둔다. 타임라인 미리보기 중이면 그 값을 유지하고 불만 굴린다.
        /// </summary>
        /// <remarks>
        /// 에디터의 Update는 무언가 바뀔 때만 불린다. 불이 계속 이글거리게 하려면 다음 틱을 직접 예약해야 한다
        /// (<c>QueuePlayerLoopUpdate</c>). 미리보기가 꺼져 있으면 예약하지 않으므로 평소 에디터 부하는 없다.
        /// </remarks>
        private void UpdateEditorPreview()
        {
            if (!drivenExternally && !previewInEditor)
            {
                // 미리보기를 끈 순간 한 번 지운다. 이후로는 아무것도 하지 않는다.
                if (currentCover > 0f || currentBurn > 0f) ApplyProgress(0f, 0f);
                return;
            }

            if (!Prepare()) return;

            SyncFromTemplate();

            // 미리보기가 켜져 있으면 타임라인보다 우선한다(SetProgress도 이때는 값을 넣지 않는다).
            if (previewInEditor)
            {
                center = centerTarget != null ? ToViewport(centerTarget.position) : new Vector2(0.5f, 0.5f);
                UpdateGeometry();
                ApplyProgress(previewCover, previewBurn);
            }

            material.SetFloat(FxTimeID, Time.realtimeSinceStartup);
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        private void OnValidate()
        {
            // 이 필드가 생기기 전에 저장된 컴포넌트면, 머티리얼 에셋에 튜닝해 둔 값을 한 번 옮겨 온다.
            if (!lookImported)
            {
                lookImported = true;
                if (curtainMaterial != null)
                {
                    ImportLookFromMaterial(curtainMaterial);

                    // OnValidate 안에서 바꾼 값은 수정됨으로 표시되지 않아 저장 때 빠질 수 있다. 다음 틱에 표시한다.
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        if (this != null) UnityEditor.EditorUtility.SetDirty(this);
                    };
                }
            }

            // 슬라이더를 움직이는 즉시 인스턴스에 넣는다(재생 중에는 Update의 동기화가 이어받는다).
            if (material != null) ApplyLook();

            // 인스펙터에서 미리보기를 켜거나 값을 바꾸면 곧바로 다시 그리게 한다.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>
        /// 새로 붙인 컴포넌트는 옮겨 올 옛 값이 없다. 머티리얼이 이미 꽂혀 있으면 그 값에서 시작하고, 아니면 셰이더 기본값이다.
        /// </summary>
        private void Reset()
        {
            lookImported = true;
            if (curtainMaterial != null) ImportLookFromMaterial(curtainMaterial);
        }

        /// <summary>
        /// 머티리얼 에셋에 조정된 모양 값을 컴포넌트로 가져온다. 인스펙터의 컴포넌트 메뉴(⋮)에서 부른다.
        /// 에셋 쪽에서 맞춰 둔 값을 되살리고 싶을 때 쓴다 — 컴포넌트에서 바꾼 값은 덮어쓴다.
        /// </summary>
        [ContextMenu("머티리얼 에셋에서 불 모양 값 가져오기")]
        private void ImportLookFromMaterialMenu()
        {
            if (curtainMaterial == null)
            {
                Debug.LogWarning($"{name}: 머티리얼 에셋이 비어 가져올 값이 없습니다.", this);
                return;
            }

            UnityEditor.Undo.RecordObject(this, "불 모양 값 가져오기");
            ImportLookFromMaterial(curtainMaterial);
            UnityEditor.EditorUtility.SetDirty(this);
            if (material != null) ApplyLook();
        }

        /// <summary>머티리얼의 모양 값을 <see cref="look"/>에 옮긴다. 셰이더에 없는 값은 건너뛴다(필드 값 유지).</summary>
        /// <param name="source">값을 읽을 머티리얼.</param>
        private void ImportLookFromMaterial(Material source)
        {
            look ??= new CurtainLook();

            look.tint = ReadColor(source, TintID, look.tint);
            look.baseStrength = ReadFloat(source, BaseStrengthID, look.baseStrength);

            look.smokeColor = ReadColor(source, SmokeColorID, look.smokeColor);
            look.fireColor = ReadColor(source, FireColorID, look.fireColor);
            look.hotColor = ReadColor(source, HotColorID, look.hotColor);
            look.whiteHot = ReadColor(source, WhiteHotID, look.whiteHot);
            look.sootColor = ReadColor(source, SootColorID, look.sootColor);

            look.noiseScale = ReadFloat(source, NoiseScaleID, look.noiseScale);
            look.ridge = ReadFloat(source, RidgeID, look.ridge);
            look.boil = ReadFloat(source, BoilID, look.boil);
            look.riseSpeed = ReadFloat(source, RiseSpeedID, look.riseSpeed);
            look.radialStretch = ReadFloat(source, RadialStretchID, look.radialStretch);
            look.swirlSpeed = ReadFloat(source, SwirlSpeedID, look.swirlSpeed);

            look.contrast = ReadFloat(source, ContrastID, look.contrast);
            look.gamma = ReadFloat(source, GammaID, look.gamma);
            look.coreBoost = ReadFloat(source, CoreBoostID, look.coreBoost);
            look.flicker = ReadFloat(source, FlickerID, look.flicker);

            look.edgeNoise = ReadFloat(source, EdgeNoiseID, look.edgeNoise);
            look.edgeTongue = ReadFloat(source, EdgeTongueID, look.edgeTongue);
            look.edgeSoftness = ReadFloat(source, EdgeSoftID, look.edgeSoftness);
            look.emberWidth = ReadFloat(source, EmberWidthID, look.emberWidth);
            look.sootWidth = ReadFloat(source, SootWidthID, look.sootWidth);
            look.burnSeed = ReadFloat(source, BurnSeedID, look.burnSeed);
        }

        private static float ReadFloat(Material source, int id, float fallback)
            => source.HasProperty(id) ? source.GetFloat(id) : fallback;

        private static Color ReadColor(Material source, int id, Color fallback)
            => source.HasProperty(id) ? source.GetColor(id) : fallback;
#endif
    }
}
