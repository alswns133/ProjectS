using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.NPCs
{
    /// <summary>NPC 머리 위 퀘스트 표시 상태. <see cref="QuestMarker"/>가 이 값으로 아이콘을 정한다.</summary>
    public enum QuestMarkerState
    {
        None,          // 표시 없음
        Available,     // ! 받을 퀘스트 있음
        Completable    // ? 반납 가능한 완료 퀘스트 있음
    }

    /// <summary>
    /// 퀘스트를 주고 반납받는 NPC 컴포넌트. 근접 감지는 같은 NPC의 <see cref="NpcOutlineTrigger"/>를
    /// 재사용한다 — Awake에서 자동으로 찾아 그 C# 이벤트(PlayerNearChanged)에 구독하므로,
    /// 인스펙터 연결 없이 컴포넌트만 붙이면 동작한다. 감지 거리 판정을 이중으로 두지 않기 위함이다.
    ///
    /// 상호작용 입력은 반드시 <see cref="PlayerInputHandler.Interacted"/>를 거친다(입력 경계 규칙).
    /// 범위 안일 때만 구독하므로, 여러 NPC가 동시에 입력에 반응하는 일이 없다.
    ///
    /// 이 컴포넌트는 '무엇을 할지'만 정하고 화면 출력은 하지 않는다. 대화·수락 확인·보상 UI는
    /// <see cref="QuestsAvailable"/>/<see cref="QuestsCompletable"/> 이벤트를 구독해 나중에 붙인다.
    /// </summary>
    public class QuestGiver : MonoBehaviour
    {
        [SerializeField] private string npcName = "NPC";

        [Tooltip("이 NPC가 주거나 반납받는 퀘스트 ID 목록. 비워두면 npcName과 QuestNpc가 일치하는 퀘스트를 JSON에서 자동으로 찾는다.")]
        [SerializeField] private List<int> questIds = new();

        [Tooltip("근접 감지기. 비워두면 Awake에서 자식 포함으로 자동으로 찾는다.")]
        [SerializeField] private NpcOutlineTrigger proximity;

        [Header("대화")]
        [Tooltip("NPC 초상화(대화창 왼쪽·Npc 화자 줄). 없으면 초상화 없이 진행한다.")]
        [SerializeField] private Sprite npcPortrait;

        [Tooltip("줄·받을 퀘스트가 모두 없을 때 재생할 잡담 대사(DialogueTable ID). " +
                 "비워두면(0) npcName과 DialogueTable.Npc가 일치하는 잡담 대사를 자동으로 찾는다(questIds와 같은 방식). " +
                 "퀘스트 도입/반납 대사는 각 퀘스트가 가진다.")]
        [SerializeField] private int idleDialogueId;

        // 한 번 찾아 캐싱한다. 씬에 플레이어는 하나뿐이라 재조회할 일이 없다.
        private PlayerInputHandler input;

        // 범위 안에 있을 때만 상호작용을 받는다. OnPlayerNearChanged가 켜고 끈다.
        private bool playerNear;

        /// <summary>이 NPC의 이름.</summary>
        public string NpcName => npcName;

        /// <summary>
        /// 상호작용 시 수락 가능한 퀘스트가 있을 때 발행(수락 처리 직전).
        /// UI가 붙기 전까지는 구독자가 없어도 무방하다.
        /// </summary>
        public event Action<List<int>> QuestsAvailable;

        /// <summary>
        /// 상호작용 시 반납 가능한 완료 퀘스트가 있을 때 발행(반납 처리 직전).
        /// </summary>
        public event Action<List<QuestData>> QuestsCompletable;

        // 근접 감지기(NpcOutlineTrigger) 참조를 확보한다.
        private void Awake()
        {
            // 자식(전용 감지용 오브젝트)에 붙어 있는 경우까지 포함해 찾는다.
            if (proximity == null)
                proximity = GetComponentInChildren<NpcOutlineTrigger>(true);
        }

        // 근접 진입/이탈 신호에 구독한다(상호작용 가능 구간을 켜고 끄는 게이트).
        private void OnEnable()
        {
            if (proximity != null)
                proximity.PlayerNearChanged += OnPlayerNearChanged;
        }

        // 씬 정리·비활성화 때 근접·입력 구독이 남지 않도록 짝을 맞춘다.
        private void OnDisable()
        {
            if (proximity != null)
                proximity.PlayerNearChanged -= OnPlayerNearChanged;

            UnsubscribeInput();
        }

        /// <summary>
        /// 플레이어의 근접 진입/이탈 처리. 보통은 NpcOutlineTrigger에 자동 구독되어 호출되지만,
        /// 다른 감지원과 연결하고 싶으면 인스펙터의 UnityEvent에 직접 걸어도 된다(중복 호출은 무해).
        /// </summary>
        /// <param name="near">범위 안이면 true, 벗어나면 false</param>
        public void OnPlayerNearChanged(bool near)
        {
            playerNear = near;
            if (near)
                SubscribeInput();
            else
                UnsubscribeInput();
        }

        /// <summary>
        /// 이 NPC의 현재 퀘스트 표시 상태를 계산한다(<see cref="QuestMarker"/>가 호출).
        /// 반납 가능(완료)을 우선한다 — 진행하던 퀘스트 마무리가 새 퀘스트 수락보다 앞선다.
        /// </summary>
        /// <returns>표시할 마커 상태(없음/받을 퀘스트/반납 가능)</returns>
        public QuestMarkerState EvaluateMarkerState()
        {
            QuestManager questManager = QuestManager.Instance;
            if (questManager == null) return QuestMarkerState.None;

            bool useIds = questIds.Count > 0;

            List<QuestData> completable = useIds
                ? questManager.GetCompletableQuests(questIds)
                : questManager.GetCompletableQuestsForNpc(npcName);
            if (completable.Count > 0) return QuestMarkerState.Completable;

            List<int> acceptable = useIds
                ? questManager.GetAcceptableQuestIds(questIds)
                : questManager.GetAcceptableQuestIdsForNpc(npcName);
            if (acceptable.Count > 0) return QuestMarkerState.Available;

            return QuestMarkerState.None;
        }

        // 상호작용 입력에 구독한다. 플레이어 입력 핸들러는 1회 조회 후 캐싱한다.
        private void SubscribeInput()
        {
            if (input == null)
                input = FindAnyObjectByType<PlayerInputHandler>();
            if (input == null) return;

            input.Interacted -= HandleInteract;   // 중복 구독 방지
            input.Interacted += HandleInteract;
        }

        // 상호작용 입력 구독을 해제한다(범위 이탈·비활성화 시).
        private void UnsubscribeInput()
        {
            if (input != null)
                input.Interacted -= HandleInteract;
        }

        // 범위 안에서 상호작용 키를 눌렀을 때. 그 상황에 맞는 대사를 재생하고, 끝나면 퀘스트를 처리한다.
        // 대사는 퀘스트에 귀속된다 — 수락 대사는 '수락 가능'일 때만 나오므로, 수락하고 나면 다시 안 나온다.
        private void HandleInteract()
        {
            if (!playerNear) return;

            QuestManager questManager = QuestManager.Instance;
            if (questManager == null) return;

            // 이미 대화 중이면 무시한다(진행은 대화 입력/버튼이 맡는다).
            DialogueManager dialogue = DialogueManager.Instance;
            if (dialogue != null && dialogue.IsPlaying) return;

            // ID를 손으로 채웠으면 그 목록으로, 비워뒀으면 npcName으로 JSON에서 자동 조회한다.
            bool useIds = questIds.Count > 0;

            // 1) 반납 가능한 완료 퀘스트 → 그 퀘스트의 반납 대사 → 반납.
            List<QuestData> completable = useIds
                ? questManager.GetCompletableQuests(questIds)
                : questManager.GetCompletableQuestsForNpc(npcName);
            if (completable.Count > 0)
            {
                QuestData quest = completable[0];
                QuestsCompletable?.Invoke(completable);
                PlayDialogueThen(quest.Definition.TurnInDialogueId, quest.Title, () => questManager.TurnInQuest(quest));
                return;
            }

            // 2) 수락 가능한 퀘스트 → 그 퀘스트의 도입 대사 → 수락.
            //    수락하면 더 이상 '수락 가능'이 아니므로 이 도입 대사는 다시 재생되지 않는다.
            List<int> acceptable = useIds
                ? questManager.GetAcceptableQuestIds(questIds)
                : questManager.GetAcceptableQuestIdsForNpc(npcName);
            if (acceptable.Count > 0)
            {
                int questId = acceptable[0];
                QuestsAvailable?.Invoke(acceptable);
                QuestTable definition = JsonManager.Instance != null ? JsonManager.Instance.Get<QuestTable>(questId) : null;
                int introId = definition != null ? definition.IntroDialogueId : 0;
                string questName = definition != null ? definition.Title : string.Empty;
                PlayDialogueThen(introId, questName, () => questManager.TryAcceptQuest(questId, out _));
                return;
            }

            // 3) 줄 것도 받을 것도 없으면 잡담 대사(있으면)만 재생한다(퀘스트명 없음).
            PlayDialogueThen(ResolveIdleDialogueId(), string.Empty, null);
        }

        // 잡담 대사 ID를 정한다. idleDialogueId를 손으로 채웠으면 그걸, 비워뒀으면(0)
        // npcName과 DialogueTable.Npc가 일치하는 대사를 자동으로 찾는다(questIds 자동 조회와 같은 방식).
        private int ResolveIdleDialogueId()
        {
            if (idleDialogueId > 0) return idleDialogueId;

            JsonManager json = JsonManager.Instance;
            if (json == null || string.IsNullOrEmpty(npcName)) return 0;

            foreach (var dialogue in json.DialogueDict.Values)
            {
                if (npcName.Equals(dialogue.Npc))
                    return dialogue.DialogueId;
            }
            return 0;
        }

        // 대사(DialogueTable ID)를 재생하고 끝나면 action을 실행한다. questName은 대화창 상단에 표시된다.
        // 대화 시스템이 없거나 대사 ID가 0이면 대화를 건너뛰고 바로 action을 실행한다.
        private void PlayDialogueThen(int dialogueId, string questName, Action action)
        {
            DialogueManager dialogue = DialogueManager.Instance;
            if (dialogue != null && dialogueId > 0)
                dialogue.Play(npcName, npcPortrait, dialogueId, questName, action);
            else
                action?.Invoke();
        }
    }
}
