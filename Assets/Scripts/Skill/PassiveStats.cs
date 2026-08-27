namespace ProjectS.Skills
{
    /// <summary>
    /// 배분한 패시브 스킬 전체가 캐릭터 스탯에 더하는 보너스 합계. <see cref="ProjectS.Items.EquipmentStats"/>와
    /// 같은 방식으로, 기본 스탯과 별개로 들고 있다가 <see cref="ProjectS.Players.PlayerStats"/>의 getter가
    /// base·장비와 함께 합성한다(스킬 배분이 바뀔 때만 재계산).
    /// </summary>
    /// <remarks>
    /// 값은 <see cref="SkillState"/>가 <see cref="ProjectS.Data.SkillGrowthTable"/>의 EffectType/EffectPerLevel과
    /// 현재 패시브 레벨로 합산해 채운다. 퍼센트 계열은 비율(0.1 = +10%), 스태미나는 정수 가산.
    /// </remarks>
    public struct PassiveStats
    {
        /// <summary>전체 공격력 증가 비율(장비 PercentAD와 같은 자리에 가산).</summary>
        public float AttackPercent;

        /// <summary>전체 방어도 증가 비율.</summary>
        public float DefensePercent;

        /// <summary>최대 HP 증가 비율.</summary>
        public float HpPercent;

        /// <summary>치명타 확률 가산(0.03 = +3%p).</summary>
        public float CritChance;

        /// <summary>치명타 피해 배율 가산.</summary>
        public float CritDamage;

        /// <summary>최대 스태미나 정수 가산.</summary>
        public float StaminaFlat;

        /// <summary>방어력 관통 가산.</summary>
        public float Penetration;
    }
}
