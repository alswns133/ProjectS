using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화창 코어 슬롯의 게이지 둘레를 도는 링 데코(GaugeCycle) 회전기.
    /// 평소엔 느리게 계속 돌고, 강화를 지르면 <b>멈칫 → 10% 커짐 → 가속 → 최고 속도 유지 → 감속 복귀</b>
    /// 순서로 연출을 받는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>프로그래머 참고 — 이건 단순 장식이 아니라 강화 연출의 일부다.</b>
    /// 판정·차감은 연출 전에 이미 끝나 있어서(<see cref="EnhancePresenter"/> 참고) 연출 구간은
    /// 화면상 아무 수치도 변하지 않는 대기 시간이다. 이 링의 가속이 그 구간에서 "지금 굴리는 중"을
    /// 보여주는 지속 신호라, 빼면 연출 시간이 통째로 정지 화면이 된다.
    /// 성공/실패와 무관하게 같은 연출을 돌린다(결과 표현은 FX_Overlay 몫).
    /// </para>
    /// <para>
    /// <b>시작·종료 시점은 이 스크립트가 정하지 않는다.</b> 회전만 이쪽 책임이고, 언제 시작하고
    /// 언제 끝나는지는 강화 흐름이 알려준다 — <see cref="EnhancePopup.OnResultPlayStarted"/> /
    /// <see cref="EnhancePopup.OnResultPlayFinished"/>에 붙어
    /// <see cref="BeginEnhance"/> / <see cref="EndEnhance"/>가 불린다. 그래서 연출 길이를 바꿔도 이 스크립트는
    /// 손댈 필요가 없고, 반대로 <b>강화 연출을 다른 경로(컷신·튜토리얼 등)에서 재생한다면 그쪽도
    /// 이 두 메서드를 같이 불러야 한다</b>(안 부르면 링이 평소 속도로만 돈다).
    /// </para>
    /// <para>
    /// <b>시간은 unscaled로 센다.</b> 강화창은 마을에서 열리고 마을은 timeScale이 0으로 떨어질 수 있다
    /// (<see cref="EnhancePopup.PlayResult"/>가 <c>WaitForSecondsRealtime</c>을 쓰는 것과 같은 이유).
    /// scaled로 세면 창은 떠 있는데 링만 얼어붙는다.
    /// </para>
    /// <para>
    /// <b>회전은 각도를 누적해 매 프레임 통째로 다시 세운다.</b> <c>Rotate()</c>로 곱해 나가면 오차가 쌓여
    /// 오래 열어둔 창에서 링이 미세하게 틀어진다. 각도 하나만 들고 있으면 몇 분을 돌려도 어긋나지 않는다.
    /// </para>
    /// (2026-08-20 TH)
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class EnhanceGaugeCycleSpin : MonoBehaviour
    {
        // 연출 단계. 시작/종료 신호를 받는 것 말고는 전부 이 안에서 시간으로 흘러간다.
        private enum Phase
        {
            Idle,       // 평상시 저속 회전 (강화창을 열어둔 동안)
            Pause,      // 강화 직후 멈칫 — 뒤이은 가속을 크게 보이게 하는 준비 동작
            SpinUp,     // 0 → 최고 속도 가속
            Peak,       // 최고 속도 유지 (연출이 끝날 때까지. 길이는 강화 흐름이 정한다)
            SpinDown    // 최고 속도 → 평소 속도 감속 + 스케일 원복
        }

        [Header("참조")]
        [Tooltip("연출 시작/종료를 알려줄 강화창. 비워두면 부모에서 자동으로 찾는다.")]
        [SerializeField] private EnhancePopup popup;

        [Header("속도 (초당 각도)")]
        [Tooltip("평소 회전 속도. 강화창을 열어둔 동안 계속 이 속도로 돈다.")]
        [SerializeField] private float idleSpeed = 25f;

        [Tooltip("강화 연출 중 최고 회전 속도. 평소 속도와 차이가 클수록 연출이 세게 읽힌다.")]
        [SerializeField] private float maxSpeed = 720f;

        [Tooltip("체크하면 시계 방향으로 돈다.")]
        [SerializeField] private bool clockwise = true;

        [Header("연출 타이밍 (초)")]
        [Tooltip("강화 시작 직후 멈춰 있는 시간. 0으로 두면 멈칫 없이 바로 가속한다.")]
        [SerializeField] private float pauseDuration = 0.15f;

        [Tooltip("멈칫이 끝나고 최고 속도에 도달하기까지 걸리는 시간.")]
        [SerializeField] private float spinUpDuration = 0.8f;

        [Tooltip("연출이 끝나고 평소 속도로 돌아오기까지 걸리는 시간.")]
        [SerializeField] private float spinDownDuration = 0.7f;

        [Header("스케일")]
        [Tooltip("강화 연출 중 커지는 배율. 1.1이면 10% 커진다.")]
        [SerializeField] private float boostScale = 1.1f;

        [Tooltip("스케일이 목표까지 따라붙는 데 걸리는 대략의 시간. 작을수록 탁 튀어나온다.")]
        [SerializeField] private float scaleSmoothTime = 0.12f;

        private RectTransform self;
        private Vector3 baseScale;      // 인스펙터에서 맞춰둔 원래 크기. 배율은 항상 여기에 곱한다.

        private Phase phase = Phase.Idle;
        private float phaseTime;
        private float currentSpeed;
        private float spinDownFrom;     // 감속 시작 순간의 속도. 최고 속도에 닿기 전에 끝나도 튀지 않게 붙잡아 둔다.
        private float angle;
        private float scaleFactor = 1f;
        private float scaleVelocity;

        /// <summary>강화 연출 구간을 도는 중인지(평상시 회전이 아닌지). 다른 연출과 타이밍을 맞출 때 쓴다.</summary>
        public bool IsBoosting => phase != Phase.Idle;

        private void Awake()
        {
            self = (RectTransform)transform;
            baseScale = self.localScale;

            // 링은 강화창 안에 들어 있으므로 부모에서 찾아 배선한다(인스펙터 연결을 깜빡해도 동작하게).
            if (popup == null) popup = GetComponentInParent<EnhancePopup>(true);
        }

        private void OnEnable()
        {
            // 연출 도중에 창이 닫히면 종료 신호를 못 받고 최고 속도인 채로 비활성화된다.
            // 그 상태가 이어지면 다음에 열 때 아무 일도 안 했는데 링이 미친 듯이 도는 창이 뜬다.
            ResetToIdle();

            if (popup != null)
            {
                popup.OnResultPlayStarted += BeginEnhance;
                popup.OnResultPlayFinished += EndEnhance;
            }
            else
            {
                Debug.LogWarning($"{name}: 강화창(EnhancePopup)을 찾지 못했습니다. 평상시 회전만 돌고 강화 연출에는 반응하지 않습니다.", this);
            }
        }

        private void OnDisable()
        {
            if (popup != null)
            {
                popup.OnResultPlayStarted -= BeginEnhance;
                popup.OnResultPlayFinished -= EndEnhance;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            phaseTime += dt;

            AdvancePhase();

            // 각도 누적. 360도로 접어 두면 창을 오래 열어둬도 float 정밀도가 떨어지지 않는다.
            angle += currentSpeed * (clockwise ? -1f : 1f) * dt;
            angle = Mathf.Repeat(angle, 360f);
            self.localRotation = Quaternion.Euler(0f, 0f, angle);

            // 스케일은 단계 전환과 따로 부드럽게 따라간다. 감속 중에 원래 크기로 돌아오는 리듬이
            // 속도 곡선과 살짝 어긋나야 힘이 "풀리는" 느낌이 난다.
            float targetScale = (phase == Phase.SpinUp || phase == Phase.Peak) ? boostScale : 1f;
            scaleFactor = Mathf.SmoothDamp(scaleFactor, targetScale, ref scaleVelocity, Mathf.Max(scaleSmoothTime, 0.0001f), Mathf.Infinity, dt);
            self.localScale = baseScale * scaleFactor;
        }

        /// <summary>
        /// 강화 연출 시작. 멈칫 → 확대 → 가속으로 들어간다.
        /// 강화창의 연출 시작 이벤트가 호출하며, 다른 경로에서 강화 연출을 재생한다면 직접 불러도 된다.
        /// </summary>
        public void BeginEnhance()
        {
            phase = Phase.Pause;
            phaseTime = 0f;
            currentSpeed = 0f;
        }

        /// <summary>
        /// 강화 연출 종료. 평소 속도로 감속하고 크기를 원래대로 되돌린다.
        /// 가속 도중에 불려도 그 시점 속도에서 이어서 줄어든다(연출이 짧게 끝나도 끊겨 보이지 않는다).
        /// </summary>
        public void EndEnhance()
        {
            if (phase == Phase.Idle) return;

            phase = Phase.SpinDown;
            phaseTime = 0f;
            spinDownFrom = currentSpeed;
        }

        // 단계별 목표 속도 계산과 다음 단계 전환. 가감속은 SmoothStep으로 양 끝을 눕혀
        // 속도가 각지지 않게 한다(선형이면 최고 속도에 닿는 순간이 툭 하고 티가 난다).
        private void AdvancePhase()
        {
            switch (phase)
            {
                case Phase.Idle:
                    currentSpeed = idleSpeed;
                    break;

                case Phase.Pause:
                    currentSpeed = 0f;
                    if (phaseTime >= pauseDuration)
                    {
                        phase = Phase.SpinUp;
                        phaseTime = 0f;
                    }
                    break;

                case Phase.SpinUp:
                    {
                        float t = spinUpDuration > 0f ? Mathf.Clamp01(phaseTime / spinUpDuration) : 1f;
                        currentSpeed = Mathf.SmoothStep(0f, maxSpeed, t);
                        if (t >= 1f) phase = Phase.Peak;
                        break;
                    }

                case Phase.Peak:
                    // 여기서 스스로 빠져나가지 않는다. 연출이 얼마나 이어질지는 강화 흐름만 안다.
                    currentSpeed = maxSpeed;
                    break;

                case Phase.SpinDown:
                    {
                        float t = spinDownDuration > 0f ? Mathf.Clamp01(phaseTime / spinDownDuration) : 1f;
                        currentSpeed = Mathf.SmoothStep(spinDownFrom, idleSpeed, t);
                        if (t >= 1f)
                        {
                            phase = Phase.Idle;
                            phaseTime = 0f;
                        }
                        break;
                    }
            }
        }

        private void ResetToIdle()
        {
            phase = Phase.Idle;
            phaseTime = 0f;
            currentSpeed = idleSpeed;
            scaleFactor = 1f;
            scaleVelocity = 0f;
            self.localScale = baseScale;
        }
    }
}
