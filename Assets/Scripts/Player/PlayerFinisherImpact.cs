using System.Collections;
using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 각성기 피니시: 화면을 순간 2톤 흑백으로 자르고 같은 프레임에 히트스톱을 건다.
    /// 둘을 한 Animation Event로 묶는 이유 — 한 프레임이라도 어긋나면 따로 노는 것처럼 보인다.
    /// </summary>
    /// <remarks>
    /// 반드시 unscaled 시간으로 굴린다. 히트스톱으로 timeScale이 0에 가까워지면 스케일된
    /// 시간은 거의 흐르지 않아 연출이 영영 안 끝난다. 같은 이유로 히트스톱 중에는 Animator도
    /// 멈춰 Animation Event가 오지 않으므로, 흑백 해제를 이벤트로 예약하지 않고 이 코루틴이
    /// 스스로 끝낸다.
    /// </remarks>
    public class PlayerFinisherImpact : MonoBehaviour
    {
        [Header("흑백")]
        [Tooltip("Renderer Feature에 물려 둔 MonoThreshold 머티리얼 에셋.")]
        [SerializeField] private Material monoMaterial;

        [Tooltip("흑백 유지 시간(초, 실제 시간). 레퍼런스 실측 0.18초.")]
        [SerializeField, Min(0f)] private float monoDuration = 0.18f;

        [Tooltip("진행도(0~1)에 따른 흑백 강도. 평평하게 두면 레퍼런스처럼 툭 켜지고 툭 꺼진다.")]
        [SerializeField] private AnimationCurve intensityOverTime = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("진행도에 따른 threshold. 카메라가 도는 구간에서 올려 화면을 어둡게 만든다(레퍼런스 f76 암전).")]
        [SerializeField] private AnimationCurve thresholdOverTime = AnimationCurve.Constant(0f, 1f, 0.30f);

        [Header("히트스톱")]
        [Tooltip("멈춰 있는 시간(초, 실제 시간). 흑백보다 먼저 끝나야 카메라가 제 속도로 돈다.")]
        [SerializeField, Min(0f)] private float hitStopDuration = 0.12f;

        [SerializeField, Range(0f, 1f)] private float hitStopTimeScale = 0.02f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");

        private Coroutine routine;

        /// <summary>Animation Event 진입점. 흑백과 히트스톱을 같은 프레임에 시작한다.</summary>
        [ContextMenu("연출 테스트")]
        public void OnFinisherImpact()
        {
            if (!Application.isPlaying) return;

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(PlayRoutine());
        }

        // 연출 도중 비활성화되면 timeScale이 낮은 채 갇히고 화면이 흑백으로 남는다.
        private void OnDisable()
        {
            if (routine != null)
            {
                Time.timeScale = 1f;
                routine = null;
            }

            ResetMaterial();
        }

        private IEnumerator PlayRoutine()
        {
            Time.timeScale = hitStopTimeScale;

            float elapsed = 0f;
            bool released = false;

            while (elapsed < monoDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / monoDuration);

                Apply(intensityOverTime.Evaluate(t), thresholdOverTime.Evaluate(t));

                // 흑백보다 히트스톱이 먼저 끝난다 — 시간이 돌아와야 카메라 오빗이 제 속도로 돈다.
                if (!released && elapsed >= hitStopDuration)
                {
                    Time.timeScale = 1f;
                    released = true;
                }

                yield return null;
            }

            if (!released) Time.timeScale = 1f;

            ResetMaterial();
            routine = null;
        }

        private void Apply(float intensity, float threshold)
        {
            if (monoMaterial == null) return;

            monoMaterial.SetFloat(IntensityId, intensity);
            monoMaterial.SetFloat(ThresholdId, threshold);
        }

        private void ResetMaterial()
        {
            if (monoMaterial != null) monoMaterial.SetFloat(IntensityId, 0f);
        }
    }
}
