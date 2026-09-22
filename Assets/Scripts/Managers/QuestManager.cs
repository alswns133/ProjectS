using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Players;

namespace ProjectS.Managers
{

    /// <summary>
    /// 진행 중/완료한 퀘스트의 소유자이자 진행 규칙의 단일 창구.
    /// 퀘스트 '정의'는 JsonManager(QuestTable)에서 읽고, 여기서는 수락·진행·반납만 다룬다.
    ///
    /// 보상은 직접 지급하지 않는다 — 반납 시 <see cref="QuestEvents.OnQuestCompleted"/>만 발행하고,
    /// 재화·아이템·경험치·스킬해금 지급은 각 소유자가 구독해서 처리한다(기획서 7장). 퀘스트 로직이
    /// 재화/스킬 시스템을 직접 참조하지 않게 하는 경계다.
    /// </summary>
    [RequireComponent(typeof(QuestRewardGranter))]
    public class QuestManager : MonoBehaviour
    {
        /// <summary>반복 퀘스트 동시 수락 슬롯 한도(기획서 2.2).</summary>
        private const int MaxRepeatSlots = 10;

        /// <summary>전역 접근점. 부트스트랩 씬에 하나 두고 DontDestroyOnLoad로 유지한다.</summary>
        public static QuestManager Instance { get; private set; }

        // 현재 진행 중인 퀘스트. NPC 조회·진행 보고가 이 목록을 훑는다.
        private readonly List<QuestData> activeQuests = new();

        /// <summary>
        /// 현재 진행 중인 퀘스트 목록(읽기 전용). 씬마다 새로 생성되는 UI(퀘스트 트래커 등)가
        /// 켜질 때 현재 상태를 다시 그리는 데 쓴다 — 이벤트는 '변화'만 알리므로 과거에 수락한 퀘스트는
        /// 이 목록을 훑어 복원해야 씬 전환 후에도 목록에 남는다.
        /// </summary>
        public IReadOnlyList<QuestData> ActiveQuests => activeQuests;

        // 반납까지 끝낸 퀘스트 ID. 선행 체인 판정의 기준이 된다(반복 퀘스트는 등록하지 않는다).
        private readonly HashSet<int> completedQuestIds = new();

        // 레벨 게이트 판정용. 씬마다 새로 스폰될 수 있어 필요할 때 지연 조회한다.
        private PlayerStats playerStats;

        // 싱글톤을 확립하고, 처치 이벤트를 구독해 Kill 목표 진행을 받는다.
        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // 처치 이벤트를 받아 Kill 목표를 진행시킨다. 진행 중 퀘스트만 훑으므로(AdvanceTargets)
            // 문서 §5.2의 "이벤트 기반, 진행 중 퀘스트만 검사" 방식과 일치한다.
            CombatEvents.OnEnemyKilled += HandleEnemyKilled;

            // 강화 결과를 받아 Enhance 목표를 진행시킨다(EnhanceEvents 주석이 예고한 구독자가 이것이다).
            EnhanceEvents.OnEnhanced += HandleEnhanced;

            // 수집 목표는 '지금 가방에 몇 개 있는가'가 곧 진행도라, 가방이 바뀔 때마다 다시 센다.
            // 획득만 세면 수락 전에 이미 가진 것이 빠지고, 쓰거나 버려도 진행도가 남는다.
            InventoryEvents.OnInventoryChanged += RefreshCollectObjectives;
            InventoryEvents.OnItemAdded += HandleItemAdded;
        }

        // 실제 인스턴스일 때만 구독했으므로, 파괴 시에도 이 인스턴스에 한해 해제한다(중복 인스턴스 영향 없음).
        private void OnDestroy()
        {
            if (Instance == this)
            {
                CombatEvents.OnEnemyKilled -= HandleEnemyKilled;
                EnhanceEvents.OnEnhanced -= HandleEnhanced;
                InventoryEvents.OnInventoryChanged -= RefreshCollectObjectives;
                InventoryEvents.OnItemAdded -= HandleItemAdded;
            }
        }

        // 세이브 복원은 퀘스트 정의(QuestTable)가 필요하므로 JsonManager 로딩을 기다린 뒤 수행한다.
        // async void는 Unity 진입점(Start)에서만 예외적으로 허용(JsonManager·PlayerStats와 같은 방침).
        private async void Start()
        {
            if (Instance != this) return;   // 중복 인스턴스(곧 파괴됨)는 복원하지 않는다

            JsonManager json = JsonManager.Instance;
            if (json != null && !json.IsReady) await json.ReadyTask;
            if (this == null) return;

            RestoreFrom(GameSession.SelectedCharacter);
        }

