using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.NPCs
{
    /// <summary>
    /// 이 NPC를 '반납 대상'으로 하는 완료 가능 퀘스트들을 <see cref="QuestWaypointRegistry"/>에 TurnIn 지점으로
    /// 등록한다. 나침반은 반납 단계 퀘스트를 이 등록으로 찾아 NPC를 조준한다. 좌표 데이터를 따로 두지 않고
    /// 씬의 NPC 위치를 그대로 쓰기 위한 다리다(반납 대상 = <see cref="NpcInteractionController.NpcName"/> ↔
    /// <see cref="QuestTable.QuestNpc"/>).
    ///
    /// 폴링 대신 퀘스트 이벤트에 반응해 등록을 갱신한다(<see cref="QuestMarker"/>와 같은 방식). 목표를 다 채워
    /// 완료 가능이 된 퀘스트는 등록되고, 반납돼 사라지면 해제된다.
    ///
    /// 배치: NPC 오브젝트(또는 그 자식)에 붙인다. controller를 비우면 부모에서 자동으로 찾는다.
    /// </summary>
    public class QuestTurnInMarker : MonoBehaviour
    {
        [Tooltip("반납 대상 NPC. 비우면 이 오브젝트/부모에서 자동으로 찾는다.")]
        [SerializeField] private NpcInteractionController controller;

        // 등록한 TurnIn 지점을 QuestId로 들고 있어, 더는 완료 가능이 아닌 것을 찾아 해제할 수 있다.
        private readonly Dictionary<int, TurnInWaypoint> waypoints = new();

        // 갱신 시 재사용하는 버퍼(매 이벤트마다 HashSet을 새로 만들지 않기 위함).
        private readonly HashSet<int> wanted = new();

        private void Awake()
        {
            if (controller == null)
                controller = GetComponentInParent<NpcInteractionController>();
        }

        // 완료 가능 여부를 바꿀 수 있는 이벤트에 구독한다(폴링 대신 이벤트 구동).
        private void OnEnable()
        {
            QuestEvents.OnQuestAccepted += OnQuestChanged;
            QuestEvents.OnQuestCompleted += OnQuestChanged;
            QuestEvents.OnQuestProgressUpdated += OnQuestProgress;
            QuestEvents.OnQuestsRestored += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestAccepted -= OnQuestChanged;
            QuestEvents.OnQuestCompleted -= OnQuestChanged;
            QuestEvents.OnQuestProgressUpdated -= OnQuestProgress;
            QuestEvents.OnQuestsRestored -= Refresh;

            // 씬 언로드 등으로 꺼질 때 등록을 전부 뺀다(파괴된 위치를 나침반이 계속 가리키지 않게).
            foreach (TurnInWaypoint waypoint in waypoints.Values)
                QuestWaypointRegistry.Unregister(waypoint);
            waypoints.Clear();
        }

        // 반납이 완료 가능해질 수 있는 순간(진행 변화)에도 다시 계산한다.
        private void OnQuestChanged(QuestData _) => Refresh();

        private void OnQuestProgress(QuestData _, int __, int ___) => Refresh();

        // 이 NPC가 반납받을 수 있는(완료 가능) 퀘스트 집합에 맞춰 등록을 더하고 뺀다.
        private void Refresh()
        {
            if (controller == null) return;

            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;   // 부트스트랩 없이 직접 씬 테스트: 매니저가 없으면 등록 생략

            List<QuestData> completable = manager.GetCompletableQuestsForNpc(controller.NpcName);

            // 새로 완료 가능이 된 퀘스트를 등록한다.
            wanted.Clear();
            foreach (QuestData quest in completable)
            {
                wanted.Add(quest.QuestId);
                if (!waypoints.ContainsKey(quest.QuestId))
                {
                    var waypoint = new TurnInWaypoint(transform, quest.QuestId);
                    waypoints[quest.QuestId] = waypoint;
                    QuestWaypointRegistry.Register(waypoint);
                }
            }

            // 더는 완료 가능이 아닌(반납됐거나 사라진) 등록을 해제한다.
            List<int> stale = null;
            foreach (int questId in waypoints.Keys)
            {
                if (!wanted.Contains(questId))
                    (stale ??= new List<int>()).Add(questId);
            }
            if (stale == null) return;

            foreach (int questId in stale)
            {
                QuestWaypointRegistry.Unregister(waypoints[questId]);
                waypoints.Remove(questId);
            }
        }

        // NPC의 현재 위치를 조준점으로 돌려주는 경량 지점. Transform만 참조한다(퀘스트별 1개).
        private sealed class TurnInWaypoint : IQuestWaypoint
        {
            private readonly Transform anchor;
            private readonly int questId;

            public TurnInWaypoint(Transform anchor, int questId)
            {
                this.anchor = anchor;
                this.questId = questId;
            }

            public QuestWaypointKind Kind => QuestWaypointKind.TurnIn;
            public int Key => questId;
            public Vector3 Position => anchor != null ? anchor.position : Vector3.zero;
            public bool IsActive => anchor != null;
        }
    }
}
