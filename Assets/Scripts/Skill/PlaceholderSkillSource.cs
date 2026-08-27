using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Skills
{
    /// <summary>
    /// 실제 스킬 데이터가 정해지기 전(2026-08-26) UI를 굴리기 위한 임시 소스.
    /// 스크린샷 그대로 액티브 4 + 패시브 7 슬롯, 각 1/5, 총 SP 45를 만든다.
    /// </summary>
    /// <remarks>
    /// 여기서 만드는 목록·SP·비용은 전부 자리표시자다. 진짜 시스템이 생기면
    /// <see cref="ISkillWindowSource"/>를 구현한 실제 소스로 갈아끼우고 이 클래스는 지운다.
    /// <see cref="Apply"/>는 로그만 남긴다(세이브·스탯 반영 대상이 아직 없음).
    /// </remarks>
    public class PlaceholderSkillSource : ISkillWindowSource
    {
        private const int ActiveCount = 4;
        private const int PassiveCount = 7;
        private const int MaxLevel = 5;

        // 레벨당 SP 비용(플레이스홀더는 전 스킬 공통 1). 11칸 × (5-1)레벨 × 1 = 44 ≤ 45.
        private const int FlatSpCost = 1;

        private readonly List<SkillSlotInfo> active = new();
        private readonly List<SkillSlotInfo> passive = new();

        /// <summary>더미 슬롯을 구성한다. SkillId는 겹치지 않게 그룹별로 대역을 나눈다.</summary>
        public PlaceholderSkillSource()
        {
            for (int i = 0; i < ActiveCount; i++)
            {
                int id = 1001 + i;
                active.Add(new SkillSlotInfo(
                    id, $"액티브 스킬 {i + 1}", "스킬 설명 (임시)", null,
                    isActive: true, minLevel: 1, maxLevel: MaxLevel, currentLevel: 1));
            }

            for (int i = 0; i < PassiveCount; i++)
            {
                int id = 2001 + i;
                passive.Add(new SkillSlotInfo(
                    id, $"패시브 스킬 {i + 1}", "스킬 설명 (임시)", null,
                    isActive: false, minLevel: 1, maxLevel: MaxLevel, currentLevel: 1));
            }
        }

        /// <inheritdoc/>
        public int TotalSp => 45;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetActiveSlots() => active;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetPassiveSlots() => passive;

        /// <inheritdoc/>
        public int SpCost(int skillId, int fromLevel) => FlatSpCost;

        /// <inheritdoc/>
        public void Apply(IReadOnlyList<SkillLevelChange> changes)
        {
            // 저장 대상(세이브의 배운 스킬 목록·SP 스탯)이 아직 없어 실제 반영은 못 한다. 확인 흐름 검증용 로그만.
            if (changes == null || changes.Count == 0) return;

            foreach (SkillLevelChange change in changes)
                Debug.Log($"[Skill] Apply {change.SkillId} → Lv.{change.NewLevel} (placeholder, 저장 안 됨)");
        }
    }
}
