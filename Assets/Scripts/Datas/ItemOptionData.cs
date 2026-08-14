using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 아이템 옵션의 등급별·레벨별 기준값. 한 행이 기획 시트의 한 행과 1:1로 대응한다.
    /// (등급 × 옵션 × 레벨)을 전부 펴면 231행이 되고 밸런스 조정 때마다 흩어진 칸을 고쳐야 하므로,
    /// 레벨 축만 배열로 접어 33행(11옵션 × 3등급)으로 유지한다.
    /// </summary>
    [Serializable]
    public class ItemOptionData : IDataRow
    {
        public int Index;
        public ItemOptionType OptionType;
        public ItemGrade Grade;

        /// <summary>툴팁에 그대로 찍히는 표시명. 코드 수정 없이 문구를 바꾸기 위해 데이터로 둔다.</summary>
        public string Label;

        /// <summary>true면 값이 퍼센트다. 표시 포맷과 계산 방식(곱연산) 모두 이 값으로 갈린다.</summary>
        public bool IsPercent;

        /// <summary>레벨 슬롯 8칸. 순서는 ItemLevelTier.Levels와 같다.</summary>
        public float[] Values;

        int IDataRow.Index => Index;

        /// <summary>
        /// 배열 길이가 어긋나면 조회할 때마다 엉뚱한 레벨의 값이 나오거나 예외가 난다.
        /// 조용히 틀린 수치를 쓰는 것보다 행을 빼는 편이 낫다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (OptionType == ItemOptionType.None)
            {
                error = $"Index {Index}: OptionType이 None (제외됨)";
                return false;
            }

            if (Values == null || Values.Length != ItemLevelTier.SlotCount)
            {
                error = $"Index {Index}: Values 길이가 {Values?.Length ?? 0} (기대값 {ItemLevelTier.SlotCount}, 제외됨)";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Label)) Label = OptionType.ToString();

            error = null;
            return true;
        }

        /// <summary>
        /// 해당 레벨의 기준값을 돌려준다. 실제 아이템 수치는 여기에 드랍 시 랜덤(0.95~1.05)을 곱한 값이다.
        /// 퍼센트 옵션의 테이블 값은 퍼센트 포인트(예: 2 = 2%)로 적혀 있지만, 런타임 소비자
        /// (툴팁 표시·<see cref="ProjectS.Items.EquipmentStatCalculator"/> 곱연산)는 모두 0~1 비율을
        /// 기대하므로 여기서 /100 해 비율로 맞춘다. 이 변환을 빼면 2%가 200%로 적용된다.
        /// </summary>
        /// <param name="level">아이템 레벨(5단위)</param>
        /// <returns>기준값. 퍼센트 옵션이면 비율(0~1), 정의되지 않은 레벨이면 0</returns>
        public float GetBaseValue(int level)
        {
            int slot = ItemLevelTier.ToSlot(level);
            if (slot < 0) return 0f;
            return IsPercent ? Values[slot] / 100f : Values[slot];
        }
    }
}
