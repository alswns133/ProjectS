using System.Collections;
using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 시간 배속(<see cref="Time.timeScale"/>)을 잠깐 늦췄다가 서서히 되돌리는 슬로우모션 연출.
    /// 보스가 마지막 타격을 받고 죽는 순간처럼 "한 방을 강조하는" 연출에 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 흐름은 <b>램프다운 → (유지) → 램프업</b>이다. 처음엔 <see cref="slowScale"/>까지 천천히 느려지고,
    /// 잠깐 머문 뒤(<see cref="holdDuration"/>) 다시 1로 서서히 돌아온다. 각 구간 길이는 인스펙터에서 조절한다.
    /// </para>
    /// <para>
    /// <b>반드시 unscaled 시간으로 굴린다.</b> 코루틴이 <see cref="Time.unscaledDeltaTime"/>으로 진행해야
    /// timeScale이 낮아진 동안에도 연출 자체는 실제 시간대로 흘러 정상 속도로 원복한다(스케일된 시간으로 재면
    /// 느려질수록 원복도 느려져 영영 안 돌아온다). 물리도 함께 느려지도록 fixedDeltaTime을 배속에 비례시킨다.
    /// </para>
    /// <para>
    /// 씬 단위 싱글턴이다. 씬을 떠날 때 timeScale이 낮은 채로 남지 않도록 <see cref="OnDisable"/>에서 원복한다.
    /// 기존 히트스톱(<c>EnemyCameraEffects</c>)도 timeScale을 건드리므로, 죽는 프레임에 둘이 겹치면 이 연출이
    /// 매 프레임 값을 덮어 우선한다(히트스톱은 짧아 곧 끝난다).
    /// </para>
    /// </remarks>
    public class SlowMotionController : MonoBehaviour
    {
        /// <summary>씬에 하나. 보스 등 호출부가 참조 없이 접근한다.</summary>
        public static SlowMotionController Instance { get; private set; }

        [Header("슬로우모션")]
        [Tooltip("가장 느려졌을 때의 배속(0~1). 낮을수록 더 느려진다.")]
        [SerializeField, Range(0f, 1f)] private float slowScale = 0.15f;

        [Tooltip("정상 속도에서 slowScale까지 느려지는 데 걸리는 시간(초, 실제 시간).")]
        [SerializeField, Min(0f)] private float rampDownDuration = 0.25f;

        [Tooltip("가장 느린 상태로 머무는 시간(초). 0이면 곧바로 원복을 시작한다.")]
        [SerializeField, Min(0f)] private float holdDuration = 0.15f;

        [Tooltip("slowScale에서 정상 속도(1)로 되돌아오는 데 걸리는 시간(초, 실제 시간).")]
        [SerializeField, Min(0f)] private float rampUpDuration = 0.9f;

        // 물리 스텝 원복용 기준값(Awake 시점의 프로젝트 설정값).
        private float baseFixedDeltaTime;

        // 현재 돌고 있는 연출. 재호출 시 갈아끼워 겹치지 않게 한다.
        private Coroutine routine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void OnDisable()
        {
            // 씬 이탈·비활성 중 연출이 진행 중이었다면 시간·물리를 정상으로 되돌린다(느린 채로 남는 사고 방지).
            if (routine != null) StopCoroutine(routine);
            routine = null;
            RestoreTime();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 슬로우모션을 처음부터 재생한다. 이미 재생 중이면 다시 처음부터 돈다.
        /// 보스 사망 등 강조하고 싶은 순간에 호출한다.
        /// </summary>
        public void Play()
        {
            if (!isActiveAndEnabled) return;

            if (routine != null) StopCoroutine(routine);
            routine = StartCoroutine(SlowRoutine());
        }

        private IEnumerator SlowRoutine()
        {
            // 램프다운: 현재 배속에서 slowScale로.
            yield return Ramp(Time.timeScale, slowScale, rampDownDuration);

            // 유지.
            if (holdDuration > 0f) yield return new WaitForSecondsRealtime(holdDuration);

            // 램프업: slowScale에서 정상 속도로.
            yield return Ramp(slowScale, 1f, rampUpDuration);

            RestoreTime();
            routine = null;
        }

        // from → to 로 duration(실제 시간)에 걸쳐 timeScale을 보간한다. duration이 0이면 즉시 적용.
        private IEnumerator Ramp(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                SetTimeScale(to);
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetTimeScale(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetTimeScale(to);
        }

        // timeScale과 함께 물리 스텝도 비례시켜, 느려지는 동안 물리 갱신이 튀지 않게 한다.
        private void SetTimeScale(float scale)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDeltaTime * scale;
        }

        private void RestoreTime()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDeltaTime;
        }
    }
}
