using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>보상 목록 한 칸. 아이콘 + 이름 + 수량. 보상 화면 뷰가 Bind로 채운다.</summary>
    public class NpcRewardSlot : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;

        /// <summary>보상 한 개를 채운다.</summary>
        /// <param name="rewardName">보상 이름(골드/경험치/아이템 등)</param>
        /// <param name="amount">수량 표기(빈 문자열이면 숨김)</param>
        /// <param name="iconSprite">아이콘(없으면 숨김)</param>
        public void Bind(string rewardName, string amount, Sprite iconSprite)
        {
            if (nameText != null) nameText.text = rewardName;

            if (amountText != null)
            {
                amountText.text = amount;
                amountText.gameObject.SetActive(!string.IsNullOrEmpty(amount));
            }

            SetIcon(iconSprite);
        }

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
