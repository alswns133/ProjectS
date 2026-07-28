using System.Text;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Events;

namespace ProjectS.Debugging
{
    /// <summary>
    /// UI가 아직 없는 동안 퀘스트 흐름(수락→진행→반납)을 콘솔에서 눈으로 확인하기 위한 테스트용 로거.
    /// <see cref="QuestEvents"/>만 구독하므로 매니저·데이터 로직과 완전히 분리돼 있고,
    /// 나중에 붙을 UI Presenter와 구독 방식이 똑같다 — 이 컴포넌트를 그대로 UI로 대체하면 된다.
    ///
    /// 로그는 <see cref="DevLog"/>를 써서 에디터에서만 출력되고 빌드에선 호출째 제거된다.
    /// 따라서 굳이 지울 필요가 없고, 끄고 싶으면 이 컴포넌트를 비활성화하면 된다.
    /// 배치: 아무 씬 오브젝트(예: QuestManager 오브젝트)에 붙이면 된다.
    /// </summary>
    public class QuestDebugLogger : MonoBehaviour
    {
        // 콘솔에서 "[Quest]"로 필터링할 수 있게 접두사를 통일한다.
        private const string Tag = "[Quest]";

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

        // 퀘스트 수락 시: 제목·목표를 콘솔에 찍는다.
        private void OnAccepted(QuestData quest)
            => DevLog.Log($"{Tag} 수락 | {Head(quest)}  목표: {Objectives(quest)}");

        // 목표 카운트가 바뀔 때: 진행도를 찍고, 다 채웠으면 반납 가능 표시를 붙인다.
        private void OnProgress(QuestData quest, int cur, int max)
        {
            string tail = quest.IsReadyToTurnIn ? "  (반납 가능!)" : "";
            DevLog.Log($"{Tag} 진행 | {Head(quest)}  {cur}/{max}{tail}");
        }

        // 반납(완료) 시: 지급 예정 보상을 찍는다(실제 지급 구독자는 아직 없음).
        private void OnCompleted(QuestData quest)
            => DevLog.Log($"{Tag} 반납 | {Head(quest)}  보상 예정: {Rewards(quest)}");

        // 예: "[601001] 슬라임 퇴치 (Repeat/Kill)"
        private static string Head(QuestData q)
            => $"[{q.QuestId}] {q.Title} ({q.QuestType}/{q.ObjectiveType})";

        // 예: "1101 x3, 1102 x5"
        private static string Objectives(QuestData q)
        {
            var sb = new StringBuilder();
            foreach (var o in q.Objectives)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append($"{o.Target.TargetId} x{o.Target.RequiredCount}");
            }
            return sb.ToString();
        }

        // 보상 지급 구독자가 아직 없으므로 "예정"만 출력한다(데이터가 제대로 실렸는지 확인용).
        private static string Rewards(QuestData q)
        {
            var sb = new StringBuilder();
            foreach (var r in q.Definition.Rewards)
            {
                if (sb.Length > 0) sb.Append(", ");
                switch (r.Type)
                {
                    case QuestRewardType.Gold: sb.Append($"Gold {r.Amount}"); break;
                    case QuestRewardType.Exp: sb.Append($"Exp {r.Amount}"); break;
                    case QuestRewardType.Item: sb.Append($"Item#{r.TargetId} x{r.Amount}"); break;
                    case QuestRewardType.SkillUnlock: sb.Append($"Skill#{r.TargetId}"); break;
                }
            }
            return sb.Length > 0 ? sb.ToString() : "(없음)";
        }
    }
}
