using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 장비창의 착용 부위 슬롯 하나(무기·헬멧·상의·하의·신발 중 하나). 인벤에서 드래그한 장비를 드롭하면 착용하고
    /// (<see cref="InventoryManager.Equip"/>), 슬롯을 클릭하면 해제한다(<see cref="InventoryManager.Unequip"/>).
    /// 표시(아이콘·강화)는 <see cref="InventoryManager.GetEquipped"/>를 읽어 <see cref="Refresh"/>가 갱신한다.
    /// </summary>
    public class EquipSlotView : MonoBehaviour, IDropHandler, IPointerClickHandler
    {
        [Tooltip("이 슬롯이 담당하는 착용 부위")]
        [SerializeField] private EquipSlot slot;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text enhanceText;   // +N (없으면 숨김)

        private int currentItemId;   // 아이콘 async 로드 stale 판정용

        /// <summary>이 슬롯의 부위.</summary>
        public EquipSlot Slot => slot;

        /// <summary>현재 착용 상태를 읽어 아이콘·강화 표시를 갱신한다(장비창이 호출).</summary>
        public void Refresh()
        {
            EquipmentInstance eq = InventoryManager.Instance != null ? InventoryManager.Instance.GetEquipped(slot) : null;

            if (eq?.Item == null)
            {
                currentItemId = 0;
                if (icon != null) { icon.sprite = null; icon.enabled = false; }
                if (enhanceText != null) enhanceText.text = string.Empty;
                return;
            }

            currentItemId = eq.Item.Index;
            if (enhanceText != null) enhanceText.text = eq.EnhanceStep > 0 ? $"+{eq.EnhanceStep}" : string.Empty;
            LoadIcon(eq.Item);
        }

        /// <summary>인벤에서 드래그한 장비를 이 부위에 착용한다(부위·직업이 안 맞으면 Equip이 거부).</summary>
        public void OnDrop(PointerEventData eventData)
        {
            InventoryItemSlot source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;

            EquipmentInstance eq = source?.Equipment;
            if (eq != null) InventoryManager.Instance?.Equip(eq);
        }

        /// <summary>슬롯을 클릭하면 착용 장비를 해제해 가방으로 되돌린다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            InventoryManager.Instance?.Unequip(slot);
        }

        private async void LoadIcon(ItemData item)
        {
            if (icon == null) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);
            if (this == null || icon == null) return;
            if (currentItemId != item.Index) return;   // 그새 다른 장비로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
