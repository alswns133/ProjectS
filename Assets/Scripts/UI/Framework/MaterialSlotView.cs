using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 재료 한 칸: 아이콘 + 이름 + 보유/필요 수량. 부족하면 수량 텍스트를 경고색으로 표시한다.
    /// 강화창 전용이 아니라 상점·인벤 상세창에서도 재활용할 수 있게 프레임워크 뷰로 둔다.
    /// (2026-07-23 TH)
    /// </summary>
    public class MaterialSlotView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Color enoughColor = Color.white;
        [SerializeField] private Color lackColor = new Color(1f, 0.24f, 0.36f); // #FF3D5C

        /// <summary>
        /// 재료 한 칸을 표시한다.
        /// </summary>
        /// <param name="iconSprite">재료 아이콘</param>
        /// <param name="materialName">재료 이름</param>
        /// <param name="owned">보유량</param>
        /// <param name="required">필요량</param>
        public void Set(Sprite iconSprite, string materialName, int owned, int required)
        {
            if (icon != null) icon.sprite = iconSprite;
            if (nameText != null) nameText.text = materialName;

            if (countText != null)
            {
                countText.text = $"{owned}/{required}";
                countText.color = owned >= required ? enoughColor : lackColor;
            }
        }
    }
}
