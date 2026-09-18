using UnityEngine;
using UnityEngine.Events;
using ProjectS.Debugging;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 대화 전용 트리거. 영역 안에서 F를 누르면 대화창을 띄우고, 대화 시작·완료·취소 시점에
    /// UnityEvent를 발행한다. 켜고 끌 오브젝트는 인스펙터 연결이 정한다.
    ///
    /// <see cref="TutorialRunTrial"/>에서 "말 걸기 → 대화" 부분만 떼어 냈다. 대화만 필요한 NPC에
    /// 달리기 과제를 붙이면 타이머·목적지·성공/실패를 억지로 비활성화해야 해서 설정이 꼬이기 쉽다.
    ///
    /// 붙이는 곳: 빈 관리자 오브젝트 하나 또는 NPC. NPC 주변 감지 영역과는 인스펙터 참조로만 이어진다.
    /// </summary>
    public class TutorialTalk : MonoBehaviour
    {
        /// <summary>대화 진행 상태.</summary>
        private enum TalkState
        {
            Idle,      // 대기 — F를 받을 수 있는 유일한 상태
            Talking,   // 대화 재생 중
            Done       // playOnce로 끝남(다시 시작하지 않는다)
        }

        [Header("연결")]
        [Tooltip("말을 걸 수 있는 영역. 여기 플레이어가 있을 때만 F가 먹는다.")]
        [SerializeField] private PlayerZoneTrigger talkZone;

        [Header("대화")]
        [Tooltip("대화창에 표시할 화자 이름.")]
        [SerializeField] private string speakerName = string.Empty;

        [SerializeField] private Sprite speakerPortrait;

        [Tooltip("DialogueTable의 대화 ID. 0이면 대화창 없이 시작·완료 이벤트만 연달아 발행된다.")]
        [SerializeField] private int dialogueId;

        [Tooltip("켜면 대화를 끝까지 마친 뒤 다시 말을 걸 수 없다. Esc로 취소한 경우는 다시 걸 수 있다.")]
        [SerializeField] private bool playOnce = true;

        [Header("이벤트")]
        [Tooltip("말을 건 순간(대화창이 뜨기 직전). 끌 오브젝트를 연결한다.")]
        [SerializeField] private UnityEvent onDialogueStarted = new UnityEvent();

        [Tooltip("대화를 끝까지(또는 스킵으로) 마쳤을 때. 켤 오브젝트를 연결한다.")]
        [SerializeField] private UnityEvent onDialogueFinished = new UnityEvent();

        [Tooltip("대화 중 Esc로 취소했을 때. 시작 시 꺼 둔 오브젝트를 되돌릴 때 쓴다(완료 이벤트는 호출되지 않는다).")]
        [SerializeField] private UnityEvent onDialogueCanceled = new UnityEvent();

        private TalkState state = TalkState.Idle;
        private PlayerInputHandler input;

        /// <summary>playOnce 대화를 끝까지 마쳤는지.</summary>
        public bool IsDone => state == TalkState.Done;

        private void OnEnable()
        {
            if (talkZone != null) talkZone.PlayerInsideChanged += OnTalkZoneChanged;
        }

        private void OnDisable()
        {
            if (talkZone != null) talkZone.PlayerInsideChanged -= OnTalkZoneChanged;
            UnsubscribeInput();
        }

        // 영역 안에 있을 때만 F를 듣는다. 항상 구독해두면 다른 상호작용(비석 등)과 같은 키를 두고 부딪힌다.
        private void OnTalkZoneChanged(bool inside)
        {
            if (inside) SubscribeInput();
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

        private void HandleInteract()
        {
            if (state != TalkState.Idle) return;

            // 다른 대화가 떠 있는 중이면 무시한다. DialogueManager는 재생 중 Play 요청을 조용히 버려서,
            // 여기서 막지 않으면 시작 이벤트만 발행되고 완료 이벤트가 영영 오지 않는다.
            DialogueManager dialogue = DialogueManager.Instance;
            if (dialogue != null && dialogue.IsPlaying) return;

            BeginTalk(dialogue);
        }

        private void BeginTalk(DialogueManager dialogue)
        {
            state = TalkState.Talking;
            onDialogueStarted?.Invoke();

            // 대화가 없거나 매니저가 없으면 곧바로 완료로 본다. 대화창 없이도 켜고 끄는 흐름은 테스트할 수 있어야 한다.
            // 다만 설정 실수일 수 있으므로 왜 건너뛰었는지 남긴다.
            if (dialogueId <= 0)
            {
                DevLog.Log($"[TutorialTalk] {name}: Dialogue Id가 0이라 대화를 건너뜁니다.");
                FinishTalk();
                return;
            }

            if (dialogue == null)
            {
                DevLog.Warning($"[TutorialTalk] {name}: 씬에 DialogueManager가 없어 대화(ID {dialogueId})를 건너뜁니다.");
                FinishTalk();
                return;
            }

            dialogue.Play(speakerName, speakerPortrait, dialogueId, null, true, FinishTalk, CancelTalk);
        }

        private void FinishTalk()
        {
            if (state != TalkState.Talking) return;

            state = playOnce ? TalkState.Done : TalkState.Idle;
            onDialogueFinished?.Invoke();
        }

        private void CancelTalk()
        {
            if (state != TalkState.Talking) return;

            state = TalkState.Idle;   // 끝까지 듣지 않았으므로 다시 말을 걸 수 있다
            onDialogueCanceled?.Invoke();
        }
    }
}
