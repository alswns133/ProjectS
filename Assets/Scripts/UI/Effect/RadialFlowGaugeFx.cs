using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 원형(Radial 360) 게이지 위로 에너지 밴드가 흐르는 연출을 구동한다
    /// (<c>ProjectS/UI Radial Flow Gauge</c> 셰이더). 밴드는 채움 시작점에서 출발해
    /// 채움 끝점으로 갈수록 밝아지고, 밝기는 스프라이트의 원래 색을 곱해 올리므로
    /// 아트가 게이지 그라디언트를 바꿔도 이 스크립트는 그대로 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>채움 값은 이쪽에서 정하지 않는다.</b> <see cref="Image.fillAmount"/>를 매 프레임 읽어
    /// 셰이더에 그대로 밀어넣기만 한다. 그래서 성공률을 누가 세팅하든(데이터 경로) 이 연출과
    /// 충돌하지 않고, 채움 끝점과 밝기 최고점이 어긋날 수도 없다.
    /// <b>fillAmount를 여기서 대신 세팅하려 들면 그 순간 데이터 경로가 두 갈래가 된다.</b>
    /// </para>
    /// <para>
    /// 링을 도는 방향과 시작점도 <see cref="Image.fillOrigin"/> / <see cref="Image.fillClockwise"/>에서
    /// 그대로 계산한다. 아트가 인스펙터에서 게이지 방향을 뒤집어도 밴드가 알아서 따라간다
    /// (강화창 <c>GaugeF</c>는 Top 시작 + 반시계).
    /// </para>
    /// <para>
    /// <b>머티리얼은 반드시 인스턴스를 뜬다.</b> 셰이더 프로퍼티는 머티리얼 전역이라 원본 에셋을
    /// 직접 만지면 같은 머티리얼을 쓰는 다른 게이지까지 물들고, 에디터에서는 .mat 에셋이 영구히 변한다.
    /// </para>
    /// <para>
    /// <b>시간은 unscaled로 센다.</b> 강화창은 마을에서 열리고 마을은 timeScale이 0으로 떨어질 수 있다.
    /// scaled로 세면 창은 떠 있는데 흐름만 얼어붙는다.
    /// </para>
    /// (2026-08-21 TH)
    /// </remarks>
    [RequireComponent(typeof(Image))]
    public class RadialFlowGaugeFx : MonoBehaviour
    {
        [Header("흐름 템포")]
        [Tooltip("밴드가 시작점에서 끝점까지 한 번 훑는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.05f)] private float cycleDuration = 2.4f;

        [Tooltip("한 번 훑고 다음 훑기까지 쉬는 시간(초). 0이면 끊김 없이 계속 돈다.")]
        [SerializeField, Min(0f)] private float restDuration = 0.6f;

        [Tooltip("채움 끝점을 지나 밴드가 완전히 사라질 때까지의 여유(0~1). " +
                 "0이면 끝점에서 밴드가 뚝 끊긴 채로 사라진다.")]
        [SerializeField, Range(0f, 0.5f)] private float overshoot = 0.18f;

        [Header("담금질")]
        [Tooltip("이 채움 비율부터 달아오르기 시작한다. 게이지가 12시(1.0)에 가까울수록 뜨겁다.")]
        [SerializeField, Range(0f, 1f)] private float heatStart = 0.55f;

        [Tooltip("최대 열 세기. 1이면 끝점이 백열까지 간다.")]
        [SerializeField, Range(0f, 1f)] private float maxHeat = 1f;

        [Tooltip("열이 올라오는 데 걸리는 시간(초). 내려치는 순간이라 짧아야 한다.")]
        [SerializeField, Min(0.01f)] private float heatFadeIn = 0.1f;

        [Tooltip("열이 식는 데 걸리는 시간(초). 쇠가 식듯 올라올 때보다 느려야 자연스럽다.")]
        [SerializeField, Min(0.01f)] private float heatFadeOut = 0.45f;

        [Header("몸통 색")]
        [Tooltip("게이지가 달아올랐을 때의 색. 평상시 색은 Image의 Color에서 그대로 가져오므로 " +
                 "여기엔 '뜨거울 때'만 지정한다.")]
        [SerializeField] private Color bodyHotColor = new Color(1f, 0.22f, 0.05f, 1f);

        [Tooltip("최대로 달아올랐을 때 위 색으로 얼마나 갈지. 1이면 완전히 갈아치운다.")]
        [SerializeField, Range(0f, 1f)] private float bodyHotBlend = 0.9f;

        [Tooltip("몸통이 물드는 곡선. 1이면 열에 정비례, 크게 줄수록 늦게까지 원래 색을 버틴다.")]
        [SerializeField, Range(0.2f, 4f)] private float bodyHotPow = 1.6f;

        [Header("끝점 좌표")]
        [Tooltip("링 반지름 / RectTransform 반너비. 불똥이 튀어나올 지점을 잡는 데 쓴다. " +
                 "링 그림이 사각 영역 안 어디에 그려져 있는지에 맞춘다.")]
        [SerializeField, Range(0.1f, 1f)] private float ringRadiusRatio = 0.68f;

        [Tooltip("링 밴드의 두께 / RectTransform 반너비. 불똥이 끝 선을 따라 퍼지는 폭이 된다. " +
                 "0이면 한 점에서만 튄다.")]
        [SerializeField, Range(0f, 0.5f)] private float ringBandRatio = 0.12f;

        [Header("세기")]
        [Tooltip("평상시 흐름 세기. 연출 중 세기는 SetIntensity()로 밖에서 곱한다.")]
        [SerializeField, Range(0f, 3f)] private float baseIntensity = 1f;

        private Image image;
        private Material instanced;

        private float timer;
        private float intensityScale = 1f;

        // 기본 성공률 지점(0~1). 이 위 채움은 셰이더가 천장 색(노랑)으로 칠한다.
        // 기본 1 = 전부 밑색(자비 0과 동일). 강화창이 대상 성공률로 채워준다(SetBaseFill).
        private float baseFill = 1f;

        // 링 매핑. 셰이더에 넘기는 값과 같은 것을 C#에서도 들고 있어야
        // 끝점 좌표(불똥 발생 위치)를 셰이더와 어긋나지 않게 계산할 수 있다.
        private float angleOffset;
        private float dirSign = 1f;

        // 열 게이트(0~1). 평상시 0이라 담금질과 불똥이 아예 나오지 않는다.
        // ★ 열을 게이지 높이만으로 정하면 안 된다 — 평상시 게이지는 현 단계 성공률 자리에 있고,
        //   낮은 단계는 성공률이 90~100%라 가만히 있어도 게이지가 가득 차 백열 상태가 된다.
        //   "강화를 돌리는 중인가"는 높이로 알 수 없으므로 밖에서 켜줘야 한다(SetHeatActive).
        private float heatGate;
        private float heatGateTarget;

        // 평상시 몸통 색(아트가 인스펙터에서 잡아둔 값). Start에서 한 번 담아둔다.
        private Color bodyBaseColor = Color.white;
        // 마지막으로 적용한 색. Graphic.color 대입은 메시를 다시 만들게 하므로
        // 값이 실제로 달라졌을 때만 쓴다(평상시엔 아예 건드리지 않는다).
        private Color bodyAppliedColor;

        /// <summary>
        /// 현재 열 세기(0~1). 게이지가 12시에 가까울수록 오른다.
        /// 불똥 방출량을 여기에 비례시킨다(<see cref="SparkSpawner"/>).
        /// </summary>
        public float Heat { get; private set; }

        private static readonly int FillId        = Shader.PropertyToID("_Fill");
        private static readonly int BaseFillId    = Shader.PropertyToID("_BaseFill");
        private static readonly int AngleOffsetId = Shader.PropertyToID("_AngleOffset");
        private static readonly int DirId         = Shader.PropertyToID("_Dir");
        private static readonly int FlowHeadId    = Shader.PropertyToID("_FlowHead");
        private static readonly int IntensityId   = Shader.PropertyToID("_FlowIntensity");
        private static readonly int UVRectId      = Shader.PropertyToID("_UVRect");
        private static readonly int HeatId        = Shader.PropertyToID("_Heat");

        // 밴드가 통째로 사라지는 위치. 셰이더에서 꼬리까지 전부 잘려 나간다.
        private const float HeadOff = -1f;

        // 한 프레임에 인정할 최대 시간(초). 에디터 멈칫·로딩 히치 뒤에는 unscaledDeltaTime이
        // 통째로 튀어(1초 이상) 들어오는데, 그대로 쓰면 연출이 재생되는 대신 한 프레임에
        // 전부 소모돼 건너뛴 것처럼 보인다. 히치 때는 느려질지언정 사라지지는 않게 잘라낸다.
        private const float MaxStep = 0.05f;


        private void Awake()
        {
            image = GetComponent<Image>();
        }

        // 머티리얼 인스턴스화를 Awake가 아니라 Start에서 한다.
        // 같은 오브젝트에 "머티리얼을 복제해 갈아끼우는" 컴포넌트가 또 있으면
        // (강화창 GaugeF에는 SegmentGaugeView가 같이 붙어 있다) Awake끼리는 호출 순서가
        // 보장되지 않는다. 늦게 도는 쪽이 이쪽 인스턴스를 다시 복제해 갈아끼우면,
        // 이 스크립트가 들고 있는 머티리얼은 화면에 안 붙은 고아가 되고 _FlowHead가
        // 기본값(-1)에 머물러 밴드가 통째로 안 보인다. 에러도 경고도 안 뜨는 종류의 사고다.
        // Start는 모든 Awake 이후라 항상 이쪽이 마지막에 자리를 잡는다.
        private void Start()
        {
            if (image.material != null)
            {
                instanced = new Material(image.material);
                image.material = instanced;
            }

            if (instanced == null || !instanced.HasProperty(FlowHeadId))
            {
                Debug.LogWarning($"{name}: 머티리얼에 _FlowHead가 없습니다. " +
                                 "'ProjectS/UI Radial Flow Gauge' 셰이더로 만든 머티리얼을 물려주세요.", this);
                enabled = false;
                return;
            }

            if (image.type != Image.Type.Filled || image.fillMethod != Image.FillMethod.Radial360)
            {
                Debug.LogWarning($"{name}: Image가 Filled/Radial360이 아닙니다. " +
                                 "밴드가 링을 타지 않습니다.", this);
            }

            RefreshMapping();
            instanced.SetFloat(FlowHeadId, HeadOff);

            bodyBaseColor = image.color;
            bodyAppliedColor = bodyBaseColor;
        }

        private void OnEnable()
        {
            // 창을 다시 열 때마다 같은 자리에서 시작하도록 되감는다.
            // (안 하면 이전에 닫힌 시점의 위상에서 이어져 밴드가 중간에 튀어나온 것처럼 보인다.)
            timer = 0f;
            if (instanced != null) instanced.SetFloat(FlowHeadId, HeadOff);

            // 연출 도중 창이 닫혔다 다시 열려도 달아오른 채로 시작하지 않게 되돌린다.
            heatGate = 0f;
            heatGateTarget = 0f;
            Heat = 0f;
            if (instanced != null) instanced.SetFloat(HeatId, 0f);
        }

        private void OnDestroy()
        {
            // Start에서 뜬 인스턴스 머티리얼은 이 오브젝트만 쓰므로 함께 정리한다(누수 방지).
            if (instanced != null) Destroy(instanced);
        }

        private void Update()
        {
            // 다른 컴포넌트가 뒤늦게 머티리얼을 갈아끼웠으면 자리를 되찾는다.
            // 새로 복제하지는 않는다 — 복제가 사슬처럼 쌓이면 그때부터는 누수다.
            if (image.material != instanced) image.material = instanced;

            float fill = Mathf.Clamp01(image.fillAmount);
            instanced.SetFloat(FillId, fill);
            instanced.SetFloat(BaseFillId, baseFill);
            instanced.SetFloat(IntensityId, baseIntensity * intensityScale);

            // 담금질은 게이지 높이 하나에서 파생된다. 성공률이나 강화 단계를 따로 받지 않는다 —
            // 게이지를 쓸어올리는 쪽(EnhanceGaugeSweep)이 열과 불똥까지 같이 몰고 간다.
            // 실패해서 게이지가 되돌아가면 열도 따라 식는다. 별도 실패 처리가 필요 없는 이유다.
            float fade = heatGateTarget > heatGate ? heatFadeIn : heatFadeOut;
            heatGate = Mathf.MoveTowards(heatGate, heatGateTarget,
                                        Mathf.Min(Time.unscaledDeltaTime, MaxStep) / fade);

            Heat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(heatStart, 1f, fill)) * maxHeat * heatGate;
            instanced.SetFloat(HeatId, Heat);

            ApplyBodyColor();

            timer += Mathf.Min(Time.unscaledDeltaTime, MaxStep);

            float period = cycleDuration + restDuration;
            float t = Mathf.Repeat(timer, period);

            if (t >= cycleDuration)
            {
                // 쉬는 구간 — 밴드를 링 밖으로 치워 통째로 숨긴다.
                instanced.SetFloat(FlowHeadId, HeadOff);
                return;
            }

            // 머리는 시작점(0)에서 채움 끝점 + 여유까지 훑는다.
            // 여유가 없으면 밝기가 최고조인 순간에 밴드가 잘려 사라져서 제일 아까운 프레임을 놓친다.
            float head = (t / cycleDuration) * (fill + overshoot);
            instanced.SetFloat(FlowHeadId, head);
        }

        /// <summary>
        /// 흐름 세기 배율을 설정한다. 강화 연출처럼 밖에서 템포를 줄 때 호출한다
        /// (0이면 밴드가 보이지 않고, 1이 평상시).
        /// </summary>
        /// <param name="scale">인스펙터의 기본 세기에 곱할 배율(음수는 0으로 잘림).</param>
        public void SetIntensity(float scale)
        {
            intensityScale = Mathf.Max(0f, scale);
        }

        /// <summary>
        /// 기본 성공률 지점을 설정한다. 이 값까지의 채움은 밑색(파랑), 그 위 채움 끝까지는 천장 색(노랑)으로
        /// 셰이더가 가른다. 강화창이 대상 성공률(자비 보너스 전 기본 확률)을 넣는다.
        /// 1이면 전부 밑색(자비 0과 동일).
        /// </summary>
        /// <param name="baseRate">0~1 기본 성공률(범위를 벗어나면 잘라낸다)</param>
        public void SetBaseFill(float baseRate)
        {
            baseFill = Mathf.Clamp01(baseRate);
        }

        // 게이지 몸통 전체의 색. 셰이더가 아니라 Image 틴트를 직접 잡는다 —
        // 스프라이트 × 틴트 위에 셰이더가 또 lerp를 얹으면 지정한 색이 중간에서 섞여 탁해진다.
        // 여기서 잡으면 인스펙터의 색 그대로 나온다.
        private void ApplyBodyColor()
        {
            float t = Mathf.Pow(Mathf.Clamp01(Heat), bodyHotPow) * bodyHotBlend;

            Color target = Color.Lerp(bodyBaseColor, bodyHotColor, t);
            target.a = bodyBaseColor.a;   // 알파는 아트가 잡은 값을 유지한다

            // 색 대입은 캔버스 메시를 다시 만들게 한다. 평상시(열 0)엔 값이 그대로라 건너뛴다.
            if (target == bodyAppliedColor) return;

            bodyAppliedColor = target;
            image.color = target;
        }

        /// <summary>
        /// 담금질(열·불똥)을 켜고 끈다. <b>평상시에는 꺼져 있어야 한다</b> —
        /// 게이지 높이는 강화 중인지 아닌지를 구분해주지 못하기 때문이다
        /// (낮은 단계는 평상시 성공률만으로도 게이지가 거의 가득 찬다).
        /// 강화 연출을 여는 <see cref="EnhanceGaugeSweep"/>이 호출한다.
        /// </summary>
        /// <param name="active">true면 달아오르기 시작하고, false면 식는다(즉시 꺼지지 않는다)</param>
        public void SetHeatActive(bool active)
        {
            heatGateTarget = active ? 1f : 0f;
        }

        /// <summary>
        /// 채움 끝점(불똥이 튀는 지점)의 월드 좌표를 돌려준다.
        /// 셰이더가 열을 얹는 지점과 같은 계산이라 둘이 어긋나지 않는다.
        /// </summary>
        /// <remarks>
        /// 로컬이 아니라 월드로 주는 이유는, 불똥 스포너가 <b>RectMask2D 바깥</b>의
        /// 다른 부모 밑에 있어야 하기 때문이다(창을 벗어나는 불똥이 잘리지 않게).
        /// 받는 쪽에서 자기 좌표계로 변환해 쓴다.
        /// 이 RectTransform이 좌우 반전(localScale.x = -1)돼 있어도 TransformPoint가 함께 처리한다.
        /// </remarks>
        public Vector3 GetTipWorldPosition() => GetTipWorldPosition(0.5f);

        /// <summary>
        /// 채움 끝점의 <b>끝 선 위 한 지점</b>을 월드 좌표로 돌려준다.
        /// 끝 선은 링을 가로지르는 선분이라, 불똥을 한 점이 아니라 이 선을 따라 뿌릴 수 있다.
        /// </summary>
        /// <param name="across">선 위의 위치. 0 = 안쪽 가장자리, 0.5 = 밴드 중앙, 1 = 바깥 가장자리</param>
        /// <returns>월드 좌표</returns>
        public Vector3 GetTipWorldPosition(float across)
        {
            float p = Mathf.Clamp01(image.fillAmount);

            // 12시를 0으로 본 시계방향 회전수. 셰이더의 p 매핑을 역으로 푼 것이다.
            float rad = (angleOffset + p * dirSign) * Mathf.PI * 2f;

            RectTransform rt = (RectTransform)transform;
            float half = Mathf.Min(rt.rect.width, rt.rect.height) * 0.5f;

            // 밴드 중앙(ringRadiusRatio)에서 안팎으로 ringBandRatio의 절반씩 벌린다.
            float radius = half * (ringRadiusRatio + (Mathf.Clamp01(across) - 0.5f) * ringBandRatio);

            return rt.TransformPoint(new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius);
        }

        /// <summary>
        /// Image의 스프라이트나 채움 방향을 런타임에 바꿨을 때 호출한다.
        /// 링 매핑(시작 각도·회전 방향·스프라이트 UV)을 다시 계산한다.
        /// 안 부르면 밴드가 링을 안 타고 비스듬히 지나간다.
        /// </summary>
        public void RefreshMapping()
        {
            if (instanced == null) return;

            // 12시를 0으로 본 시계방향 시작 각도.
            float offset;
            switch ((Image.Origin360)image.fillOrigin)
            {
                case Image.Origin360.Right:  offset = 0.25f; break;
                case Image.Origin360.Bottom: offset = 0.5f;  break;
                case Image.Origin360.Left:   offset = 0.75f; break;
                default:                     offset = 0f;    break; // Top
            }

            angleOffset = offset;
            dirSign = image.fillClockwise ? 1f : -1f;

            instanced.SetFloat(AngleOffsetId, angleOffset);
            instanced.SetFloat(DirId, dirSign);

            // 스프라이트가 아틀라스에 묶여 있으면 uv가 0~1이 아니다.
            // 극좌표 중심을 맞추려면 실제 텍스처 안에서의 rect를 넘겨줘야 한다.
            Vector4 uvRect = new Vector4(0f, 0f, 1f, 1f);
            Sprite sprite = image.sprite;
            if (sprite != null && sprite.texture != null)
            {
                Rect tr = sprite.textureRect;
                float tw = sprite.texture.width;
                float th = sprite.texture.height;
                uvRect = new Vector4(tr.x / tw, tr.y / th, tr.width / tw, tr.height / th);
            }

            instanced.SetVector(UVRectId, uvRect);
        }
    }
}
