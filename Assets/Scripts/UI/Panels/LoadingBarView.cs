using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 로딩 화면의 일자형 진행 바. 화면 폭을 가로지르는 얇은 선에 채움과 밝은 머리(현재 진행 지점)를 그린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>채움은 스프라이트 Filled가 아니라 앵커 폭으로 그린다.</b> 채움 사각형의 오른쪽 앵커를 진행률로 옮기므로
    /// 스프라이트가 필요 없고, 막대를 어떤 길이로 늘려도 모양이 찌그러지지 않는다. 머리도 같은 앵커 위치에 붙인다.
    /// </para>
    /// <para>
    /// 원형 게이지(결과 화면·로그인에서 쓰는 PerformanceGauge)를 로딩까지 재활용하면 화면마다 같은 물건이 반복돼
    /// 일자형으로 정했다. 배치·배선은 <c>Tools ▸ ProjectS ▸ Build Loading Screen</c>이 한다.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public class LoadingBarView : MonoBehaviour
    {
        [Tooltip("채움 사각형. 왼쪽 끝에서 진행률만큼 오른쪽으로 늘어난다.")]
        [SerializeField] private RectTransform fill;

        [Tooltip("채움 끝에 붙는 밝은 눈금(선택).")]
        [SerializeField] private RectTransform head;

        [Tooltip("퍼센트 표기(선택).")]
        [SerializeField] private TMP_Text percent;

        [Tooltip("다 차면 머리를 숨긴다. 100%에서 끝에 눈금이 남아 있으면 아직 진행 중처럼 보인다.")]
        [SerializeField] private bool hideHeadWhenFull = true;

        private int shownPercent = -1;

        /// <summary>
        /// 진행률(0~1)을 채움·머리·퍼센트에 반영한다. 로딩 루프가 매 프레임 부른다.
        /// </summary>
        public void SetProgress(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);

            if (fill != null)
            {
                fill.anchorMin = new Vector2(0f, fill.anchorMin.y);
                fill.anchorMax = new Vector2(ratio, fill.anchorMax.y);
                fill.offsetMin = new Vector2(0f, fill.offsetMin.y);
                fill.offsetMax = new Vector2(0f, fill.offsetMax.y);
            }

            if (head != null)
            {
                head.anchorMin = new Vector2(ratio, head.anchorMin.y);
                head.anchorMax = new Vector2(ratio, head.anchorMax.y);
                bool visible = !(hideHeadWhenFull && ratio >= 1f);
                if (head.gameObject.activeSelf != visible) head.gameObject.SetActive(visible);
            }

            int value = Mathf.RoundToInt(ratio * 100f);
            if (percent == null || value == shownPercent) return;

            shownPercent = value;
            percent.text = $"{value}%";
        }
    }
}
