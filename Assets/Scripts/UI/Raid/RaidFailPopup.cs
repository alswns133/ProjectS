using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Events;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 레이드 실패(파티 전멸) 팝업. <b>재시도 투표 / 마을 복귀</b> 중 하나를 고르게 하는 모달이다.
    /// 표시는 <see cref="RaidFailTrigger"/>가 <see cref="RaidFailEvents.OnFailed"/>를 받아 띄운다
    /// (팝업 자신은 비활성이라 이벤트를 못 받으므로 — <see cref="DeathPopupTrigger"/>와 같은 방침).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 규칙(2026-09-22 사용자 확정):
    /// <list type="number">
    /// <item><b>재시도는 전원 동의</b>. 한 명이라도 거절하거나 제한 시간이 지나면 무산되고 전원 마을 복귀.
    ///       집계는 서버(<c>RaidFailSession</c>)가 하고 이 화면은 현황만 보여 준다.</item>
    /// <item><b>"마을 복귀" = 재시도 거절</b>이다. 파티 전체가 함께 움직이므로 혼자만 빠지는 선택지가 아니다.</item>
    /// <item><b>싱글 레이드도 같은 화면</b>을 쓴다. 총원이 1이라 재시도를 누르면 곧바로 성립한다.</item>
    /// <item><b>ESC로 닫히지 않는다</b> — 닫히면 죽은 채 아무 선택도 못 하는 상태에 갇힌다(사망 팝업과 같은 이유).</item>
    /// </list>
    /// </para>
    /// <para>
    /// 표시용 필드는 전부 선택 사항이다. 비어 있으면 연출 없이 기능만 돈다 — 아트가 붙기 전에도
    /// 흐름을 검증할 수 있게 하기 위함이다(사망 팝업과 같은 방침).
    /// </para>
    /// </remarks>
    public class RaidFailPopup : BasePopup
    {
        [Header("버튼")]
        [Tooltip("재시도(동의). 누르면 투표를 보내고 다른 파티원을 기다린다.")]
        [SerializeField] private Button retryButton;

        [Tooltip("마을 복귀(= 재시도 거절). 누르면 파티 전체의 재시도가 무산된다.")]
        [SerializeField] private Button returnButton;

        [Header("표시")]
        [Tooltip("안내 문구. 선택 중 / 대기 중 / 재시도 / 무산에 따라 바뀐다.")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("동의 현황 텍스트. 싱글(총원 1)에서는 꺼 둔다.")]
        [SerializeField] private TMP_Text voteText;

        [Tooltip("남은 초. 투표 제한 시간과 마을 복귀 카운트다운에 공용으로 쓴다.")]
        [SerializeField] private TMP_Text countdownText;

        [Tooltip("선택 버튼 묶음. 선택이 끝나면 꺼서 '이제 기다리는 중'임을 분명히 한다.")]
        [SerializeField] private GameObject choiceGroup;

        [Header("연출")]
        [Tooltip("타이틀 글리치 연출. 비워두면 자식에서 자동으로 찾는다.")]
        [SerializeField] private GlitchTextFx titleFx;

        [Header("문구")]
        [SerializeField, TextArea] private string chooseMessage = "레이드에 실패했습니다.\n다시 도전할까요?";
        [SerializeField, TextArea] private string waitingMessage = "다른 파티원의 응답을 기다리는 중입니다.";
        [SerializeField, TextArea] private string retryingMessage = "레이드를 다시 시작합니다.";
        [Tooltip("동의 현황 표기. {0}=동의한 수, {1}=총원.")]
        [SerializeField] private string voteFormat = "{0} / {1} 동의";

        [Header("설정")]
        [Tooltip("재시도가 무산된 뒤 마을로 옮겨지기까지의 시간(초). 사유를 읽을 여유다.")]
        [SerializeField, Min(0f)] private float returnDelay = 3f;

        [Tooltip("재시도가 확정된 뒤 이 창을 닫기까지의 시간(초). 씬 로딩이 화면을 덮을 여유다.")]
        [SerializeField, Min(0f)] private float retryCloseDelay = 1.5f;

        // 카운트다운이 끝나면 무엇을 할지. 투표 대기 중에는 표시만 하고 판정은 서버가 한다.
        private enum Mode
        {
            None,
            Voting,     // 선택 중(버튼 표시) — 남은 시간은 투표 제한 시간
            Waiting,    // 내 투표를 보냈고 다른 파티원을 기다리는 중
            Retrying,   // 재시도 확정 — 잠시 뒤 창을 닫는다
            Returning   // 무산 — 카운트다운 뒤 마을로
        }

        private Mode mode;
        private float remain;

        private int total = 1;
        private float voteSeconds;

        /// <inheritdoc/>
        /// <remarks>닫히면 죽은 채 아무 선택도 못 하는 상태에 갇힌다(사망 팝업과 같은 이유).</remarks>
        public override bool CanCloseByBack => false;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만(BasePopup이 OnInit을 1회만 호출 → 중복 구독이 쌓이지 않는다).
            if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
            if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);

            if (titleFx == null) titleFx = GetComponentInChildren<GlitchTextFx>(true);
        }

        /// <summary>
        /// 띄우기 <b>전에</b> 투표 정보를 넘긴다. <see cref="ProjectS.Managers.UIManager.ShowPopup{T}"/>는
        /// 인자를 받을 수 없어, 트리거가 이 메서드로 먼저 채운다.
        /// </summary>
        /// <param name="voteTotal">투표 총원(싱글은 1).</param>
        /// <param name="seconds">투표 제한 시간(초). 0 이하면 제한 없음.</param>
        public void Prepare(int voteTotal, float seconds)
        {
            total = Mathf.Max(1, voteTotal);
            voteSeconds = seconds;
        }

        protected override void OnShow()
        {
            // 이벤트 구독은 열려 있는 동안만. 닫힌 뒤에도 남아 있으면 다음 판의 신호에 꺼진 창이 반응한다.
            RaidFailEvents.OnRetryVoteChanged += OnVoteChanged;
            RaidFailEvents.OnRetryStarting += OnRetryStarting;
            RaidFailEvents.OnRetryCancelled += OnRetryCancelled;

            if (titleFx != null) titleFx.Play();

            // 마우스로 버튼을 누르려면 커서를 풀어야 한다(플레이 중엔 잠겨 숨겨져 있음).
            SetCursorFree(true);

            mode = Mode.Voting;
            remain = voteSeconds;

            SetMessage(chooseMessage);
            SetChoiceVisible(true);
            ShowVote(0, total);

            // 싱글(총원 1)은 제한 시간이 없다 — 기다릴 상대가 없어 카운트다운이 의미가 없다.
            if (countdownText != null) countdownText.gameObject.SetActive(voteSeconds > 0f);
        }

        protected override void OnHide()
        {
            RaidFailEvents.OnRetryVoteChanged -= OnVoteChanged;
            RaidFailEvents.OnRetryStarting -= OnRetryStarting;
            RaidFailEvents.OnRetryCancelled -= OnRetryCancelled;

            mode = Mode.None;
            SetCursorFree(false);
        }

        private void Update()
        {
            if (mode == Mode.None) return;

            // 사망·실패 연출로 timeScale이 낮아질 수 있어 unscaled로 센다.
            remain -= Time.unscaledDeltaTime;

            if (countdownText != null && countdownText.gameObject.activeSelf)
                countdownText.text = Mathf.CeilToInt(Mathf.Max(0f, remain)).ToString();

            if (remain > 0f) return;

            // 투표 제한 시간이 다 된 경우의 판정은 서버가 한다(무산 신호가 곧 온다). 화면은 0에서 멈춘다.
            if (mode == Mode.Voting || mode == Mode.Waiting) return;

            Mode done = mode;
            mode = Mode.None;   // 한 프레임 늦어도 두 번 돌지 않게 먼저 내린다

            if (done == Mode.Retrying)
            {
                RequestClose();
                return;
            }

            RequestClose();
            RaidFailFlow.ReturnToVillage();
        }

        // 재시도 동의. 화면을 먼저 '대기'로 바꾼 뒤 투표를 보낸다 — 싱글은 이 호출이 곧바로
        // OnRetryStarting을 되돌려 주므로, 순서가 반대면 대기 화면이 재시도 화면을 덮어쓴다.
        private void OnRetryClicked()
        {
            if (mode != Mode.Voting) return;

            mode = Mode.Waiting;
            SetChoiceVisible(false);
            SetMessage(waitingMessage);

            RaidFailFlow.VoteRetry(true);
        }

        // 마을 복귀 = 재시도 거절. 결과(무산)는 서버가 전원에게 돌려주므로 여기서 바로 나가지 않는다.
        private void OnReturnClicked()
        {
            if (mode != Mode.Voting) return;

            mode = Mode.Waiting;
            SetChoiceVisible(false);
            SetMessage(waitingMessage);

            RaidFailFlow.VoteRetry(false);
        }

        private void OnVoteChanged(int agreed, int voteTotal)
        {
            total = Mathf.Max(1, voteTotal);
            ShowVote(agreed, total);
        }

        private void OnRetryStarting()
        {
            mode = Mode.Retrying;
            remain = retryCloseDelay;

            SetChoiceVisible(false);
            SetMessage(retryingMessage);

            if (countdownText != null) countdownText.gameObject.SetActive(false);
        }

        private void OnRetryCancelled(string reason)
        {
            mode = Mode.Returning;
            remain = returnDelay;

            SetChoiceVisible(false);
            SetMessage(reason);

            if (countdownText != null)
            {
                countdownText.gameObject.SetActive(true);
                countdownText.text = Mathf.CeilToInt(returnDelay).ToString();
            }
        }

        private void ShowVote(int agreed, int voteTotal)
        {
            if (voteText == null) return;

            // 혼자면 현황이 의미가 없다.
            voteText.gameObject.SetActive(voteTotal > 1);
            voteText.text = string.Format(voteFormat, agreed, voteTotal);
        }

        private void SetChoiceVisible(bool visible)
        {
            if (choiceGroup != null) choiceGroup.SetActive(visible);
            if (retryButton != null) retryButton.gameObject.SetActive(visible);
            if (returnButton != null) returnButton.gameObject.SetActive(visible);
        }

        private void SetMessage(string message)
        {
            if (messageText != null && !string.IsNullOrEmpty(message)) messageText.text = message;
        }

        private void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
