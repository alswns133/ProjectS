using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Managers;

namespace ProjectS.Items
{
    /// <summary>
    /// 착용 장비들의 주 스탯(롤값 + 강화 보너스)과 옵션을 버킷별로 합산해 <see cref="EquipmentStats"/>를 만든다.
    /// 강화 보너스는 강화창과 같은 경로(<see cref="EnhanceBonusData.GetTotalBonus"/>, 장비종류×등급)를 재사용한다.
    /// 장착/해제 시 InventoryManager가 호출해 PlayerStats에 반영한다.
    /// </summary>
    public static class EquipmentStatCalculator
    {
        /// <summary>착용 장비 목록으로 스탯 보너스 합계를 계산한다.</summary>
        /// <param name="equipped">착용 중인 장비 인스턴스들(null·빈칸 안전)</param>
        public static EquipmentStats Compute(IEnumerable<EquipmentInstance> equipped)
        {
            EquipmentStats s = default;
            if (equipped == null) return s;

            foreach (EquipmentInstance eq in equipped)
            {
                if (eq?.Equipment == null || eq.Item == null) continue;

                // 주 스탯 = 롤된 기준값 + 강화 보너스. 무기=공격력, 방어구=방어도.
                int main = eq.RolledMainStat + EnhanceBonus(eq.Item.Category, eq.Item.Grade, eq.EnhanceStep);
                if (eq.Equipment.MainStatType == MainStatType.AttackDamage) s.FlatAD += main;
                else if (eq.Equipment.MainStatType == MainStatType.Defense) s.FlatDef += main;

                if (eq.Options == null) continue;
                foreach (ItemOption opt in eq.Options)
                    AddOption(ref s, opt);
            }

            return s;
        }

        // 강화창과 동일: (장비종류 × 등급) 보너스 행에서 단계별 총 보너스.
        private static int EnhanceBonus(ItemCategory category, ItemGrade grade, int step)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return 0;

            foreach (EnhanceBonusData row in json.EnhanceBonusDict.Values)
                if (row.Category == category && row.Grade == grade)
                    return row.GetTotalBonus(step);
            return 0;
        }

        private static void AddOption(ref EquipmentStats s, ItemOption opt)
        {
            switch (opt.Type)
            {
                case ItemOptionType.AttackFlat: s.FlatAD += opt.Value; break;
                case ItemOptionType.AttackPercent: s.PercentAD += opt.Value; break;
                case ItemOptionType.HealthFlat: s.FlatHp += opt.Value; break;
                case ItemOptionType.HealthPercent: s.PercentHp += opt.Value; break;
                case ItemOptionType.DefenseFlat: s.FlatDef += opt.Value; break;
                case ItemOptionType.DefensePercent: s.PercentDef += opt.Value; break;
                case ItemOptionType.CriticalRate: s.CritChance += opt.Value; break;
                case ItemOptionType.CriticalDamage: s.CritDamage += opt.Value; break;
                case ItemOptionType.DamageIncrease: s.DamageIncrease += opt.Value; break;
                case ItemOptionType.BossDamage: s.BossDamage += opt.Value; break;
                case ItemOptionType.DefensePenetration: s.DefensePen += opt.Value; break;
            }
        }
    }
}
