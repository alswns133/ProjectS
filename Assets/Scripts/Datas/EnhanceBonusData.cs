using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 강화 단계별 주 스탯 증가분. 무기 4행 + 방어구 4행, 총 8행이면 전 구간이 커버된다.
    /// 강화 테이블 전 구간을 대조한 결과 증가분이 아이템 레벨과 무관하기 때문이다.
    /// (예: 노말 무기 Lv1 20→+9 7,230 / Lv25 2,810→+9 10,020, 둘 다 증가분 7,210 동일)
    /// 그래서 아이템 행마다 10칸짜리 강화 배열을 들고 있을 필요가 없다.
    /// </summary>
    [Serializable]
    public class EnhanceBonusData : IDataRow
    {
        public int Index;

        /// <summary>Weapon 또는 Armor. 무기는 감속형, 방어구는 가속형 곡선이라 반드시 구분해야 한다.</summary>
        public ItemCategory Category;

        public ItemGrade Grade;

        /// <summary>+1~+9 각 단계의 증가분(누적 아님). 길이 9.</summary>
        public int[] Bonus;

        /// <summary>강화 최대 단계. 전 등급 공통 +9.</summary>
        public const int MaxStep = 9;

        int IDataRow.Index => Index;

        /// <summary>
        /// 배열 길이가 어긋나면 특정 강화 단계에서만 수치가 틀어져 발견이 늦어진다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (Category != ItemCategory.Weapon && Category != ItemCategory.Armor)
            {
                error = $"Index {Index}: Category가 {Category} (장비가 아님, 제외됨)";
                return false;
            }

            if (Bonus == null || Bonus.Length != MaxStep)
            {
                error = $"Index {Index}: Bonus 길이가 {Bonus?.Length ?? 0} (기대값 {MaxStep}, 제외됨)";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// +0부터 지정 단계까지의 누적 증가분. 주 스탯 표시값 계산에 쓴다.
        /// </summary>
        /// <param name="step">강화 단계(0~9). 범위를 벗어나면 잘라낸다.</param>
        /// <returns>누적 증가분. step이 0이면 0</returns>
        public int GetTotalBonus(int step)
        {
            if (step > MaxStep) step = MaxStep;

            int total = 0;
            for (int i = 0; i < step; i++) total += Bonus[i];
            return total;
        }
    }
}
