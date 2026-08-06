using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Managers;

namespace ProjectS.Items
{
    /// <summary>
    /// 새 장비 드랍 시 인스턴스별 주 스탯·옵션을 랜덤으로 굴려 <see cref="EquipmentInstance"/>를 만든다.
    /// 주 스탯은 MainStatBase에, 옵션은 <see cref="ItemOptionData.GetBaseValue"/>에 드랍 랜덤(0.95~1.05)을 곱한다.
    /// 옵션 종류는 아이템 등급에 해당하는 옵션 풀에서 중복 없이 <see cref="EquipmentData.OptionCount"/>개를 뽑는다.
    /// (세이브 복원은 롤하지 않고 저장된 값으로 EquipmentInstance 생성자를 직접 부른다.)
    /// </summary>
    public static class ItemOptionRoller
    {
        private const float MinRoll = 0.95f;
        private const float MaxRoll = 1.05f;

        /// <summary>새 드랍 장비 인스턴스를 롤해서 만든다.</summary>
        /// <param name="item">공통 아이템 행</param>
        /// <param name="equip">장비 고유 행</param>
        /// <returns>롤된 주 스탯·옵션이 담긴 인스턴스</returns>
        public static EquipmentInstance Create(ItemData item, EquipmentData equip)
        {
            int mainStat = Mathf.RoundToInt(equip.MainStatBase * Random.Range(MinRoll, MaxRoll));
            List<ItemOption> options = RollOptions(item, equip);
            return new EquipmentInstance(item, equip, 0, mainStat, options);
        }

        // 등급 풀에서 중복 없이 OptionCount개를 뽑아 값까지 롤한다.
        private static List<ItemOption> RollOptions(ItemData item, EquipmentData equip)
        {
            var result = new List<ItemOption>();
            if (equip.OptionCount <= 0) return result;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return result;

            // 이 등급에서 뽑을 수 있는 옵션 행 풀.
            var pool = new List<ItemOptionData>();
            foreach (ItemOptionData od in json.ItemOptionDict.Values)
                if (od.Grade == item.Grade && od.OptionType != ItemOptionType.None)
                    pool.Add(od);

            // Fisher-Yates 셔플로 앞에서부터 OptionCount개 = 중복 없는 랜덤 선택.
            for (int i = 0; i < pool.Count; i++)
            {
                int j = Random.Range(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int take = Mathf.Min(equip.OptionCount, pool.Count);
            for (int i = 0; i < take; i++)
            {
                ItemOptionData od = pool[i];
                float value = od.GetBaseValue(item.Level) * Random.Range(MinRoll, MaxRoll);
                result.Add(new ItemOption(od.OptionType, value, od.IsPercent, od.Label));
            }
            return result;
        }
    }
}
