using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 레벨별 플레이어 성장 수치 행. 레벨 자체가 키다.
    /// 캐릭터 구분이 없다 = 모든 캐릭터가 같은 성장 곡선을 공유한다는 뜻이다.
    /// 캐릭터마다 곡선이 갈라지면 그때 CharacterId 열을 추가하고 키를 복합키로 바꾼다.
    /// </summary>
    [Serializable]
    public class PlayerLevelTable : IDataRow
    {
        public int Level;

        /// <summary>이 레벨의 기본 AD. 장비·패시브가 없는 현재는 이 값이 곧 총 AD다.</summary>
        public int BaseAD;

        public int BaseHP;
        public int BaseDefense;

        /// <summary>이 레벨에서 다음 레벨로 가는 데 필요한 경험치.</summary>
        public int RequiredExp;

        int IDataRow.Index => Level;

        /// <summary>
        /// BaseHP가 0 이하면 그 레벨에 도달하는 순간 즉시 사망 판정이 나므로 행을 탈락시킨다.
        /// 나머지는 음수만 0으로 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (BaseHP <= 0)
            {
                error = $"Level {Level}: BaseHP가 0 이하 (제외됨)";
                return false;
            }

            if (BaseAD < 0) BaseAD = 0;
            if (BaseDefense < 0) BaseDefense = 0;
            if (RequiredExp < 0) RequiredExp = 0;

            error = null;
            return true;
        }
    }
}
