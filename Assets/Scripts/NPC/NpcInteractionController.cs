using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;
using ProjectS.Debugging;

namespace ProjectS.NPCs
{
    /// <summary>NPC 상호작용 화면. 뷰는 <see cref="NpcInteractionController.ScreenChanged"/>로 자기 화면일 때만 켠다.</summary>
    public enum NpcScreen
    {
        None,       // 대화 중이거나 상호작용 밖 — 허브/리스트 숨김
        Hub,        // 상점/퀘스트목록/닫기 버튼
        QuestList   // 퀘스트 선택 리스트 (보상은 별도 화면이 아니라 수락/반납 대화 마지막 줄에 표시)
    }

    /// <summary>NPC 머리 위 퀘스트 표시 상태. <see cref="QuestMarker"/>가 이 값으로 아이콘을 정한다.</summary>
    public enum QuestMarkerState
    {
        None,          // 표시 없음
        Available,     // ! 받을 퀘스트 있음
        Completable    // ? 반납 가능한 완료 퀘스트 있음
    }

    /// <summary>
    /// NPC 허브의 고유 기능. NPC마다 가진 것만 허브에 버튼으로 뜬다. 새 기능이 생기면 여기에 값 하나 추가하고
    /// 대응 버튼(<see cref="NpcHubFeatureButton"/>)을 씬에 배치하면 된다 — 컨트롤러/뷰 코드는 안 건드려도 된다.
    /// 선택 시 실제 동작은 <see cref="NpcInteractionController.HubFeatureSelected"/> 구독자가 맡는다
    /// (컨트롤러는 '무엇이 눌렸는지'만 알린다).
    /// </summary>
    public enum NpcHubFeature
    {
        None,      // 기능 없음(순수 대화 NPC). 인스펙터 기본값 = 미설정 버튼/빈 슬롯이 상점으로 잡히지 않게 하는 안전판.
        Shop,      // 상점
        Enhance,   // 강화
        Storage,   // 창고
        Craft      // 제작
    }

    /// <summary>
    /// NPC 상호작용 상태 머신. F → 인사말(스킵off) → 허브(상점/퀘스트목록/닫기) →
    /// 퀘스트 리스트(안받음→도입대화·수락 / 완료가능→보상→반납) → … 처음으로=인사말 복귀.
    ///
    /// 뷰(허브/리스트/보상)에 하드 의존하지 않는다. 화면 전환은 <see cref="ScreenChanged"/> 이벤트로 알리고
    /// 데이터는 프로퍼티(QuestEntries 등)로 읽어가며, 버튼/키는 이 클래스의 공개 메서드(SelectQuest 등)를 호출한다.
    /// → 컨트롤러(브레인)를 뷰보다 먼저 만들 수 있고, 뷰는 이 계약에 맞춰 붙이면 된다.
    ///
    /// 상호작용 동안 플레이어 입력·카메라·커서를 컨트롤러가 통째로 관리한다(대화는 ManageFreeze=false로 관여 안 함)
    /// → 대화↔허브 전환 때 커서가 사라지지 않고, 대화 종료 키가 게임 입력으로 새지 않는다.
    ///
    /// ※ 옛 QuestGiver(자동 수락/반납)를 대체·삭제한 상위 호환이다. 머리 위 마커(QuestMarker)와 이름표
    ///   (NpcNameplate)도 이 컨트롤러를 참조한다. NPC에는 이 컨트롤러 하나만 붙인다.
    /// </summary>
    public class NpcInteractionController : MonoBehaviour
    {
        [SerializeField] private string npcName = "NPC";
        [SerializeField] private Sprite npcPortrait;

        [Tooltip("인사말 대화 ID. 비워두면(0) npcName과 DialogueTable.Npc가 일치하는 인사말 행을 자동으로 찾는다.")]
        [SerializeField] private int greetingDialogueId;

