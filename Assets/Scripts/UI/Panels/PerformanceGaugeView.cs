using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 결과 화면의 원형 퍼포먼스 게이지(UI_RS_002). 채움 비율 표시와 잠금 연출 재생을 한 곳에 모은 뷰다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 패널이 Image·TMP를 직접 들지 않는가</b>: 이 게이지는 프리팹이라 제너레이터가 인스턴스만 꽂는다.
    /// 패널이 안쪽 조각을 직접 들면 제너레이터가 <b>프리팹 내부를 이름으로 뒤져야</b> 하고, 그 순간 프리팹 안
    /// 이름을 바꾸면 배선이 조용히 끊긴다(애니메이션 커브 경로가 이름에 묶이는 것과 같은 함정).
    /// 루트에 이 뷰 하나만 두면 패널 ↔ 게이지 배선이 참조 하나로 끝난다.
    /// </para>
    /// <para>
    /// <b>잠금 연출은 애니메이션 클립이 맡는다.</b> 4조각이 게이지 둘레를 돌다 맞물린 뒤 가운데로 조여드는
    /// 안무라, 맞물리는 타이밍과 되튐은 커브로 잡는 편이 코드 상수를 튜닝하는 것보다 정확하다.
    /// 이 스크립트는 <b>재생 신호만</b> 준다 — <see cref="EnhanceGaugeCycleSpin"/>이 회전만 맡고 시작·종료는
    /// 강화 흐름에서 받는 것과 같은 분담이다.
    /// 컨트롤러가 아직 없으면 조용히 넘어가므로 클립을 만들기 전에도 게이지는 정상 동작한다.
    /// </para>
    /// (2026-08-24 TH)
    /// </remarks>
    public class PerformanceGaugeView : MonoBehaviour
    {
        [Header("게이지")]
        [Tooltip("Filled · Radial360 이미지. 채움(fillAmount)은 잠금 애니메이션 클립이 직접 몬다(코드가 안 건드림).")]
        [SerializeField] private Image fill;

        [Tooltip("퍼센트 표기(TMP). 매 프레임 fill의 채움 값을 읽어 따라 적는다 — 애니메이션과 자동 동기화.")]
        [SerializeField] private TMP_Text num;

        [Header("등급")]
        [SerializeField] private TMP_Text rank;

        [Header("잠금 연출")]
        [Tooltip("4조각 잠금 안무를 도는 Animator. 비워 두면 연출 없이 값만 표시한다.")]
        [SerializeField] private Animator lockAnimator;

        [Tooltip("재생할 애니메이터 State 이름. 매번 같은 연출이라 파라미터 없이 이름으로 바로 재생한다.")]
        [SerializeField] private string lockStateName = "Lock";

        // 마지막으로 숫자에 반영한 퍼센트. 값이 바뀔 때만 TMP 텍스트를 갱신해 매 프레임 재빌드를 피한다.
        private int shownPercent = -1;

        // 게이지 채움(fill.fillAmount)은 잠금 애니메이션 클립이 직접 몬다(프레임 단위로 애니와 완벽히 동기화).
        // 숫자(%)는 커브로 만들 수 없는 TMP 텍스트라, 여기서 매 프레임 fill 값을 읽어 그대로 따라 적는다.
        // 이렇게 두면 채움도 숫자도 애니메이션 하나가 원천이라 속도가 어긋나지 않는다.
        private void Update()
        {
            if (fill == null || num == null) return;

            int percent = Mathf.RoundToInt(fill.fillAmount * 100f);
            if (percent == shownPercent) return;

            shownPercent = percent;
            num.text = $"{percent}%";
        }

        /// <summary>
        /// 게이지 중앙의 성과 등급 문자를 채운다. 표시 On/Off는 잠금 애니메이션(GaugeRank)이 맡으므로
        /// 여기서는 내용만 갱신한다. 비어 있으면 "-"로 둔다.
        /// </summary>
        /// <param name="grade">등급 문자(S/A/B…). 비어 있으면 미정 표시("-").</param>
        public void SetRank(string grade)
        {
            if (rank != null) rank.text = string.IsNullOrEmpty(grade) ? "-" : grade;
        }

        /// <summary>
        /// 잠금 연출을 처음부터 재생한다. 결과 화면이 성과 페이지에 들어설 때 부른다.
        /// </summary>
        /// <remarks>
        /// 컨트롤러가 없으면 아무 일도 하지 않는다. Animator에 컨트롤러가 없는 상태로 Play를 부르면
        /// 콘솔에 경고만 쌓이고 화면은 그대로라, 클립 작업 전 단계에서 로그가 지저분해진다.
        /// </remarks>
        public void PlayLock()
        {
            if (lockAnimator == null || lockAnimator.runtimeAnimatorController == null) return;
            if (string.IsNullOrEmpty(lockStateName)) return;

            lockAnimator.Play(lockStateName, 0, 0f);
        }
    }
}
