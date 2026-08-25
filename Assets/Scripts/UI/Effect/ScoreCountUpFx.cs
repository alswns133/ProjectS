using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 큰 수를 시작값에서 목표값까지 굴려 올리는 카운트업 연출. 던전 결과 화면의 플레이 점수처럼
    /// 자릿수가 많은 값에 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 <see cref="NumberReelFx"/>를 쓰지 않는가</b>: 릴은 목표 앞뒤의 <b>연속된 정수</b>를 세로로
    /// 세워 굴린다. 레벨(2 → 3)처럼 한두 자리에서는 슬롯머신처럼 보이지만, 413875 같은 값에서는
    /// 413861~413875가 지나갈 뿐이라 앞자리가 전혀 움직이지 않는다 — "점수가 쌓인다"는 느낌이 나오지 않는다.
    /// 표시값을 1~wrapMax로 감아 도는 처리도 큰 수에는 맞지 않는다. 그래서 릴 대신 값 자체를 보간한다.
    /// </para>
    /// <para>
    /// <b>자릿수 흔들림</b>: 비례폭 폰트는 숫자마다 폭이 달라, 값이 바뀔 때마다 글자가 미세하게 떤다.
    /// <c>monoSpacing</c>을 0보다 크게 주면 TMP의 mspace 태그로 고정폭을 씌워 떨림을 없앤다.
    /// 0으로 두면 폰트 원래 폭을 쓴다(자간을 손본 아트 폰트라면 그쪽이 나을 수 있다).
    /// </para>
    /// (2026-08-24 TH)
    /// </remarks>
    public class ScoreCountUpFx : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("숫자를 쓸 라벨. 비워두면 자기 자신 → 자식 순서로 찾는다.")]
        [SerializeField] private TMP_Text label;

        [Header("굴리기")]
        [Tooltip("시작값에서 목표값까지 가는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.05f)] private float duration = 1.2f;

        [Tooltip("굴러가는 속도 곡선. 끝이 완만할수록 목표에 부드럽게 안착한다.")]
        [SerializeField]
        private AnimationCurve curve = new(new Keyframe(0f, 0f, 0f, 2.6f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("어디서부터 세기 시작할지. 결과 화면은 0에서 시작한다.")]
        [SerializeField] private int startValue;

        [Header("표시")]
        [Tooltip("천 단위 콤마를 넣는다(413,875).")]
        [SerializeField] private bool useThousandSeparator = true;

        [Tooltip("글자 폭을 고정할 값(em). 0이면 폰트 원래 폭을 쓴다.")]
        [SerializeField, Min(0f)] private float monoSpacing = 0.62f;

        [Tooltip("시간을 unscaled로 센다. 결과 화면은 연출로 timeScale이 떨어진 중에도 뜬다.")]
        [SerializeField] private bool useUnscaledTime = true;

        private Coroutine routine;
        private int target;
        private int shown;
        private bool hasShown;

        /// <summary>연출이 끝났을 때(또는 <see cref="Skip"/>로 건너뛰었을 때) 발행한다. 다음 연출로 이어가는 용도.</summary>
        public event Action Finished;

        /// <summary>이번에 맞출 목표 숫자.</summary>
        public int Value => target;

        /// <summary>지금 굴러가는 중인지.</summary>
        public bool IsCounting => routine != null;

        private void Awake()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);

            if (label == null)
            {
                Debug.LogError($"{name}: 숫자를 쓸 TMP 라벨이 없습니다. 같은 오브젝트나 자식에 텍스트를 두세요.", this);
                enabled = false;
            }
        }

        // 비활성화되면 코루틴은 유니티가 죽인다. 참조만 남으면 IsCounting이 영원히 true가 되므로 함께 정리한다.
        private void OnDisable() => StopRoutine();

        /// <summary>
        /// 시작값에서 <paramref name="value"/>까지 굴린다. 이미 굴러가는 중이면 처음부터 다시 시작한다.
        /// </summary>
        /// <param name="value">최종적으로 보여줄 숫자</param>
        public void Play(int value)
        {
            target = value;

            // 꺼진 상태에서는 코루틴이 돌지 않는다. 값이 조용히 0으로 남지 않게 즉시 맞추고 끝난 것으로 친다.
            if (!isActiveAndEnabled)
            {
                Apply(value);
                Finished?.Invoke();
                return;
            }

            StopRoutine();
            routine = StartCoroutine(CountRoutine());
        }

        /// <summary>연출 없이 숫자만 맞춘다(되돌아오기·재바인딩용). <see cref="Finished"/>는 발행하지 않는다.</summary>
        /// <param name="value">보여줄 숫자</param>
        public void SetImmediate(int value)
        {
            StopRoutine();
            target = value;
            Apply(value);
        }

        /// <summary>굴러가는 중이면 즉시 목표로 끊고 <see cref="Finished"/>를 발행한다(플레이어가 연출을 건너뛸 때).</summary>
        public void Skip()
        {
            if (routine == null) return;

            StopRoutine();
            Apply(target);
            Finished?.Invoke();
        }

        private IEnumerator CountRoutine()
        {
            int from = startValue;
            float elapsed = 0f;

            Apply(from);

            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float k = curve.Evaluate(Mathf.Clamp01(elapsed / duration));
                Apply(Mathf.RoundToInt(Mathf.LerpUnclamped(from, target, k)));
                yield return null;
            }

            Apply(target);
            routine = null;
            Finished?.Invoke();
        }

        private void Apply(int value)
        {
            if (label == null) return;

            // 같은 값이면 TMP를 다시 만들지 않는다. 매 프레임 도는 연출이라 이 한 줄이 메시 재생성을 줄인다.
            if (hasShown && shown == value) return;
            shown = value;
            hasShown = true;

            string body = useThousandSeparator
                ? value.ToString("N0", CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);

            label.text = monoSpacing > 0f
                ? $"<mspace={monoSpacing.ToString("0.###", CultureInfo.InvariantCulture)}em>{body}"
                : body;
        }

        private void StopRoutine()
        {
            if (routine == null) return;

            StopCoroutine(routine);
            routine = null;
        }
    }
}
