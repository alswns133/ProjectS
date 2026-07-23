namespace ProjectS.Data
{
    /// <summary>
    /// 아이템 레벨은 5단위로만 존재한다(1/5/10/15/20/25/30/35).
    /// 옵션·강화·장비 수치표가 전부 이 8칸을 공통 축으로 쓰기 때문에,
    /// 레벨을 배열 인덱스로 바꾸는 변환을 한 곳에 모아둔다.
    /// 여기가 흩어지면 테이블마다 열 순서가 어긋났을 때 원인을 찾기 어려워진다.
    /// </summary>
    public static class ItemLevelTier
    {
        /// <summary>수치표 열 순서와 정확히 같은 레벨 목록.</summary>
        public static readonly int[] Levels = { 1, 5, 10, 15, 20, 25, 30, 35 };

        /// <summary>수치 배열이 가져야 하는 길이. Validate에서 이 값으로 검사한다.</summary>
        public const int SlotCount = 8;

        /// <summary>
        /// 아이템 레벨을 수치 배열의 인덱스로 바꾼다.
        /// </summary>
        /// <param name="level">아이템 레벨(5단위)</param>
        /// <returns>0~7 슬롯 인덱스. 정의되지 않은 레벨이면 -1</returns>
        public static int ToSlot(int level)
        {
            for (int i = 0; i < Levels.Length; i++)
            {
                if (Levels[i] == level) return i;
            }

            return -1;
        }

        /// <summary>
        /// 정의된 레벨인지 확인한다. 데이터 행 검증에서 사용.
        /// </summary>
        public static bool IsValidLevel(int level) => ToSlot(level) >= 0;
    }
}