        // 처치 이벤트 콜백: 죽은 몬스터 ID를 Kill 목표 보고로 넘긴다.
        private void HandleEnemyKilled(int monsterId) => ReportKill(monsterId);

        // 획득 이벤트 콜백: AddItem은 개수와 무관하게 한 번만 발행되므로, 세지 말고 다시 센다.
        private void HandleItemAdded(ItemData item) => RefreshCollectObjectives();

        // 강화 이벤트 콜백: 실패는 목표 진행으로 치지 않는다(단계가 그대로이므로 '도달'도 없다).
        private void HandleEnhanced(EnhanceResult result)
        {
            if (result.Success) ReportEnhance(result.StepAfter);
        }

        // ---------- 세이브 / 복원 ----------

        /// <summary>
        /// 현재 퀘스트 진행(완료 목록 + 진행 중 목표 카운트·핀)을 세이브에 기록한다. 저장 시점에 호출한다.
        /// </summary>
        /// <param name="save">기록 대상 세이브(선택된 캐릭터). null이면 무시.</param>
        public void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            save.completedQuestIds = new List<int>(completedQuestIds);

            save.activeQuests = new List<QuestSave>(activeQuests.Count);
            foreach (QuestData quest in activeQuests)
            {
                QuestSave entry = new QuestSave { questId = quest.QuestId, pinned = quest.IsPinned };
                foreach (ObjectiveProgress objective in quest.Objectives)
                    entry.objectiveCounts.Add(objective.CurrentCount);
                save.activeQuests.Add(entry);
            }
        }

        /// <summary>
        /// 세이브의 퀘스트 진행을 현재 상태로 복원한다(부트스트랩에서 JsonManager 로딩 후 1회).
        /// 정의가 사라진 퀘스트는 건너뛴다. 트래커 등 UI는 씬 생성 시 ActiveQuests를 훑어 다시 그리므로
        /// 여기서 이벤트는 발행하지 않는다(복원은 '수락'이 아니라 상태 재구성이다).
        /// </summary>
        /// <param name="save">복원할 세이브. null이면 아무것도 하지 않는다.</param>
        public void RestoreFrom(CharacterSaveData save)
        {
            if (save == null) return;

            completedQuestIds.Clear();
            if (save.completedQuestIds != null)
            {
                foreach (int id in save.completedQuestIds)
                    completedQuestIds.Add(id);
            }

            activeQuests.Clear();
            if (save.activeQuests != null)
            {
                foreach (QuestSave entry in save.activeQuests)
                {
                    if (entry == null) continue;

                    QuestTable definition = GetDefinition(entry.questId);
                    if (definition == null) continue;   // 정의 없음(테이블 변경 등) → 건너뜀

                    QuestData quest = new QuestData(definition);

                    // 목표별 저장 카운트로 진행 복원(Advance가 목표치 도달 시 완료로 굳힌다).
                    int count = entry.objectiveCounts != null
                        ? Mathf.Min(entry.objectiveCounts.Count, quest.Objectives.Count)
                        : 0;
                    for (int i = 0; i < count; i++)
                        quest.Objectives[i].Advance(entry.objectiveCounts[i]);

                    quest.IsPinned = entry.pinned;
                    activeQuests.Add(quest);
                }
            }

            // 복원 결과를 알린다 — 로딩(async)보다 먼저 켜진 트래커 등이 빈 채로 남지 않도록 다시 그리게 한다.
            QuestEvents.FireQuestsRestored();
        }

        // 플레이어를 못 찾으면 레벨 제한을 통과시킨다(퀘스트가 막히는 것보다 낫다).
        private int PlayerLevel
        {
            get
            {
                if (playerStats == null)
                    playerStats = FindAnyObjectByType<PlayerStats>();

                return playerStats != null ? playerStats.Level : int.MaxValue;
            }
        }

        // ---------- NPC 조회용 ----------

        /// <summary>지정한 ID 목록 중 지금 수락할 수 있는 것만 골라 반환한다.</summary>
        public List<int> GetAcceptableQuestIds(IEnumerable<int> questIds)
        {
            var result = new List<int>();
            if (questIds == null) return result;

            foreach (int id in questIds)
            {
                if (CanAccept(id))
                    result.Add(id);
            }
            return result;
        }

        /// <summary>지정한 ID 목록 중 진행 중이면서 반납 가능한(모든 목표 완료) 퀘스트를 반환한다.</summary>
        public List<QuestData> GetCompletableQuests(IEnumerable<int> questIds)
        {
            var result = new List<QuestData>();
            if (questIds == null) return result;

            foreach (var quest in activeQuests)
            {
                if (!quest.IsReadyToTurnIn) continue;

                foreach (int id in questIds)
                {
                    if (quest.QuestId == id)
                    {
                        result.Add(quest);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// NPC 이름과 <see cref="QuestTable.QuestNpc"/>가 일치하는 퀘스트 중 수락 가능한 ID를 반환한다.
        /// QuestGiver가 ID 목록을 손으로 채우는 대신 이름으로 자동 조회할 때 쓴다.
        /// </summary>
        public List<int> GetAcceptableQuestIdsForNpc(string npcName)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(npcName) || JsonManager.Instance == null) return result;

            foreach (var definition in JsonManager.Instance.QuestDict.Values)
            {
                if (npcName.Equals(definition.QuestNpc) && CanAccept(definition.QuestId))
                    result.Add(definition.QuestId);
            }
            return result;
        }

        /// <summary>NPC 이름과 일치하고 반납 가능한(모든 목표 완료) 진행 중 퀘스트를 반환한다.</summary>
        public List<QuestData> GetCompletableQuestsForNpc(string npcName)
        {
            var result = new List<QuestData>();
            if (string.IsNullOrEmpty(npcName)) return result;

            foreach (var quest in activeQuests)
            {
                if (quest.IsReadyToTurnIn && npcName.Equals(quest.Definition.QuestNpc))
                    result.Add(quest);
            }
            return result;
        }

        /// <summary>
        /// NPC 퀘스트 목록(상태 포함)을 만든다. 그 NPC의 퀘스트를 훑어 안받음(수락가능)/진행중/완료가능만 담는다
        /// (완료해 재수락 불가한 것은 제외). 퀘스트 선택 리스트 UI가 그대로 뿌린다.
        /// </summary>
        /// <param name="npcName">조회할 NPC 이름</param>
        /// <returns>표시할 항목 목록(없으면 빈 목록)</returns>
        public List<NpcQuestEntry> GetNpcQuestEntries(string npcName)
        {
            var result = new List<NpcQuestEntry>();
            if (string.IsNullOrEmpty(npcName) || JsonManager.Instance == null) return result;

            foreach (var definition in JsonManager.Instance.QuestDict.Values)
            {
                if (!npcName.Equals(definition.QuestNpc)) continue;

                QuestData active = FindActive(definition.QuestId);
                NpcQuestStatus status;
                if (active != null)
                    status = active.IsReadyToTurnIn ? NpcQuestStatus.Completable : NpcQuestStatus.InProgress;
                else if (CanAccept(definition.QuestId))
                    status = NpcQuestStatus.Acceptable;
                else
                    continue;   // 완료(비반복)·조건 미달 등 → 목록에서 제외

                result.Add(new NpcQuestEntry(definition.QuestId, definition.Title, definition.QuestType, status));
            }
            return result;
        }

        /// <summary>
        /// 이 퀘스트를 이미 완료(반납)했는지. 세이브에 남는 기록이라 껐다 켜도 유지된다.
        /// 퀘스트 진척으로 열리는 문·연출처럼 "그 일이 있었나"를 물어야 하는 씬 오브젝트가 쓴다
        /// (<see cref="ProjectS.Scenes.QuestGateDoor"/>). 반복 퀘스트는 완료 등록을 하지 않으므로 항상 false다.
        /// </summary>
        /// <param name="questId">확인할 퀘스트 ID</param>
        /// <returns>완료했으면 true</returns>
        public bool IsCompleted(int questId) => completedQuestIds.Contains(questId);

        /// <summary>진행 중 퀘스트 중 해당 ID를 찾는다(없으면 null).</summary>
        public QuestData FindActive(int questId)
        {
            foreach (var quest in activeQuests)
            {
                if (quest.QuestId == questId)
                    return quest;
            }
            return null;
        }

        /// <summary>
        /// 이 퀘스트를 지금 수락할 수 있는지 판정한다.
        /// 공통: 진행 중 아님 + 레벨 + 선행 퀘스트. 메인은 완료 시 재수락 불가, 반복은 3슬롯 제한.
        /// </summary>
        /// <param name="questId">판정할 퀘스트 ID</param>
        /// <returns>수락 가능하면 true</returns>
        public bool CanAccept(int questId)
        {
            if (IsActive(questId)) return false;   // 이미 진행 중이면 중복 수락 금지

            QuestTable definition = GetDefinition(questId);
            if (definition == null) return false;

            bool isRepeat = definition.QuestType == QuestType.Repeat;

            if (isRepeat)
            {
                // 반복: 동시 수락 슬롯이 가득 차면 새로 못 받는다(비워야 함).
                if (CountActiveRepeats() >= MaxRepeatSlots)
                    return false;
            }
            else
            {
                // 메인: 한 번 반납하면 다시 못 받는다.
                if (completedQuestIds.Contains(questId))
                    return false;
            }

            // 레벨 제한(0이면 제한 없음).
            if (definition.RequiredLevel > 0 && PlayerLevel < definition.RequiredLevel)
                return false;

            // 선행 퀘스트(0이면 없음)가 완료돼 있어야 한다. 메인 체인용.
            if (definition.PrerequisiteQuestId != 0 && !completedQuestIds.Contains(definition.PrerequisiteQuestId))
                return false;

            return true;
        }

        // ---------- 수락 / 반납 ----------

        /// <summary>
        /// 퀘스트를 수락해 진행 목록에 올리고 <see cref="QuestEvents.OnQuestAccepted"/>를 발행한다.
        /// </summary>
        /// <param name="questId">수락할 퀘스트 ID</param>
        /// <param name="quest">성공 시 생성된 런타임 인스턴스</param>
        /// <returns>수락에 성공했으면 true</returns>
        public bool TryAcceptQuest(int questId, out QuestData quest)
        {
            quest = null;
            if (!CanAccept(questId)) return false;

            QuestTable definition = GetDefinition(questId);
            if (definition == null) return false;

            quest = new QuestData(definition);
            activeQuests.Add(quest);

            // 수집 퀘스트는 받은 즉시 이미 가진 개수를 반영한다(3/5로 시작하는 식).
            if (quest.ObjectiveType == ObjectiveType.Collect) RefreshCollectObjectives();
            // TODO(sound): 퀘스트 수락음 — SoundManager.Instance.PlaySFX(<퀘스트 수락 SFX>);
            QuestEvents.FireQuestAccepted(quest);

            // 커밋(①): 수락은 중요 이벤트 → 즉시 저장(씬 전환 전에 종료돼도 유실 방지).
            PlayerSaveService.SaveNow();
            return true;
        }

        /// <summary>
        /// 모든 목표를 마친 퀘스트를 반납한다. 진행 목록에서 빼고(메인이면 완료 등록)
        /// <see cref="QuestEvents.OnQuestCompleted"/>를 발행한다. 보상 지급은 그 구독자의 몫이다.
        /// 반복 퀘스트는 완료 등록을 하지 않아 슬롯이 비고 다시 수락할 수 있다.
        /// </summary>
        /// <param name="quest">반납할 퀘스트</param>
        /// <returns>반납에 성공했으면 true</returns>
        public bool TurnInQuest(QuestData quest)
        {
            if (quest == null || !quest.IsReadyToTurnIn) return false;
            if (!activeQuests.Remove(quest)) return false;

            // 수집은 '가져다주는' 퀘스트라 반납 시 요구 수량을 회수한다. 진행도를 세는 기준과 같은 가방에서
            // 빼므로(착용 중 장비는 애초에 세지 않는다) 모자랄 일이 없다.
            TakeCollectItems(quest);

            if (quest.QuestType != QuestType.Repeat)
                completedQuestIds.Add(quest.QuestId);

            // TODO(sound): 퀘스트 완료/반납음 — SoundManager.Instance.PlaySFX(<퀘스트 완료 SFX>); (보상 지급 팡파레와 겹치지 않게 조율)
            QuestEvents.FireQuestCompleted(quest);

            // 커밋(①): 보상 지급(OnQuestCompleted 구독자가 동기 처리 — 골드·경험치 등)이 끝난 뒤 즉시 저장한다.
            // 그래야 이 저장 스냅샷에 반납 보상까지 함께 담긴다.
            PlayerSaveService.SaveNow();
            return true;
        }

        /// <summary>
        /// 진행 중인 퀘스트를 포기한다. 진행 목록에서 빼고 <see cref="QuestEvents.OnQuestAbandoned"/>를 발행한다.
        /// 반납(<see cref="TurnInQuest"/>)과 달리 보상을 주지 않고 완료로 등록하지도 않으므로(completedQuestIds에
        /// 넣지 않는다), 선행 판정상 '수락 전' 상태로 돌아가 NPC에서 다시 수락할 수 있다. 진행도는 버려진다.
        /// </summary>
        /// <param name="quest">포기할 퀘스트</param>
        /// <returns>포기에 성공했으면 true(진행 목록에 없으면 false)</returns>
        public bool AbandonQuest(QuestData quest)
        {
            if (quest == null) return false;
            if (!activeQuests.Remove(quest)) return false;

            QuestEvents.FireQuestAbandoned(quest);

            // 커밋(①): 포기도 진행 목록을 바꾸는 중요 변화 → 즉시 저장(씬 전환 전 종료돼도 포기한 퀘스트가 되살아나지 않게).
            PlayerSaveService.SaveNow();
            return true;
        }

        // ---------- 목표 진행 보고 (목표 소스가 호출) ----------

        /// <summary>몬스터 처치를 보고한다. 대상이 일치하는 Kill 목표를 1 진행시킨다.</summary>
        /// <param name="monsterId">처치한 몬스터 ID</param>
        public void ReportKill(int monsterId) => AdvanceTargets(ObjectiveType.Kill, monsterId);

        /// <summary>
        /// 수집 목표를 지금 가방 보유량에 맞춘다. 인벤토리 변화·퀘스트 수락 때 자동으로 불리므로
        /// 보통 직접 부를 일은 없다(외부에서 강제로 다시 세고 싶을 때만).
        /// </summary>
        public void ReportCollect() => RefreshCollectObjectives();

        // 진행 중인 수집 퀘스트의 목표를 가방 보유량으로 다시 맞춘다.
        // 이미 가진 것도 세고(수락 즉시 반영), 쓰거나 버리면 도로 내려간다.
        private void RefreshCollectObjectives()
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null) return;

            bool dirty = false;

            foreach (var quest in activeQuests)
            {
                if (quest.ObjectiveType != ObjectiveType.Collect) continue;

                foreach (var objective in quest.Objectives)
                {
                    int owned = inventory.GetItemCount(ResolveCollectTargetId(objective.Target.TargetId));
                    if (!objective.SetCount(owned)) continue;

                    QuestEvents.FireQuestProgressUpdated(quest, objective.CurrentCount, objective.Target.RequiredCount);
                    dirty = true;
                }
            }

            // 수집은 가방 변화에 따라 오르내리므로 마일스톤 저장(SaveNow)을 쓰지 않는다.
            // 실제 확정은 반납 시점이고, 그때 TurnInQuest가 즉시 저장한다.
            if (dirty) PlayerSaveService.MarkDirty();
        }

        /// <summary>
        /// 레벨 도달/지역 도착을 보고한다. 대상이 일치하는 Reach 목표를 진행시킨다.
        /// 실제로 진행시킨 목표가 있었는지를 반환한다 — <see cref="ProjectS.Scenes.QuestReachTrigger"/>가
        /// 이 값으로 "한 번 쓰고 잠글지"를 판단한다(받기 전에 지나가서 허탕 친 경우까지 잠기면 안 되므로).
        /// </summary>
        /// <param name="targetId">도달한 레벨 또는 지역 ID</param>
        /// <returns>일치하는 진행 중 Reach 목표를 하나라도 진행시켰으면 true</returns>
        public bool ReportReach(int targetId) => AdvanceTargets(ObjectiveType.Reach, targetId);

        /// <summary>던전/레이드 클리어를 보고한다. 대상이 일치하는 Clear 목표를 1 진행시킨다.</summary>
        /// <param name="dungeonId">클리어한 던전/레이드 ID</param>
        public void ReportClear(int dungeonId) => AdvanceTargets(ObjectiveType.Clear, dungeonId);

        /// <summary>
        /// 강화 성공을 보고한다. Enhance 목표의 TargetId는 '도달해야 할 강화 단계'이므로
        /// <b>일치가 아니라 이상(&gt;=)으로 판정</b>한다 — "+1 강화에 성공하라"(TargetId 1)는 +3에서 +4로 올려도
        /// 만족하고, "+5를 만들어라"(TargetId 5)는 5단계에 닿아야 만족한다.
        /// </summary>
        /// <param name="stepAfter">강화 성공 후의 단계</param>
        public void ReportEnhance(int stepAfter)
        {
            foreach (var quest in activeQuests)
            {
                if (quest.ObjectiveType != ObjectiveType.Enhance) continue;

                foreach (var objective in quest.Objectives)
                {
                    if (objective.IsCompleted) continue;
                    if (stepAfter < objective.Target.TargetId) continue;   // 요구 단계에 못 미침

                    AdvanceObjective(quest, objective);
                    break;
                }
            }
        }

        // 수집 퀘스트 반납: 목표 수량만큼 가방에서 회수한다. 수집이 아니면 아무 일도 하지 않는다.
        private static void TakeCollectItems(QuestData quest)
        {
            if (quest.ObjectiveType != ObjectiveType.Collect) return;

            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null) return;

            foreach (ObjectiveProgress objective in quest.Objectives)
                inventory.TakeItems(ResolveCollectTargetId(objective.Target.TargetId), objective.Target.RequiredCount);
        }

        /// <summary>
        /// Collect 목표의 TargetId를 현재 캐릭터 기준으로 환산한다. 저작은 검(소드) ID로 하고,
        /// 거너 캐릭터면 같은 종류의 총 ID로 자동 치환한다(<see cref="QuestRewardData.ResolveClassWeaponId"/>
        /// — ClassWeapon 보상과 같은 함수를 재사용). 무기가 아닌 아이템은 그대로 반환한다.
        /// </summary>
        /// <param name="targetId">퀘스트 데이터에 저작된 TargetId(검 ID 기준)</param>
        /// <returns>현재 캐릭터가 실제로 들고 있을 아이템 ID</returns>
        private static int ResolveCollectTargetId(int targetId)
        {
            int charType = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
            return QuestRewardData.ResolveClassWeaponId(targetId, charType);
        }

        // 목표 하나를 1 올리고 발행·저장까지 한다. 보고 경로(일치 비교 / 단계 이상 비교)가 갈려도
        // 진행 후 처리는 같아야 하므로 여기로 모은다.
        private static void AdvanceObjective(QuestData quest, ObjectiveProgress objective)
        {
            objective.Advance(1);
            QuestEvents.FireQuestProgressUpdated(quest, objective.CurrentCount, objective.Target.RequiredCount);

            // 부분 진행은 dirty로 묶어 오토세이브(②)에 맡기고, 목표 완주는 마일스톤이라 즉시 저장(①).
            if (objective.IsCompleted) PlayerSaveService.SaveNow();
            else PlayerSaveService.MarkDirty();
        }

        // 한 사건이 같은 퀘스트를 이중 진행하지 않도록 퀘스트마다 첫 매치 하나만 올리고,
        // 서로 다른 퀘스트는 각각 진행시킨다. 진행 방식은 이벤트 기반이라 진행 중 퀘스트만 훑는다.
        // 반환값은 호출자가 "실제로 뭔가 진행됐는지"를 알아야 할 때 쓴다(예: QuestReachTrigger의 1회성 잠금).
        private bool AdvanceTargets(ObjectiveType type, int targetId)
        {
            bool advanced = false;

            foreach (var quest in activeQuests)
            {
                if (quest.ObjectiveType != type) continue;

                foreach (var objective in quest.Objectives)
                {
                    if (objective.IsCompleted) continue;
                    if (objective.Target.TargetId != targetId) continue;

                    AdvanceObjective(quest, objective);
                    advanced = true;
                    break;
                }
            }

            return advanced;
        }

        // ---------- 내부 유틸 ----------

        // 이미 진행 중인 퀘스트인지 확인한다(중복 수락 방지용).
        private bool IsActive(int questId)
        {
            foreach (var quest in activeQuests)
            {
                if (quest.QuestId == questId)
                    return true;
            }
            return false;
        }

        // 현재 진행 중인 반복 퀘스트 개수. 3슬롯 제한 판정에 쓴다.
        private int CountActiveRepeats()
        {
            int count = 0;
            foreach (var quest in activeQuests)
            {
                if (quest.QuestType == QuestType.Repeat)
                    count++;
            }
            return count;
        }

        // 퀘스트 정의(JSON 행)를 조회한다. 로딩 전이거나 없는 ID면 null.
        private static QuestTable GetDefinition(int questId)
            => JsonManager.Instance != null ? JsonManager.Instance.Get<QuestTable>(questId) : null;
    }
}
