using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Data
{
    /// <summary>
    /// 수락한 퀘스트의 '런타임 인스턴스'. 불변 정의(<see cref="QuestTable"/>)를 참조하면서
    /// 진행 중 바뀌는 상태(목표별 카운트·완료 여부)는 자신이 들고 있다.
    /// QuestEvents(수락/진행/완료)가 이 타입을 실어 나른다. 진행도 변경은 QuestManager로 모은다(internal).
    /// </summary>
    public class QuestData
    {
        private readonly List<ObjectiveProgress> objectives;

        /// <summary>이 인스턴스가 참조하는 불변 정의.</summary>
        public QuestTable Definition { get; }

        /// <summary>목표별 진행 상태(정의의 ObjectiveTargets와 1:1, 순서 동일).</summary>
        public IReadOnlyList<ObjectiveProgress> Objectives => objectives;

        /// <summary>
        /// 트래커에서 고정(핀)했는지 여부. 핀은 표시 상태지만 이 런타임 인스턴스에 두어
        /// 씬을 넘어 유지되는 QuestManager와 함께 살아남게 한다 — 트래커는 씬마다 새로 생성되므로
        /// 여기에 저장해야 씬 전환 후에도 고정이 복원된다. 설정은 UI(트래커)가 한다.
        /// </summary>
        public bool IsPinned { get; set; }

        /// <summary>퀘스트 고유 ID(정의에서 그대로 노출).</summary>
        public int QuestId => Definition.QuestId;

        /// <summary>메인/반복 종류.</summary>
        public QuestType QuestType => Definition.QuestType;

        /// <summary>목표 판정 방식(처치/수집/도달/클리어).</summary>
        public ObjectiveType ObjectiveType => Definition.ObjectiveType;

        /// <summary>표시용 제목.</summary>
        public string Title => Definition.Title;

        /// <summary>모든 목표가 완료되어 NPC에게 반납할 수 있는 상태인지.</summary>
        public bool IsReadyToTurnIn
        {
            get
            {
                foreach (var objective in objectives)
                {
                    if (!objective.IsCompleted)
                        return false;
                }
                return objectives.Count > 0;
            }
        }

        /// <summary>
        /// 현재 상태(기획서 3장). 인스턴스가 존재하는 동안은 진행중/완료가능만 나타난다
        /// (미수락 = 인스턴스 없음, 완료 = QuestManager가 진행 목록에서 제거 후 completedQuestIds에 등록).
        /// </summary>
        public QuestState State => IsReadyToTurnIn ? QuestState.Completable : QuestState.InProgress;

        /// <summary>정의의 목표 대상으로부터 진행 인스턴스를 만들어 초기화한다.</summary>
        /// <param name="definition">이 퀘스트의 불변 정의</param>
        public QuestData(QuestTable definition)
        {
            Definition = definition;

            int count = definition.ObjectiveTargets?.Count ?? 0;
            objectives = new List<ObjectiveProgress>(count);
            if (definition.ObjectiveTargets != null)
            {
                foreach (var target in definition.ObjectiveTargets)
                    objectives.Add(new ObjectiveProgress(target));
            }
        }
    }

    /// <summary>
    /// 목표 대상 하나의 런타임 진행 상태. 정의(<see cref="ObjectiveTarget"/>)는 공유·불변이므로
    /// 카운트/완료 같은 변하는 값을 여기에 둔다. 진행은 QuestManager만 올린다(internal).
    /// </summary>
    public class ObjectiveProgress
    {
        /// <summary>참조하는 목표 대상 정의.</summary>
        public ObjectiveTarget Target { get; }

        /// <summary>현재 진행 카운트(0 ~ RequiredCount).</summary>
        public int CurrentCount { get; private set; }

        /// <summary>이 목표가 완료됐는지.</summary>
        public bool IsCompleted { get; private set; }

        /// <summary>목표 정의를 받아 진행 카운트를 0에서 시작한다.</summary>
        /// <param name="target">참조할 목표 대상 정의</param>
        public ObjectiveProgress(ObjectiveTarget target)
        {
            Target = target;
        }

        /// <summary>
        /// 목표를 진행시킨다. 목표치에 도달하면 완료로 굳는다. 이미 완료된 목표는 무시한다(중복 진행 방지).
        /// </summary>
        /// <param name="amount">증가량(보통 1)</param>
        internal void Advance(int amount)
        {
            if (IsCompleted) return;

            CurrentCount = Mathf.Min(Target.RequiredCount, CurrentCount + amount);
            if (CurrentCount >= Target.RequiredCount)
                IsCompleted = true;
        }
    }
}
