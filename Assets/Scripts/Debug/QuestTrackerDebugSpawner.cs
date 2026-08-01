using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 퀘스트 트래커 UI를 NPC 대화 없이 눈으로 확인하기 위한 테스트용 스포너.
    /// 카드는 <see cref="QuestEvents.OnQuestAccepted"/>가 떠야 생기는데, 정상 경로로는 NPC를 찾아가
    /// 대화를 끝까지 봐야 해서 UI 한 줄 고칠 때마다 반복하기엔 너무 번거롭다. 그래서 이벤트를 직접 쏜다.
    ///
    /// <see cref="QuestEvents"/>만 쓰므로 QuestManager·JsonManager가 없어도 동작한다
    /// (테이블이 준비돼 있으면 실제 행을, 아니면 코드로 만든 가짜 행을 쓴다).
    /// 배치: 아무 씬 오브젝트에 붙이고, 인스펙터 컴포넌트 헤더를 우클릭해 메뉴를 실행한다.
    /// </summary>
    public class QuestTrackerDebugSpawner : MonoBehaviour
    {
        [Header("소스")]
        [Tooltip("켜면 JsonManager의 실제 QuestTable 행을 쓴다. 로딩 전이거나 행이 없으면 가짜로 대체한다.")]
        [SerializeField] private bool useJsonTable = true;

        [Tooltip("실제 행을 쓸 때 수락할 퀘스트 ID 목록.")]
        [SerializeField] private int[] questIds = { 501001, 501002, 601001, 601002 };

        [Header("동작")]
        [Tooltip("플레이 시작과 동시에 수락한다. 끄면 컨텍스트 메뉴로만 실행된다.")]
        [SerializeField] private bool spawnOnStart = true;

        [Tooltip("한 번에 수락할 개수.")]
        [SerializeField] private int spawnCount = 3;

        // 수락한 순서대로 보관한다. 진행·반납 메뉴가 '맨 앞'을 집을 때 기준이 된다.
        private readonly List<QuestData> spawned = new();

        private void Start()
        {
            if (spawnOnStart) Spawn();
        }

        /// <summary>퀘스트를 spawnCount개 수락시켜 카드를 만든다.</summary>
        [ContextMenu("1. 퀘스트 수락")]
        public void Spawn()
        {
            for (int i = 0; i < spawnCount; i++)
            {
                QuestTable definition = GetDefinition(i);
                if (definition == null) continue;

                QuestData quest = new QuestData(definition);
                spawned.Add(quest);
                QuestEvents.FireQuestAccepted(quest);
            }

            DevLog.Log($"[QuestDebug] 수락 {spawned.Count}개");
        }

        /// <summary>맨 앞 퀘스트의 목표를 하나 진행시킨다. 카드 진행도와 창 높이 갱신을 확인할 때 쓴다.</summary>
        [ContextMenu("2. 맨 앞 퀘스트 진행 +1")]
        public void AdvanceFirst()
        {
            QuestData quest = First();
            if (quest == null) return;

            foreach (var objective in quest.Objectives)
            {
                if (objective.IsCompleted) continue;

                // Advance는 internal이지만 같은 어셈블리(Assembly-CSharp)라 호출할 수 있다.
                objective.Advance(1);
                QuestEvents.FireQuestProgressUpdated(quest, objective.CurrentCount, objective.Target.RequiredCount);
                return;
            }

            DevLog.Log("[QuestDebug] 이미 모든 목표가 완료됨");
        }

        /// <summary>맨 앞 퀘스트를 '반납 대기(완료)' 상태로 만든다. 제목이 녹색으로 바뀌고 맨 위로 올라간다.</summary>
        [ContextMenu("3. 맨 앞 퀘스트 완료 상태로")]
        public void CompleteFirst()
        {
            QuestData quest = First();
            if (quest == null) return;

            foreach (var objective in quest.Objectives)
            {
                while (!objective.IsCompleted)
                    objective.Advance(1);

                QuestEvents.FireQuestProgressUpdated(quest, objective.CurrentCount, objective.Target.RequiredCount);
            }
        }

        /// <summary>맨 앞 퀘스트를 반납한다. 카드가 목록에서 사라진다.</summary>
        [ContextMenu("4. 맨 앞 퀘스트 반납(카드 제거)")]
        public void TurnInFirst()
        {
            QuestData quest = First();
            if (quest == null) return;

            spawned.RemoveAt(0);
            QuestEvents.FireQuestCompleted(quest);
        }

        /// <summary>수락한 퀘스트를 모두 반납해 목록을 비운다.</summary>
        [ContextMenu("5. 전부 제거")]
        public void ClearAll()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
                QuestEvents.FireQuestCompleted(spawned[i]);

            spawned.Clear();
        }

        private QuestData First() => spawned.Count > 0 ? spawned[0] : null;

        // 실제 테이블 행을 우선 쓰고, 준비가 안 됐거나 ID가 없으면 가짜 행으로 대체한다.
        // 트래커 UI만 보는 게 목적이라 로딩 완료를 기다리게 만들지 않는다.
        private QuestTable GetDefinition(int index)
        {
            if (useJsonTable && JsonManager.Instance != null && JsonManager.Instance.IsReady &&
                questIds != null && index < questIds.Length)
            {
                QuestTable row = JsonManager.Instance.Get<QuestTable>(questIds[index]);
                if (row != null) return row;
            }

            return MakeFake(index);
        }

        // 길이가 다른 요약을 섞어, 한 줄짜리와 두 줄짜리 카드가 같이 있을 때의 모습을 함께 본다.
        private static QuestTable MakeFake(int index)
        {
            string[] trackerTexts =
            {
                "마을 앞 슬라임 처치",
                "던전1 깊은 곳의 원거리 몬스터를 정리하라",
                "노말 상의 수집",
                "폐허가 된 초소에서 보급품을 회수하라",
            };

            int objectiveCount = index % 2 == 0 ? 1 : 2;
            var targets = new List<ObjectiveTarget>(objectiveCount);
            for (int i = 0; i < objectiveCount; i++)
                targets.Add(new ObjectiveTarget { TargetId = 1101 + i, RequiredCount = 3 + i * 2 });

            return new QuestTable
            {
                QuestId = 501900 + index,
                QuestType = QuestType.Main,
                ObjectiveType = ObjectiveType.Kill,
                ObjectiveTargets = targets,
                Title = $"테스트 퀘스트 {index + 1}",
                Description = "테스트용 상세 스토리입니다. 상세 팝업에서 이 문장이 보입니다.",
                TrackerText = trackerTexts[index % trackerTexts.Length],
            };
        }
    }
}
