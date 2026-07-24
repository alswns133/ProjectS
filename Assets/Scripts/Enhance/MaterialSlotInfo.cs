using UnityEngine;

namespace ProjectS.Enhance
{
    /// <summary>
    /// 재료 슬롯 한 칸의 표시용 DTO. Presenter가 데이터(보유량 + 필요량)에서 조립해 View에 넘긴다.
    /// View는 이 값을 받아 MaterialSlotView에 뿌리기만 한다(판정·비교 없음).
    /// (2026-07-23 TH)
    /// </summary>
    public readonly struct MaterialSlotInfo
    {
        /// <summary>재료 아이콘.</summary>
        public readonly Sprite Icon;

        /// <summary>재료 이름.</summary>
        public readonly string Name;

        /// <summary>현재 보유량.</summary>
        public readonly int Owned;

        /// <summary>이번 강화에 필요한 양.</summary>
        public readonly int Required;

        public MaterialSlotInfo(Sprite icon, string name, int owned, int required)
        {
            Icon = icon;
            Name = name;
            Owned = owned;
            Required = required;
        }
    }
}
