using System;
using ProjectS.Data;

namespace ProjectS.Events
{
    public class InventoryEvents
    {
        /// <summary>
        /// 인벤토리에 아이템이 추가됐을 때 발행 → 인벤토리/획득 알림 UI가 갱신
        /// </summary>
        public static event Action<ItemData> OnItemAdded;

        /// <summary>
        /// 인벤토리에서 아이템이 제거됐을 때 발행 → 인벤토리 UI가 갱신
        /// </summary>
        public static event Action<ItemData> OnItemRemoved;

        /// <summary>
        /// 아이템을 장착했을 때 발행 → 장비창·스탯 UI가 갱신
        /// </summary>
        public static event Action<ItemData> OnItemEquipped;

        /// <summary>
        /// 장착을 해제했을 때 발행 → 장비창·스탯 UI가 갱신
        /// </summary>
        public static event Action<ItemData> OnItemUnequipped;

        /// <summary>
        /// 포션 퀵슬롯 등록이 바뀌었을 때 발행(index, 등록 소비품 itemId — 0이면 해제) → HUD 슬롯이 갱신
        /// </summary>
        public static event Action<int, int> OnQuickSlotChanged;

        /// <summary>
        /// 소비품을 사용했을 때 발행(itemId, 쿨다운 초) → HUD 퀵슬롯이 쿨다운 연출을 시작
        /// </summary>
        public static event Action<int, float> OnConsumableUsed;


        public static void FireItemAdded(ItemData item) => OnItemAdded?.Invoke(item);

        public static void FireItemRemoved(ItemData item) => OnItemRemoved?.Invoke(item);

        public static void FireItemEquipped(ItemData item) => OnItemEquipped?.Invoke(item);

        public static void FireItemUnequipped(ItemData item) => OnItemUnequipped?.Invoke(item);

        public static void FireQuickSlotChanged(int index, int itemId) => OnQuickSlotChanged?.Invoke(index, itemId);

        public static void FireConsumableUsed(int itemId, float cooldownSec) => OnConsumableUsed?.Invoke(itemId, cooldownSec);
    }
}
