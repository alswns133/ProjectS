using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.Skills
{
    /// <summary>
    /// 현재 배운 스킬 레벨의 런타임 단일 소스(in-memory). 스킬창 [확인]이 여기에 커밋하고,
    /// 액티브 계수 성장(<see cref="SkillProgress"/>)·패시브 스탯(<see cref="ProjectS.Players.PlayerStats"/>)이 여기서 읽는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>세이브(영속화)는 아직 없다(2026-08-26)</b> — 게임 재시작 시 초기화된다. 배운 레벨을 저장하는
    /// CharacterSaveData 필드가 생기면, ①복원 시 <see cref="SetLevels"/>로 주입하고 ②<see cref="SetLevels"/>에서
    /// 저장을 걸면 된다. "레벨을 아는 유일한 자리"를 이 클래스 하나로 모아 그 작업을 한 곳으로 좁혔다.
    /// </para>
    /// <para>
    /// GameSession과 같은 static 상태라 씬 배선이 필요 없다. 플레이 모드 재진입 시 static이 남지 않게
    /// <see cref="ResetOnLoad"/>로 초기화한다(프로젝트의 static 리셋 관례).
    /// </para>
    /// </remarks>
    public static class SkillState
    {
        // skillId → 현재 레벨. 없으면 그 스킬의 시작 레벨(액티브 1 / 패시브 0)로 본다.
        private static readonly Dictionary<int, int> levels = new();

        /// <summary>단축키 슬롯 개수(1~4).</summary>
        public const int SlotCount = 4;

        // 단축키 슬롯번호(1~SlotCount) → 등록된 스킬ID(없으면 0). 처음 접근 시 액티브 순서로 기본 구성한다.
        private static readonly Dictionary<int, int> loadout = new();
        private static bool loadoutInitialized;

        // 해금한 액티브 스킬 ID(스킬2·3·4). 스킬1·패시브는 항상 열려 있어 담지 않는다.
        private static readonly HashSet<int> unlocked = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            levels.Clear();
            loadout.Clear();
            loadoutInitialized = false;
            unlocked.Clear();
        }

        // ---------- 해금(스킬1 항상 / 스킬2·3·4 메인 퀘스트 보상) ----------

        /// <summary>
        /// 이 스킬이 사용 가능한(해금된) 상태인지. 공용 액션(평타 등)·캐릭터 스킬1·패시브는 항상 true,
        /// 캐릭터 스킬2·3·4(각성기 포함)는 해금돼야 true다.
        /// </summary>
        /// <param name="skillId">스킬 ID</param>
        /// <returns>사용 가능하면 true</returns>
        public static bool IsUnlocked(int skillId)
        {
            if (skillId < 100) return true;      // 공용 액션(평타·피니시·강공격) — 게이트 없음
            int num = skillId % 100;
            if (num == 1) return true;           // 캐릭터 스킬1 — 생성 시부터 항상
            if (num >= 11) return true;          // 패시브(11~) — 항상 사용 가능
            return unlocked.Contains(skillId);   // 캐릭터 스킬2·3·4
        }

        /// <summary>
        /// 스킬을 해금한다(메인 퀘스트 보상 SkillUnlock 등). 이미 해금됐으면 무시한다(중복 배너 방지).
        /// 해금 배너용 이벤트를 발행하고, 빈 단축키 슬롯이 있으면 자동 등록한 뒤 즉시 저장한다.
        /// </summary>
        /// <param name="skillId">해금할 스킬 ID</param>
        public static void Unlock(int skillId)
        {
            // 보상 TargetId가 스킬 번호(2·3·4)면 현재 캐릭터 스킬ID로 환산한다(공용 메인 퀘스트 대응,
            // ClassWeapon 보상과 같은 방식). 완성 ID(102 등)를 넣었으면 그대로 쓴다.
            skillId = ResolveSkillId(skillId);
            if (skillId <= 0 || IsUnlocked(skillId)) return;

            unlocked.Add(skillId);
            SkillEvents.FireSkillUnlocked(skillId);   // 해금 배너가 구독해 알림을 띄운다.

            // 빈 단축키 슬롯이 있으면 새 스킬을 자동 등록한다(플레이어 배치는 덮지 않는다).
            EnsureLoadout();
            for (int n = 1; n <= SlotCount; n++)
            {
                if (!loadout.TryGetValue(n, out int id) || id == 0)
                {
                    loadout[n] = skillId;
                    SkillEvents.FireLoadoutChanged(n, skillId);
                    break;
                }
            }

            PlayerSaveService.SaveNow();   // 해금은 딜리버릿 이벤트 → 즉시 저장.
        }

        // 스킬 번호(1~4)를 현재 캐릭터 스킬ID로 환산한다. 이미 완성 ID(>=100)면 그대로.
        private static int ResolveSkillId(int idOrNumber)
        {
            if (idOrNumber >= 100) return idOrNumber;

            int charId = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
            return charId > 0 ? charId * 100 + idOrNumber : idOrNumber;
        }

        // ---------- 세이브(배운 레벨 + 로드아웃) ----------

        /// <summary>현재 배운 레벨과 로드아웃을 세이브 데이터에 기록한다. PlayerSaveService.SaveNow가 호출한다.</summary>
        /// <param name="save">기록 대상(선택된 캐릭터). null이면 무시.</param>
        public static void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            // 배운 레벨: 시작 레벨(액티브 1 / 패시브 0)을 넘긴 것만 저장(기본값은 생략).
            save.skillLevels ??= new List<SkillLevelSave>();
            save.skillLevels.Clear();
            foreach (KeyValuePair<int, int> pair in levels)
            {
                if (pair.Value > DefaultLevel(pair.Key))
                    save.skillLevels.Add(new SkillLevelSave { skillId = pair.Key, level = pair.Value });
            }

            // 로드아웃: 슬롯 1~SlotCount → int[](인덱스 i = 슬롯 i+1).
            EnsureLoadout();
            if (save.skillLoadout == null || save.skillLoadout.Length != SlotCount)
                save.skillLoadout = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                save.skillLoadout[i] = loadout.TryGetValue(i + 1, out int id) ? id : 0;

            // 해금 목록.
            save.unlockedSkills ??= new List<int>();
            save.unlockedSkills.Clear();
            save.unlockedSkills.AddRange(unlocked);
        }

        /// <summary>
        /// 세이브 데이터의 배운 레벨·로드아웃을 주입한다. 접속 후 데이터·플레이어가 준비된 뒤(PlayerStats.Start 말미)
        /// 호출한다. 로드아웃을 HUD에 반영하고, 배운 레벨로 패시브 스탯을 다시 계산한다.
        /// </summary>
        /// <param name="save">복원할 세이브(선택된 캐릭터). null이면 기본 상태로 둔다.</param>
        public static void RestoreFrom(CharacterSaveData save)
        {
            levels.Clear();
            loadout.Clear();
            loadoutInitialized = false;
            unlocked.Clear();

            if (save != null && save.skillLevels != null)
            {
                foreach (SkillLevelSave s in save.skillLevels)
                    if (s != null && s.skillId != 0) levels[s.skillId] = s.level;
            }

            // 해금 목록 먼저 채운다(로드아웃 기본 배치가 해금 여부를 참조하므로).
            if (save != null && save.unlockedSkills != null)
            {
                foreach (int id in save.unlockedSkills)
                    if (id != 0) unlocked.Add(id);
            }

            // 저장된 로드아웃이 있으면 그대로, 없으면 기본(액티브 순서).
            bool hasSaved = save != null && save.skillLoadout != null && HasAnyNonZero(save.skillLoadout);
            if (hasSaved)
            {
                for (int i = 0; i < SlotCount && i < save.skillLoadout.Length; i++)
                    loadout[i + 1] = save.skillLoadout[i];
                loadoutInitialized = true;
            }
            else
            {
                EnsureLoadout();   // 기본 배치
            }

            // HUD 슬롯이 복원된 로드아웃을 그리도록 각 슬롯 변경을 발행하고, 패시브 스탯을 반영한다.
            for (int n = 1; n <= SlotCount; n++)
                SkillEvents.FireLoadoutChanged(n, loadout.TryGetValue(n, out int id) ? id : 0);
            RecomputeAndApply();
        }

        private static bool HasAnyNonZero(int[] arr)
        {
            foreach (int v in arr)
                if (v != 0) return true;
            return false;
        }

        // ---------- 로드아웃(단축키 1~4 등록) ----------

        /// <summary>
        /// 로드아웃이 기본 배치까지 마쳤는지. 데이터·캐릭터가 준비되면 true. 이 값이 true인데 슬롯이 0이면
        /// "의도적으로 비운 슬롯"이므로 그 키는 스킬을 발동하지 않아야 한다(초기화 전 폴백과 구분).
        /// </summary>
        public static bool IsLoadoutReady
        {
            get { EnsureLoadout(); return loadoutInitialized; }
        }

        /// <summary>단축키 슬롯에 등록된 스킬ID. 없으면 0.</summary>
        /// <param name="slotNumber">슬롯 번호(1~SlotCount)</param>
        /// <returns>등록된 스킬ID(없으면 0)</returns>
        public static int GetSlot(int slotNumber)
        {
            EnsureLoadout();
            return loadout.TryGetValue(slotNumber, out int id) ? id : 0;
        }

        /// <summary>이 스킬이 등록된 슬롯 번호. 없으면 0.</summary>
        public static int GetSlotOf(int skillId)
        {
            EnsureLoadout();
            foreach (KeyValuePair<int, int> pair in loadout)
                if (pair.Value == skillId) return pair.Key;
            return 0;
        }

        /// <summary>
        /// 스킬을 단축키 슬롯에 등록한다. 한 스킬은 한 슬롯에만 있으므로, 이미 다른 슬롯에 있으면 그 자리를 비우고
        /// (두 슬롯이 같은 스킬을 가리키지 않게), 대상 슬롯에 있던 스킬은 밀려난다(스왑이 아니라 대체).
        /// </summary>
        /// <param name="slotNumber">대상 슬롯(1~SlotCount)</param>
        /// <param name="skillId">등록할 스킬ID</param>
        public static void SetSlot(int slotNumber, int skillId)
        {
            if (slotNumber < 1 || slotNumber > SlotCount || skillId == 0) return;
            if (!IsUnlocked(skillId)) return;   // 잠긴 스킬은 등록 불가
            EnsureLoadout();

            // 같은 스킬이 이미 다른 슬롯에 있으면 그 슬롯을 비운다(중복 등록 방지).
            int previous = GetSlotOf(skillId);
            if (previous != 0 && previous != slotNumber)
            {
                loadout[previous] = 0;
                SkillEvents.FireLoadoutChanged(previous, 0);
            }

            loadout[slotNumber] = skillId;
            SkillEvents.FireLoadoutChanged(slotNumber, skillId);
            PlayerSaveService.MarkDirty();   // 등록은 캐릭터 영구 상태 → 다음 오토세이브/경계에 저장.
        }

        /// <summary>두 단축키 슬롯의 등록을 맞바꾼다(HUD 슬롯끼리 드래그로 재배치). 각 슬롯 변경을 발행한다.</summary>
        /// <param name="a">슬롯 A(1~SlotCount)</param>
        /// <param name="b">슬롯 B(1~SlotCount)</param>
        public static void SwapSlots(int a, int b)
        {
            if (a == b || a < 1 || a > SlotCount || b < 1 || b > SlotCount) return;
            EnsureLoadout();

            loadout.TryGetValue(a, out int idA);
            loadout.TryGetValue(b, out int idB);

            loadout[a] = idB;
            loadout[b] = idA;
            SkillEvents.FireLoadoutChanged(a, idB);
            SkillEvents.FireLoadoutChanged(b, idA);
            PlayerSaveService.MarkDirty();
        }

        /// <summary>단축키 슬롯 등록을 해제한다(드래그로 빼거나 우클릭 해제).</summary>
        /// <param name="slotNumber">대상 슬롯(1~SlotCount)</param>
        public static void ClearSlot(int slotNumber)
        {
            if (slotNumber < 1 || slotNumber > SlotCount) return;
            EnsureLoadout();

            if (loadout.TryGetValue(slotNumber, out int id) && id != 0)
            {
                loadout[slotNumber] = 0;
                SkillEvents.FireLoadoutChanged(slotNumber, 0);
                PlayerSaveService.MarkDirty();
            }
        }

        // 첫 접근 시 현재 캐릭터의 액티브 스킬을 SlotOrder 순으로 1~4에 기본 배치한다.
        // 데이터 로딩 전이면 초기화를 미룬다(다음 접근에서 다시 시도).
        private static void EnsureLoadout()
        {
            if (loadoutInitialized) return;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;

            int characterId = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
            if (characterId <= 0) return;   // 캐릭터가 아직 없으면 기본 배치를 미룬다(다음 접근에서 재시도).

            var actives = new List<SkillGrowthTable>();
            foreach (SkillGrowthTable row in json.SkillGrowthDict.Values)
            {
                if (row == null || row.Kind != SkillKind.Active) continue;
                if (characterId > 0 && row.CharacterId != characterId) continue;
                if (!IsUnlocked(row.SkillId)) continue;   // 잠긴 스킬(2·3·4 미해금)은 기본 배치에서 제외
                actives.Add(row);
            }
            actives.Sort((a, b) => a.SlotOrder.CompareTo(b.SlotOrder));

            for (int i = 0; i < SlotCount; i++)
                loadout[i + 1] = i < actives.Count ? actives[i].SkillId : 0;

            loadoutInitialized = true;
        }

        /// <summary>이 스킬의 현재 레벨. 저장된 값이 없으면 시작 레벨(성장행 없으면 1).</summary>
        /// <param name="skillId">스킬 식별자</param>
        /// <returns>현재 레벨</returns>
        public static int GetLevel(int skillId)
        {
            if (levels.TryGetValue(skillId, out int lv)) return lv;
            return DefaultLevel(skillId);
        }

        /// <summary>
        /// 스킬창 [확인]에서 바뀐 레벨을 커밋한다. 반영 후 플레이어 패시브 스탯을 재계산한다.
        /// </summary>
        /// <param name="changes">바뀐 스킬 레벨 목록</param>
        public static void SetLevels(IReadOnlyList<SkillLevelChange> changes)
        {
            if (changes == null || changes.Count == 0) return;

            foreach (SkillLevelChange change in changes)
                levels[change.SkillId] = change.NewLevel;

            RecomputeAndApply();
            PlayerSaveService.SaveNow();   // 스킬 배분 [확인]은 딜리버릿 커밋 → 즉시 저장(강화·레벨업과 같은 정책).
        }

        /// <summary>
        /// 현재 캐릭터의 패시브 합계를 다시 계산해 플레이어에 반영한다. 스킬 변경·플레이어 스폰 직후 호출한다.
        /// </summary>
        public static void RecomputeAndApply()
        {
            ProjectS.Players.PlayerStats stats =
                PlayerManager.Instance != null && PlayerManager.Instance.Player != null
                    ? PlayerManager.Instance.Player.Stats
                    : null;

            if (stats == null) return;   // 플레이어가 아직 없으면 스폰 후 다시 호출된다.
            stats.ApplyPassiveStats(BuildPassiveStats(stats.CharacterId));
        }

        // 현재 캐릭터의 패시브 스킬들을 훑어 EffectType별로 합산한다(레벨 초과분 × 레벨당 효과).
        private static PassiveStats BuildPassiveStats(int characterId)
        {
            PassiveStats result = default;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return result;

            foreach (SkillGrowthTable row in json.SkillGrowthDict.Values)
            {
                if (row == null || row.Kind != SkillKind.Passive) continue;
                if (characterId > 0 && row.CharacterId != characterId) continue;

                int steps = GetLevel(row.SkillId) - row.StartLevel;   // 패시브 StartLevel=0
                if (steps <= 0) continue;

                float total = row.EffectPerLevel * steps;

                switch (row.EffectType)
                {
                    case SkillEffectType.AttackPercent: result.AttackPercent += total; break;
                    case SkillEffectType.DefensePercent: result.DefensePercent += total; break;
                    case SkillEffectType.HpPercent: result.HpPercent += total; break;
                    case SkillEffectType.CritChance: result.CritChance += total; break;
                    case SkillEffectType.CritDamagePercent: result.CritDamage += total; break;
                    case SkillEffectType.StaminaMax: result.StaminaFlat += total; break;
                    case SkillEffectType.ArmorPenetrationPercent: result.Penetration += total; break;
                }
            }

            return result;
        }

        // 저장값이 없을 때의 레벨. 성장행이 있으면 그 시작 레벨(액티브 1 / 패시브 0), 없으면(평타 등) 1.
        private static int DefaultLevel(int skillId)
        {
            JsonManager json = JsonManager.Instance;
            if (json != null && json.IsReady)
            {
                SkillGrowthTable row = json.Get<SkillGrowthTable>(skillId);
                if (row != null) return row.StartLevel;
            }
            return 1;
        }
    }
}
