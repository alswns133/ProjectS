using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 모든 아이템이 공통으로 가지는 정보. 장비·소비품·재료가 이 테이블을 공유한다.
    /// 카테고리별 고유 수치는 같은 Index를 가진 EquipmentData / ConsumableData 행에 둔다.
    /// 한 테이블에 전부 넣으면 소비품 행에 MainStat이 비어 있는 식의 빈 칸이 계속 늘어난다.
    /// </summary>
    [Serializable]
    public class ItemData : IDataRow
    {
        public int Index;
        public string Name;
        public ItemCategory Category;
        public ItemGrade Grade;

        /// <summary>아이템 레벨이자 요구 레벨(5단위). 소비품·재료는 0(제한 없음).</summary>
        public int Level;

        /// <summary>아이콘 어드레서블 주소. 프리팹이 아니라 스프라이트 주소다.</summary>
        public string IconAddress;

        /// <summary>장비 1, 소비품·재료 99. 인벤 슬롯 병합 판정에 쓴다.</summary>
        public int MaxStack;

        /// <summary>판매가. 기획상 상점 구매가의 20%.</summary>
        public int SellPrice;

        public string Description;

        int IDataRow.Index => Index;

        /// <summary>
        /// 이름이 없으면 UI에 표시할 수 없으므로 행을 제외한다.
        /// 나머지는 데이터 입력 실수를 안전한 값으로 보정만 한다(행을 살려 게임은 굴러가게).
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                error = $"Index {Index}: Name이 비어있음 (제외됨)";
                return false;
            }

            // 0은 "레벨 제한 없음"이라 허용한다. 그 외 5단위가 아닌 값은 시트 오타로 본다.
            if (Level != 0 && !ItemLevelTier.IsValidLevel(Level))
            {
                error = $"Index {Index}: 정의되지 않은 Level {Level} (5단위 아님, 제외됨)";
                return false;
            }

            if (MaxStack < 1) MaxStack = 1;
            if (SellPrice < 0) SellPrice = 0;

            error = null;
            return true;
        }
    }
}
