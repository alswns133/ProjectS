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
    /// <b>중심은 화면 정중앙이 아니라 폭발이 터진 자리다.</b> <see cref="Cover"/>에 폭발의 월드 좌표를 주면
    /// 그 지점의 화면 좌표에서 불이 자라난다. 덮이기 시작하는 순간엔 실제 폭발 파티클이 아직 화면에 있으므로,
    /// 같은 자리에서 같은 색이 번지면 <b>화면 캡처 없이도</b> "그 폭발이 화면을 삼켰다"로 이어져 보인다.
    /// (캡처한 폭발 프레임을 굳이 깔고 싶으면 셰이더의 <c>_MainTex</c>에 RenderTexture를 물리면 된다.
    ///  기본으로 쓰지 않는 이유는 정지 프레임이 한 순간에 "멈춤"으로 읽혀 유지 구간을 길게 못 쓰기 때문이다.)
    /// </para>
    /// <para>
    /// <b>중심 좌표는 덮는 순간에 한 번만 떠서 고정한다.</b> 가려진 동안 카메라가 보스를 잡는 각도로
    /// 움직이는데, 월드 좌표를 매 프레임 따라가면 불의 중심이 화면에서 미끄러진다.
    /// 걷힘의 중심을 따로 주고 싶으면(예: 보스 위치에서 불이 밀려 나가게) <see cref="Burn(Vector3)"/>를 쓴다.
    /// </para>
    /// <para>
    /// <b>덮임과 걷힘은 따로 부른다.</b> 그 사이에 보스가 자리를 잡고 포즈를 끝내고 카메라가 붙는 시간이
    /// 들어가는데, 그 길이는 연출(Timeline)이 정할 일이지 이 컴포넌트가 정할 일이 아니다.
    /// 한 번에 굴리고 싶으면 <see cref="Play"/>로 유지 시간을 넘긴다.
    /// </para>
    /// <para>
    /// <b>머티리얼은 인스턴스를 쓴다</b>(<see cref="AshDissolveFx"/>·<see cref="GlitchTextFx"/>와 같은 방침).
    /// 에셋을 직접 만지면 씬 diff가 남고 다음 판이 탄 상태로 시작한다.
    /// </para>
    /// <para>
    /// 시간은 unscaled로 센다. 보스 등장은 히트스톱·슬로우모션(<c>SlowMotionController</c>)과 겹치기 쉬운데,
    /// 그때 불까지 함께 느려지면 안 된다. 셰이더의 <c>_FxTime</c>도 같은 이유로 여기서 채운다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class FireCurtainFx : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("화면을 가득 채우는 Graphic(Image 권장, 흰 스프라이트). 비우면 이 오브젝트에서 찾는다. " +
                 "RectTransform은 앵커를 화면 전체로 늘려 둔다.")]
        [SerializeField] private Graphic target;

        [Tooltip("'ProjectS/UI Fire Curtain' 셰이더. 이걸로 머티리얼 인스턴스를 만들어 Graphic에 물린다.")]
        [SerializeField] private Shader curtainShader;

        [Tooltip("폭발의 월드 좌표를 화면 좌표로 옮길 카메라. 비우면 Camera.main을 쓴다.")]
        [SerializeField] private Camera worldCamera;

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

        [Header("색")]
        [Tooltip("폭발 파티클과 같은 색을 넣는다. 두 색이 어긋나면 다른 불이 덮은 것으로 보인다.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color fireColor = new(1f, 0.24f, 0.05f, 1f);

        [Tooltip("코어·불씨 색. HDR이라 Bloom이 집는다.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color hotColor = new(1f, 0.78f, 0.3f, 1f);

        private RectTransform self;
        private Material material;
        private Coroutine routine;

        // 덮는 순간에 뜬 화면 좌표. 카메라가 움직여도 불의 중심이 미끄러지지 않게 고정해 둔다.
        private Vector2 center = new(0.5f, 0.5f);

        private static readonly int CoverID = Shader.PropertyToID("_Cover");
        private static readonly int BurnID = Shader.PropertyToID("_Burn");
        private static readonly int CenterID = Shader.PropertyToID("_Center");
        private static readonly int RectSizeID = Shader.PropertyToID("_RectSize");
        private static readonly int MaxRadiusID = Shader.PropertyToID("_MaxRadius");
        private static readonly int FireColorID = Shader.PropertyToID("_FireColor");
        private static readonly int HotColorID = Shader.PropertyToID("_HotColor");
        private static readonly int FxTimeID = Shader.PropertyToID("_FxTime");

        /// <summary>지금 화면이 가려져 있거나 가려지는 중인지.</summary>
        public bool IsCovering => routine != null || (material != null && material.GetFloat(CoverID) > 0.001f);

        private void Awake()
        {
            self = (RectTransform)transform;
            if (target == null) target = GetComponent<Graphic>();

            // 가림막은 입력을 받지 않는다. 켜 두면 화면을 덮는 동안 아래 HUD 클릭을 삼킨다.
            if (target != null) target.raycastTarget = false;

            Prepare();
            Clear();
        }

        private void Update()
        {
            if (material == null) return;

            // 셰이더 내장 _Time은 timeScale을 타므로 히트스톱에 불까지 느려진다. 실제 시간을 직접 넣는다.
            material.SetFloat(FxTimeID, Time.unscaledTime);
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
        /// 씬 전환·보스 즉사처럼 화면이 통째로 바뀔 때 호출한다.
        /// </summary>
        public void Clear()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            if (!Prepare()) return;

            material.SetFloat(CoverID, 0f);
            material.SetFloat(BurnID, 0f);
        }

        private IEnumerator CoverInternal()
        {
            if (!Prepare()) yield break;

            // 지난 판이 걷히는 도중에 끊겼으면 탄 자국이 남아 있다. 매번 성한 상태에서 시작한다.
            material.SetFloat(BurnID, 0f);

            routine = StartCoroutine(Drive(CoverID, coverCurve, coverDuration));
            yield return routine;
            routine = null;
        }

        private IEnumerator BurnInternal()
        {
            if (!Prepare()) yield break;

            routine = StartCoroutine(Drive(BurnID, burnCurve, burnDuration));
            yield return routine;
            routine = null;

            // 다 걷혔으면 덮임도 함께 내려 둔다. 남겨 두면 다음 판이 이미 가려진 채로 시작한다.
            material.SetFloat(CoverID, 0f);
            material.SetFloat(BurnID, 0f);
        }

        /// <summary>지정한 셰이더 값을 0에서 <see cref="overshoot"/>만큼 넘긴 지점까지 곡선대로 민다.</summary>
        private IEnumerator Drive(int propertyID, AnimationCurve curve, float duration)
        {
            float goal = 1f + overshoot;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                UpdateGeometry();

                // 곡선이 잘못 잡혀도 범위를 벗어난 값이 셰이더로 새어 나가지 않게 막는다.
                float t = Mathf.Clamp01(curve.Evaluate(Mathf.Clamp01(elapsed / duration)));
                material.SetFloat(propertyID, t * goal);
                yield return null;
            }

            material.SetFloat(propertyID, goal);
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

            Rect rect = self.rect;
            float w = Mathf.Max(1f, rect.width);
            float h = Mathf.Max(1f, rect.height);

            // 중심에서 네 모서리까지 중 가장 먼 거리. 중심이 한쪽으로 치우칠수록 커진다.
            float dx = Mathf.Max(center.x, 1f - center.x) * w;
            float dy = Mathf.Max(center.y, 1f - center.y) * h;

            material.SetVector(CenterID, new Vector4(center.x, center.y, 0f, 0f));
            material.SetVector(RectSizeID, new Vector4(w, h, 0f, 0f));
            material.SetFloat(MaxRadiusID, Mathf.Sqrt(dx * dx + dy * dy));
        }

        /// <summary>머티리얼 인스턴스를 준비한다. 참조가 빠져 있으면 경고 후 조용히 물러난다.</summary>
        private bool Prepare()
        {
            if (material != null) return true;

            if (target == null) target = GetComponent<Graphic>();
            if (target == null)
            {
                Debug.LogWarning($"{name}: 가림막을 그릴 Graphic이 없어 불 연출을 재생하지 못한다.", this);
                return false;
            }

            if (curtainShader == null)
            {
                Debug.LogWarning($"{name}: 'ProjectS/UI Fire Curtain' 셰이더가 비어 불 연출을 재생하지 못한다.", this);
                return false;
            }

            // 에셋이 아니라 인스턴스를 만든다. 에셋을 직접 만지면 씬 diff가 남고
            // 다음 판이 탄 상태로 시작한다. DontSave로 에디터 재생에서도 남지 않게 한다.
            material = new Material(curtainShader) { hideFlags = HideFlags.DontSave };
            material.SetColor(FireColorID, fireColor);
            material.SetColor(HotColorID, hotColor);

            target.material = material;

            if (self == null) self = (RectTransform)transform;
            UpdateGeometry();
            return true;
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsCovering이 영영 true가 된다.
            routine = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (material == null) return;

            material.SetColor(FireColorID, fireColor);
            material.SetColor(HotColorID, hotColor);
        }
#endif

        private IEnumerator Wait(float seconds)
        {
            float remain = seconds;
            while (remain > 0f)
            {
                remain -= Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
