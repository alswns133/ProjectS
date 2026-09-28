using System;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Enhance;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화창 원형 게이지(GaugeF)의 채움을 소유하고 연출로 움직인다.
    /// 평상시엔 비어 있고(0), 강화를 지르면 12시(=1.0)를 향해 쓸어올라간다.
    /// <b>성공하면 끝에 도달하고, 실패하면 닿을 듯하다 힘을 잃으며 0으로 되돌아온다.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 게이지는 성공률 "표시 위젯"이 아니라 연출축이다.</b> 예상 성공률은 뒤에 깔린 GaugeB가
    /// 따로 보여 준다(<see cref="EnhancePopup"/>가 직접 채운다, 2026-09-28 분리). 예전엔 이 게이지가 평상시
    /// 성공률 자리에 머물며 두 역할을 겸해, 강화 게이지와 예상 확률이 한 링에 섞여 보였다.
    /// 게이지 높이가 곧 담금질 열과 불똥 세기가 된다(<see cref="RadialFlowGaugeFx"/>가
    /// fillAmount를 미러링해 <c>_Heat</c>을 만든다). 그래서 열·불똥에 별도 배선이 없다 —
    /// 여기서 채움을 움직이면 나머지가 따라온다.
    /// </para>
    /// <para>
    /// <b>fillAmount의 주인은 이 컴포넌트 하나뿐이어야 한다.</b> 다른 곳에서 같이 세팅하면
    /// 연출 중 값이 튀거나 평상시 위치가 어긋난다. GaugeB(예상 확률)는 별도 Image라 이 규칙과 무관하다.
    /// </para>
    /// <para>
    /// <b>전체 길이가 <see cref="EnhancePopup.PlayResult"/>의 대기 시간(1.2초)을 넘으면 안 된다.</b>
    /// 넘으면 연출이 끝나기 전에 창이 다음 단계로 넘어가 뒷부분이 잘린다.
    /// 기본값은 실패 경로가 정확히 0.75 + 0.20 + 0.25 = 1.2초로 맞춰져 있다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 강화창은 마을에서 열리고 마을은 timeScale이 0일 수 있다.</para>
    /// (2026-08-21 TH)
    /// </remarks>
    [RequireComponent(typeof(Image))]
    public class EnhanceGaugeSweep : MonoBehaviour
    {
        private enum Phase
        {
            Idle,       // 평상시 — 비어 있음(0). 성공 직후엔 다음 대상 갱신 전까지 1.0에 머문다
            Rise,       // 출발 → 정점. 달아오르며 차오른다
            Reach,      // 성공: 정점 → 1.0 도달 후 유지
            Stall,      // 실패: 정점에서 멈칫 ("닿을 듯한" 구간)
            Fall        // 실패: 힘을 잃고 원래 자리로 후퇴
        }

        [Header("참조")]
        [Tooltip("결과와 성공률을 알려줄 강화창. 비워두면 부모에서 자동으로 찾는다.")]
        [SerializeField] private EnhancePopup popup;

        [Header("스윕 타이밍 (초)")]
        [Tooltip("출발 높이에서 정점까지 차오르는 시간.")]
        [SerializeField, Min(0.05f)] private float riseDuration = 0.75f;

        [Tooltip("성공 시 정점에서 1.0까지 밀어붙이는 시간.")]
        [SerializeField, Min(0.01f)] private float reachDuration = 0.15f;

        [Tooltip("실패 시 정점에서 멈칫하는 시간. 이 구간이 '닿을 듯하다'를 만든다.")]
        [SerializeField, Min(0f)] private float stallDuration = 0.2f;

        [Tooltip("실패 시 원래 자리로 되돌아가는 시간.")]
        [SerializeField, Min(0.01f)] private float fallDuration = 0.25f;

        [Header("높이")]
        [Tooltip("실패 시 게이지가 닿을 듯 올라가는 최고 높이. 1에 너무 가까우면 성공과 구별이 안 된다.")]
        [SerializeField, Range(0.5f, 1f)] private float peak = 0.97f;

        [Header("디버그")]
        [Tooltip("체크하면 강화 중인 상태를 붙잡아 둔다. 강화를 누르지 않고도 담금질·불똥을 볼 수 있다. " +
                 "체크를 풀면 평상시 자리로 돌아간다.")]
        [SerializeField] private bool debugEnhancing;

        [Tooltip("디버그 중 게이지 높이. 열은 이 높이에서 나오므로 " +
                 "RadialFlowGaugeFx의 heatStart보다 높여야 달아오르는 게 보인다.")]
        [SerializeField, Range(0f, 1f)] private float debugFill = 0.9f;

        private Image image;
        private RadialFlowGaugeFx fx;
        private Phase phase = Phase.Idle;
        private float timer;

        // 평상시 자리. 실패 후 여기로 되돌아온다. 성공률은 GaugeB 몫이라 이 게이지는 비워 둔다.
        private const float RestFill = 0f;
        // 이번 스윕이 출발한 높이.
        private float startFill;
        private bool success;


        // 한 프레임에 인정할 최대 시간(초). 에디터 멈칫·로딩 히치 뒤에는 unscaledDeltaTime이
        // 통째로 튀어(1초 이상) 들어오는데, 그대로 쓰면 연출이 재생되는 대신 한 프레임에
        // 전부 소모돼 건너뛴 것처럼 보인다. 히치 때는 느려질지언정 사라지지는 않게 잘라낸다.
        private const float MaxStep = 0.05f;

        // 체크박스가 방금 바뀌었는지 보기 위한 직전 값. 매 프레임 켜고 끄지 않으려는 것.
        private bool debugWasOn;

        /// <summary>
        /// 게이지가 끝(12시)에 정확히 닿는 순간. 성공했을 때만 온다.
        /// 도달 순간의 불똥 버스트가 여기에 붙는다(<see cref="GaugeHeatSparks"/>).
        /// </summary>
        /// <remarks>
        /// 인스펙터 참조가 아니라 이벤트로 낸 이유는, 불꽃 레이어가 마스크를 피해 다른 계층에 있어야 해서
        /// 참조를 손으로 이어야 하고 <b>그게 빠지면 아무 경고 없이 버스트만 사라지기</b> 때문이다.
        /// 구독하는 쪽은 이미 게이지를 알고 있으니 거기서 이 컴포넌트를 찾아 붙는다.
        /// </remarks>
        public event Action OnReachedEnd;

        private void Awake()
        {
            image = GetComponent<Image>();
            fx = GetComponent<RadialFlowGaugeFx>();

            // 천장(자비) 노랑 구간은 성공률을 이 게이지에 얹던 시절의 표시다. 성공률이 GaugeB로 옮겨 가
            // 여기선 쓰지 않으므로 전부 밑색으로 고정한다.
            if (fx != null) fx.SetBaseFill(1f);

            if (popup == null) popup = GetComponentInParent<EnhancePopup>();

            if (popup == null)
            {
                Debug.LogWarning($"{name}: EnhancePopup을 찾지 못했습니다. 게이지가 움직이지 않습니다.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (popup == null) return;

            popup.OnTargetChanged += HandleTargetChanged;
            popup.OnResultPlay += HandleResultPlay;
            popup.OnOpened += HandleOpened;

            // 연출 도중 창이 닫히면 OnResultPlayFinished가 오지 않는다.
            // 다시 열렸을 때 게이지가 정점에 얼어붙어 있지 않도록 평상시 자리로 되돌린다.
            SetPhase(Phase.Idle);
            Apply(RestFill);

            debugWasOn = false;
            debugEnhancing = false;
        }

        private void OnDisable()
        {
            if (popup == null) return;

            popup.OnTargetChanged -= HandleTargetChanged;
            popup.OnResultPlay -= HandleResultPlay;
            popup.OnOpened -= HandleOpened;
        }

        // 대상 갱신(강화 결과 반영 포함) 시 평상시 자리로 되돌린다. 성공 연출 뒤 1.0에 머물던 게이지를
        // 여기서 비운다 — Presenter가 PlayResult가 끝난 뒤 SetTarget을 부르므로 "해냈다"는 그 전까지 화면에 남는다.
        // 연출 중이면 건드리지 않는다(궤적이 끊긴다). 스윕은 끝나면 스스로 제자리로 돌아온다.
        private void HandleTargetChanged(EnhanceInfo info)
        {
            if (phase == Phase.Idle) Apply(RestFill);
        }

        // 팝업이 빈 화면(대상 미선택)으로 열리면 게이지도 비운다.
        private void HandleOpened()
        {
            if (phase == Phase.Idle) Apply(RestFill);
        }

        private void HandleResultPlay(EnhanceResult result)
        {
            success = result.Success;
            startFill = image.fillAmount;
            timer = 0f;
            SetPhase(Phase.Rise);
        }

        private void Update()
        {
            if (debugEnhancing != debugWasOn) ToggleDebug(debugEnhancing);

            if (debugEnhancing)
            {
                // 디버그 중엔 스윕 대신 슬라이더가 게이지를 잡는다.
                // 열·불똥은 게이지 높이에서 파생되므로 이것만으로 실제 연출과 같은 그림이 나온다.
                Apply(debugFill);
                return;
            }

            if (phase == Phase.Idle) return;

            timer += Mathf.Min(Time.unscaledDeltaTime, MaxStep);

            switch (phase)
            {
                case Phase.Rise:
                {
                    float t = Mathf.Clamp01(timer / riseDuration);
                    // ease-out — 처음에 확 치고 올라갔다가 정점 근처에서 느려진다.
                    Apply(Mathf.Lerp(startFill, peak, 1f - (1f - t) * (1f - t)));

                    if (t >= 1f) Advance(success ? Phase.Reach : Phase.Stall);
                    break;
                }

                case Phase.Reach:
                {
                    float t = Mathf.Clamp01(timer / reachDuration);
                    Apply(Mathf.Lerp(peak, 1f, t));

                    // 성공은 1.0에 도달한 채로 머문다. 창이 결과를 표시하고 넘어갈 때까지
                    // 게이지가 가득 차 있어야 "해냈다"가 화면에 남는다.
                    if (t >= 1f)
                    {
                        // 닿는 그 순간의 한 방. Idle로 넘기기 전에 알려야 한다 —
                        // SetPhase(Idle)이 열을 끄기 시작하므로 순서가 뒤바뀌면
                        // 이미 식는 중인 상태에서 터져 김이 빠진다.
                        OnReachedEnd?.Invoke();

                        SetPhase(Phase.Idle);
                    }
                    break;
                }

                case Phase.Stall:
                {
                    // 멈칫 — 값은 그대로 둔다. 아무것도 안 하는 이 구간이 실패 연출의 핵심이다.
                    if (timer >= stallDuration) Advance(Phase.Fall);
                    break;
                }

                case Phase.Fall:
                {
                    float t = Mathf.Clamp01(timer / fallDuration);
                    // ease-in — 처음엔 버티다가 점점 빨리 떨어진다(= 힘을 잃는 느낌).
                    Apply(Mathf.Lerp(peak, RestFill, t * t));

                    if (t >= 1f)
                    {
                        SetPhase(Phase.Idle);
                        Apply(RestFill);
                    }
                    break;
                }
            }
        }

        // 디버그 체크박스 처리. 켤 때 phase를 SetPhase가 아니라 직접 넣는 이유는,
        // SetPhase(Idle)이 열을 꺼버려서 켜자마자 다시 식어버리기 때문이다.
        private void ToggleDebug(bool on)
        {
            debugWasOn = on;

            if (on)
            {
                phase = Phase.Idle;   // 진행 중이던 스윕이 있으면 접는다
                timer = 0f;
                if (fx != null) fx.SetHeatActive(true);
            }
            else
            {
                if (fx != null) fx.SetHeatActive(false);
                Apply(RestFill);
            }
        }

        private void Advance(Phase next)
        {
            SetPhase(next);
            timer = 0f;
        }

        // 담금질은 "강화를 돌리는 중"에만 켠다.
        // 게이지 높이로는 판단할 수 없다 — 평상시에도 낮은 단계는 성공률이 90~100%라
        // 게이지가 거의 가득 찬 상태로 서 있기 때문이다. 그래서 연출 구간을 아는 이쪽이 켜준다.
        // 끄더라도 즉시 꺼지지 않고 식는 시간을 거친다(RadialFlowGaugeFx.heatFadeOut).
        private void SetPhase(Phase next)
        {
            phase = next;
            if (fx == null) return;

            fx.SetHeatActive(next != Phase.Idle);
        }

        private void Apply(float fill)
        {
            image.fillAmount = Mathf.Clamp01(fill);
        }
    }
}
