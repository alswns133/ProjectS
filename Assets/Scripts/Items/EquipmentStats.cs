namespace ProjectS.Items
{
    /// <summary>
    /// 착용 장비 전체가 캐릭터 스탯에 더하는 보너스 합계. 기본 스탯(테이블)과 별개로 들고 있다가
    /// <see cref="ProjectS.Players.PlayerStats"/>의 getter가 base와 합성한다(장비 변경 시에만 재계산).
    /// 옵션 종류에 SG·스태미나가 없어 그 둘은 장비 영향을 받지 않는다(필드 없음).
    /// </summary>
    public struct EquipmentStats
    {
        /// <summary>고정 공격력 합(깡공). 기본 AD에 더한 뒤 퍼센트 배율을 곱한다.</summary>
        public float FlatAD;

        /// <summary>공격력 퍼센트 합(0.1 = +10%). 패시브 공퍼와 합산해 (1 + 합)으로 곱한다.</summary>
        public float PercentAD;

        /// <summary>고정 최대 HP 합(깡체).</summary>
        public float FlatHp;

        /// <summary>최대 HP 퍼센트 합(0.1 = +10%).</summary>
        public float PercentHp;

        /// <summary>고정 방어도 합(깡방).</summary>
        public float FlatDef;

        /// <summary>방어도 퍼센트 합(0.1 = +10%).</summary>
        public float PercentDef;

        /// <summary>치명타 확률 가산(0.05 = +5%p). 캐릭터 기본 치확에 더한다.</summary>
        public float CritChance;

        /// <summary>치명타 배율 가산. 캐릭터 기본 치피 배율에 더한다.</summary>
        public float CritDamage;

        /// <summary>보스 추가 피해 비율(장비 옵션 전용). 대상이 보스일 때 (1 + 값)으로 곱한다.</summary>
        public float BossDamage;

        /// <summary>방어력 관통 비율(장비 옵션 전용). 대상 경감률에서 뺀다.</summary>
        public float DefensePen;

        /// <summary>데미지 증가 비율(장비 옵션 전용). 최종 피해에 (1 + 값)으로 곱한다.</summary>
        public float DamageIncrease;
    }
}
