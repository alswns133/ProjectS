using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using ProjectS.Data;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 퀘스트 목록(Objective_List). 진행 중인 퀘스트마다 한 줄(<see cref="QuestTrackerEntry"/>)을
    /// 컨테이너에 생성해 제목·진행도를 표시한다. 수락하면 줄이 늘고 완료(반납)하면 사라진다 →
    /// 퀘스트 수에 맞춰 목록이 자동으로 늘고 준다(컨테이너의 Layout Group이 배치를 맡는다).
    ///
    /// 여닫기(접기/펼치기)를 제공한다 — 퀘스트가 많아 화면이 번잡할 때 버튼으로 목록을 접을 수 있다.
    /// <see cref="QuestEvents"/>만 구독하므로 매니저·데이터 로직과 분리돼 있다.
    /// 배치: 줄이 쌓일 컨테이너를 container에, 한 줄 프리팹을 entryPrefab에 연결한다.
    /// </summary>
    public class QuestTrackerHud : MonoBehaviour
    {
        /// <summary>인스펙터에 노출하기 위한 구체 이벤트 타입(UnityEvent&lt;T&gt;는 그대로는 직렬화되지 않는다).</summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("목록")]
        [Tooltip("줄이 쌓일 컨테이너. Vertical Layout Group + Content Size Fitter를 붙이면 줄 수에 맞춰 자동 정렬·확장된다.")]
        [SerializeField] private Transform container;

        [Tooltip("퀘스트 한 개를 표시할 줄 프리팹(QuestTrackerEntry 포함).")]
        [SerializeField] private QuestTrackerEntry entryPrefab;

        [Header("여닫기")]
        [Tooltip("시작 시 접힌 상태로 둘지.")]
        [SerializeField] private bool startCollapsed = false;

        [Tooltip("접힘/펼침이 바뀔 때 발행(true=접힘). 화살표 아이콘 회전 등 연출에 연결.")]
        [SerializeField] private BoolEvent onCollapsedChanged = new BoolEvent();

        // 진행 중 퀘스트 → 그 줄. 진행/완료 때 해당 줄을 찾고 지우는 데 쓴다.
        private readonly Dictionary<QuestData, QuestTrackerEntry> entries = new();

        private bool collapsed;

        /// <summary>목록이 접혀 있는지.</summary>
        public bool IsCollapsed => collapsed;

        private void OnEnable()
        {
            QuestEvents.OnQuestAccepted += OnAccepted;
            QuestEvents.OnQuestProgressUpdated += OnProgress;
            QuestEvents.OnQuestCompleted += OnCompleted;

            SetCollapsed(startCollapsed);
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestAccepted -= OnAccepted;
            QuestEvents.OnQuestProgressUpdated -= OnProgress;
            QuestEvents.OnQuestCompleted -= OnCompleted;
        }

        // ---------- 여닫기 ----------

        /// <summary>접힘/펼침을 토글한다. 여닫기 버튼의 OnClick에 연결한다.</summary>
        public void ToggleCollapsed() => SetCollapsed(!collapsed);

        /// <summary>접힘 상태를 지정한다. 접으면 줄 컨테이너를 숨긴다(줄 데이터는 유지되어 펼치면 그대로 보인다).</summary>
        /// <param name="value">true=접힘, false=펼침</param>
        public void SetCollapsed(bool value)
        {
            collapsed = value;
            if (container != null) container.gameObject.SetActive(!collapsed);
            onCollapsedChanged?.Invoke(collapsed);
        }

        // ---------- 목록 갱신 ----------

        // 퀘스트 수락 시: 줄을 하나 만들어 목록에 추가한다.
        private void OnAccepted(QuestData quest)
        {
            if (entryPrefab == null || container == null) return;
            if (entries.ContainsKey(quest)) return;   // 중복 생성 방지

            QuestTrackerEntry entry = Instantiate(entryPrefab, container);
            entry.SetTitle(quest.Title);
            entry.SetProgress(BuildProgress(quest));
            // 수락 직후는 보통 미완료라 체크는 꺼진다(목표 0개 같은 예외 대비해 실제 상태로 초기화).
            entry.SetQuestCompletedCheck(quest.IsReadyToTurnIn);
            entries.Add(quest, entry);
        }

        // 목표 카운트가 바뀔 때: 해당 줄의 진행도와 완료 체크를 갱신한다.
        private void OnProgress(QuestData quest, int cur, int max)
        {
            if (!entries.TryGetValue(quest, out QuestTrackerEntry entry)) return;

            entry.SetProgress(BuildProgress(quest));
            // 모든 목표를 채워 '반납 대기(완료가능)'가 되면 체크를 켠다. 실제 반납되면 OnCompleted가 줄을 지운다.
            entry.SetQuestCompletedCheck(quest.IsReadyToTurnIn);
        }

        // 반납(완료) 시: 그 줄을 목록에서 제거한다.
        private void OnCompleted(QuestData quest)
        {
            if (!entries.TryGetValue(quest, out QuestTrackerEntry entry)) return;
            
            entries.Remove(quest);
            if (entry != null) Destroy(entry.gameObject);
        }

        // 목표별 "현재/목표"를 이어 붙이고, 설명이 있으면 앞에 둔다.
        // 예) "들판의 슬라임 3마리를 처치하라  1/3"
        private static string BuildProgress(QuestData quest)
        {
            var sb = new StringBuilder();
            foreach (var objective in quest.Objectives)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(objective.CurrentCount).Append('/').Append(objective.Target.RequiredCount);
            }

            string description = quest.Definition.Description;
            return string.IsNullOrEmpty(description) ? sb.ToString() : $"{description}  {sb}";
        }
    }
}
