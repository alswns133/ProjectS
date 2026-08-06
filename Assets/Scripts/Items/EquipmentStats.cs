namespace ProjectS.Items
{
    /// <summary>
    /// 착용 장비 전체가 캐릭터 스탯에 더하는 보너스 합계. 기본 스탯(테이블)과 별개로 들고 있다가
    /// <see cref="ProjectS.Players.PlayerStats"/>의 getter가 base와 합성한다(장비 변경 시에만 재계산).
    /// 옵션 종류에 SG·스태미나가 없어 그 둘은 장비 영향을 받지 않는다(필드 없음).
    /// </summary>
    public struct EquipmentStats
    {
        public float FlatAD;
        public float PercentAD;      // 0.1 = +10%
        public float FlatHp;
        public float PercentHp;
        public float FlatDef;
        public float PercentDef;
        public float CritChance;     // 기본 치확에 가산(0.05 = +5%p)
        public float CritDamage;     // 기본 치피 배율에 가산
        public float BossDamage;     // 보스 추가 피해(장비 옵션 전용)
        public float DefensePen;     // 방어력 관통(장비 옵션 전용)
        public float DamageIncrease; // 데미지 증가(장비 옵션 전용)
    }
}
