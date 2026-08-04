using ProjectS.Data;

namespace ProjectS.Items
{
    /// <summary>
    /// 인벤토리에 쌓이는 스택형 아이템 1칸(소비품·재료). 장비는 인스턴스마다 +N이 달라
    /// <see cref="ProjectS.Enhance.EquipmentInstance"/>로 따로 다루고, 스택형은 "같은 아이템 N개"라
    /// 이 타입으로 수량만 센다. 병합/분할(MaxStack 초과 처리)은 InventoryManager가 조율한다.
    /// </summary>
    public class ItemStack
    {
        /// <summary>공통 아이템 정보(이름·아이콘·카테고리·MaxStack).</summary>
        public ItemData Item { get; }

        /// <summary>소비품 고유 정보(회복량·지속·쿨다운). 재료(Material)는 null이다.</summary>
        public ConsumableData Consumable { get; }

        /// <summary>현재 수량(1 이상).</summary>
        public int Count { get; private set; }

        /// <summary>더 담을 수 없는 상태(MaxStack 도달).</summary>
        public bool IsFull => Item == null || Count >= Item.MaxStack;

        /// <summary>이 스택이 소비 가능한 소비품인지(재료는 false).</summary>
        public bool IsConsumable => Consumable != null;

        /// <summary>
        /// 스택형 아이템 1칸을 만든다.
        /// </summary>
        /// <param name="item">공통 아이템 행</param>
        /// <param name="consumable">소비품 고유 행(재료면 null)</param>
        /// <param name="count">초기 수량(기본 1)</param>
        public ItemStack(ItemData item, ConsumableData consumable, int count = 1)
        {
            Item = item;
            Consumable = consumable;
            Count = count;
        }

        /// <summary>
        /// 수량을 늘리고, MaxStack을 넘어 담지 못한 잉여분을 돌려준다(0이면 전부 수용).
        /// 매니저가 여러 스택으로 나눠 담을 때 잉여분을 다음 스택으로 넘기는 데 쓴다.
        /// </summary>
        /// <param name="amount">추가할 수량</param>
        /// <returns>담지 못한 잉여 수량</returns>
        public int Add(int amount)
        {
            if (amount <= 0 || Item == null) return amount > 0 ? amount : 0;

            int space = Item.MaxStack - Count;
            int accepted = amount < space ? amount : space;
            Count += accepted;
            return amount - accepted;
        }

        /// <summary>
        /// 수량을 줄이고 실제로 줄인 양을 돌려준다(보유분보다 크게 요청하면 보유분까지만).
        /// </summary>
        /// <param name="amount">줄일 수량</param>
        /// <returns>실제로 줄인 수량</returns>
        public int Remove(int amount)
        {
            if (amount <= 0) return 0;

            int removed = amount < Count ? amount : Count;
            Count -= removed;
            return removed;
        }
    }
}
