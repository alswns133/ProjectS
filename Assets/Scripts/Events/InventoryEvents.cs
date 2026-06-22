using System;

public class InventoryEvents
{
    // 아이템 추가
    public static event Action<ItemData> OnItemAdded;

    // 아이템 제거
    public static event Action<ItemData> OnItemRemoved;

    // 아이템 장착
    public static event Action<ItemData> OnItemEquipped;

    // 아이템 해제
    public static event Action<ItemData> OnItemUnequipped;

    public static void FireItemAdded(ItemData item) => OnItemAdded?.Invoke(item);

    public static void FireItemRemoved(ItemData item) => OnItemRemoved?.Invoke(item);

    public static void FireItemEquipped(ItemData item) => OnItemEquipped?.Invoke(item);

    public static void FireItemUnequipped(ItemData item) => OnItemUnequipped?.Invoke(item);
}
