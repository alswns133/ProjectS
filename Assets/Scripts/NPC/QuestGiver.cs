using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;

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

        // 범위 안에서 상호작용 키를 눌렀을 때. 반납 우선, 없으면 수락 순으로 처리한다.
        private void HandleInteract()
        {
            if (!playerNear) return;

            QuestManager questManager = QuestManager.Instance;
            if (questManager == null) return;

            // ID를 손으로 채웠으면 그 목록으로, 비워뒀으면 npcName으로 JSON에서 자동 조회한다.
            bool useIds = questIds.Count > 0;

            // 1) 반납 가능한 완료 퀘스트가 있으면 먼저 처리한다.
            List<QuestData> completable = useIds
                ? questManager.GetCompletableQuests(questIds)
                : questManager.GetCompletableQuestsForNpc(npcName);
            if (completable.Count > 0)
            {
                QuestsCompletable?.Invoke(completable);
                questManager.TurnInQuest(completable[0]);
                return;
            }

            // 2) 수락 가능한 퀘스트가 있으면 첫 번째를 수락한다.
            List<int> acceptable = useIds
                ? questManager.GetAcceptableQuestIds(questIds)
                : questManager.GetAcceptableQuestIdsForNpc(npcName);
            if (acceptable.Count > 0)
            {
                QuestsAvailable?.Invoke(acceptable);
                questManager.TryAcceptQuest(acceptable[0], out _);
            }
        }
    }
}
