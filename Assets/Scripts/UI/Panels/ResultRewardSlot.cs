using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;

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

        private ItemSlotGradeView gradeView;   // 등급 표현(선택). 프리팹에 안 붙어 있으면 null이고 등급 표시는 생략된다.

        private void Awake()
        {
            gradeView = GetComponent<ItemSlotGradeView>();
        }

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

            // 슬롯은 재사용되므로 이전 보상의 등급 표시가 남지 않게 매번 지운다.
            // 등급을 보여줄 보상이면 호출 쪽이 이어서 SetGradeItem으로 다시 채운다('?' 보상은 등급도 감춘다).
            SetGradeItem(null);
        }

        /// <summary>이 슬롯 보상의 등급 표시(배경색·이펙트)를 입힌다. <see cref="Set"/> 뒤에 호출한다.</summary>
        /// <param name="item">보상 아이템 행(null이면 등급 표시 끔)</param>
        public void SetGradeItem(ItemData item)
        {
            if (gradeView != null) gradeView.SetItem(item);
        }

        /// <summary>슬롯을 비운다(이번 판에 해당 보상이 없을 때).</summary>
        public void Clear()
        {
            if (unknownMark != null) unknownMark.SetActive(false);
            if (itemNameText != null) itemNameText.gameObject.SetActive(false);
            if (icon != null) icon.enabled = false;
            if (countNum != null) countNum.gameObject.SetActive(false);
            SetGradeItem(null);
        }
    }
}
