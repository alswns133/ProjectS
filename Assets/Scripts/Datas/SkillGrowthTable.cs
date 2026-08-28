using System;

namespace ProjectS.Data
{
    /// <summary>액티브(4칸)와 패시브(7칸)를 가른다. 스킬창 좌측 두 그룹에 대응한다.</summary>
    public enum SkillKind
    {
        /// <summary>액티브 스킬(스킬1~3 + 각성기). SkillTable에 데미지 행이 있다.</summary>
        Active,

        /// <summary>패시브 스킬. 상시 효과라 SkillTable 데미지 행이 없을 수 있다.</summary>
        Passive,
    }

    /// <summary>
    /// 패시브가 올리는 스탯 종류. 퍼센트 계열(Percent)은 비율, 그 외는 정수 값이다.
    /// 액티브(데미지 스킬)는 <see cref="SkillEffectType.None"/>.
    /// </summary>
    /// <remarks>
    /// <b>표시·데이터용 분류일 뿐, 아직 스탯에 실제로 적용하는 시스템은 없다(2026-08-26).</b>
    /// 적용은 배운 레벨 저장(LearnedSkillIds/레벨)과 함께 붙을 다음 시스템의 몫이다
    /// (읽는 쪽은 <c>ProjectS.Skills.SkillProgress.GetLevel</c>의 TODO 참고).
    /// </remarks>
    public enum SkillEffectType
    {
        /// <summary>효과 없음(액티브 스킬).</summary>
        None,

        /// <summary>전체 공격력 +x%/레벨.</summary>
        AttackPercent,

        /// <summary>치명타 확률 +x/레벨(비율).</summary>
        CritChance,

        /// <summary>최대 스태미나 +x/레벨(정수).</summary>
        StaminaMax,

        /// <summary>치명타 피해 +x%/레벨.</summary>
        CritDamagePercent,

        /// <summary>전체 방어도 +x%/레벨.</summary>
        DefensePercent,

        /// <summary>최대 HP +x%/레벨.</summary>
        HpPercent,

        /// <summary>방어력 관통 +x%/레벨.</summary>
        ArmorPenetrationPercent,
    }

    /// <summary>
    /// 스킬 "성장·배분" 행. 스킬창(K)이 레벨업/SP 배분에 쓴다. 한 방의 데미지 수치는
    /// <see cref="SkillTable"/>가 소유하고(역할 분리), 이 테이블은 <see cref="SkillId"/>로 그 행과 이어진다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 왜 나눴나: SkillTable은 "한 타격의 계수·게이지·쿨타임"이고, 이 테이블은 "얼마나 컸고 SP가 얼마 드나"라
    /// 축이 다르다. 합치면 레벨 없는 평타/피니시 행까지 성장 컬럼을 달아야 해 지저분해진다.
    /// (2026-08-26 신설 — 기존 스킬창 UI가 요구하는 데이터에 맞춰 최소 컬럼으로 시작)
    /// </para>
    /// <para>
    /// <b>ID 규칙(docs/ID_NUMBERING.md):</b> 캐릭터 스킬 3자리 <c>[캐릭터1][스킬2]</c> = <c>CharacterId*100 + 스킬번호</c>.
    /// 액티브 <c>01~04</c>(04=각성기), 패시브 <c>11~17</c>. 예) 검사 액티브1=101, 검사 각성기=104, 검사 패시브1=111 / 거너 패시브1=211.
    /// </para>
    /// </remarks>
    [Serializable]
    public class SkillGrowthTable : IDataRow
    {
        /// <summary>스킬 식별자. 액티브는 <see cref="SkillTable"/>.SkillId와 동일(외래키), 패시브는 11~17 대역.</summary>
        public int SkillId;

        /// <summary>소속 캐릭터(검사=1/거너=2). 스킬창이 현재 캐릭터 것만 걸러 보여준다.</summary>
        public int CharacterId;

        /// <summary>액티브/패시브 구분(좌측 두 그룹).</summary>
        public SkillKind Kind;

        /// <summary>표시 이름(우측 프리뷰의 "스킬 이름"). 지금은 원문 문자열(로컬라이즈 키로 바뀔 수 있음).</summary>
        public string Name;

        /// <summary>표시 설명(우측 프리뷰의 "스킬 설명").</summary>
        public string Description;

        /// <summary>아이콘 어드레서블 주소. 아이템 아이콘과 같은 로더 경로.</summary>
        public string IconAddress;

        /// <summary>스킬 소개 영상/이미지 주소. 영상 재생은 후속 작업이라 지금은 이미지 프리뷰 자리로만 쓴다.</summary>
        public string PreviewMediaAddress;

        /// <summary>최대 레벨(스테퍼의 "/N"). 스크린샷 기준 5.</summary>
        public int MaxLevel;

        /// <summary>
        /// 시작(바닥) 레벨. <b>액티브는 1</b>(해금되면 1레벨부터), <b>패시브는 0</b>(찍기 전엔 <c>0/5</c>에서 시작).
        /// SP는 이 레벨을 넘어 올린 만큼만 든다. <see cref="Kind"/>에서 유도하므로 JSON에 적지 않는다.
        /// </summary>
        public int StartLevel => Kind == SkillKind.Active ? 1 : 0;

