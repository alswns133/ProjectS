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
    /// SP 예산은 플레이어 레벨(레벨당 1P)에서 온다. 배운 레벨의 영속화(세이브)만 아직 없어
    /// 게임 재시작 시 배분이 초기화된다(SkillState 참고).
    /// </para>
    /// <para>
    /// 테이블이 비어 있으면(어드레서블/JSON 미등록) UI가 통째로 빈칸이 되지 않게
    /// <see cref="PlaceholderSkillSource"/>로 대체하고 경고를 남긴다.
    /// </para>
    /// </remarks>
    public class TableSkillSource : ISkillWindowSource
    {
        // SP 예산 = 플레이어 레벨(레벨당 1P: 4레벨=4P). 창을 열 때의 레벨로 고정한다.
        private readonly int totalSp;

        private readonly List<SkillSlotInfo> active = new();
        private readonly List<SkillSlotInfo> passive = new();

        // skillId → 레벨업 비용 배열(인덱스 = fromLevel - 시작레벨). 테이블/플레이스홀더 어느 쪽이 채웠든 여기서 조회한다.
        private readonly Dictionary<int, int[]> costById = new();

        // skillId → 시작(바닥) 레벨. 액티브 1 / 패시브 0으로 스킬마다 다르므로 비용 인덱싱에 함께 쓴다.
        private readonly Dictionary<int, int> startById = new();

        public TableSkillSource()
        {
            ProjectS.Players.PlayerStats stats =
                PlayerManager.Instance != null && PlayerManager.Instance.Player != null
                    ? PlayerManager.Instance.Player.Stats
                    : null;

            int characterId = stats != null ? stats.CharacterId
                : (PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0);
            totalSp = stats != null ? Mathf.Max(1, stats.Level) : 1;   // 레벨당 1P

            BuildFromTable(characterId);

            if (active.Count == 0 && passive.Count == 0)
            {
                Debug.LogWarning("[Skill] SkillGrowthTable에서 현재 캐릭터의 스킬을 찾지 못해 플레이스홀더로 대체합니다 " +
                                 "(어드레서블 'SkillGrowthTable' 등록/캐릭터 ID를 확인하세요).");
                FillFromPlaceholder();
            }
        }

        /// <inheritdoc/>
        public int TotalSp => totalSp;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetActiveSlots() => active;

        /// <inheritdoc/>
        public IReadOnlyList<SkillSlotInfo> GetPassiveSlots() => passive;

        /// <inheritdoc/>
        public int SpCost(int skillId, int fromLevel)
        {
            if (costById.TryGetValue(skillId, out int[] costs))
            {
                int start = startById.TryGetValue(skillId, out int s) ? s : 1;
                int index = fromLevel - start;
                if (costs != null && index >= 0 && index < costs.Length) return costs[index];
            }
            return 1;   // 비용 정보가 없으면 1로 본다(Validate가 정규화하므로 정상 데이터에선 도달하지 않는다).
        }

        /// <inheritdoc/>
        public void Apply(IReadOnlyList<SkillLevelChange> changes)
        {
            // 런타임 레벨 저장소에 커밋한다 → 패시브 스탯 재계산(플레이어·장비창 반영) + 액티브 계수 성장 발동.
            // 영속화(세이브)는 SkillState의 TODO 참고(게임 재시작 전까지만 유지).
            SkillState.SetLevels(changes);
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
            // 바닥 = 시작 레벨(액티브 1 / 패시브 0). 현재 레벨은 이미 배분한 값(SkillState)에서 읽어,
            // 창을 다시 열면 직전에 찍어 둔 레벨이 그대로 보인다.
            int start = row.StartLevel;
            int current = SkillState.GetLevel(row.SkillId);
            costById[row.SkillId] = row.SpCostPerLevel;
            startById[row.SkillId] = start;
            target.Add(new SkillSlotInfo(
                row.SkillId, row.Name, row.Description, row.IconAddress,
                row.Kind == SkillKind.Active, start, row.MaxLevel, current, row.PreviewMediaAddress,
                SkillState.IsUnlocked(row.SkillId)));
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

                int start = info.MinLevel;
                startById[info.SkillId] = start;

                int need = Mathf.Max(0, info.MaxLevel - start);
                int[] costs = new int[need];
                for (int i = 0; i < need; i++) costs[i] = placeholder.SpCost(info.SkillId, start + i);
                costById[info.SkillId] = costs;
            }
        }
    }
}
