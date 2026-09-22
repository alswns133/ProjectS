using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.NPCs;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 퀘스트 진척에 따라 열리는 문. 닫힌 문과 열린 문을 <b>두 벌 두고 갈아 끼우는</b> 방식이라
    /// 애니메이션이 없어도 되고, 씬을 다시 들어왔을 때의 복원도 켜고 끄기로 끝난다.
    ///
    /// 열리는 순간 그 자리에서 대사를 띄우고 퀘스트를 반납한 뒤 다음 퀘스트까지 이어 줄 수 있다.
    /// NPC에게 되돌아가는 걸음을 없애기 위한 것으로, 튜토리얼 구간의 첫 퀘스트를 염두에 둔 기능이다.
    ///
    /// <b>열림 여부는 저장하지 않는다.</b> 퀘스트 완료 기록과 진행 중 목표의 진행도가 이미 세이브에 들어 있어
    /// (<c>completedQuestIds</c>, <c>QuestSave.objectiveCounts</c>) 씬에 들어올 때마다 그걸 보고 다시 계산한다.
    /// 별도 플래그를 두면 그 플래그와 실제 퀘스트 상태가 어긋날 수 있어 일부러 두지 않았다.
    ///
    /// 도착 감지는 <see cref="QuestReachTrigger"/>가 맡는다. 그 트리거의 onReached에 <see cref="OpenNow"/>를 연결한다.
    ///
    /// <b>연출(Timeline)을 물리면 문 교체를 연출이 끝난 뒤로 미룬다.</b> 타임라인이 "닫힌 문이 열리는 장면"을
    /// 보여 주는데 시작하자마자 닫힌 문이 사라지면 장면이 성립하지 않기 때문이다. 연출을 비우면 즉시 교체한다.
    /// </summary>
    public class QuestGateDoor : MonoBehaviour
    {
        [Header("문 오브젝트")]
        [Tooltip("닫혀 있을 때 켜 둘 것들. 길을 막는 콜라이더도 여기에 넣으면 열릴 때 같이 꺼진다.")]
        [SerializeField] private GameObject[] closedObjects;

        [Tooltip("열렸을 때 켤 것들. 닫힌 쪽과 Transform을 맞춰 두어야 전환 순간에 문이 튀지 않는다.")]
        [SerializeField] private GameObject[] openObjects;

        [Header("열림 판정")]
        [Tooltip("이 문과 묶인 퀘스트 ID. 이 퀘스트를 완료했거나, 진행 중이면서 아래 도착 목표를 이미 찍었으면 " +
                 "씬에 들어올 때부터 열린 상태로 시작한다.")]
        [SerializeField] private int questId;

        [Tooltip("그 퀘스트 안에서 이 문에 해당하는 도착 지점 ID(QuestReachTrigger의 값과 같게).")]
        [SerializeField] private int pointId;

        [Header("도착했을 때")]
        [Tooltip("문이 열리는 연출 Timeline. 지정하면 이걸 먼저 재생하고, 끝나는 순간에 문을 교체한다. " +
                 "비우면 도착 즉시 교체한다(연출 없이 팍 바뀜).")]
        [SerializeField] private PlayableDirector cutscene;

        [Tooltip("문이 바뀌는 순간 호출(연출이 있으면 연출이 끝난 뒤). 파티클·사운드·카메라 흔들림을 연결한다. " +
                 "갈아 끼우기만 하면 한 프레임에 '팍' 바뀌므로 연출이 없을 때는 여기에 한 겹 덮는 것이 좋다.")]
        [SerializeField] private UnityEvent onOpened;

        [Tooltip("문이 열린 뒤 그 자리에서 띄울 대화 ID(DialogueTable). 0이면 대화 없이 바로 반납한다.")]
        [SerializeField] private int dialogueId;

        [Tooltip("대화창에 표시할 화자 이름. 보통 이 퀘스트를 준 NPC(예: 칼슨).")]
        [SerializeField] private string speakerName = string.Empty;

        [Tooltip("화자 초상화. 없으면 이름과 대사만 표시된다.")]
        [SerializeField] private Sprite speakerPortrait;

        [Tooltip("문이 열린 뒤 대화가 뜨기까지 기다릴 시간(초). 연출(Timeline)을 쓰면 그 종료 시점부터 잰다. " +
                 "0이면 문이 열리자마자 대화창이 뜬다.")]
        [SerializeField, Min(0f)] private float dialogueDelay = 0.5f;

        [Header("대화가 끝나면")]
        [Tooltip("켜면 이 자리에서 퀘스트를 반납한다(보상 지급). 끄면 NPC에게 돌아가 반납해야 한다.")]
        [SerializeField] private bool turnInOnArrive = true;

        [Tooltip("반납 뒤 퀘스트 목록을 띄울 NPC. 채우면 NPC에게 말 걸었을 때와 같은 선택 창이 떠서 " +
                 "플레이어가 다음 퀘스트를 직접 고른다. 비우면 아래 nextQuestId로 자동 수락한다.")]
        [SerializeField] private NpcInteractionController questGiver;

        [Tooltip("반납 뒤 이어서 자동 수락할 퀘스트 ID. 0이면 수락하지 않는다. " +
                 "위 questGiver를 채웠으면 목록이 우선이라 이 값은 쓰이지 않는다. " +
                 "이 퀘스트의 선행이 위 questId라면 반드시 반납 뒤에 수락해야 하므로 순서는 코드가 고정한다.")]
        [SerializeField] private int nextQuestId;

        // 이번 씬에서 이미 열렸는지. OpenNow 중복 호출(트리거 재진입 등)을 막는다.
        private bool opened;

        // 도착 연출이 도는 중인지. 연출이 있으면 문 교체가 끝에 오므로 opened만으로는 중복 재생을 못 막는다.
        private bool sequenceRunning;

        // 타임라인 종료(stopped)를 놓쳤을 때 문이 영영 안 열리는 것을 막는 여유 시간.
        // Wrap Mode가 Hold/Loop면 stopped가 아예 오지 않는다.
        private const float CutsceneGrace = 1f;

        // 반납 대화가 닫히기를 기다리는 상한. 닫힘을 놓쳐도 목록을 영영 못 여는 일이 없게 한다.
        private const float DialogueCloseGrace = 2f;

        /// <summary>지금 열린 상태인지.</summary>
        public bool IsOpen => opened;

        // 세이브 복원(QuestManager)은 JsonManager 로딩을 기다린 뒤 끝나므로 이 문의 Start보다 늦을 수 있다.
        // 복원이 끝난 시점에 다시 판정해야, 이미 열었던 문이 닫힌 채로 남지 않는다
        // (QuestTrackerHud가 같은 이유로 이 이벤트를 구독한다).
        private void OnEnable()
        {
            QuestEvents.OnQuestsRestored += RefreshFromQuestState;
        }

        private void OnDisable()
        {
            QuestEvents.OnQuestsRestored -= RefreshFromQuestState;
        }

        // 씬에 들어온 시점의 퀘스트 상태로 문 모양을 맞춘다. 이미 열린 문이면 연출도 대화도 하지 않는다 —
        // 마을에 들를 때마다 같은 컷신이 나오면 금방 피곤해진다.
        private void Start()
        {
            RefreshFromQuestState();
        }

        // 퀘스트 상태만 보고 문 모양을 맞춘다(연출·대화 없음). 이미 연 문은 다시 닫지 않는다.
        private void RefreshFromQuestState()
        {
            if (opened) return;

            ApplyOpenState(IsAlreadyOpen());
        }

        /// <summary>
        /// 문을 지금 연다(도착 트리거가 호출). 문을 갈아 끼우고 연출을 발행한 뒤,
        /// 설정에 따라 대사 → 반납 → 다음 퀘스트 수락까지 이어 간다.
        /// 이미 열린 뒤에 다시 불려도 아무 일도 하지 않는다.
        /// </summary>
        public void OpenNow()
        {
            if (opened || sequenceRunning) return;

            // 퀘스트와 묶인 문이면, 그 퀘스트가 진행 중이어서 도착이 실제로 집계됐을 때만 연다.
            // (트리거는 퀘스트가 없어도 onReached를 쏜다 — 연출 전용으로도 쓰이기 때문이다.)
            // 수락 전에 지나가면 문은 그대로 닫혀 있고, 퀘스트를 받아 다시 오면 열린다.
            if (questId > 0 && !IsAlreadyOpen()) return;

            sequenceRunning = true;
            StartCoroutine(RunArrivalSequence());
        }

        // 연출 → 문 교체 → (지연) → 대화 → 반납 → 다음 퀘스트.
        private IEnumerator RunArrivalSequence()
        {
            yield return PlayCutscene();

            // 문 교체는 연출이 끝난 뒤다. 연출이 없으면 여기까지 한 프레임도 쓰지 않는다.
            ApplyOpenState(true);
            onOpened?.Invoke();
            sequenceRunning = false;

            // 대화도 반납도 이어질 퀘스트도 없으면 문만 열고 끝낸다(연출용 문).
            if (dialogueId <= 0 && !turnInOnArrive && nextQuestId <= 0 && questGiver == null) yield break;

            if (dialogueId > 0 && dialogueDelay > 0f)
                yield return new WaitForSeconds(dialogueDelay);

            DialogueManager dialogue = DialogueManager.Instance;
            QuestTable definition = GetDefinition(questId);

            if (dialogueId > 0 && dialogue != null)
            {
                // 마지막 줄에 보상 목록을 함께 띄운다(NPC에게 반납할 때와 같은 표시).
                // 취소(Esc)하면 반납하지 않고 남겨 둔다 — 퀘스트는 '반납 가능' 상태 그대로라
                // NPC에게 걸어가 반납할 수 있어 막히지 않는다.
                bool finished = false;
                bool cancelled = false;

                dialogue.Play(
                    speakerName,
                    speakerPortrait,
                    dialogueId,
                    definition != null ? definition.Title : string.Empty,
                    true,
                    () => finished = true,
                    () => cancelled = true,
                    null,
                    definition != null ? definition.Rewards : null);

                yield return new WaitUntil(() => finished || cancelled);
                if (cancelled) yield break;
            }

            FinishArrival();
            yield return OpenQuestGiverList();
        }

        // 반납 대화가 닫힌 뒤 퀘스트 목록을 띄운다. NPC에게 말 걸었을 때와 같은 선택 창이라
        // 플레이어가 다음 퀘스트를 직접 고른다.
        // 앞 대화가 닫히기 전에 열면 DialogueManager가 '이미 재생 중'으로 보고 조용히 무시하므로,
        // 반드시 닫힘을 기다린 뒤에 연다(이게 없으면 선택 창이 안 뜨는 것처럼 보인다).
        private IEnumerator OpenQuestGiverList()
        {
            if (questGiver == null) yield break;

            DialogueManager dialogue = DialogueManager.Instance;
            float waited = 0f;
            while (dialogue != null && dialogue.IsPlaying && waited < DialogueCloseGrace)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            yield return null;   // 닫기 처리가 한 프레임 뒤에 끝나므로 한 박자 더 둔다

            questGiver.OpenQuestListExternally();
        }

        // 연출 Timeline을 재생하고 끝날 때까지 기다린다. 연출이 없으면 즉시 돌아온다.
        // 종료는 director.stopped로 잡는다(BossIntroDirector와 같은 방식) — 타임라인 길이를 바꿔도
        // 초를 다시 맞출 필요가 없다. 다만 Wrap Mode가 Hold/Loop면 stopped가 오지 않으므로
        // 길이 + CutsceneGrace를 넘기면 스스로 빠져나온다(문이 영영 안 열리는 것보다 낫다).
        private IEnumerator PlayCutscene()
        {
            if (cutscene == null) yield break;

            bool finished = false;
            void OnStopped(PlayableDirector _) => finished = true;

            cutscene.stopped += OnStopped;
            cutscene.Play();

            float limit = (float)cutscene.duration + CutsceneGrace;
            float elapsed = 0f;
            while (!finished && elapsed < limit)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            cutscene.stopped -= OnStopped;
        }

        // 반납 → 다음 퀘스트 수락. 순서를 지켜야 한다 — 다음 퀘스트의 선행이 이 퀘스트면,
        // 반납으로 완료 등록이 끝난 뒤라야 수락 판정(CanAccept)이 통과한다.
        private void FinishArrival()
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null) return;

            if (turnInOnArrive)
            {
                QuestData active = manager.FindActive(questId);
                if (active != null) manager.TurnInQuest(active);
            }

            // 선택 창을 띄울 NPC가 있으면 수락은 그쪽에 맡긴다(플레이어가 직접 고른다).
            if (questGiver == null && nextQuestId > 0) manager.TryAcceptQuest(nextQuestId, out _);
        }

        // 열림 조건: 퀘스트를 이미 완료했거나, 아직 진행 중이지만 이 문의 도착 목표를 이미 찍었거나.
        // 후자를 함께 보는 이유는, 도착하고 반납하기 전에 씬을 옮겨도 문이 다시 닫히지 않게 하기 위해서다
        // (목표 진행도는 세이브에 남는다).
        private bool IsAlreadyOpen()
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null || questId <= 0) return false;

            if (manager.IsCompleted(questId)) return true;

            QuestData active = manager.FindActive(questId);
            if (active == null) return false;

            foreach (ObjectiveProgress objective in active.Objectives)
            {
                if (objective.Target != null && objective.Target.TargetId == pointId && objective.IsCompleted)
                    return true;
            }
            return false;
        }

        private void ApplyOpenState(bool open)
        {
            opened = open;

            SetActiveAll(closedObjects, !open);
            SetActiveAll(openObjects, open);
        }

        private static void SetActiveAll(GameObject[] targets, bool active)
        {
            if (targets == null) return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].SetActive(active);
            }
        }

        private static QuestTable GetDefinition(int id)
        {
            if (id <= 0 || JsonManager.Instance == null) return null;
            return JsonManager.Instance.Get<QuestTable>(id);
        }
    }
}
