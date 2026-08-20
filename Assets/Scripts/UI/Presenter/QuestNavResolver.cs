using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Scenes;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 하나를 "지금 어디로 안내할지"로 환원하는 순수 해석기. 표시(카드 안 <see cref="QuestCompassEntry"/>)와
    /// 목록 관리(<see cref="QuestTrackerHud"/>)에서 분리해 둔 판정 로직이다.
    ///
    /// 목표 위치는 데이터가 아니라 씬의 <see cref="QuestWaypointRegistry"/>에서 온다. 퀘스트 상태로부터 '위치 토큰'을
    /// 정하고(Kill/Clear/Collect→던전, Reach 위치→웨이포인트, 반납→NPC), 토큰을 레지스트리에서 조회한다.
    /// 결과는 세 갈래다: 목표 던전 도착(정적 아이콘) / 특정 월드 지점(화살표+거리) / 안내 없음(숨김).
    /// </summary>
    public static class QuestNavResolver
    {
        /// <summary>마을(던전 밖)을 뜻하는 던전 ID. 던전에서 완료한 퀘스트의 반납 안내가 마을 출구 게이트를 찾는 키로도 쓴다.</summary>
        public const int TownDungeonId = 0;

        /// <summary>
        /// 퀘스트의 현재 안내 목표를 해석한다.
        /// </summary>
        /// <param name="quest">해석할 퀘스트</param>
        /// <param name="from">거리·근접 비교 기준(보통 플레이어 위치)</param>
        /// <param name="inTargetDungeon">true면 이미 목표 던전 안 → 화살표 대신 정적 아이콘.</param>
        /// <param name="worldPos">조준할 월드 좌표(inTargetDungeon=false일 때 유효).</param>
        /// <returns>안내할 목표가 있으면 true. false면 숨김(레벨 도달·미배치 등).</returns>
        public static bool TryResolve(QuestData quest, Vector3 from, out bool inTargetDungeon, out Vector3 worldPos)
        {
            inTargetDungeon = false;
            worldPos = Vector3.zero;

            if (quest == null) return false;

            // 반납 단계: NPC(TurnIn) 조준. 현재 씬에 있으면 그 위치, 없으면(던전 안 등) 마을 출구 게이트로 breadcrumb.
            if (quest.IsReadyToTurnIn)
            {
                if (QuestWaypointRegistry.TryGetNearest(QuestWaypointKind.TurnIn, quest.QuestId, from, out IQuestWaypoint npc))
                {
                    worldPos = npc.Position;
                    return true;
                }
                return TryResolveGate(TownDungeonId, from, out worldPos);
            }

            // 진행 단계: 미완료 목표 중 첫 번째를 안내한다.
            int targetId = FirstIncompleteTargetId(quest);
            if (targetId == 0) return false;

            if (quest.ObjectiveType == ObjectiveType.Reach)
            {
                // 위치 도달: 그 지점 웨이포인트가 현재 씬에 있으면 조준, 없으면 레벨 도달로 보고 숨김(존재가 판별자).
                if (QuestWaypointRegistry.TryGetNearest(QuestWaypointKind.Objective, targetId, from, out IQuestWaypoint point))
                {
                    worldPos = point.Position;
                    return true;
                }
                return false;
            }

            // Kill/Clear/Collect: 목표가 속한 던전으로 안내한다.
            int dungeonId = ResolveObjectiveDungeon(quest.ObjectiveType, targetId);
            if (dungeonId <= 0) return false;

            // 던전 번호끼리 비교한다. CurrentDungeonId는 난이도까지 담은 2자리(11=던전1 노말 · 13=매니악)라
            // 그대로 비교하면 어느 난이도로 들어와도 거짓이 되어, 던전 안에서도 계속 게이트를 가리킨다.
            // 목표가 속한 던전(몬스터 테이블의 DungeonId·아이템의 SourceDungeonId)은 난이도가 없는 한 자리다.
            if (DungeonContext.DungeonNumber == dungeonId)
            {
                inTargetDungeon = true;   // 이미 목표 던전 안 → 화살표 대신 정적 아이콘.
                return true;
            }

            return TryResolveGate(dungeonId, from, out worldPos);
        }

        /// <summary>월드 방향 벡터를 카메라 기준 방위각(도)으로 바꾼다. 0=화면 정면(카메라 전방), 시계방향 +.</summary>
        /// <param name="worldDir">목표까지의 월드 방향(XZ 평면).</param>
        /// <param name="cam">기준 카메라.</param>
        /// <returns>카메라 전방 대비 방위각(도).</returns>
        public static float BearingRelativeToCamera(Vector3 worldDir, Camera cam)
        {
            Vector3 camForward = cam.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude < 0.0001f) camForward = Vector3.forward;

            float targetYaw = Mathf.Atan2(worldDir.x, worldDir.z) * Mathf.Rad2Deg;
            float camYaw = Mathf.Atan2(camForward.x, camForward.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(camYaw, targetYaw);
        }

        private static bool TryResolveGate(int dungeonId, Vector3 from, out Vector3 worldPos)
        {
            if (QuestWaypointRegistry.TryGetNearest(QuestWaypointKind.Gate, dungeonId, from, out IQuestWaypoint gate))
            {
                worldPos = gate.Position;
                return true;
            }

            worldPos = Vector3.zero;
            return false;
        }

        // Kill=몬스터→DungeonId, Clear=TargetId(던전 ID 그대로), Collect=아이템→SourceDungeonId.
        private static int ResolveObjectiveDungeon(ObjectiveType type, int targetId)
        {
            if (type == ObjectiveType.Clear) return targetId;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return 0;   // 로딩 전에는 안내하지 않는다(다음 프레임에 다시 시도).

            if (type == ObjectiveType.Kill)
            {
                MonsterStatTable monster = json.Get<MonsterStatTable>(targetId);
                return monster != null ? monster.DungeonId : 0;
            }

            if (type == ObjectiveType.Collect)
            {
                ItemData item = json.Get<ItemData>(targetId);
                return item != null ? item.SourceDungeonId : 0;
            }

            return 0;
        }

        // 미완료 목표 중 첫 번째의 TargetId(없으면 0).
        private static int FirstIncompleteTargetId(QuestData quest)
        {
            foreach (ObjectiveProgress objective in quest.Objectives)
            {
                if (!objective.IsCompleted)
                    return objective.Target.TargetId;
            }
            return 0;
        }
    }
}
