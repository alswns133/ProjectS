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
    /// <b>모양은 머티리얼 에셋, 진행은 이 컴포넌트.</b> 불의 결·색·경계 같은 모양 값은
    /// <see cref="curtainMaterial"/> 에셋의 인스펙터에서 조정한다. 이 컴포넌트는 그 에셋을 복사한 인스턴스에
    /// 덮임·걷힘 진행과 중심·시간만 넣는다. 에셋 자체에 진행값을 쓰면 재생할 때마다 .mat 파일이 바뀌어
    /// 커밋에 섞이고, 다음 판이 탄 상태로 시작한다. 에디터에서는 매 프레임 에셋 값을 인스턴스로 다시 복사하므로
    /// 재생 중에도 에셋을 만지면 곧바로 화면에 반영된다.
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

        [Tooltip("불의 모양 값(결·색·경계)을 담은 머티리얼 에셋. 여기서 값을 조정한다. " +
                 "실행 중에는 이 에셋을 복사한 인스턴스를 쓰므로 에셋 파일이 진행값으로 더러워지지 않는다.")]
        [SerializeField] private Material curtainMaterial;

        [Tooltip("머티리얼 에셋이 비었을 때만 쓰는 대체 셰이더. 이 경우 모양 값은 셰이더 기본값 그대로다.")]
        [SerializeField] private Shader curtainShader;

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

        // 이번 프레임의 렉트 크기·최대 반지름. UpdateGeometry가 채우고 불똥 방출이 그대로 쓴다.
        private Vector2 rectSize = new(1920f, 1080f);
        private float maxRadius = 1100f;

        // 참조가 빠졌다는 경고를 한 번만 낸다. 에디터 미리보기는 매 틱 Prepare를 부르므로 그대로 두면 콘솔이 넘친다.
        private bool warnedMissing;

        private static readonly int CoverID = Shader.PropertyToID("_Cover");
        private static readonly int BurnID = Shader.PropertyToID("_Burn");
        private static readonly int CenterID = Shader.PropertyToID("_Center");
        private static readonly int RectSizeID = Shader.PropertyToID("_RectSize");
        private static readonly int MaxRadiusID = Shader.PropertyToID("_MaxRadius");
        private static readonly int FxTimeID = Shader.PropertyToID("_FxTime");

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

        /// <summary>가려진 화면이 중심부터 타며 걷힌다. 덮을 때 쓴 중심을 그대로 쓴다.</summary>
        public IEnumerator Burn()
        {
            yield return BurnInternal();
        }

        /// <summary>
        /// 가려진 화면이 <paramref name="worldCenter"/>의 화면 위치부터 타며 걷힌다.
        /// 보스 위치를 넘기면 "보스가 불을 밀어내고 드러난다"로 읽힌다.
        /// </summary>
        /// <param name="worldCenter">걷힘이 시작될 월드 좌표.</param>
        public IEnumerator Burn(Vector3 worldCenter)
        {
            SetCenter(ToViewport(worldCenter));
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

            float radius = progress * maxRadius;

            // 반지름 0에서는 낼 자리가 없다. 화면 절반쯤 왔을 때가 기준 개수가 되도록 정규화한다.
            float density = Mathf.Clamp01(radius / Mathf.Max(1f, maxRadius * 0.5f));

            emberCarry += embersPerSecond * density * deltaTime;

            int amount = Mathf.FloorToInt(emberCarry);
            if (amount <= 0) return;

            emberCarry -= amount;

            Vector2 centerPx = new((center.x - 0.5f) * rectSize.x, (center.y - 0.5f) * rectSize.y);
            embers.EmitRing(centerPx, radius, amount, emberScatter);
        }

        /// <summary>월드 좌표를 뷰포트(0~1)로 옮긴다. 카메라 뒤에 있으면 화면 중앙으로 떨어뜨린다.</summary>
        private Vector2 ToViewport(Vector3 worldCenter)
        {
            Camera cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return new Vector2(0.5f, 0.5f);

            Vector3 vp = cam.WorldToViewportPoint(worldCenter);

            // z가 음수면 카메라 뒤라 x/y가 반전된 쓰레기 값이다. 그대로 넘기면 불이 엉뚱한 데서 자란다.
            if (vp.z <= 0f) return new Vector2(0.5f, 0.5f);

            return new Vector2(vp.x, vp.y);
        }

        private void SetCenter(Vector2 viewport)
        {
            center = viewport;
            UpdateGeometry();
        }

        /// <summary>
        /// 렉트 크기와, 중심에서 가장 먼 모서리까지의 거리를 셰이더에 넣는다.
        /// </summary>
        /// <remarks>
        /// 이 거리로 반지름을 정규화하기 때문에 진행값 1이 곧 "화면 끝까지"가 된다.
        /// 중심이 화면 구석이면 반대편 모서리가 훨씬 머니, 고정값을 쓰면 덜 덮이거나 덜 걷힌다.
        /// 해상도 변경에도 따라가야 해서 매 프레임 갱신한다(값 세팅뿐이라 비용은 무시할 수준).
        /// </remarks>
        private void UpdateGeometry()
        {
            if (material == null) return;
            if (self == null) self = (RectTransform)transform;

            Rect rect = self.rect;
            float w = Mathf.Max(1f, rect.width);
            float h = Mathf.Max(1f, rect.height);

            // 중심에서 네 모서리까지 중 가장 먼 거리. 중심이 한쪽으로 치우칠수록 커진다.
            float dx = Mathf.Max(center.x, 1f - center.x) * w;
            float dy = Mathf.Max(center.y, 1f - center.y) * h;

            rectSize = new Vector2(w, h);
            maxRadius = Mathf.Sqrt(dx * dx + dy * dy);

            material.SetVector(CenterID, new Vector4(center.x, center.y, 0f, 0f));
            material.SetVector(RectSizeID, new Vector4(w, h, 0f, 0f));
            material.SetFloat(MaxRadiusID, maxRadius);
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

            if (self == null) self = (RectTransform)transform;
            UpdateGeometry();
            ApplyProgress(currentCover, currentBurn);

            // IMaterialModifier가 새 인스턴스를 돌려주도록 다시 그리게 한다.
            target.SetMaterialDirty();
            return true;
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
        /// 머티리얼 에셋의 모양 값을 인스턴스로 다시 복사하고, 이 컴포넌트가 쥔 진행·중심 값을 다시 얹는다.
        /// </summary>
        /// <remarks>
        /// 복사하면 <c>_Cover</c>·<c>_Burn</c> 같은 진행값까지 에셋 값(0)으로 덮이므로, 반드시 뒤에서 되돌려 넣는다.
        /// 에디터 전용이다 — 빌드에서는 에셋이 실행 중에 바뀔 일이 없어 매 프레임 복사할 이유가 없다.
        /// </remarks>
        private void SyncFromTemplate()
        {
            if (material == null || curtainMaterial == null) return;

            material.CopyPropertiesFromMaterial(curtainMaterial);
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
            // 인스펙터에서 미리보기를 켜거나 값을 바꾸면 곧바로 다시 그리게 한다.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }
}
