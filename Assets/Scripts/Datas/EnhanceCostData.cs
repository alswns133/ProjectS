using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 강화 시도 1회의 성공률과 비용. 등급·장비종류와 무관하게 단계로만 결정되므로
    /// EnhanceBonusData와 분리해 9행으로 관리한다.
    /// 실패해도 강화 수치는 유지되고 재화·재료만 소모된다(하락 없음).
    /// </summary>
    [Serializable]
    public class EnhanceCostData : IDataRow
    {
        public int Index;

        /// <summary>강화 시도 단계(1~9). Index와 같은 값이지만 의미를 드러내기 위해 따로 둔다.</summary>
        public int Step;

        /// <summary>성공 확률(0~1).</summary>
        public float SuccessRate;

        public int ZenyCost;
        public int LowMaterial;
        public int HighMaterial;

        int IDataRow.Index => Index;

        /// <summary>
        /// 성공률이 0이면 영원히 성공하지 못하는 단계가 되므로 행을 제외한다.
        /// 범위를 벗어난 확률은 데이터 입력 실수로 보고 0~1로 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (Step < 1 || Step > EnhanceBonusData.MaxStep)
            {
                error = $"Index {Index}: Step {Step}이 범위(1~{EnhanceBonusData.MaxStep}) 밖 (제외됨)";
                return false;
            }

            if (SuccessRate <= 0f)
            {
                error = $"Index {Index}: SuccessRate가 0 이하 (성공 불가, 제외됨)";
                return false;
            }

            if (SuccessRate > 1f) SuccessRate = 1f;

            if (ZenyCost < 0) ZenyCost = 0;
            if (LowMaterial < 0) LowMaterial = 0;
            if (HighMaterial < 0) HighMaterial = 0;

            error = null;
            return true;
        }
    }
}
