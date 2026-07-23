using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 플레이어 캐릭터별 고유 정보 행. PlayerStats가 characterId로 조회한다.
    /// HP·공격력·방어력은 레벨에 따라 자라는 값이라 여기 없고 PlayerLevelTable이 소유한다
    /// → 캐릭터가 늘어도 성장 곡선은 한 벌만 유지된다.
    /// </summary>
    [Serializable]
    public class PlayerStatTable : IDataRow
    {
        public int CharacterId;
        public string NameKey;

        /// <summary>치명타 확률(0~1). 레벨이 아니라 캐릭터·아이템·패시브로만 변한다.</summary>
        public float CritChance;

        /// <summary>치명타 배율. 기본 1.5.</summary>
        public float CritDamage;

        /// <summary>
        /// 이 캐릭터가 쓰는 스킬 묶음의 접두사(검사 "SW", 거너 "GN").
        /// SkillTable에서 NameKey가 "<c>{접두사}_</c>"로 시작하는 행들이 이 캐릭터의 스킬이다.
        /// 스킬 ID를 캐릭터 ID로부터 산술로 유도하지 않는 이유: 캐릭터가 늘거나 ID가 재배치돼도
        /// 데이터만 고치면 되고 코드의 계산식을 따라 고칠 필요가 없기 때문.
        /// </summary>
        public string SkillSetPrefix;

        int IDataRow.Index => CharacterId;

        /// <summary>
        /// 접두사가 없으면 이 캐릭터의 스킬을 하나도 찾을 수 없어 스킬이 전부 막히므로 행을 탈락시킨다.
        /// 치명타 값은 데이터 입력 실수를 방어하기 위해 안전 범위로 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(SkillSetPrefix))
            {
                error = $"CharacterId {CharacterId}: SkillSetPrefix가 비어있음 (제외됨)";
                return false;
            }

            // 확률은 0~1을 벗어나면 계산이 무의미해진다.
            CritChance = Math.Clamp(CritChance, 0f, 1f);

            // 치명타 배율이 1 미만이면 치명타가 오히려 손해가 된다 → 최소 1로 보정.
            if (CritDamage < 1f) CritDamage = 1f;

            error = null;
            return true;
        }
    }
}
