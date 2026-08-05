using UnityEngine;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Players;
using ProjectS.Debugging;

namespace ProjectS.Managers
{
    /// <summary>
    /// 퀘스트 완료 보상을 실제로 지급하는 구독자. QuestManager가 반납(TurnInQuest) 시 발행하는
    /// <see cref="QuestEvents.OnQuestCompleted"/>를 받아 보상 종류별로 지급한다.
    /// 지급 주체를 QuestManager에서 분리해(퀘스트 매니저는 상태만 관리), 보상 시스템(인벤토리/경험치/스킬)이
    /// 준비되는 대로 이 클래스만 채우면 되게 한다.
    ///
    /// 현재 골드(InventoryManager)·경험치(PlayerStats.AddExp, 자동 레벨업)·아이템(InventoryManager.AddItem)은
    /// 실제 지급하고, 스킬해금만 아직 로그 스텁이다(세이브 LearnedSkillIds 붙는 대로 교체).
    /// 배치: 씬을 넘어 유지되는 매니저 오브젝트에 붙인다(QuestManager와 함께).
    /// </summary>
    public class QuestRewardGranter : MonoBehaviour
    {
        private void OnEnable() => QuestEvents.OnQuestCompleted += OnQuestCompleted;
        private void OnDisable() => QuestEvents.OnQuestCompleted -= OnQuestCompleted;

        // 반납으로 완료된 퀘스트의 보상을 하나씩 지급한다.
        private void OnQuestCompleted(QuestData quest)
        {
            if (quest?.Definition?.Rewards == null) return;

            foreach (QuestRewardData reward in quest.Definition.Rewards)
                Grant(reward, quest.Title);
        }

        private void Grant(QuestRewardData reward, string questTitle)
        {
            switch (reward.Type)
            {
                case QuestRewardType.Gold:
                    if (InventoryManager.Instance != null)
                        InventoryManager.Instance.AddGold(reward.Amount);
                    DevLog.Log($"[Reward] '{questTitle}' 골드 +{reward.Amount}");
                    break;

                case QuestRewardType.Exp:
                    PlayerStats stats = PlayerManager.Instance != null ? PlayerManager.Instance.Player?.Stats : null;
                    if (stats != null) stats.AddExp(reward.Amount);   // 임계치 넘으면 자동 레벨업 + HUD 갱신
                    DevLog.Log($"[Reward] '{questTitle}' 경험치 +{reward.Amount}");
                    break;

                case QuestRewardType.Item:
                    if (InventoryManager.Instance != null)
                        InventoryManager.Instance.AddItem(reward.TargetId, reward.Amount);   // 인벤 추가 + 세이브 dirty
                    DevLog.Log($"[Reward] '{questTitle}' 아이템 {reward.TargetId} x{reward.Amount}");
                    break;

                case QuestRewardType.SkillUnlock:
                    // TODO: 캐릭터 세이브 LearnedSkillIds에 추가로 교체.
                    DevLog.Log($"[Reward] '{questTitle}' 스킬 해금 {reward.TargetId} (스텁 — 세이브 대기)");
                    break;
            }
        }
    }
}
