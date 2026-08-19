namespace ProjectS.Enhance
{
    /// <summary>
    /// 재료 슬롯 한 칸의 표시용 DTO. Presenter가 데이터(보유량 + 필요량)에서 조립해 View에 넘긴다.
    /// View는 이 값을 받아 MaterialSlotView에 뿌리기만 한다(판정·비교 없음).
    /// 아이콘은 스프라이트가 아니라 어드레서블 주소로 넘겨, View가 ItemIconLoader로 비동기 로드한다
    /// (인벤 슬롯과 같은 캐시 경로를 공유하기 위함).
    /// (2026-07-23 TH)
    /// </summary>
    public readonly struct MaterialSlotInfo
    {
        /// <summary>재료 아이콘 어드레서블 주소(<see cref="ProjectS.Data.ItemData.IconAddress"/>). 없으면 null.</summary>
        public readonly string IconAddress;

        /// <summary>재료 이름.</summary>
        public readonly string Name;

        /// <summary>현재 보유량.</summary>
        public readonly int Owned;

        /// <summary>이번 강화에 필요한 양.</summary>
        public readonly int Required;

        public MaterialSlotInfo(string iconAddress, string name, int owned, int required)
        {
            IconAddress = iconAddress;
            Name = name;
            Owned = owned;
            Required = required;
        }
    }
}
