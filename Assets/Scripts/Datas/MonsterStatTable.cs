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

        /// <summary>
        /// 풀 HP일 때 표시할 줄(세그먼트) 수. 기획자가 "이 보스는 몇 줄"인지 직접 넣는다(예: 2000 HP를 10줄).
        /// 코드가 한 줄당 HP를 <c>MaxHp / SegmentCount</c>로 계산하고, 남은 줄 수(X N)와 현재 줄 채움 비율을 그린다.
        /// 0이면 세그먼트 없이 단일 바(줄 카운트 미표시)로 취급한다. 보스 행에만 의미가 있고, 일반몹 행은 0으로 둔다.
        /// </summary>
        public int SegmentCount;

        /// <summary>
        /// 그로기(무력화) 게이지 최대치. 스킬의 그로기 데미지가 이 값을 깎고, 0이 되면 무력화된다.
        /// 0 이하면 그로기 없음(EnemyGroggy가 붙어 있어도 사실상 비활성). 보스 행에만 의미가 있다.
        /// </summary>
        public float GroggyMax;

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
            if (SegmentCount < 0) SegmentCount = 0;   // 음수는 단일 바로 취급
            if (GroggyMax < 0f) GroggyMax = 0f;       // 음수는 그로기 없음으로 취급

            error = null;
            return true;
        }
    }
}
