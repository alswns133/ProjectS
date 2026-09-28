using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 육각 타일 배경(<c>ProjectS/UI Hex Wave</c> 셰이더)을 모는 컴포넌트. 일정 간격으로 중심(게이지)에서 웨이브를 쏘고,
    /// 로그인 게이지가 맞물리는 순간 큰 웨이브를 한 번 더 쏜다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>셰이더가 그리드를 다 그리고, 이 스크립트는 값만 넣는다.</b> 매 프레임 넣는 값은 현재 시각·원점·화면 크기·웨이브 슬롯뿐이라
    /// 캔버스 리빌드가 일어나지 않는다(머티리얼 프로퍼티만 바뀜).
    /// </para>
    /// <para>
    /// <b>시각을 직접 넣는 이유</b>: 셰이더 내장 <c>_Time</c>은 timeScale의 영향을 받는다. 로그인 화면은 로딩·일시정지와
    /// 무관하게 돌아야 해서 <see cref="Time.unscaledTime"/>을 <c>_Now</c>로 넘긴다. 웨이브 시작 시각도 같은 시계로 찍는다.
    /// </para>
    /// <para>
    /// <b>원점은 게이지 링의 중심</b>을 매 프레임 따라간다. 진입 연출에서 게이지가 화면 가운데로 이동해도 웨이브가 함께 옮겨 간다.
    /// 게이지 루트의 피벗은 연출 중 바뀌므로(위쪽→가운데) 피벗이 아니라 사각형 중심을 쓴다.
    /// </para>
    /// 배치·배선은 <c>Tools ▸ ProjectS ▸ Style Login Screen</c>이 한다.
    /// </remarks>
    [RequireComponent(typeof(Graphic))]
    public class HexWaveBackground : MonoBehaviour
    {
        [Header("원점")]
        [Tooltip("웨이브가 퍼져 나가는 중심. 이 사각형의 중심을 쓴다(로그인 게이지 루트).")]
        [SerializeField] private RectTransform waveCenter;

        [Tooltip("중심 주변을 비우는 반지름 = 원점 사각형 반지름 × 이 배율. 게이지 뒤로 타일이 비치지 않게 한다. 0이면 비우지 않는다.")]
        [SerializeField, Min(0f)] private float clearRadiusScale = 1.05f;

        [Header("주기 웨이브")]
        [Tooltip("자동으로 웨이브를 쏘는 간격(초). 0이면 자동 발사 안 함.")]
        [SerializeField, Min(0f)] private float interval = 4f;

        [Tooltip("켜진 뒤 첫 웨이브까지의 시간(초).")]
        [SerializeField, Min(0f)] private float firstDelay = 0.8f;

        [Tooltip("퍼지는 속도(캔버스 px/초).")]
        [SerializeField, Min(1f)] private float waveSpeed = 700f;

        [Tooltip("파도 띠의 두께(px). 클수록 여러 줄의 타일이 함께 반응한다.")]
        [SerializeField, Min(1f)] private float waveWidth = 80f;

        [SerializeField, Min(0f)] private float waveStrength = 1f;

        [Header("결합 웨이브")]
        [Tooltip("게이지가 맞물리는 순간 쏠 웨이브의 출처(선택). 비우면 결합 웨이브 없음.")]
        [SerializeField] private LoginGaugeView gauge;

        [SerializeField, Min(1f)] private float impactSpeed = 1500f;
        [SerializeField, Min(1f)] private float impactWidth = 140f;
        [SerializeField, Min(0f)] private float impactStrength = 1.6f;

        [Tooltip("로그인 순간 이미 퍼지고 있던 웨이브가 사라지는 시간(초). " +
                 "끊지 않으면 막 쏜 웨이브가 화면 끝까지 퍼지며 게이지 연출과 겹친다.")]
        [SerializeField, Min(0f)] private float holdFadeDuration = 0.25f;

        private const int SlotCount = 4;

        private static readonly int NowId = Shader.PropertyToID("_Now");
        private static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        private static readonly int OriginId = Shader.PropertyToID("_WaveOrigin");
        private static readonly int ClearRadiusId = Shader.PropertyToID("_ClearRadius");
        private static readonly int GlitchKickTimeId = Shader.PropertyToID("_GlitchKickTime");
        private static readonly int[] WaveIds =
        {
            Shader.PropertyToID("_Wave0"),
            Shader.PropertyToID("_Wave1"),
            Shader.PropertyToID("_Wave2"),
            Shader.PropertyToID("_Wave3"),
        };

        private Graphic graphic;
        private RectTransform rect;
        private Material instance;
        private int nextSlot;
        private float nextWaveTime;

        // 셰이더에 넣은 웨이브 슬롯의 사본. 보류 시작 때 진행 중인 웨이브의 세기를 줄이려면 원래 값을 알아야 한다.
        private readonly Vector4[] slots = new Vector4[SlotCount];
        private readonly bool[] fadingSlots = new bool[SlotCount];
        private bool isHolding;
        private float holdStartTime;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            rect = (RectTransform)transform;

            // 공유 머티리얼 에셋을 매 프레임 고치면 에디터에서 에셋 파일이 계속 더러워지고,
            // 같은 머티리얼을 쓰는 다른 화면과 값이 섞인다. 인스턴스를 만들어 이 이미지만 쓴다.
            if (graphic.material != null && graphic.material != graphic.defaultMaterial)
            {
                instance = new Material(graphic.material) { name = graphic.material.name + " (Instance)" };
                graphic.material = instance;
            }
        }

        private void OnEnable()
        {
            ClearWaves();
            nextWaveTime = Time.unscaledTime + firstDelay;
            if (gauge != null) gauge.Engaged += OnGaugeEngaged;
        }

        private void OnDisable()
        {
            if (gauge != null) gauge.Engaged -= OnGaugeEngaged;
        }

        private void OnDestroy()
        {
            if (instance != null) Destroy(instance);
        }

        private void Update()
        {
            if (instance == null) return;

            float now = Time.unscaledTime;

            // 로그인 버튼을 누른 순간부터 조각이 맞물리기 전까지는 자동 웨이브를 쉬고, 이미 퍼지던 웨이브는 걷는다.
            // 결합 순간 웨이브(OnGaugeEngaged)가 조용한 배경 위에서 터져야 "쾅"이 또렷하다.
            bool hold = gauge != null && gauge.IsUnlocking;
            if (hold && !isHolding) BeginHold(now);
            isHolding = hold;

            if (isHolding)
            {
                // 보류가 풀린 직후(실패로 대기 복귀) 곧바로 쏘지 않도록 다음 발사를 계속 뒤로 민다.
                nextWaveTime = now + firstDelay;
            }
            else if (interval > 0f && now >= nextWaveTime)
            {
                FireWave(waveSpeed, waveWidth, waveStrength);
                nextWaveTime = now + interval;
            }

            FadeHeldWaves(now);

            Vector2 size = rect.rect.size;
            instance.SetFloat(NowId, now);
            instance.SetVector(RectSizeId, size);
            UpdateOrigin(size);
        }

        /// <summary>
        /// 지금 시각에 원점에서 웨이브를 하나 쏜다. 슬롯 4개를 돌려 쓰므로 겹쳐 쏘면 가장 오래된 웨이브가 밀려난다.
        /// </summary>
        /// <param name="speed">퍼지는 속도(px/초)</param>
        /// <param name="width">파도 띠 두께(px)</param>
        /// <param name="strength">세기(1 = 기본). 1보다 크면 타일이 더 크게 튄다.</param>
        public void FireWave(float speed, float width, float strength)
        {
            if (instance == null) return;

            SetSlot(nextSlot, new Vector4(Time.unscaledTime, speed, width, strength));
            fadingSlots[nextSlot] = false; // 새로 쏜 웨이브(결합 웨이브 등)는 보류 페이드 대상이 아니다.
            nextSlot = (nextSlot + 1) % SlotCount;
        }

        // 보류 시작: 지금 살아 있는 웨이브들을 페이드 대상으로 표시한다.
        private void BeginHold(float now)
        {
            holdStartTime = now;
            for (int i = 0; i < SlotCount; i++)
                fadingSlots[i] = slots[i].w > 0f;
        }

        // 표시된 웨이브의 세기를 holdFadeDuration 동안 0으로 줄인다. 셰이더엔 줄인 값만 넣고 원래 세기(slots)는 보존한다.
        private void FadeHeldWaves(float now)
        {
            float t = holdFadeDuration > 0f ? Mathf.Clamp01((now - holdStartTime) / holdFadeDuration) : 1f;

            for (int i = 0; i < SlotCount; i++)
            {
                if (!fadingSlots[i]) continue;

                Vector4 wave = slots[i];
                wave.w *= 1f - t;
                instance.SetVector(WaveIds[i], wave);

                if (t >= 1f)
                {
                    fadingSlots[i] = false;
                    SetSlot(i, new Vector4(0f, 0f, 1f, 0f));
                }
            }
        }

        private void SetSlot(int index, Vector4 wave)
        {
            slots[index] = wave;
            instance.SetVector(WaveIds[index], wave);
        }

        /// <summary>
        /// 홀로그램 글리치를 지금부터 강제로 터뜨린다(셰이더의 Kick Glitch Duration 동안 여러 띠가 크게 튄다).
        /// 결합 충격에 투사가 흔들리는 연출로 쓰며, 다른 충격 연출에서도 부를 수 있다.
        /// </summary>
        public void KickGlitch()
        {
            if (instance != null) instance.SetFloat(GlitchKickTimeId, Time.unscaledTime);
        }

        private void OnGaugeEngaged()
        {
            FireWave(impactSpeed, impactWidth, impactStrength);
            KickGlitch();
            // 결합 직후 주기 웨이브가 곧바로 겹치지 않게 다음 발사를 한 간격 뒤로 민다.
            nextWaveTime = Time.unscaledTime + interval;
        }

        // 원점·비울 반지름을 이 사각형의 왼쪽 아래 기준 px로 바꿔 넣는다(셰이더의 UV × 크기와 같은 좌표계).
        private void UpdateOrigin(Vector2 size)
        {
            if (waveCenter == null)
            {
                instance.SetVector(OriginId, size * 0.5f);
                instance.SetFloat(ClearRadiusId, 0f);
                return;
            }

            Vector3 centerWorld = waveCenter.TransformPoint(waveCenter.rect.center);
            Vector2 local = (Vector2)rect.InverseTransformPoint(centerWorld) - rect.rect.min;
            instance.SetVector(OriginId, local);

            // 게이지가 커지면(진입 연출) 비우는 원도 함께 커진다 — 가장자리 점을 같은 공간으로 옮겨 재기 때문.
            Vector3 edgeWorld = waveCenter.TransformPoint(waveCenter.rect.center + new Vector2(waveCenter.rect.width * 0.5f, 0f));
            float radius = Vector2.Distance(rect.InverseTransformPoint(centerWorld), rect.InverseTransformPoint(edgeWorld));
            instance.SetFloat(ClearRadiusId, radius * clearRadiusScale);
        }

        private void ClearWaves()
        {
            if (instance == null) return;

            for (int i = 0; i < SlotCount; i++)
            {
                SetSlot(i, new Vector4(0f, 0f, 1f, 0f));
                fadingSlots[i] = false;
            }
            nextSlot = 0;
            isHolding = false;
        }
    }
}
