using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 몬스터 도감 행. 도감은 난이도별이 아니라 <b>종(種) 단위</b>로 한 장씩 보여 주므로,
    /// 난이도마다 행이 나뉘는 <see cref="MonsterStatTable"/>과 별도 테이블로 둔다(설명·지역 같은 표시 텍스트 전용).
    /// </summary>
    /// <remarks>
    /// 키는 몬스터의 <b>base ID</b>(홈 던전 노말 행, 예: 1101)다 — 프리팹 배치 규칙(docs/ID_NUMBERING.md §3)과 같은 값이라
    /// "이 도감 항목이 어느 몬스터인가"가 스탯 테이블과 그대로 맞물린다. 보스 여부(탭 분류)는 여기 두지 않고
    /// 스탯 테이블의 <see cref="MonsterStatTable.IsBoss"/>를 읽는다(같은 사실을 두 곳에 두면 어긋날 수 있어서).
    /// </remarks>
    [Serializable]
    public class MonsterIndexTable : IDataRow
    {
        /// <summary>몬스터 base ID(홈 던전 노말 행). MonsterStatTable 조회 키이자 이 테이블의 Index.</summary>
        public int MonsterId;

        /// <summary>도감 번호. 카드에 3자리(예: 012)로 표시되고 목록 정렬 기준이 된다.</summary>
        public int No;

        /// <summary>표시 이름. 비어 있으면 MonsterStatTable의 Name으로 폴백한다.</summary>
        public string Name;

        /// <summary>몬스터 분류(예: 기계형 · 근접). 상세 화면의 Type 칸.</summary>
        public string Category;

        /// <summary>출몰 지역. 상세 화면의 Area 칸.</summary>
        public string Area;

        /// <summary>도감 설명문.</summary>
        public string Description;

        /// <summary>일러스트 스프라이트 어드레서블 주소. 비어 있으면 일러스트 대신 자리표시를 보여 준다.</summary>
        public string IllustAddress;

        /// <summary>
        /// 2페이즈 일러스트 스프라이트 어드레서블 주소(선택). 페이즈에 따라 모습이 바뀌는 보스처럼
        /// "같은 몬스터의 두 모드"를 한 도감 항목에서 보여 주기 위한 칸이다. 비어 있으면 페이즈가 하나인 몬스터로 보고
        /// 상세 화면의 페이즈 전환 버튼을 숨긴다. 카드 초상은 항상 <see cref="IllustAddress"/>(1페이즈)를 쓴다.
        /// </summary>
        public string Phase2IllustAddress;

        /// <summary>2페이즈 일러스트가 있는 항목인가(상세 화면의 페이즈 전환 버튼 표시 기준).</summary>
        public bool HasPhase2 => !string.IsNullOrEmpty(Phase2IllustAddress);

        int IDataRow.Index => MonsterId;

        /// <summary>
        /// ID가 0 이하거나 도감 번호가 없으면 정렬·조회가 성립하지 않으므로 탈락시킨다.
        /// 텍스트 칸의 null은 표시측에서 매번 검사하지 않도록 빈 문자열로 보정한다.
        /// </summary>
        /// <param name="error">탈락 사유(통과 시 null)</param>
        /// <returns>사용 가능한 행이면 true</returns>
        public bool Validate(out string error)
        {
            if (MonsterId <= 0)
            {
                error = $"MonsterIndex: MonsterId가 0 이하 (제외됨)";
                return false;
            }

            if (No <= 0)
            {
                error = $"MonsterIndex {MonsterId}: No(도감 번호)가 0 이하 (제외됨)";
                return false;
            }

            Name ??= string.Empty;
            Category ??= string.Empty;
            Area ??= string.Empty;
            Description ??= string.Empty;
            IllustAddress ??= string.Empty;
            Phase2IllustAddress ??= string.Empty;

            error = null;
            return true;
        }
    }
}
