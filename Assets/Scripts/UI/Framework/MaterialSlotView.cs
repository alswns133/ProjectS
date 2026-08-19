using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Items;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 재료 한 칸: 아이콘 + 이름 + 보유/필요 수량. 부족하면 수량 텍스트를 경고색으로 표시한다.
    /// 강화창 전용이 아니라 상점·인벤 상세창에서도 재활용할 수 있게 프레임워크 뷰로 둔다.
    /// 아이콘은 <see cref="ItemIconLoader"/>로 어드레서블에서 비동기 로드한다(인벤 슬롯과 같은 경로).
    /// (2026-07-23 TH)
    /// </summary>
    public class MaterialSlotView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Color enoughColor = Color.white;
        [SerializeField] private Color lackColor = new Color(1f, 0.24f, 0.36f); // #FF3D5C

        // 비동기 아이콘 로드 중 이 슬롯이 다른 재료로 갱신되면 늦게 온 스프라이트를 버리는 기준.
        private string currentAddress;

        /// <summary>
        /// 재료 한 칸을 표시한다. 아이콘은 주소로 비동기 로드한다.
        /// </summary>
        /// <param name="iconAddress">재료 아이콘 어드레서블 주소(없으면 null)</param>
        /// <param name="materialName">재료 이름</param>
        /// <param name="owned">보유량</param>
        /// <param name="required">필요량</param>
        public void Set(string iconAddress, string materialName, int owned, int required)
        {
            if (nameText != null) nameText.text = materialName;

            if (countText != null)
            {
                countText.text = $"{owned}/{required}";
                countText.color = owned >= required ? enoughColor : lackColor;
            }

            currentAddress = iconAddress;
            LoadIcon(iconAddress);
        }

        // 아이콘을 어드레서블에서 비동기 로드한다. 로드 전엔 비워, 스프라이트 없는 Image가 흰 사각형으로
        // 보이는 팝인을 막는다. 대기 중 슬롯이 다른 재료로 바뀌면 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(string address)
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (this == null || icon == null) return;
            if (!string.Equals(currentAddress, address)) return;   // 슬롯이 다른 재료로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