        [Tooltip("이 NPC가 제공하는 허브 고유기능(상점/강화/…). 여기 담긴 것만 허브에 버튼으로 뜬다.")]
        [SerializeField] private List<NpcHubFeature> hubFeatures = new();

        [Tooltip("근접 감지기. 비우면 자식에서 자동 탐색.")]
        [SerializeField] private NpcOutlineTrigger proximity;

        private PlayerInputHandler input;
        private CameraPivotController cameraPivot;
        private bool playerNear;
        private bool interacting;

        private CursorLockMode savedLockState;
        private bool savedCursorVisible;

        private readonly List<NpcQuestEntry> questEntries = new();
        private int lastGreetingIndex = -1;   // 직전 인사말 줄(연속 중복 회피)

        private QuestData rewardTurnInTarget;   // 반납 모드일 때의 반납 대상(수락 미리보기면 null)
        private int rewardAcceptId;             // 수락 미리보기일 때 수락할 퀘스트 id

        // ---- 공유 뷰가 물리는 static 훅 ----

        /// <summary>지금 상호작용 중인 컨트롤러(동시 하나). 허브/리스트 뷰는 싱글톤이라 이걸로 대상 NPC를 안다.</summary>
        public static NpcInteractionController Active { get; private set; }

        /// <summary>상호작용 시작(컨트롤러)/종료(null) 시 발행. 뷰는 이걸 받아 Active.ScreenChanged에 붙었다 뗀다.</summary>
        public static event Action<NpcInteractionController> ActiveChanged;

        /// <summary>허브 고유기능 버튼을 눌렀을 때 발행(컨트롤러, 기능). 상점/강화 등 실제 구현이 이걸 구독해 연다.</summary>
        public static event Action<NpcInteractionController, NpcHubFeature> HubFeatureSelected;

        // ---- 뷰가 읽는 현재 화면 데이터 ----

        /// <summary>현재 화면. 뷰는 이 값이 자기 화면이면 켜고 아니면 끈다.</summary>
        public NpcScreen CurrentScreen { get; private set; } = NpcScreen.None;

        /// <summary>이 NPC의 이름(대화 화자·표시용).</summary>
        public string NpcName => npcName;

        /// <summary>이 NPC의 일러스트(허브·리스트·대화에서 표시). 없으면 null.</summary>
        public Sprite NpcPortrait => npcPortrait;

        /// <summary>허브 본문에 띄울 인사말 한 줄(상호작용/처음으로마다 새로 뽑힘).</summary>
        public string GreetingText { get; private set; } = string.Empty;

        /// <summary>이 NPC가 해당 허브 기능을 제공하는지(허브 뷰가 버튼을 켤지 판단).</summary>
        /// <param name="feature">확인할 기능</param>
        /// <returns>제공하면 true</returns>
        public bool HasFeature(NpcHubFeature feature) => hubFeatures.Contains(feature);

        /// <summary>허브에 퀘스트목록 버튼을 보일지(표시할 퀘스트가 있을 때만).</summary>
        public bool HubHasQuests { get; private set; }

        /// <summary>퀘스트 리스트에 뿌릴 항목(제목·종류·상태).</summary>
        public IReadOnlyList<NpcQuestEntry> QuestEntries => questEntries;

        /// <summary>화면이 바뀔 때 발행. 뷰는 자기 화면이면 켜고 아니면 끈다(데이터는 프로퍼티에서 읽는다).</summary>
        public event Action<NpcScreen> ScreenChanged;

        private void Awake()
        {
            if (proximity == null)
                proximity = GetComponentInChildren<NpcOutlineTrigger>(true);
        }

        private void OnEnable()
        {
            if (proximity != null)
                proximity.PlayerNearChanged += OnPlayerNearChanged;
        }

        private void OnDisable()
        {
            if (proximity != null)
                proximity.PlayerNearChanged -= OnPlayerNearChanged;
            UnsubscribeInput();
        }

