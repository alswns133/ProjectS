using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Managers;

namespace ProjectS.Skills
{
    /// <summary>
    /// <see cref="SkillGrowthTable"/>(+현재 캐릭터)에서 스킬창 데이터를 읽는 실제 소스.
    /// 현재 캐릭터의 행만 걸러 액티브/패시브로 나누고 <c>SlotOrder</c>로 정렬한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 아직 없는 두 가지는 명확히 자리를 비워 뒀다(2026-08-26):
    /// ① 배운 레벨 저장이 없어 현재 레벨은 모두 <see cref="MinLevel"/>에서 시작하고,
    /// ② SP 예산 스탯이 없어 <see cref="TotalSp"/>는 상수(<see cref="DefaultTotalSp"/>)다.
    /// 세이브/SP 시스템이 생기면 이 두 곳만 실제 값으로 바꾸면 된다.
    /// </para>
    /// <para>
    /// 테이블이 비어 있으면(어드레서블/JSON 미등록) UI가 통째로 빈칸이 되지 않게
    /// <see cref="PlaceholderSkillSource"/>로 대체하고 경고를 남긴다.
    /// </para>
    /// </remarks>
    public class TableSkillSource : ISkillWindowSource
    {
        // 배운 스킬의 바닥 레벨. 스크린샷 기준 배운 스킬은 1부터 시작한다(0=미해금 개념은 아직 없음).
        private const int MinLevel = 1;

        // SP 예산. TODO: SP 스탯/세이브가 생기면 거기서 읽는다.
        private const int DefaultTotalSp = 45;

        private readonly List<SkillSlotInfo> active = new();
        private readonly List<SkillSlotInfo> passive = new();

        // skillId → 레벨업 비용 배열(인덱스 = fromLevel - MinLevel). 테이블/플레이스홀더 어느 쪽이 채웠든 여기서 조회한다.
        private readonly Dictionary<int, int[]> costById = new();

        public TableSkillSource()
        {
            int characterId = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
            BuildFromTable(characterId);

            if (active.Count == 0 && passive.Count == 0)
            {
                Debug.LogWarning("[Skill] SkillGrowthTable에서 현재 캐릭터의 스킬을 찾지 못해 플레이스홀더로 대체합니다 " +
                                 "(어드레서블 'SkillGrowthTable' 등록/캐릭터 ID를 확인하세요).");
                FillFromPlaceholder();
            }
        }

        /// <inheritdoc/>
        public int TotalSp => DefaultTotalSp;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetActiveSlots() => active;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetPassiveSlots() => passive;

        /// <inheritdoc/>
        public int SpCost(int skillId, int fromLevel)
        {
            if (costById.TryGetValue(skillId, out int[] costs))
            {
                int index = fromLevel - MinLevel;
                if (costs != null && index >= 0 && index < costs.Length) return costs[index];
            }
            return 1;   // 비용 정보가 없으면 1로 본다(Validate가 정규화하므로 정상 데이터에선 도달하지 않는다).
        }

        /// <inheritdoc/>
        public void Apply(IReadOnlyList<SkillLevelChange> changes)
        {
            // 배운 레벨 저장 대상(세이브의 스킬 레벨·SP)이 아직 없어 실제 반영은 못 한다. 확인 흐름 검증용 로그만.
            // TODO: 세이브 스키마에 스킬 레벨/사용 SP가 생기면 여기서 커밋한다.
            if (changes == null || changes.Count == 0) return;

            foreach (SkillLevelChange change in changes)
                Debug.Log($"[Skill] Apply {change.SkillId} → Lv.{change.NewLevel} (저장 미구현, 아직 반영 안 됨)");
        }

        // 테이블에서 현재 캐릭터 행만 골라 두 그룹으로 나눈다. SlotOrder로 정렬해 창 배치를 데이터가 정하게 한다.
        private void BuildFromTable(int characterId)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;   // 로딩 전이면 비운다 → 호출측 폴백

            var activeRows = new List<SkillGrowthTable>();
            var passiveRows = new List<SkillGrowthTable>();

            foreach (SkillGrowthTable row in json.SkillGrowthDict.Values)
            {
                if (row == null) continue;
                if (characterId > 0 && row.CharacterId != characterId) continue;

                (row.Kind == SkillKind.Active ? activeRows : passiveRows).Add(row);
            }

            activeRows.Sort((a, b) => a.SlotOrder.CompareTo(b.SlotOrder));
            passiveRows.Sort((a, b) => a.SlotOrder.CompareTo(b.SlotOrder));

            foreach (SkillGrowthTable row in activeRows) AddRow(active, row);
            foreach (SkillGrowthTable row in passiveRows) AddRow(passive, row);
        }

        private void AddRow(List<SkillSlotInfo> target, SkillGrowthTable row)
        {
            costById[row.SkillId] = row.SpCostPerLevel;
            target.Add(new SkillSlotInfo(
                row.SkillId, row.Name, row.Description, row.IconAddress,
                row.Kind == SkillKind.Active, MinLevel, row.MaxLevel, MinLevel, row.PreviewMediaAddress));
        }

        // 테이블이 비었을 때만 쓰는 개발용 대체 데이터. 비용은 플레이스홀더 규칙(레벨당 1)으로 채운다.
        private void FillFromPlaceholder()
        {
            var placeholder = new PlaceholderSkillSource();
            CopyGroup(placeholder, placeholder.GetActiveSlots(), active);
            CopyGroup(placeholder, placeholder.GetPassiveSlots(), passive);
        }

        private void CopyGroup(PlaceholderSkillSource placeholder, IReadOnlyList<SkillSlotInfo> from, List<SkillSlotInfo> to)
        {
            foreach (SkillSlotInfo info in from)
            {
                to.Add(info);

                int need = Mathf.Max(0, info.MaxLevel - MinLevel);
                int[] costs = new int[need];
                for (int i = 0; i < need; i++) costs[i] = placeholder.SpCost(info.SkillId, MinLevel + i);
                costById[info.SkillId] = costs;
            }
        }
    }
}
