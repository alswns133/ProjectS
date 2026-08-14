using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;

namespace ProjectS.UI
{
    /// <summary>
    /// 상점 그리드의 카드 한 장. 아이템 하나(이름·정보·아이콘)와 가격을 표시하고, 클릭하면 호스트(ShopPopup)에
    /// 자기를 알려 선택 상태로 만든다. 구입/판매 어느 쪽이든 같은 카드를 쓰고, 실제로 무엇을 거래할지는
    /// <see cref="Payload"/>(구입=ShopItemEntry, 판매=ItemStack 또는 EquipmentInstance)에 담아 호스트가 해석한다.
    /// 아이콘은 <see cref="ItemIconLoader"/>로 어드레서블에서 비동기 로드한다(InventoryItemSlot과 같은 방식).
    /// 클릭을 받으려면 이 오브젝트에 raycastTarget인 Graphic(배경/아이콘)이 있어야 한다.
    /// </summary>
    public class ShopItemCard : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private TMP_Text priceText;

        [Tooltip("선택 시 켜는 하이라이트(테두리 등). 없어도 동작한다.")]
        [SerializeField] private GameObject selectedHighlight;

        // 늦게 온 아이콘을 버리기 위한 현재 아이템(그리드 재사용 중 다른 아이템으로 재바인딩 대비).
        private ItemData currentItem;
        private Action<ShopItemCard> onClick;

        /// <summary>이 카드가 거래하는 대상. 구입=<see cref="ShopItemEntry"/>, 판매=ItemStack 또는 EquipmentInstance.</summary>
        public object Payload { get; private set; }

        /// <summary>카드에 아이템·가격·거래 대상을 채우고 클릭 콜백을 건다(호스트가 카드 재사용마다 호출).</summary>
        /// <param name="item">표시할 아이템 정의(이름·정보·아이콘)</param>
        /// <param name="price">표시할 가격(구입가 또는 판매가)</param>
        /// <param name="payload">거래 대상(구입=ShopItemEntry, 판매=ItemStack/EquipmentInstance)</param>
        /// <param name="clickHandler">카드 클릭 시 호출할 콜백</param>
        public void Bind(ItemData item, int price, object payload, Action<ShopItemCard> clickHandler)
        {
            currentItem = item;
            Payload = payload;
            onClick = clickHandler;

            if (nameText != null) nameText.text = item != null ? item.Name : string.Empty;
            if (infoText != null) infoText.text = item != null ? item.Description : string.Empty;
            if (priceText != null) priceText.text = price.ToString();

            SetSelected(false);
            LoadIcon(item);
        }

        /// <summary>선택 하이라이트를 켜고 끈다(호스트가 선택 변경 시 호출).</summary>
        /// <param name="on">선택 상태면 true</param>
        public void SetSelected(bool on)
        {
            if (selectedHighlight != null) selectedHighlight.SetActive(on);
        }

        /// <summary>카드를 클릭하면 호스트에 자기를 알린다(선택 교체는 호스트가 처리).</summary>
        public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke(this);

        // 아이콘을 비동기 로드한다. 대기 중 카드가 다른 아이템으로 재바인딩되면 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(ItemData item)
        {
            // 로드 전엔 아이콘을 비운다 — 스프라이트 없는 Image의 흰 사각형 팝인을 막는다(완료 시 다시 켠다).
            if (icon != null) { icon.sprite = null; icon.enabled = false; }
            if (item == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);

            if (this == null || icon == null) return;
            if (!ReferenceEquals(currentItem, item)) return;   // 카드가 다른 아이템으로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
