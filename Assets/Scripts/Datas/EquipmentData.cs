using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 장비(무기·방어구)만 가지는 정보. Index는 같은 아이템의 ItemData.Index와 동일하다.
    /// 강화 보너스는 여기 두지 않는다. 강화 증가폭이 아이템 레벨과 무관하다는 것이 확인돼(전 구간 대조),
    /// EnhanceBonusData 한 장으로 (장비종류 × 등급 × 단계)만 관리하면 충분하기 때문이다.
    /// </summary>
    [Serializable]
    public class EquipmentData : IDataRow
    {
        public int Index;
        public EquipSlot EquipSlot;

        /// <summary>무기 종류이자 직업 제한. 방어구는 None.</summary>
        public WeaponType WeaponType;

        public MainStatType MainStatType;

        /// <summary>+0강 기준값. 실제 표시값은 여기에 드랍 시 랜덤(0.95~1.05)과 강화 보너스를 더한 값이다.</summary>
        public int MainStatBase;

        /// <summary>
        /// 붙는 옵션 개수. 등급에서 파생하면 안 된다.
        /// 스토리 보상인 Lv30 유물 무기가 "옵션 없음"이라 유물인데 0개인 예외가 이미 존재한다.
        /// </summary>
        public int OptionCount;

        int IDataRow.Index => Index;

        /// <summary>
        /// 장착 자체가 불가능해지는 결함(부위 미지정)만 행을 제외하고, 나머지는 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (EquipSlot == EquipSlot.None)
            {
                error = $"Index {Index}: EquipSlot이 None (장착 불가, 제외됨)";
                return false;
            }

            // 무기는 반드시 직업이 갈리고, 방어구는 갈리면 안 된다. 둘 다 시트 입력 실수로 자주 나온다.
            if (EquipSlot == EquipSlot.Weapon && WeaponType == WeaponType.None)
            {
                error = $"Index {Index}: 무기인데 WeaponType이 None (직업 판정 불가, 제외됨)";
                return false;
            }

            if (EquipSlot != EquipSlot.Weapon && WeaponType != WeaponType.None)
            {
                WeaponType = WeaponType.None;
            }

            if (MainStatBase < 0) MainStatBase = 0;
            if (OptionCount < 0) OptionCount = 0;

            error = null;
            return true;
        }
    }
}
