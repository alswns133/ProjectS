using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 몬스터 종류별 스탯 행. EnemyStats가 monsterId로 조회한다.
    /// </summary>
    [Serializable]
    public class MonsterStatTable : IDataRow
    {
        public int MonsterId;
        public string NameKey;
        public int DungeonId;

        /// <summary>난이도. 1=노말, 2=하드, 3=매니악.</summary>
        public int Difficulty;

        /// <summary>MELEE / RANGED. 현재 코드 분기에는 쓰지 않고 기획 참고용이다.</summary>
        public string AttackType;

        public int MaxHp;

        /// <summary>몬스터의 총 AD. 공격 패턴의 계수와 곱해져 피해가 된다.</summary>
        public float AttackPower;

        public float Defense;

        /// <summary>
        /// ★ 계산에 쓰지 않는다. Defense로부터 <c>Defense/(Defense+2000)</c>로 유도되는 파생값이며,
        /// 기획이 시트에서 경감률을 눈으로 확인하려고 넣어둔 열이다.
        /// 실제 경감은 DamageCalculator가 Defense에서 직접 계산하므로,
        /// 이 값을 또 곱하면 경감이 이중 적용된다.
        /// </summary>
        public float DamageReduction;

        /// <summary>보스 여부. 공격자의 보스 추가뎀% 적용 조건이 된다.</summary>
        public bool IsBoss;

        int IDataRow.Index => MonsterId;

        /// <summary>
        /// MaxHp가 0 이하면 스폰 즉시 사망 판정이 나므로 행을 탈락시킨다.
        /// 나머지는 데이터 입력 실수를 방어하기 위해 음수만 0으로 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (MaxHp <= 0)
            {
                error = $"MonsterId {MonsterId}: MaxHp가 0 이하 (제외됨)";
                return false;
            }

            if (AttackPower < 0f) AttackPower = 0f;
            if (Defense < 0f) Defense = 0f;

            error = null;
            return true;
        }
    }
}
