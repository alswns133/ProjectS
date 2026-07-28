using System;

namespace ProjectS.Data
{
    /// <summary>
    /// 퀘스트 목표 대상 하나(기획서 8.1의 ObjectiveTargets 원소). 퀘스트의 <see cref="ObjectiveType"/>은
    /// 퀘스트 단위로 하나뿐이고, 그 타입 안에서 "무엇을 몇 개" 요구하는지를 이 구조가 나열한다.
    /// 다중 목표(예: 고블린 10 + 늑대 5)는 이 원소를 여러 개 두어 표현한다.
    /// 진행 중 바뀌는 카운트는 여기 두지 않는다(런타임 <see cref="ObjectiveProgress"/>가 들고 있다).
    /// </summary>
    [Serializable]
    public class ObjectiveTarget
    {
        /// <summary>대상 ID. Kill=몬스터ID, Collect=아이템ID, Reach=레벨(또는 지역)ID, Clear=던전ID.</summary>
        public int TargetId;

        /// <summary>필요 수량. Reach처럼 한 번이면 되는 목표는 1. (QuestTable.Validate가 1 미만을 1로 보정)</summary>
        public int RequiredCount = 1;
    }
}
