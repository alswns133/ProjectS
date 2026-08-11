using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>
    /// 퀘스트 네비게이션 목표의 종류. 목표마다 "무엇으로 식별되는지(Key)"가 달라 이 종류로 가른다.
    /// 나침반 UI는 퀘스트 상태로부터 종류를 정한 뒤 <see cref="QuestWaypointRegistry"/>에서 조회한다.
    /// </summary>
    public enum QuestWaypointKind
    {
        /// <summary>던전으로 들어가는 마을 게이트. Key = dungeonId. 목표가 다른 씬(던전)에 있을 때 조준한다.</summary>
        Gate,

        /// <summary>씬에 배치된 목표 지점(예: 위치 도달점). Key = ObjectiveTarget.TargetId. 현재 씬에 있으면 직접 조준한다.</summary>
        Objective,

        /// <summary>반납받는 NPC. Key = QuestId. 목표를 다 채운 뒤 되돌아갈 대상.</summary>
        TurnIn
    }

    /// <summary>
    /// 퀘스트 네비게이션이 조준할 수 있는 월드 지점. 게이트·목표 웨이포인트·반납 NPC가 각자 구현해
    /// <see cref="QuestWaypointRegistry"/>에 자기 등록하면, 나침반 UI는 소스 구분 없이 <see cref="Kind"/>+<see cref="Key"/>로
    /// 위치만 조회한다. 좌표를 데이터(JSON)에 넣지 않고 씬 오브젝트가 들고 있게 하기 위한 경계다
    /// (프로젝트 데이터 방침: Transform 참조는 인스펙터에 둔다).
    /// </summary>
    public interface IQuestWaypoint
    {
        /// <summary>이 지점의 종류. 나침반이 어떤 상태의 퀘스트에 매칭할지를 정한다.</summary>
        QuestWaypointKind Kind { get; }

        /// <summary>
        /// 종류별 식별자. Gate=dungeonId, Objective=TargetId, TurnIn=QuestId.
        /// 나침반은 퀘스트에서 이 값을 뽑아(예: 몬스터→DungeonId) 같은 Key의 지점을 찾는다.
        /// </summary>
        int Key { get; }

        /// <summary>조준할 월드 좌표. 보통 <c>transform.position</c>를 그대로 돌려준다.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// 지금 조준 대상으로 유효한지. 등록은 해두되 일시적으로 대상이 아닐 때(예: 반납 NPC가 아직 완료되지 않은 퀘스트)
        /// false로 두면, 매 이벤트마다 등록/해제를 반복하지 않고 플래그만 바꿔 걸러낼 수 있다. 조회 시 false는 건너뛴다.
        /// </summary>
        bool IsActive { get; }
    }
}