        /// <summary>근접 진입/이탈. NpcOutlineTrigger에 자동 구독되거나 인스펙터로 연결.</summary>
        /// <param name="near">범위 안이면 true</param>
        public void OnPlayerNearChanged(bool near)
        {
            playerNear = near;
            if (near) SubscribeInput();
            else UnsubscribeInput();
        }

        private void SubscribeInput()
        {
            if (input == null) input = FindAnyObjectByType<PlayerInputHandler>();
            if (input == null) return;

            input.Interacted -= HandleInteract;   // 중복 구독 방지
            input.Interacted += HandleInteract;
        }

        private void UnsubscribeInput()
        {
            if (input != null) input.Interacted -= HandleInteract;
        }

        // F: 근처에서 상호작용 중이 아닐 때만 시작한다.
        private void HandleInteract()
        {
            if (!playerNear || interacting) return;
            BeginInteraction();
        }

        private void BeginInteraction()
        {
            interacting = true;
            SetActive(this);          // 뷰가 이 컨트롤러에 붙도록 먼저 알린다
            FreezeForInteraction();
            OpenHub();                // 인사말은 허브 본문에 뜬다(별도 대화창 아님)
        }

        // ---- 뷰가 호출하는 공개 메서드 ----

        /// <summary>허브: 고유기능 선택. 실제 동작은 <see cref="HubFeatureSelected"/> 구독자가 맡는다(지금은 로그 스텁).</summary>
        /// <param name="feature">선택한 기능</param>
        public void SelectFeature(NpcHubFeature feature)
        {
            if (feature == NpcHubFeature.None) return;   // 기능 없음 — 눌러도 아무 것도 하지 않는다

            DevLog.Log($"[NPC] {npcName} 허브기능 선택: {feature}");
            HubFeatureSelected?.Invoke(this, feature);
        }

        /// <summary>허브: 퀘스트 목록 열기.</summary>
        public void OpenQuestList()
        {
            RefreshQuestEntries();
            SetScreen(NpcScreen.QuestList);
        }

        // 수락 후 되돌아갈 곳. 목록을 갱신해 남은(받거나 반납할) 퀘스트가 있으면 목록으로,
        // 하나도 없으면 상호작용을 닫는다(빈 목록을 띄우지 않는다).
        private void OpenQuestListOrClose()
        {
            RefreshQuestEntries();
            if (questEntries.Count > 0)
                SetScreen(NpcScreen.QuestList);
            else
                CloseInteraction();
        }

        /// <summary>허브/리스트/보상: 상호작용 종료(닫기).</summary>
        public void CloseInteraction()
        {
            ClearReward();
            SetScreen(NpcScreen.None);   // 뷰 숨김
            interacting = false;
            UnfreezeAfterInteraction();
            SetActive(null);             // 뷰 구독 해제
        }

        /// <summary>리스트/보상: 처음으로 — 인사말(허브)로 되돌아간다(인사말 줄 새로 뽑힘).</summary>
        public void BackToGreeting() => OpenHub();

        /// <summary>
        /// 리스트: 퀘스트 선택. 상태에 따라 갈린다 —
        /// 안받음→도입 대화(스킵on)→수락→목록 갱신 / 완료가능→보상 화면 / 진행중→무시(정보만).
        /// </summary>
        /// <param name="questId">선택한 퀘스트 ID</param>
        public void SelectQuest(int questId)
        {
            QuestManager qm = QuestManager.Instance;
            if (qm == null) return;

            QuestData active = qm.FindActive(questId);
            if (active != null)
            {
                if (active.IsReadyToTurnIn)
                {
                    // 완료 가능 → 반납 대화. 마지막 대사에 보상 미리보기가 함께 뜨고, 확인 시 반납·지급.
                    rewardTurnInTarget = active;
                    rewardAcceptId = 0;
                    PlayDialogue(active.Definition.TurnInDialogueId, active.Title, active.Definition.Rewards, ConfirmReward);
                }
                return;                            // 진행 중이면 아무 것도 안 함
            }

            if (!qm.CanAccept(questId)) return;

            // 안 받음 → 도입 대화. 마지막 대사에 보상 미리보기가 함께 뜨고, 확인 시 수락.
            //  유저가 받을 보상을 먼저 보고 수락 여부를 정하게 한다(특히 반복 퀘 가성비 판단).
            QuestTable definition = JsonManager.Instance != null ? JsonManager.Instance.Get<QuestTable>(questId) : null;
            int introId = definition != null ? definition.IntroDialogueId : 0;
            string title = definition != null ? definition.Title : string.Empty;

            rewardTurnInTarget = null;
            rewardAcceptId = questId;
            PlayDialogue(introId, title, definition != null ? definition.Rewards : null, ConfirmReward);
        }