        /// <summary>
        /// 레벨업 SP 비용. 인덱스 <c>i</c> = <c>(StartLevel+i) → (StartLevel+i+1)</c>로 올리는 비용.
        /// 길이는 <c>MaxLevel-StartLevel</c>. 비었거나 짧으면 Validate가 부족분을 1로 채운다(현재 전부 1).
        /// </summary>
        public int[] SpCostPerLevel;

        /// <summary>패시브가 올리는 스탯 종류(액티브는 None). 데이터·표시용 — 아직 스탯 적용 시스템은 없다.</summary>
        public SkillEffectType EffectType;

        /// <summary>
        /// 레벨당 효과량. 퍼센트 계열은 비율(<c>0.02 = +2%/레벨</c>), 정수 계열은 값(<c>10 = +10/레벨</c>).
        /// 최대치 = <c>EffectPerLevel × (MaxLevel - StartLevel)</c>. 적용은 후속 시스템 몫(값만 보관).
        /// </summary>
        public float EffectPerLevel;

        /// <summary>
        /// 레벨별 계수(데미지) 배율. 인덱스 <c>i</c> = 레벨 <c>i+1</c>의 배율. 길이는 <c>MaxLevel</c>.
        /// </summary>
        /// <remarks>
        /// <b>기본값(<see cref="SkillTable"/>.Coef)은 레벨 1 기준</b>이므로 인덱스 0(=Lv1)은 반드시 1.0이다
        /// (아니면 이중 계산). Validate가 <c>[0]=1.0</c>으로 강제하고 길이를 <c>MaxLevel</c>로 맞춘다.
        /// 런타임 최종 계수 = <c>SkillTable.Coef × CoefMultiplierPerLevel[현재레벨-1]</c>
        /// (<c>ProjectS.Skills.SkillProgress</c>가 계산, <c>PlayerCombat</c>이 사용).
        /// 패시브처럼 데미지가 없는 스킬은 값이 무시되므로 전부 1.0으로 둔다.
        /// </remarks>
        public float[] CoefMultiplierPerLevel;

        /// <summary>창 안 배치 순서(작을수록 앞). 같은 그룹 안에서만 의미 있다.</summary>
        public int SlotOrder;

        int IDataRow.Index => SkillId;

        /// <summary>
        /// 넘버링·필수 수치를 검사·보정한다. ID가 인코딩한 캐릭터(<c>SkillId/100</c>)와 <see cref="CharacterId"/>가
        /// 어긋나면(오타) 행을 탈락시켜 로딩 시점에 거른다(ID_NUMBERING 원칙 3).
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            // CharacterId 미입력(0)은 ID 앞자리에서 유도한다(같은 사실을 한 곳만 적어도 되게).
            if (CharacterId <= 0) CharacterId = SkillId / 100;

            if (SkillId <= 0 || SkillId / 100 != CharacterId)
            {
                error = $"SkillGrowthTable {SkillId}: ID 앞자리(캐릭터)와 CharacterId({CharacterId}) 불일치 (제외됨)";
                return false;
            }

            // 레벨이 1 미만이면 스테퍼가 성립하지 않는다.
            if (MaxLevel < 1) MaxLevel = 1;

            // 비용 배열을 (MaxLevel-StartLevel) 길이로 정규화한다(부족분 = 1). 올릴 구간이 없으면 빈 배열.
            // 패시브(StartLevel 0)는 5칸, 액티브(StartLevel 1)는 4칸이 된다.
            int need = MaxLevel - StartLevel;
            if (need < 0) need = 0;
            if (SpCostPerLevel == null || SpCostPerLevel.Length != need)
            {
                int[] fixedCost = new int[need];
                for (int i = 0; i < need; i++)
                {
                    int v = (SpCostPerLevel != null && i < SpCostPerLevel.Length) ? SpCostPerLevel[i] : 1;
                    fixedCost[i] = v < 0 ? 0 : v;   // 음수 비용은 SP가 늘어나 버리므로 0으로 자른다.
                }
                SpCostPerLevel = fixedCost;
            }

            // 계수 배율을 길이 MaxLevel로 정규화한다. 부족분은 직전 값을 이어(성장 곡선이 끊기지 않게),
            // 값이 아예 없으면 1.0. Lv1(인덱스 0)은 base가 이미 그 값이라 반드시 1.0으로 강제한다.
            float[] mult = new float[MaxLevel];
            float prev = 1f;
            for (int i = 0; i < MaxLevel; i++)
            {
                float v = (CoefMultiplierPerLevel != null && i < CoefMultiplierPerLevel.Length)
                    ? CoefMultiplierPerLevel[i] : prev;
                if (v < 0f) v = 0f;   // 음수 배율은 회복이 되어버리므로 자른다.
                mult[i] = v;
                prev = v;
            }
            mult[0] = 1f;   // Lv1 = base 기준(이중 계산 방지)
            CoefMultiplierPerLevel = mult;

            error = null;
            return true;
        }
    }
}
