using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>
    /// 현재 로드된 씬에 존재하는 <see cref="IQuestWaypoint"/>들을 모으는 static 레지스트리.
    /// 게이트·목표 웨이포인트·반납 NPC가 <c>OnEnable</c>에서 <see cref="Register"/>, <c>OnDisable</c>에서
    /// <see cref="Unregister"/>한다(씬 언로드 = 자동 해제). 그래서 레지스트리는 항상 "지금 씬에 실제로 있는 것"만 안다 —
    /// 마을에 있을 때 던전 웨이포인트는 애초에 들어오지 않으므로, 나침반은 "이 씬에 목표가 있나"를 조회만으로 판정한다.
    ///
    /// 나침반 UI는 <see cref="TryGetNearest"/>로 조준점을 얻고, <see cref="Has"/>로 존재 여부만 물을 수 있다
    /// (예: Reach 목표에 웨이포인트가 있으면 '위치 도달', 없으면 '레벨 도달'이라 나침반을 숨긴다 — 별도 플래그 없이 존재가 판별자).
    /// 목록이 바뀌면 <see cref="Changed"/>를 발행하니, 나침반은 매 프레임 조회하지 않고 이 이벤트에 반응해 조준점을 다시 잡으면 된다.
    /// </summary>
    public static class QuestWaypointRegistry
    {
        // (종류, 키)별로 지점 목록을 담는다. 같은 키에 여러 개일 수 있어(예: 같은 목표의 웨이포인트 여럿) List로 둔다.
        private static readonly Dictionary<(QuestWaypointKind Kind, int Key), List<IQuestWaypoint>> byKey = new();

        // 중복 등록을 막고 Unregister에서 어느 버킷에 있는지 되찾기 위해 등록된 원소를 함께 들고 있는다.
        private static readonly HashSet<IQuestWaypoint> registered = new();

        /// <summary>
        /// 등록/해제로 목록이 바뀔 때 발행한다. 나침반은 이 신호에만 조준점을 다시 계산하면 되므로
        /// 매 프레임 레지스트리를 훑지 않아도 된다(회전만 매 프레임, 조회는 변화 시).
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// 지점을 등록한다. 이미 등록돼 있으면 무시한다(멱등). 보통 <c>OnEnable</c>에서 부른다.
        /// </summary>
        /// <param name="waypoint">등록할 지점. null이면 무시한다.</param>
        public static void Register(IQuestWaypoint waypoint)
        {
            if (waypoint == null || !registered.Add(waypoint)) return;

            var key = (waypoint.Kind, waypoint.Key);
            if (!byKey.TryGetValue(key, out List<IQuestWaypoint> list))
            {
                list = new List<IQuestWaypoint>();
                byKey[key] = list;
            }
            list.Add(waypoint);

            Changed?.Invoke();
        }

        /// <summary>
        /// 지점을 해제한다. 등록돼 있지 않으면 무시한다. 보통 <c>OnDisable</c>(씬 언로드 포함)에서 부른다.
        /// 빠뜨리면 파괴된 오브젝트가 목록에 남아 나침반이 사라진 지점을 조준하게 된다.
        /// </summary>
        /// <param name="waypoint">해제할 지점. null이면 무시한다.</param>
        public static void Unregister(IQuestWaypoint waypoint)
        {
            if (waypoint == null || !registered.Remove(waypoint)) return;

            var key = (waypoint.Kind, waypoint.Key);
            if (byKey.TryGetValue(key, out List<IQuestWaypoint> list))
            {
                list.Remove(waypoint);
                if (list.Count == 0) byKey.Remove(key);
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// 해당 종류·키의 활성 지점이 현재 씬에 하나라도 있는지. Reach의 '위치 vs 레벨' 구분처럼
        /// 좌표는 필요 없고 존재 여부만 알고 싶을 때 쓴다.
        /// </summary>
        /// <param name="kind">지점 종류</param>
        /// <param name="key">종류별 식별자(Gate=dungeonId, Objective=TargetId, TurnIn=QuestId)</param>
        /// <returns>활성 지점이 있으면 true</returns>
        public static bool Has(QuestWaypointKind kind, int key)
        {
            if (!byKey.TryGetValue((kind, key), out List<IQuestWaypoint> list)) return false;

            foreach (IQuestWaypoint waypoint in list)
            {
                if (waypoint != null && waypoint.IsActive) return true;
            }
            return false;
        }

        /// <summary>
        /// 해당 종류·키의 활성 지점 중 <paramref name="from"/>에서 가장 가까운 것을 찾는다.
        /// 같은 키에 지점이 여럿이어도(웨이포인트 여러 개 등) 자연스럽게 하나로 좁혀진다.
        /// </summary>
        /// <param name="kind">지점 종류</param>
        /// <param name="key">종류별 식별자(Gate=dungeonId, Objective=TargetId, TurnIn=QuestId)</param>
        /// <param name="from">거리 비교 기준(보통 플레이어 위치)</param>
        /// <param name="waypoint">찾은 지점(없으면 null)</param>
        /// <returns>활성 지점을 찾았으면 true</returns>
        public static bool TryGetNearest(QuestWaypointKind kind, int key, Vector3 from, out IQuestWaypoint waypoint)
        {
            waypoint = null;

            if (!byKey.TryGetValue((kind, key), out List<IQuestWaypoint> list)) return false;

            float bestSqr = float.PositiveInfinity;
            foreach (IQuestWaypoint candidate in list)
            {
                if (candidate == null || !candidate.IsActive) continue;

                float sqr = (candidate.Position - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    waypoint = candidate;
                }
            }

            return waypoint != null;
        }

        // 플레이 모드 진입 시 static 상태를 비운다. 도메인 리로드를 끈 프로젝트에서는 이전 플레이의 등록이
        // 남아 파괴된 지점을 계속 들고 있게 되므로, 이벤트 허브와 같은 방침으로 초기화 경로에서 리셋한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            byKey.Clear();
            registered.Clear();
            Changed = null;
        }
    }
}