        // 대화의 마지막 대사에서 확인을 눌렀을 때. 반납 대상이 있으면 반납·지급, 아니면 수락한다.
        // 반납 지급은 QuestEvents.OnQuestCompleted 구독자가 처리한다.
        private void ConfirmReward()
        {
            QuestManager qm = QuestManager.Instance;
            if (qm != null)
            {
                if (rewardTurnInTarget != null)
                    qm.TurnInQuest(rewardTurnInTarget);
                else if (rewardAcceptId > 0)
                    qm.TryAcceptQuest(rewardAcceptId, out _);
            }

            ClearReward();
            OpenQuestListOrClose();   // 남은 퀘스트 있으면 목록, 없으면 상호작용 종료
        }

        // 보상 대상 상태를 비운다(확인·취소·상호작용 종료 시).
        private void ClearReward()
        {
            rewardTurnInTarget = null;
            rewardAcceptId = 0;
        }

        /// <summary>
        /// 이 NPC의 머리 위 마커 상태를 계산한다(<see cref="QuestMarker"/>가 호출). 상호작용 상태와 무관하게
        /// 언제든 조회 가능하다. 반납 가능(완료)을 우선한다 — 진행하던 퀘스트 마무리가 새 수락보다 앞선다.
        /// </summary>
        /// <returns>표시할 마커 상태(없음/받을 퀘스트/반납 가능)</returns>
        public QuestMarkerState EvaluateMarkerState()
        {
            QuestManager qm = QuestManager.Instance;
            if (qm == null) return QuestMarkerState.None;

            if (qm.GetCompletableQuestsForNpc(npcName).Count > 0) return QuestMarkerState.Completable;
            if (qm.GetAcceptableQuestIdsForNpc(npcName).Count > 0) return QuestMarkerState.Available;
            return QuestMarkerState.None;
        }

        // ---- 내부 ----

        private void OpenHub()
        {
            GreetingText = PickGreetingLine();
            RefreshQuestEntries();
            HubHasQuests = questEntries.Count > 0;
            SetScreen(NpcScreen.Hub);
        }

        // 리스트에는 액션 가능한 것만 — 수락 가능(!)·완료 가능(?)만 담고 진행중은 뺀다(진행 추적은 HUD 몫).
        private void RefreshQuestEntries()
        {
            questEntries.Clear();
            if (QuestManager.Instance == null) return;

            foreach (NpcQuestEntry entry in QuestManager.Instance.GetNpcQuestEntries(npcName))
            {
                if (entry.Status == NpcQuestStatus.InProgress) continue;
                questEntries.Add(entry);
            }
        }

        // 인사말 줄을 하나 뽑는다. 인사말 행의 여러 줄 중 하나(직전과 다르게), 없으면 빈 문자열.
        private string PickGreetingLine()
        {
            DialogueTable row = ResolveGreetingRow();
            if (row == null || row.Lines == null || row.Lines.Length == 0) return string.Empty;
            if (row.Lines.Length == 1) return row.Lines[0].Text;

            int i;
            do { i = UnityEngine.Random.Range(0, row.Lines.Length); } while (i == lastGreetingIndex);
            lastGreetingIndex = i;
            return row.Lines[i].Text;
        }

