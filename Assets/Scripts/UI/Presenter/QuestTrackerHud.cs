using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 퀘스트 목록(QuestList). 진행 중인 퀘스트마다 카드 한 장(<see cref="QuestTrackerEntry"/>)을
    /// Content에 생성해 제목·진행 내용을 표시한다. 수락하면 카드가 늘고 완료(반납)하면 사라진다.
    ///
    /// 창의 크기·스크롤·접기는 <see cref="ExpandableScrollList"/>가 담당한다. 여기서는 카드를 넣고 뺀 뒤
    /// Refresh만 호출한다 — 레이아웃 규칙이 퀘스트 로직 안으로 새어 들어가지 않게 하기 위한 분리다.
    /// <see cref="QuestEvents"/>만 구독하므로 매니저·데이터 로직과도 분리돼 있다.
    ///
    /// 배치: 창 루트에 ExpandableScrollList와 함께 붙이고, 카드가 쌓일 Content를 content에,
    /// 카드 프리팹을 cardPrefab에 연결한다.
    /// </summary>
    public class QuestTrackerHud : MonoBehaviour
    {
        /// <summary>인스펙터에 노출하기 위한 구체 이벤트 타입(UnityEvent&lt;T&gt;는 그대로는 직렬화되지 않는다).</summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("목록")]
        [Tooltip("창 본체. 높이 계산·스크롤·접기를 맡는다.")]
        [SerializeField] private ExpandableScrollList window;

        [Tooltip("카드가 쌓일 Content(ScrollRect의 Content). Vertical Layout Group + Content Size Fitter가 붙어 있어야 한다.")]
        [FormerlySerializedAs("container")]
        [SerializeField] private Transform content;

        [Tooltip("퀘스트 한 개를 표시할 카드 프리팹(QuestTrackerEntry 포함).")]
        [FormerlySerializedAs("entryPrefab")]
        [SerializeField] private QuestTrackerEntry cardPrefab;

        [Header("여닫기")]
        [Tooltip("접힘/펼침이 바뀔 때 발행(true=접힘). 사운드 등 추가 연출에 연결(화살표 회전은 창이 처리).")]
        [SerializeField] private BoolEvent onCollapsedChanged = new BoolEvent();

        // 진행 중 퀘스트 → 그 카드. 진행/완료 때 해당 카드를 찾고 지우는 데 쓴다.
        private readonly Dictionary<QuestData, QuestTrackerEntry> entries = new();

        /// <summary>목록이 접혀 있는지.</summary>
        public bool IsCollapsed => window != null && window.IsCollapsed;

        private void OnEnable()
        {
            QuestEvents.OnQuestAccepted += OnAccepted;
            QuestEvents.OnQuestProgressUpdated += OnProgress;
            QuestEvents.OnQuestCompleted += OnCompleted;
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestAccepted -= OnAccepted;
            QuestEvents.OnQuestProgressUpdated -= OnProgress;
            QuestEvents.OnQuestCompleted -= OnCompleted;
        }

        // ---------- 여닫기 ----------

        /// <summary>접힘/펼침을 토글한다. 여닫기 버튼의 OnClick에 연결한다.</summary>
        public void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

        /// <summary>접힘 상태를 지정한다. 카드 데이터는 유지되므로 펼치면 그대로 보인다.</summary>
        /// <param name="value">true=접힘, false=펼침</param>
        public void SetCollapsed(bool value)
        {
            if (window == null) return;

            window.SetCollapsed(value);
            onCollapsedChanged?.Invoke(value);
        }

        // ---------- 목록 갱신 ----------

        // 퀘스트 수락 시: 카드를 하나 만들어 목록에 추가한다.
        private void OnAccepted(QuestData quest)
        {
            if (cardPrefab == null || content == null) return;
            if (entries.ContainsKey(quest)) return;   // 중복 생성 방지

            QuestTrackerEntry card = Instantiate(cardPrefab, content);
            card.SetTitle(quest.Title);
            card.SetProgress(BuildProgress(quest));
            // 수락 직후는 보통 미완료라 체크는 꺼진다(목표 0개 같은 예외 대비해 실제 상태로 초기화).
            card.SetQuestCompletedCheck(quest.IsReadyToTurnIn);
            entries.Add(quest, card);

            if (window != null) window.Refresh();
        }

        // 목표 카운트가 바뀔 때: 해당 카드의 진행 내용과 완료 체크를 갱신한다.
        private void OnProgress(QuestData quest, int cur, int max)
        {
            if (!entries.TryGetValue(quest, out QuestTrackerEntry card)) return;

            card.SetProgress(BuildProgress(quest));
            // 모든 목표를 채워 '반납 대기(완료가능)'가 되면 체크를 켠다. 실제 반납되면 OnCompleted가 카드를 지운다.
            card.SetQuestCompletedCheck(quest.IsReadyToTurnIn);

            // 진행도 문자열의 줄 수가 바뀌면 카드 높이도 바뀌므로 창을 다시 잰다.
            if (window != null) window.Refresh();
        }

        // 반납(완료) 시: 그 카드를 목록에서 제거한다.
        private void OnCompleted(QuestData quest)
        {
            if (!entries.TryGetValue(quest, out QuestTrackerEntry card)) return;

            entries.Remove(quest);
            if (card != null)
            {
                // Destroy는 프레임 끝에 처리돼, 그대로 두면 사라진 카드의 자리가 한 프레임 남는다.
                // 비활성 자식은 Layout Group이 즉시 무시하므로 먼저 끄고 지운다.
                card.gameObject.SetActive(false);
                Destroy(card.gameObject);
            }

            if (window != null) window.Refresh();
        }

        // 설명을 첫 줄에 두고, 목표별 "현재/목표"를 한 줄씩 쌓는다.
        // 줄바꿈으로 쌓기 때문에 목표가 많은 퀘스트일수록 카드가 그만큼 세로로 늘어난다.
        // 예) "들판의 슬라임을 처치하라\n1/3"
        private static string BuildProgress(QuestData quest)
        {
            var sb = new StringBuilder();

            string description = quest.Definition.Description;
            if (!string.IsNullOrEmpty(description)) sb.Append(description);

            foreach (var objective in quest.Objectives)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(objective.CurrentCount).Append('/').Append(objective.Target.RequiredCount);
            }

            return sb.ToString();
        }
    }
}
