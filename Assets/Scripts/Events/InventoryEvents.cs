using System;
using ProjectS.Data;

namespace ProjectS.Events
{
    /// <summary>인벤토리·장비·퀵슬롯 변화를 UI에 알리는 static 이벤트 허브. 발행은 반드시 FireXxx로 한다.</summary>
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

        /// <summary>
        /// 아이템 배치가 바뀌었을 때 발행(위치 이동 등) → 인벤 UI가 격자를 다시 그린다. 추가/제거는 기존 이벤트를 쓴다.
        /// </summary>
        public static event Action OnInventoryChanged;


        /// <summary><see cref="OnItemAdded"/>를 발행한다. 인벤토리에 아이템을 넣은 쪽이 호출한다.</summary>
        public static void FireItemAdded(ItemData item) => OnItemAdded?.Invoke(item);

        /// <summary><see cref="OnItemRemoved"/>를 발행한다. 인벤토리에서 아이템을 뺀 쪽이 호출한다.</summary>
        public static void FireItemRemoved(ItemData item) => OnItemRemoved?.Invoke(item);

        /// <summary><see cref="OnItemEquipped"/>를 발행한다. 장착 처리 후 호출한다.</summary>
        public static void FireItemEquipped(ItemData item) => OnItemEquipped?.Invoke(item);

        /// <summary><see cref="OnItemUnequipped"/>를 발행한다. 장착 해제 처리 후 호출한다.</summary>
        public static void FireItemUnequipped(ItemData item) => OnItemUnequipped?.Invoke(item);

        /// <summary><see cref="OnQuickSlotChanged"/>를 발행한다. itemId가 0이면 슬롯 해제.</summary>
        /// <param name="index">퀵슬롯 인덱스.</param>
        /// <param name="itemId">등록한 소비품 itemId(0 = 해제).</param>
        public static void FireQuickSlotChanged(int index, int itemId) => OnQuickSlotChanged?.Invoke(index, itemId);

        /// <summary><see cref="OnConsumableUsed"/>를 발행한다. 소비품을 실제로 사용한 뒤 호출해야 HUD 쿨다운이 맞게 돈다.</summary>
        /// <param name="itemId">사용한 소비품 itemId.</param>
        /// <param name="cooldownSec">쿨다운(초).</param>
        public static void FireConsumableUsed(int itemId, float cooldownSec) => OnConsumableUsed?.Invoke(itemId, cooldownSec);

        /// <summary><see cref="OnInventoryChanged"/>를 발행한다. 추가/제거가 아닌 배치 변경(위치 이동 등) 때 호출한다.</summary>
        public static void FireInventoryChanged() => OnInventoryChanged?.Invoke();
    }
}
