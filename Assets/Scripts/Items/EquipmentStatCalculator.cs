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
                int main = MainStat(eq);
                if (eq.Equipment.MainStatType == MainStatType.AttackDamage) s.FlatAD += main;
                else if (eq.Equipment.MainStatType == MainStatType.Defense) s.FlatDef += main;

                if (eq.Options == null) continue;
                foreach (ItemOption opt in eq.Options)
                    AddOption(ref s, opt);
            }

            return s;
        }

        /// <summary>
        /// 장비 1개의 주 스탯 실제값(롤된 기준값 + 강화 보너스)을 돌려준다. 무기=공격력, 방어구=방어도.
        /// 툴팁·상세 표시가 전투 스탯·강화창과 <b>같은 값</b>을 쓰게 하는 단일 계산 경로다 — 강화하면 이 값이 오른다.
        /// (툴팁이 RolledMainStat만 찍어 강화 보너스가 안 보이던 것을 이 경로로 통일.)
        /// </summary>
        /// <param name="eq">장비 인스턴스(장비 아님·데이터 미로드면 0)</param>
        /// <returns>강화 반영 주 스탯</returns>
        public static int MainStat(EquipmentInstance eq)
        {
            if (eq?.Equipment == null || eq.Item == null) return 0;
            return eq.RolledMainStat + EnhanceBonus(eq.Item.Category, eq.Item.Grade, eq.EnhanceStep);
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
