using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ProjectS.Data;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 보상 목록 한 칸. 아이콘 + 이름 + 수량. 보상 화면 뷰가 Bind로 채운다.
    /// 아이템 보상이면 <see cref="SetGradeItem"/>으로 받은 아이템을 기억해 두었다가, 마우스를 올리면
    /// 아이템 정보창(<see cref="ItemTooltip.ShowDefinition"/>)을 띄운다. 골드·경험치·스킬 보상은 아이템이 없어 뜨지 않는다.
    /// </summary>
    public class NpcRewardSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;

        private ItemSlotGradeView gradeView;   // 등급 표현(선택). 프리팹에 안 붙어 있으면 null이고 등급 표시는 생략된다.
        private ItemData item;                  // 이 칸의 보상 아이템(정보창용). 아이템이 아닌 보상이면 null.

        private void Awake()
        {
            gradeView = GetComponent<ItemSlotGradeView>();
        }

        /// <summary>보상 한 개를 채운다.</summary>
        /// <param name="rewardName">보상 이름(골드/경험치/아이템 등)</param>
        /// <param name="amount">수량 표기(빈 문자열이면 숨김)</param>
        /// <param name="iconSprite">아이콘(없으면 숨김)</param>
        public void Bind(string rewardName, string amount, Sprite iconSprite)
        {
            if (nameText != null) nameText.text = rewardName;

            if (amountText != null)
            {
                amountText.SetText(amount);
                amountText.gameObject.SetActive(!string.IsNullOrEmpty(amount));
            }

            SetIcon(iconSprite);

            // 칸은 풀링으로 재사용되므로, 이전에 담겼던 아이템의 등급 표시·정보창 대상이 남지 않게 매번 지운다.
            // 아이템 보상이면 호출 쪽이 이어서 SetGradeItem으로 다시 채운다.
            // 이 칸이 띄운 정보창이 떠 있으면 옛 보상 정보가 남으므로 함께 닫는다.
            ItemTooltip.Instance?.Hide(this);
            SetGradeItem(null);
        }

        /// <summary>
        /// 이 칸의 보상이 아이템일 때 등급 표시(배경색·이펙트)를 입히고, hover 정보창 대상으로 기억한다. Bind 뒤에 호출한다.
        /// </summary>
        /// <param name="item">보상 아이템(아이템이 아닌 보상이면 null → 등급 표시 끔·정보창 없음)</param>
        public void SetGradeItem(ItemData item)
        {
            this.item = item;
            if (gradeView != null) gradeView.SetItem(item);
        }

        /// <summary>마우스를 올리면 보상 아이템의 정보창을 띄운다(아이템이 아닌 보상이면 무시).</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (item == null || ItemTooltip.Instance == null) return;

            // 보상은 아직 받지 않은 아이템이라 정의 미리보기로 띄운다. owner로 this를 넘겨,
            // 퀘스트 창·대화창이 닫혀 이 칸이 꺼질 때(OnDisable)만 정보창이 닫히게 한다.
            ItemTooltip.Instance.ShowDefinition(item, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 정보창을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => ItemTooltip.Instance?.Hide();

        // 창이 닫혀 칸이 꺼지면 이 칸이 띄운 정보창을 닫는다(마우스가 안 움직여 Exit가 안 와도).
        private void OnDisable() => ItemTooltip.Instance?.Hide(this);

        /// <summary>아이콘만 갈아끼운다(아이템 보상 아이콘의 비동기 로드가 늦게 끝났을 때 덮어쓰기용).</summary>
        /// <param name="iconSprite">아이콘(없으면 숨김)</param>
        public void SetIcon(Sprite iconSprite)
        {
            if (icon == null) return;

            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
        }
    }
}
