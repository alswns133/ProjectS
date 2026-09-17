using System.Collections.Generic;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Managers;

namespace ProjectS.Items
{
    /// <summary>
    /// 세이브(<see cref="EquipmentSave"/>/<see cref="EquippedSave"/>)를 런타임 <see cref="EquipmentInstance"/>로
    /// 복원하는 <b>공용·순수</b> 헬퍼. 원래 <c>InventoryManager</c>의 private이었으나, 서버권위 스탯 도출
    /// (<see cref="ProjectS.Networking.ServerStatDeriver"/>)이 같은 복원을 서버에서도 해야 해서 한곳으로 모았다.
    ///
    /// <para><b>왜 공유하나.</b> "세이브 → 장비 인스턴스" 변환이 두 벌로 갈리면 클라와 서버가 다른 스탯을 만들어
    /// 데미지가 어긋난다. 이 단일 경로를 클라(<c>InventoryManager.RestoreFrom</c>)와 서버(도출기)가 함께 써서
    /// 옵션·강화·주스탯 계산이 항상 같게 한다.</para>
    /// </summary>
    public static class EquipmentSaveLoader
    {
        /// <summary>
        /// 착용 세이브(<see cref="CharacterSaveData.equipped"/>)를 <see cref="EquipmentInstance"/> 목록으로 복원한다.
        /// 스탯 도출(<see cref="EquipmentStatCalculator.Compute"/>)의 입력. 정의가 사라진 항목은 건너뛴다.
        /// </summary>
        /// <param name="save">복원할 캐릭터 세이브. null이거나 테이블 미준비면 빈 목록.</param>
        public static List<EquipmentInstance> BuildEquipped(CharacterSaveData save)
        {
            var list = new List<EquipmentInstance>();
            if (save?.equipped == null) return list;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return list;

            foreach (EquippedSave es in save.equipped)
            {
                if (es == null) continue;

                EquipmentInstance inst = BuildEquipment(json, es.tableId, es.enhanceStep, es.mainStat, es.options);
                if (inst != null) list.Add(inst);
            }
            return list;
        }

        /// <summary>
        /// 세이브 값으로 장비 인스턴스를 재구성한다. 정의(ItemData/EquipmentData)가 없으면 null(호출측이 skip).
        /// (원래 <c>InventoryManager.BuildEquipment</c> — 클라·서버 공용으로 이관.)
        /// </summary>
        public static EquipmentInstance BuildEquipment(JsonManager json, int tableId, int enhanceStep, int mainStat, List<ItemOptionSave> optionSaves)
        {
            ItemData item = json.Get<ItemData>(tableId);
            EquipmentData equip = json.Get<EquipmentData>(tableId);
            if (item == null || equip == null) return null;

            List<ItemOption> options = RebuildOptions(item.Grade, optionSaves);
            int rolled = mainStat > 0 ? mainStat : -1;   // 구세이브(0)는 생성자에서 MainStatBase로 폴백
            return new EquipmentInstance(item, equip, enhanceStep, rolled, options);
        }

        /// <summary>
        /// 세이브된 옵션을 런타임 <see cref="ItemOption"/>으로 복원한다. 퍼센트·라벨은 ItemOptionData(타입×등급)에서 재조립.
        /// (원래 <c>InventoryManager.RebuildOptions</c> — 클라·서버 공용으로 이관.)
        /// </summary>
        public static List<ItemOption> RebuildOptions(ItemGrade grade, List<ItemOptionSave> saved)
        {
            var list = new List<ItemOption>(saved?.Count ?? 0);
            if (saved == null) return list;

            JsonManager json = JsonManager.Instance;
            foreach (ItemOptionSave os in saved)
            {
                if (os == null) continue;

                var type = (ItemOptionType)os.type;
                bool isPercent = false;
                string label = type.ToString();

                if (json != null && json.IsReady)
                    foreach (ItemOptionData od in json.ItemOptionDict.Values)
                        if (od.OptionType == type && od.Grade == grade)
                        {
                            isPercent = od.IsPercent;
                            label = od.Label;
                            break;
                        }

                list.Add(new ItemOption(type, os.value, isPercent, label));
            }
            return list;
        }
    }
}
