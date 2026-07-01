using System;

public class InventoryEvents
{
    // 인벤토리에 아이템이 추가됐을 때 발행 → 인벤토리/획득 알림 UI가 갱신
    public static event Action<ItemData> OnItemAdded;

    // 인벤토리에서 아이템이 제거됐을 때 발행 → 인벤토리 UI가 갱신
    public static event Action<ItemData> OnItemRemoved;

    // 아이템을 장착했을 때 발행 → 장비창·스탯 UI가 갱신
    public static event Action<ItemData> OnItemEquipped;

    // 장착을 해제했을 때 발행 → 장비창·스탯 UI가 갱신
    public static event Action<ItemData> OnItemUnequipped;

    public static void FireItemAdded(ItemData item) => OnItemAdded?.Invoke(item);

    public static void FireItemRemoved(ItemData item) => OnItemRemoved?.Invoke(item);

    public static void FireItemEquipped(ItemData item) => OnItemEquipped?.Invoke(item);

    public static void FireItemUnequipped(ItemData item) => OnItemUnequipped?.Invoke(item);
}
