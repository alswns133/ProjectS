using ProjectS.Enhance;

namespace ProjectS.Items
{
    /// <summary>
    /// 판매 슬롯 한 칸에 올라간 "판매 예정" 항목. 장비(낱개) 또는 스택(+개수) 중 하나만 채운다.
    /// 인벤 데이터는 최종 판매 확정 전까지 건드리지 않고, 이 객체가 "무엇을 몇 개 팔 예정인가"만 기억한다
    /// (예약 모델 — 창을 닫거나 튕겨도 아이템이 사라지지 않게 하기 위함).
    /// UI(ShopSellView)와 매니저(ShopManager.SellAll)가 함께 쓰므로 어느 한쪽 층이 아니라 Items에 둔다.
    /// </summary>
    public class ShopSellEntry
    {
        /// <summary>판매할 장비. 스택 항목이면 null.</summary>
        public EquipmentInstance Equipment { get; }

        /// <summary>판매할 스택. 장비 항목이면 null.</summary>
        public ItemStack Stack { get; }

        /// <summary>판매 개수. 장비는 항상 1, 스택은 수량 팝업에서 정한 값(같은 스택을 또 올리면 합산).</summary>
        public int Count { get; set; }

        /// <summary>장비 항목으로 만든다(개수 1 고정).</summary>
        public ShopSellEntry(EquipmentInstance equipment)
        {
            Equipment = equipment;
            Count = 1;
        }

        /// <summary>스택 항목으로 만든다.</summary>
        public ShopSellEntry(ItemStack stack, int count)
        {
            Stack = stack;
            Count = count;
        }

        /// <summary>이 항목의 판매 합계(판매가 × 개수). 판매 UI 상단 예상 골드 합산에 쓴다.</summary>
        public int TotalPrice
        {
            get
            {
                if (Equipment?.Item != null) return Equipment.Item.SellPrice;
                if (Stack?.Item != null) return Stack.Item.SellPrice * Count;
                return 0;
            }
        }
    }
}
