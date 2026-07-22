using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 소비품만 가지는 정보. Index는 같은 아이템의 ItemData.Index와 동일하다.
    /// 즉시 회복(메디박스)과 지속 회복(힐링 젤리)을 DurationSec 하나로 구분한다.
    /// 타입 enum을 따로 두지 않는 이유는 "지속시간이 0이면 즉시"가 데이터로 자명하기 때문이다.
    /// </summary>
    [Serializable]
    public class ConsumableData : IDataRow
    {
        public int Index;

        /// <summary>즉시형은 총 회복량, 지속형은 초당 회복량.</summary>
        public int HealAmount;

        /// <summary>0이면 즉시 회복. 양수면 그 시간 동안 초당 HealAmount만큼 회복한다.</summary>
        public float DurationSec;

        public float CooldownSec;

        int IDataRow.Index => Index;

        /// <summary>
        /// 회복량이 0이면 사용해도 아무 일이 없으므로 행을 제외한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (HealAmount <= 0)
            {
                error = $"Index {Index}: HealAmount가 0 이하 (효과 없음, 제외됨)";
                return false;
            }

            if (DurationSec < 0f) DurationSec = 0f;
            if (CooldownSec < 0f) CooldownSec = 0f;

            error = null;
            return true;
        }

        /// <summary>실제로 회복되는 총량. 지속형은 초당 회복량 × 지속시간이다.</summary>
        public int TotalHeal => DurationSec > 0f ? (int)(HealAmount * DurationSec) : HealAmount;
    }
}
