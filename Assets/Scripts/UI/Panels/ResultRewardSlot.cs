using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 결과 보상 페이지의 슬롯 하나(기본 보상 · 확정 획득 · 랜덤 획득). 아이콘 + 수량만 보여준다.
    /// </summary>
    /// <remarks>
    /// 드랍 테이블이 아직 없어(2026-08-24) 이 뷰는 <b>무엇을 줄지 모른 채</b> 만들어져 있다.
    /// 그래서 아이템을 받는 대신 스프라이트·수량·미지 여부만 받는다 — 드랍이 붙으면 호출부만 바뀌고
    /// 이 뷰는 그대로 쓴다. 랜덤 보상은 열어보기 전까지 아이콘을 감추고 '?'만 보인다.
    /// (2026-08-24 TH)
    /// </remarks>
    public class ResultRewardSlot : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text countNum;

        [Tooltip("랜덤 보상일 때 아이콘 대신 보여줄 '?' 표시.")]
        [SerializeField] private GameObject unknownMark;

        /// <summary>슬롯에 보상을 채운다.</summary>
        /// <param name="sprite">아이템 아이콘. null이면 아이콘 칸을 비운다</param>
        /// <param name="itemName">슬롯 옆에 적을 이름. 비어 있으면 이름 칸을 감춘다</param>
        /// <param name="count">수량. 2 이상일 때만 "×N"으로 표시한다</param>
        /// <param name="unknown">true면 아이콘을 감추고 '?'를 보여준다(랜덤 보상)</param>
        public void Set(Sprite sprite, string itemName, int count, bool unknown = false)
        {
            if (unknownMark != null) unknownMark.SetActive(unknown);

            if (itemNameText != null)
            {
                bool hasName = !string.IsNullOrEmpty(itemName);
                itemNameText.gameObject.SetActive(hasName);
                if (hasName) itemNameText.text = itemName;
            }

            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = !unknown && sprite != null;
            }

            if (countNum != null)
            {
                bool showCount = count > 1;
                countNum.gameObject.SetActive(showCount);
                if (showCount) countNum.text = $"×{count}";
            }
        }

        /// <summary>슬롯을 비운다(이번 판에 해당 보상이 없을 때).</summary>
        public void Clear()
        {
            if (unknownMark != null) unknownMark.SetActive(false);
            if (itemNameText != null) itemNameText.gameObject.SetActive(false);
            if (icon != null) icon.enabled = false;
            if (countNum != null) countNum.gameObject.SetActive(false);
        }
    }
}
