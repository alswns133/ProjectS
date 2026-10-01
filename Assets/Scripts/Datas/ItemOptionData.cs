using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 아이템 옵션의 등급별 후보값. 한 행이 기획 시트의 한 행과 1:1로 대응한다(33행 = 11옵션 × 3등급).
    /// 옵션 값은 아이템 레벨과 무관하고 등급으로만 갈린다 — 같은 등급이면 Lv1 장비든 Lv35 장비든
    /// 같은 후보에서 뽑는다. (예전에는 8칸을 레벨 1~35 슬롯으로 읽어 Lv35 장비가 늘 마지막 칸,
    /// 즉 최대치만 받았다. 8칸은 레벨 축이 아니라 추첨 후보다.)
    /// </summary>
    [Serializable]
    public class ItemOptionData : IDataRow
    {
        /// <summary>행 ID(테이블 키).</summary>
        public int Index;

        /// <summary>옵션 종류(치확·치피·보스뎀 등). 스탯 합산 시 어느 칸에 더할지를 정한다.</summary>
        public ItemOptionType OptionType;

        /// <summary>이 옵션 행이 적용되는 장비 등급.</summary>
        public ItemGrade Grade;

        /// <summary>툴팁에 그대로 찍히는 표시명. 코드 수정 없이 문구를 바꾸기 위해 데이터로 둔다.</summary>
        public string Label;

        /// <summary>true면 값이 퍼센트다. 표시 포맷과 계산 방식(곱연산) 모두 이 값으로 갈린다.</summary>
        public bool IsPercent;

        /// <summary>
        /// 이 등급 옵션의 추첨 후보 8칸. 드랍 시 한 칸을 균등하게 뽑는다.
        /// 같은 값을 여러 칸에 적으면 그 값이 더 자주 나온다 — 기획이 칸 배치로 확률 가중치를 준다
        /// (예: [1,1,2,2,3,3,5,6]이면 1·2·3이 각 25%, 5·6이 각 12.5%). 순서는 의미가 없다.
        /// </summary>
        public float[] Values;

        int IDataRow.Index => Index;

        /// <summary>
        /// 후보 칸 수가 시트와 어긋나면(칸 누락·밀림) 기획이 의도한 확률 가중치가 조용히 틀어진다.
        /// 틀린 분포로 드랍되는 것보다 행을 빼는 편이 낫다.
        /// (길이 기준으로 ItemLevelTier.SlotCount를 쓰는 건 시트 칸 수가 우연히 8로 같아서다 — 레벨과는 무관.)
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
        /// <see cref="Values"/> 후보 중 한 칸을 균등하게 뽑아 옵션 값을 돌려준다. 새 드랍 시
        /// <see cref="ProjectS.Items.ItemOptionRoller"/>만 호출하며, 세이브 복원은 저장된 값을 쓰므로 다시 굴리지 않는다.
        /// 퍼센트 옵션의 테이블 값은 퍼센트 포인트(예: 2 = 2%)로 적혀 있지만, 런타임 소비자
        /// (툴팁 표시·<see cref="ProjectS.Items.EquipmentStatCalculator"/> 곱연산)는 모두 0~1 비율을
        /// 기대하므로 여기서 /100 해 비율로 맞춘다. 이 변환을 빼면 2%가 200%로 적용된다.
        /// </summary>
        /// <returns>뽑힌 옵션 값. 퍼센트 옵션이면 비율(0~1), 후보가 없으면 0</returns>
        public float GetRollValue()
        {
            if (Values == null || Values.Length == 0) return 0f;
            int index = UnityEngine.Random.Range(0, Values.Length);
            return IsPercent ? Values[index] / 100f : Values[index];
        }
    }
}
