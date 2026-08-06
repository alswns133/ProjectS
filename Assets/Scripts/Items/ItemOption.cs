using ProjectS.Data;

namespace ProjectS.Items
{
    /// <summary>
    /// 장비 인스턴스에 실제로 붙은 옵션 하나(드랍 시 롤된 결과). 정적 기준값(<see cref="ItemOptionData"/>)과 달리
    /// 이 값은 인스턴스마다 다르며 세이브에 저장된다. 표시(툴팁)와 스탯 집계가 이 값을 읽는다.
    /// </summary>
    public class ItemOption
    {
        /// <summary>옵션 종류(치확·공퍼·보스추뎀 등).</summary>
        public ItemOptionType Type { get; }

        /// <summary>롤된 실제 값. 퍼센트 옵션이면 비율(예: 0.05 = 5%)이거나 표기 규약에 따른다.</summary>
        public float Value { get; }

        /// <summary>퍼센트 옵션인지(표시 포맷·곱연산 판정). <see cref="ItemOptionData.IsPercent"/>에서 온다.</summary>
        public bool IsPercent { get; }

        /// <summary>툴팁 표시명(<see cref="ItemOptionData.Label"/>).</summary>
        public string Label { get; }

        /// <summary>
        /// 롤된 옵션 하나를 만든다.
        /// </summary>
        /// <param name="type">옵션 종류</param>
        /// <param name="value">롤된 값</param>
        /// <param name="isPercent">퍼센트 여부</param>
        /// <param name="label">표시명</param>
        public ItemOption(ItemOptionType type, float value, bool isPercent, string label)
        {
            Type = type;
            Value = value;
            IsPercent = isPercent;
            Label = label;
        }
    }
}