        // 인사말 행을 정한다. greetingDialogueId를 지정했으면 그 행, 비웠으면(0) npcName과
        // DialogueTable.Npc가 일치하는 행을 자동으로 찾는다(옛 QuestGiver 잡담 자동조회와 같은 방식).
        private DialogueTable ResolveGreetingRow()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null) return null;

            if (greetingDialogueId > 0)
                return json.Get<DialogueTable>(greetingDialogueId);

            if (string.IsNullOrEmpty(npcName)) return null;
            foreach (DialogueTable dialogue in json.DialogueDict.Values)
            {
                if (npcName.Equals(dialogue.Npc))
                    return dialogue;
            }
            return null;
        }

        private void SetScreen(NpcScreen screen)
        {
            CurrentScreen = screen;
            ScreenChanged?.Invoke(screen);
        }

        private static void SetActive(NpcInteractionController controller)
        {
            Active = controller;
            ActiveChanged?.Invoke(controller);
        }

        // 수락/반납 대사 재생. 마지막 대사 줄에 보상 미리보기(rewards)가 대화창 안에 함께 뜨고, 확인 시 onDone.
        // 대화 동안 허브/리스트는 숨긴다(None). 컨트롤러가 얼리므로 DialogueManager 자체 얼리기는 끈다.
        // 대사가 없으면(ID 0) 미리보기 없이 바로 onDone. 대화 중 Esc(취소)=상호작용 종료, Z(처음으로)=허브 복귀.
        private void PlayDialogue(int dialogueId, string questName, IReadOnlyList<QuestRewardData> rewards, Action onDone)
        {
            SetScreen(NpcScreen.None);

            DialogueManager dm = DialogueManager.Instance;
            if (dm != null && dialogueId > 0)
            {
                dm.ManageFreeze = false;
                dm.Play(npcName, npcPortrait, dialogueId, questName, true, onDone, CloseInteraction, BackToGreeting, rewards);
            }
            else
            {
                onDone?.Invoke();
            }
        }

        // 상호작용 동안(대화·허브·리스트·보상 공통) 플레이어 입력·카메라를 끄고 커서를 보여준다.
        private void FreezeForInteraction()
        {
            if (input == null) input = FindAnyObjectByType<PlayerInputHandler>();
            if (input != null) input.enabled = false;

            if (cameraPivot == null) cameraPivot = FindAnyObjectByType<CameraPivotController>();
            if (cameraPivot != null) cameraPivot.enabled = false;

            savedLockState = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void UnfreezeAfterInteraction()
        {
            if (cameraPivot != null) cameraPivot.enabled = true;
            Cursor.lockState = savedLockState;
            Cursor.visible = savedCursorVisible;

            DialogueManager dm = DialogueManager.Instance;
            if (dm != null) dm.ManageFreeze = true;   // 대화 자체 얼리기 원복

            // 카메라·커서는 즉시 복구하되, 이동/공격 입력은 상호작용을 닫은 공유 키(Space/Esc)가
            // 떼어질 때까지 기다렸다 켠다. 마지막 수락/확인의 Space가 그대로 점프로 새는 것을 막는다.
            // (대화가 ManageFreeze=false로 돌아 DialogueManager의 같은 처리를 안 타므로 여기서 한다.)
            if (input != null)
            {
                StopAllCoroutines();
                StartCoroutine(ReenableInputWhenReleased());
            }
        }

        private IEnumerator ReenableInputWhenReleased()
        {
            Keyboard kb = Keyboard.current;
            while (kb != null && (kb.spaceKey.isPressed || kb.escapeKey.isPressed))
                yield return null;

            // 그 사이 새 상호작용이 시작됐으면 건드리지 않는다(그 상호작용이 얼리기를 소유).
            if (!interacting && input != null)
                input.enabled = true;
        }
    }
}
