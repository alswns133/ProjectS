using UnityEngine;
using UnityEngine.Events;
using ProjectS.Data;
using ProjectS.Debugging;
using ProjectS.Events;
using ProjectS.NPCs;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 퀘스트 NPC의 "말을 건 순간"과 "퀘스트를 수락한 순간"을 인스펙터 이벤트로 넘겨주는 중계기.
    /// 켜고 끌 오브젝트는 인스펙터 연결이 정한다.
    ///
    /// <b>대화를 직접 띄우지 않는다.</b> 퀘스트 NPC는 <see cref="NpcInteractionController"/>가 이미 F·허브·
    /// 퀘스트 대화를 처리한다. 여기서 F를 또 들으면(<see cref="TutorialTalk"/>) 같은 키로 허브와 대화창이
    /// 동시에 열리고 입력 잠금도 이중으로 걸린다. 그래서 NPC 흐름은 건드리지 않고 결과 신호만 구독한다.
    ///
    /// 붙이는 곳: 켜져 있는 관리자 오브젝트. 꺼진 오브젝트에 붙이면 신호를 받지 못한다.
    /// </summary>
    public class TutorialNpcEvents : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("말을 걸 NPC(NpcInteractionController가 붙은 오브젝트).")]
        [SerializeField] private NpcInteractionController npc;

        [Tooltip("수락을 감지할 퀘스트 ID(QuestTable). 0이면 수락 이벤트는 발행되지 않는다.")]
        [SerializeField] private int questId;

        [Tooltip("켜면 말을 건 이벤트를 처음 한 번만 발행한다. 끄면 말을 걸 때마다 발행한다.")]
        [SerializeField] private bool talkStartedOnce = true;

        [Header("이벤트")]
        [Tooltip("NPC에게 말을 건 순간(허브 창이 열릴 때).")]
        [SerializeField] private UnityEvent onTalkStarted = new UnityEvent();

        [Tooltip("지정한 퀘스트를 수락한 순간.")]
        [SerializeField] private UnityEvent onQuestAccepted = new UnityEvent();

        private bool hasTalked;

        private void Awake()
        {
            // 비어 있으면 아무 일도 일어나지 않는데 화면상으로는 "트리거가 안 먹는다"로 보인다. 여기서 바로 알린다.
            if (npc == null)
                DevLog.Warning($"[TutorialNpcEvents] {name}: npc가 비어 있어 말을 건 이벤트가 발행되지 않습니다.");
            if (questId <= 0)
                DevLog.Warning($"[TutorialNpcEvents] {name}: questId가 0이라 퀘스트 수락 이벤트가 발행되지 않습니다.");
        }

        private void OnEnable()
        {
            NpcInteractionController.ActiveChanged += OnNpcActiveChanged;
            QuestEvents.OnQuestAccepted += OnQuestAccepted;
        }

        private void OnDisable()
        {
            NpcInteractionController.ActiveChanged -= OnNpcActiveChanged;
            QuestEvents.OnQuestAccepted -= OnQuestAccepted;
        }

        // 상호작용 시작 시 그 NPC, 종료 시 null이 온다. 다른 NPC와의 상호작용은 무시한다.
        private void OnNpcActiveChanged(NpcInteractionController active)
        {
            if (npc == null || active != npc) return;
            if (talkStartedOnce && hasTalked) return;

            hasTalked = true;
            onTalkStarted?.Invoke();
        }

        private void OnQuestAccepted(QuestData quest)
        {
            if (questId <= 0 || quest == null || quest.QuestId != questId) return;

            onQuestAccepted?.Invoke();
        }
    }
}
