using UnityEngine;
using UnityEngine.Events;
using ProjectS.Debugging;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 달리기 과제. 비석에 말을 걸면 대화가 나오고, 대화가 끝나면 제한시간 안에
    /// 도착 지점까지 달려야 한다. 성공하면 <see cref="onSuccess"/>가 발행되고, 실패하면
    /// 처음 상태로 돌아가 비석에서 다시 시도할 수 있다.
    ///
    /// <b>문(door)을 직접 참조하지 않는다.</b> 성공 시 <see cref="onSuccess"/> UnityEvent만 쏘고,
    /// 무엇이 열릴지는 인스펙터 연결이 정한다. 지금 붙어 있는 임시 문이 나중에 다른 스크립트로
    /// 교체돼도 이 클래스는 고칠 필요가 없다(public 메서드 하나만 있으면 연결이 끝난다).
    /// 달리기 안내 UI도 같은 이유로 <see cref="onChallengeStarted"/>에 연결할 자리만 비워 뒀다.
    ///
    /// 붙이는 곳: 빈 관리자 오브젝트 하나. 비석·도착 지점과는 인스펙터 참조로만 이어진다.
    /// </summary>
    public class TutorialRunTrial : MonoBehaviour
    {
        /// <summary>과제 진행 상태.</summary>
        private enum TrialState
        {
            Idle,        // 대기 — 비석에서 F를 받을 수 있는 유일한 상태
            Briefing,    // 대화 재생 중
            Running,     // 제한시간 카운트다운 중
            Completed    // 성공으로 종료(다시 시작하지 않는다)
        }

        [Header("연결")]
        [Tooltip("비석 근처 감지기. 여기 플레이어가 있을 때만 F가 먹는다.")]
        [SerializeField] private PlayerZoneTrigger startZone;

        [Tooltip("도착 지점 감지기. 보통 도착 오브젝트(초록 원)의 자식에 둔다.")]
        [SerializeField] private PlayerZoneTrigger goalZone;

        [Tooltip("도착 지점 연출 오브젝트(초록 원). 대기 중에는 꺼두고 도전 중에만 켠다.")]
        [SerializeField] private GameObject goalObject;

        [Tooltip("제한시간 표시. 없어도 동작한다(타이머만 안 보인다).")]
        [SerializeField] private TutorialTimerView timerView;

        [Header("대화")]
        [Tooltip("대화창에 표시할 화자 이름. 비석이면 비워두거나 \"비석\" 같은 이름을 넣는다.")]
        [SerializeField] private string speakerName = string.Empty;

        [SerializeField] private Sprite speakerPortrait;

        [Tooltip("DialogueTable의 대화 ID. 0이면 대화 없이 곧바로 타이머가 시작된다.")]
        [SerializeField] private int dialogueId;

        [Header("규칙")]
        [SerializeField, Min(0.1f)] private float timeLimit = 7f;

        [Header("이벤트")]
        [Tooltip("도전 시작(대화가 끝난 시점). 달리기 안내 UI를 나중에 여기 연결한다.")]
        [SerializeField] private UnityEvent onChallengeStarted = new UnityEvent();

        [Tooltip("성공. 문의 잠금 해제 메서드를 여기 연결한다.")]
        [SerializeField] private UnityEvent onSuccess = new UnityEvent();

        [Tooltip("실패. 소리나 연출을 붙일 자리(붙이지 않아도 도전은 처음 상태로 돌아간다).")]
        [SerializeField] private UnityEvent onFailed = new UnityEvent();

        private TrialState state = TrialState.Idle;
        private float remaining;
        private PlayerInputHandler input;

        /// <summary>이미 성공해서 끝난 과제인지. 성공 후 재도전은 지금 지원하지 않는다.</summary>
        public bool IsCompleted => state == TrialState.Completed;

        private void Awake()
        {
            // 씬에 켜둔 채로 저장돼 있어도 시작은 항상 꺼진 상태다.
            // 도전 전에 도착 지점이 보이면 순서를 오해하게 된다.
            if (goalObject != null) goalObject.SetActive(false);
            if (timerView != null) timerView.Hide();
        }

        private void OnEnable()
        {
            if (startZone != null) startZone.PlayerInsideChanged += OnStartZoneChanged;
            if (goalZone != null) goalZone.PlayerInsideChanged += OnGoalZoneChanged;
        }

        private void OnDisable()
        {
            if (startZone != null) startZone.PlayerInsideChanged -= OnStartZoneChanged;
            if (goalZone != null) goalZone.PlayerInsideChanged -= OnGoalZoneChanged;
            UnsubscribeInput();
        }

        private void Update()
        {
            if (state != TrialState.Running) return;

            remaining -= Time.deltaTime;

            if (remaining <= 0f)
            {
                Fail();
                return;
            }

            if (timerView != null) timerView.SetRemaining(remaining, timeLimit);
        }

        // 비석 근처에 있을 때만 F를 듣는다. 멀리서 누른 F가 과제를 시작하면 안 되고,
        // 항상 구독해두면 다른 상호작용(NPC 등)과 같은 키를 두고 부딪힌다.
        private void OnStartZoneChanged(bool inside)
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

        // 대기 상태에서만 시작한다. 대화 중·도전 중·성공 후의 F는 무시된다.
        private void HandleInteract()
        {
            if (state != TrialState.Idle) return;

            BeginBriefing();
        }

        private void BeginBriefing()
        {
            state = TrialState.Briefing;

            DialogueManager dialogue = DialogueManager.Instance;

            // 대화가 없거나(ID 0) DialogueManager가 씬에 없으면 대화를 건너뛰고 바로 시작한다.
            // 대화창이 준비되기 전에도 달리기 자체는 테스트할 수 있어야 하기 때문이다.
            // 다만 '대화를 넣었는데 안 뜨는' 경우는 설정 실수이므로 왜 건너뛰었는지 남긴다
            // (조용히 넘어가면 씬에 매니저가 없는 건지 ID가 비어 있는 건지 구분할 수가 없다).
            if (dialogueId <= 0)
            {
                DevLog.Log($"[TutorialRunTrial] {name}: Dialogue Id가 0이라 대화를 건너뜁니다.");
                BeginRun();
                return;
            }

            if (dialogue == null)
            {
                DevLog.Warning($"[TutorialRunTrial] {name}: 씬에 DialogueManager가 없어 대화(ID {dialogueId})를 " +
                               "건너뜁니다. VillageGather(민준) 씬에서 DialogueManager를 가져오세요.");
                BeginRun();
                return;
            }

            // onCancel(대화 중 Esc)이면 시작하지 않고 대기로 되돌린다 — 비석에서 다시 걸 수 있다.
            dialogue.Play(speakerName, speakerPortrait, dialogueId, null, true, BeginRun, CancelBriefing);
        }

        private void CancelBriefing()
        {
            if (state != TrialState.Briefing) return;

            state = TrialState.Idle;
        }

        private void BeginRun()
        {
            state = TrialState.Running;
            remaining = timeLimit;

            if (goalObject != null) goalObject.SetActive(true);

            if (timerView != null)
            {
                timerView.Show();
                timerView.SetRemaining(remaining, timeLimit);
            }

            onChallengeStarted?.Invoke();
        }

        // 도착 판정은 '도전 중'일 때만 유효하다. 도전 전에 그 자리를 지나가도 성공이 되면 안 된다.
        private void OnGoalZoneChanged(bool inside)
        {
            if (!inside || state != TrialState.Running) return;

            Succeed();
        }

        private void Succeed()
        {
            state = TrialState.Completed;
            StopChallenge();
            onSuccess?.Invoke();
        }

        private void Fail()
        {
            state = TrialState.Idle;   // 비석에서 다시 시도할 수 있다
            StopChallenge();
            onFailed?.Invoke();
        }

        // 성공·실패 공통 정리. 도착 오브젝트와 타이머를 화면에서 치운다.
        private void StopChallenge()
        {
            if (goalObject != null) goalObject.SetActive(false);
            if (timerView != null) timerView.Hide();
        }
    }
}
