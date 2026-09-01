using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 레이드 아레나를 덮는 에너지 방벽(오버레이 구체)에 생동감을 주는 연출 컴포넌트.
    /// 밝기 맥동과 UV 스크롤을 함께 돌려 "정지된 막"이 아니라 "흐르는 에너지"로 보이게 한다.
    /// 스크롤은 세로(상승)가 주역이고 가로는 보조다. 돔이 UV 스피어라 가로 스크롤은
    /// 극점으로 갈수록 실제 이동 거리가 0에 수렴하는 반면, 세로는 위도와 무관하게 균일하기 때문이다.
    /// 머티리얼을 복제하지 않고 MaterialPropertyBlock으로만 값을 덮어쓰므로,
    /// 같은 머티리얼을 쓰는 다른 오브젝트에 영향을 주지 않고 런타임 머티리얼 누수도 없다.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class EnergyDomeEffect : MonoBehaviour
    {
        [Header("맥동")]
        [Tooltip("한 번 밝아졌다 어두워지는 데 걸리는 시간(초).")]
        [SerializeField] private float pulsePeriod = 6f;

        [Tooltip("기준 밝기 대비 위아래로 흔들리는 비율. 0.25면 ±25%.")]
        [SerializeField, Range(0f, 1f)] private float pulseAmount = 0.25f;

        [Header("흐름 - 가로")]
        [Tooltip("첫 번째 겹의 가로 UV 스크롤 속도. 초당 UV 단위. " +
                 "UV 스피어라 극점으로 갈수록 실제 이동 거리가 0에 수렴하므로, " +
                 "가로는 무늬가 정수리로 뭉치지 않게 비틀어주는 보조 역할만 한다.")]
        [SerializeField] private float scrollSpeed = 0.004f;

        [Tooltip("두 번째 겹의 스크롤 속도. 첫 번째와 다르게 잡아야 시차가 생겨 흐르는 느낌이 난다. " +
                 "부호를 반대로 주면 두 겹이 엇갈려 지나가면서 방전처럼 보인다.")]
        [SerializeField] private float detailScrollSpeed = -0.011f;

        [Header("흐름 - 세로(에너지 상승)")]
        [Tooltip("첫 번째 겹이 위로 오르는 속도. 초당 UV 단위이고, 양수가 위쪽이다. " +
                 "위도와 무관하게 속도가 균일해 상승감은 이쪽이 담당한다. " +
                 "돔 반지름 55 기준 0.06이면 무늬가 초당 1유닛쯤 올라간다.")]
        [SerializeField] private float riseSpeed = 0.06f;

        [Tooltip("두 번째 겹에 '추가로' 얹히는 상승 속도. 두 번째 겹은 첫 번째 겹의 오프셋 위에 " +
                 "쌓이므로 실제 속도는 riseSpeed + 이 값이다. 0이면 두 겹이 한 덩어리로 " +
                 "움직여 깊이감이 사라지므로 반드시 0이 아닌 값을 준다.")]
        [SerializeField] private float detailRiseSpeed = 0.12f;

        [Header("불규칙 서지")]
        [Tooltip("가끔 순간적으로 밝아지는 연출을 켠다. 고장 난 방벽 느낌을 준다.")]
        [SerializeField] private bool useSurge = true;

        [Tooltip("서지가 일어나는 평균 간격(초).")]
        [SerializeField] private float surgeInterval = 9f;

        [Tooltip("서지 시 기준 밝기에 곱해지는 최대 배율.")]
        [SerializeField, Range(1f, 3f)] private float surgeStrength = 1.6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int DetailOffsetId = Shader.PropertyToID("_DetailOffset");
        private static readonly int DetailOffsetYId = Shader.PropertyToID("_DetailOffsetY");

        private Renderer domeRenderer;
        private MaterialPropertyBlock block;

        private Color baseColor;
        private Vector4 baseMapSt;
        private float baseDetailOffset;
        private float baseDetailOffsetY;

        private float scrollOffset;
        private float detailOffset;
        private float riseOffset;
        private float detailRiseOffset;
        private float surgeTimer;
        private float surgeValue;

        private void Awake()
        {
            domeRenderer = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();

            // 인스펙터에서 맞춰둔 색과 타일링을 기준값으로 삼는다.
            // sharedMaterial을 읽어야 머티리얼 인스턴스가 생기지 않는다.
            Material source = domeRenderer.sharedMaterial;
            baseColor = source.HasProperty(BaseColorId) ? source.GetColor(BaseColorId) : Color.white;
            baseMapSt = source.HasProperty(BaseMapStId) ? source.GetVector(BaseMapStId) : new Vector4(1f, 1f, 0f, 0f);
            baseDetailOffset = source.HasProperty(DetailOffsetId) ? source.GetFloat(DetailOffsetId) : 0f;
            baseDetailOffsetY = source.HasProperty(DetailOffsetYId) ? source.GetFloat(DetailOffsetYId) : 0f;

            ScheduleNextSurge();
        }

        private void Update()
        {
            float intensity = baseColor.a;

            if (pulsePeriod > 0.01f && pulseAmount > 0f)
            {
                float phase = Time.time / pulsePeriod * Mathf.PI * 2f;
                intensity *= 1f + Mathf.Sin(phase) * pulseAmount;
            }

            if (useSurge)
            {
                UpdateSurge();
                intensity *= surgeValue;
            }

            // Additive 블렌딩에서는 알파가 곧 세기이므로 알파만 흔든다.
            Color tinted = baseColor;
            tinted.a = Mathf.Clamp01(intensity);

            scrollOffset = Mathf.Repeat(scrollOffset + scrollSpeed * Time.deltaTime, 1f);
            detailOffset = Mathf.Repeat(detailOffset + detailScrollSpeed * Time.deltaTime, 1f);

            // UV 오프셋을 키우면 같은 정점이 텍스처의 더 위쪽을 샘플링하므로 무늬는 아래로 내려간다.
            // 인스펙터에서 양수가 "위로"로 읽히도록 여기서 부호를 뒤집는다.
            riseOffset = Mathf.Repeat(riseOffset - riseSpeed * Time.deltaTime, 1f);
            detailRiseOffset = Mathf.Repeat(detailRiseOffset - detailRiseSpeed * Time.deltaTime, 1f);

            Vector4 st = baseMapSt;
            st.z = baseMapSt.z + scrollOffset;
            st.w = baseMapSt.w + riseOffset;

            domeRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tinted);
            block.SetVector(BaseMapStId, st);
            block.SetFloat(DetailOffsetId, baseDetailOffset + detailOffset);
            block.SetFloat(DetailOffsetYId, baseDetailOffsetY + detailRiseOffset);
            domeRenderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 서지를 짧게 올렸다 빠르게 되돌린다. 간격을 매번 무작위로 잡아 규칙적으로 보이지 않게 한다.
        /// </summary>
        private void UpdateSurge()
        {
            surgeTimer -= Time.deltaTime;

            if (surgeTimer <= 0f)
            {
                surgeValue = Random.Range(1f, surgeStrength);
                ScheduleNextSurge();
            }
            else
            {
                // 튀어오른 값이 원래대로 잦아드는 구간.
                surgeValue = Mathf.Lerp(surgeValue, 1f, Time.deltaTime * 4f);
            }
        }

        private void ScheduleNextSurge()
        {
            surgeTimer = surgeInterval * Random.Range(0.5f, 1.5f);
        }

        private void OnDisable()
        {
            // 꺼질 때 기준값으로 되돌려, 씬을 저장하거나 다시 켤 때 값이 튀지 않게 한다.
            if (domeRenderer == null)
            {
                return;
            }

            domeRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetVector(BaseMapStId, baseMapSt);
            block.SetFloat(DetailOffsetId, baseDetailOffset);
            block.SetFloat(DetailOffsetYId, baseDetailOffsetY);
            domeRenderer.SetPropertyBlock(block);
        }
    }
}
