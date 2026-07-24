using TMPro;
using UnityEngine;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 스탯 프리뷰 한 줄: 라벨 + 현재값 + (선택) 변화 후 값.
    /// 강화 프리뷰뿐 아니라 장비 상세·비교 UI에서도 재활용할 수 있게 프레임워크 뷰로 둔다.
    /// (2026-07-23 TH)
    /// </summary>
    public class StatRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text nextText;
        [SerializeField] private TMP_Text deltaText;

        [Header("델타 색")]
        [SerializeField] private Color gainColor = new Color(0.93f, 0.74f, 0.30f, 1f); // 상승: 골드
        [SerializeField] private Color lossColor = new Color(1f, 0.36f, 0.48f, 1f);    // 하락: 경고

        /// <summary>
        /// 스탯 한 줄을 표시한다. next/delta가 비어 있으면 해당 칸을 숨긴다.
        /// 델타 색은 표시 문자열이 아니라 deltaValue의 부호로 정한다(양수 골드, 음수 경고).
        /// 표시 포맷(%, 천단위 콤마 등)은 값마다 달라 호출자가 문자열로 넘기고, 색만 여기서 판정한다.
        /// </summary>
        /// <param name="label">스탯 이름</param>
        /// <param name="current">현재값 표시 문자열</param>
        /// <param name="next">변화 후 값 표시 문자열(없으면 null → 칸 숨김)</param>
        /// <param name="delta">변화량 표시 문자열(예: "+124", "-3", 없으면 null → 칸 숨김)</param>
        /// <param name="deltaValue">변화량의 부호 판정용 실제 값. 음수면 경고색, 그 외 상승색</param>
        public void Set(string label, string current, string next = null, string delta = null, float deltaValue = 0f)
        {
            if (labelText != null) labelText.text = label;
            if (valueText != null) valueText.text = current;

            if (nextText != null)
            {
                bool hasNext = !string.IsNullOrEmpty(next);
                nextText.gameObject.SetActive(hasNext);
                if (hasNext) nextText.text = next;
            }

            if (deltaText != null)
            {
                bool hasDelta = !string.IsNullOrEmpty(delta);
                deltaText.gameObject.SetActive(hasDelta);
                if (hasDelta)
                {
                    deltaText.text = delta;
                    deltaText.color = deltaValue < 0f ? lossColor : gainColor;
                }
            }
        }
    }
}
