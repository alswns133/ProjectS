namespace ProjectS.Data
{
    /// <summary>
    /// 아이템 대분류. 어떤 부가 테이블(EquipmentData / ConsumableData)을 참조할지 결정한다.
    /// </summary>
    public enum ItemCategory
    {
        None = 0,
        Weapon,
        Armor,
        Consumable,
        Material
    }

    /// <summary>
    /// 아이템 등급. 옵션 개수와 수치 배율의 기준이 되지만,
    /// 배율(×1.0/1.1/1.25/1.5)은 이미 수치표에 반영되어 있으므로 런타임에서 다시 곱하지 않는다.
    /// </summary>
    public enum ItemGrade
    {
        Normal = 0,
        Magic,
        Rare,
        Relic
    }

    /// <summary>
    /// 장착 부위. 악세서리는 기획에서 제외됐다(무기 1 + 방어구 4 = 총 5슬롯).
    /// </summary>
    public enum EquipSlot
    {
        None = 0,
        Weapon,
        Top,
        Bottom,
        Gloves,
        Shoes
    }

    /// <summary>
    /// 무기 종류. 직업 제한은 이 값에서 파생된다(검=검사 전용, 총=거너 전용).
    /// 별도 RequiredClass를 두지 않는 이유는 두 필드가 어긋날 여지를 없애기 위해서다.
    /// 방어구·소비품은 None이며 직업 제한이 없다.
    /// </summary>
    public enum WeaponType
    {
        None = 0,
        Sword,
        Gun
    }

    /// <summary>
    /// 장비가 올려주는 주 스탯. 무기는 공격력, 방어구는 방어도 하나씩만 가진다.
    /// </summary>
    public enum MainStatType
    {
        None = 0,
        AttackDamage,
        Defense
    }

    /// <summary>
    /// 아이템 옵션 종류 11가지. 각 값이 어디에 어떻게 적용되는지는 데미지 계산식을 따른다.
    /// (깡 계열은 덧셈, 퍼 계열은 곱연산)
    /// </summary>
    public enum ItemOptionType
    {
        None = 0,
        AttackFlat,          // 깡공: 총 AD에 고정값 덧셈 (배율 계산 이후)
        AttackPercent,       // 공퍼: 총 AD에 곱연산
        HealthFlat,          // 깡체: 최대 HP에 고정값 덧셈
        HealthPercent,       // 체퍼: 최대 HP에 곱연산
        DefenseFlat,         // 깡방: 방어도에 고정값 덧셈
        DefensePercent,      // 방퍼: 방어도에 곱연산
        CriticalRate,        // 치확: 기본 치명타 확률에 추가
        CriticalDamage,      // 치피: 치명타 배율에 추가
        DamageIncrease,      // 피해량: 최종 데미지에 곱연산
        BossDamage,          // 보스추뎀: 보스 대상일 때만 최종 데미지에 곱연산
        DefensePenetration   // 방어력관통
    }
}
