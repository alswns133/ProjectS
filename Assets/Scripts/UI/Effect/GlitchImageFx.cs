using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// UI 이미지에 상시 지지직거리는 글리치를 입힌다(<c>ProjectS/UI Glitch Image</c> 셰이더 구동).
    /// 보스 등장 경고의 위험 표시처럼 "신호가 불안한 화면"을 표현할 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="GlitchTextFx"/>와 역할이 다르다.</b> 그쪽은 글리치를 1에서 0으로 떨어뜨려
    /// 부서진 글자가 <b>한 번</b> 모이는 연출이고, 이쪽은 낮은 값에서 계속 흔들리다가
    /// 이따금 튀는 <b>지속</b> 상태다. 경고 표시는 도달점이 아니라 계속되는 이상 신호이기 때문이다.
    /// </para>
    /// <para>
    /// <b>머티리얼은 반드시 인스턴스를 뜬다.</b> 셰이더 프로퍼티는 머티리얼 전역이라 원본 에셋을 직접 만지면
    /// 같은 머티리얼을 쓰는 다른 UI까지 물들고, 에디터에서는 에셋이 영구히 변한다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 연출 중 timeScale이 낮아져도 지지직은 같은 속도로 떨려야 한다.</para>
    /// </remarks>
    [RequireComponent(typeof(Graphic))]
    public class GlitchImageFx : MonoBehaviour
    {
        [Header("평상시")]
        [Tooltip("상시 유지할 글리치 세기. 0이면 원본 그대로 보인다.")]
        [SerializeField, Range(0f, 1f)] private float idleGlitch = 0.14f;

        [Header("튀는 순간")]
        [Tooltip("이따금 튈 때의 글리치 세기.")]
        [SerializeField, Range(0f, 1f)] private float spikeGlitch = 0.7f;

        [Tooltip("한 번 튀어 있는 시간(초).")]
        [SerializeField, Min(0f)] private float spikeDuration = 0.07f;

        [Tooltip("튀는 간격의 최소·최대(초). 둘을 같게 두면 규칙적으로 튄다.")]
        [SerializeField, Min(0f)] private float spikeIntervalMin = 0.4f;

        [SerializeField, Min(0f)] private float spikeIntervalMax = 1.3f;

        private Graphic graphic;
        private Material instanced;

        private float nextSpike;
        private float spikeRemain;

        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");

        private void Awake()
        {
            graphic = GetComponent<Graphic>();

            if (graphic != null && graphic.material != null)
            {
                instanced = new Material(graphic.material);
                graphic.material = instanced;
            }

            if (instanced == null || !instanced.HasProperty(GlitchId))
            {
                Debug.LogWarning($"{name}: 머티리얼에 _Glitch가 없습니다. " +
                                 "'ProjectS/UI Glitch Image' 셰이더로 만든 머티리얼을 물려주세요.", this);
                enabled = false;
                return;
            }

            Apply(idleGlitch);
            ScheduleNextSpike();
        }

        private void OnEnable()
        {
            // 꺼졌다 켜질 때(깜박임) 매번 같은 리듬으로 시작하지 않게 다시 뽑는다.
            ScheduleNextSpike();
        }

        private void OnDestroy()
        {
            // Awake에서 뜬 인스턴스 머티리얼은 이 오브젝트만 쓰므로 함께 정리한다(누수 방지).
            if (instanced != null) Destroy(instanced);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (spikeRemain > 0f)
            {
                spikeRemain -= dt;
                if (spikeRemain <= 0f) Apply(idleGlitch);
                return;
            }

            nextSpike -= dt;
            if (nextSpike > 0f) return;

            Pulse();
        }

        /// <summary>지금 즉시 한 번 크게 튀게 한다. 깜박임처럼 밖에서 박자를 줄 때 호출한다.</summary>
        public void Pulse()
        {
            spikeRemain = spikeDuration;
            Apply(spikeGlitch);
            ScheduleNextSpike();
        }

        private void ScheduleNextSpike()
        {
            float max = Mathf.Max(spikeIntervalMin, spikeIntervalMax);
            nextSpike = Random.Range(spikeIntervalMin, max);
        }

        private void Apply(float value)
        {
            if (instanced != null) instanced.SetFloat(GlitchId, value);
        }
    }
}
