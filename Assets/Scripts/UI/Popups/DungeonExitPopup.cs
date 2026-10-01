using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 결과창의 2페이즈 — 클리어 후 다음 행동을 고르는 선택창. 기획서 5-3의
    /// UI_RS_ReturnBtn · UI_RS_RetryBtn · UI_RS_022에 해당한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 패널의 세 번째 페이지가 아니라 팝업인가</b>: 뒤의 결과 연출은 그대로 살아 있고 그 위에
    /// 선택창만 얹히는 구조라 <see cref="BasePopup"/>의 정의와 그대로 맞는다. 더 중요한 이유는
    /// <see cref="CanCloseByBack"/>다 — 이 창은 <b>ESC로 닫히면 안 된다</b>. 닫히면 클리어된 던전에
    /// 플레이어만 남고 창을 다시 부를 방법이 없다(사망 팝업이 false인 것과 같은 이유).
    /// 패널 페이지에는 이 가드가 없다.
    /// </para>
    /// <para>
    /// 두 버튼 모두 씬을 떠나므로 판이 끝난 것으로 보고 결과 스냅샷을 지운다. 안 지우면 다음 판이
    /// 시작되기 전까지 이전 판의 결과가 남아, 결과 화면을 다시 열었을 때 옛 점수가 뜬다.
    /// </para>
    /// (2026-08-24 TH)
    /// </remarks>
    public class DungeonExitPopup : BasePopup
    {
        [Header("선택")]
        [SerializeField] private Button returnButton;   // ① UI_RS_ReturnBtn
        [SerializeField] private Button retryButton;    // ② UI_RS_RetryBtn

        [Header("안내")]
        [SerializeField] private TMP_Text missionNoticeText;   // ③ UI_RS_022

        [Header("파티 재입장 안내")]
        [Tooltip("파티에서 재입장을 누른 뒤 표시. {0}=동의 수, {1}=총원.")]
        [SerializeField] private string waitingFormat = "파티원의 선택을 기다리는 중… ({0}/{1})";
        [SerializeField] private string retryingMessage = "다시 입장합니다.";

        private int remainingMissions;

        // 안내 줄의 원래 문구. 파티 재입장 대기 중엔 이 줄을 투표 현황으로 빌려 쓰므로, 다시 열 때 되돌린다.
        private string defaultNotice;

        // 파티 재입장 투표를 이미 보냈는지. 두 번 보내지 않고, 결과(시작/무산)가 올 때까지 재입장 버튼을 감춘다.
        private bool voted;

        /// <summary>ESC로 닫지 못한다. 둘 중 하나를 반드시 골라야 던전을 빠져나갈 수 있다.</summary>
        public override bool CanCloseByBack => false;

        /// <summary>
        /// 안내에 쓸 남은 미션 수를 넣는다. <b>여는 쪽이 ShowPopup 전에</b> 호출해야 한다 —
        /// 뒤에 부르면 이미 옛 수량으로 그려진 뒤다.
        /// </summary>
        /// <param name="count">클리어 가능한 미션 남은 수. 0이면 안내 줄을 감춘다</param>
        public void SetMissionCount(int count)
        {
            remainingMissions = count;
        }

        protected override void OnInit()
        {
            if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);
            if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
            if (missionNoticeText != null) defaultNotice = missionNoticeText.text;
        }

        // 파티 재입장 결과는 서버가 투표 이벤트(RaidFailEvents)로 돌려준다. 창이 열려 있는 동안만 받는다
        // — 실패 팝업(RaidFailPopup)과 같은 이벤트라, 닫힌 뒤에도 남아 있으면 다음 판의 실패 투표에 반응한다.
        protected override void OnShow()
        {
            voted = false;
            if (retryButton != null) retryButton.gameObject.SetActive(true);
            if (missionNoticeText != null) missionNoticeText.text = defaultNotice;

            RaidFailEvents.OnRetryVoteChanged += OnVoteChanged;
            RaidFailEvents.OnRetryStarting += OnRetryStarting;
            RaidFailEvents.OnRetryCancelled += OnRetryCancelled;
        }

        protected override void OnHide()
        {
            RaidFailEvents.OnRetryVoteChanged -= OnVoteChanged;
            RaidFailEvents.OnRetryStarting -= OnRetryStarting;
            RaidFailEvents.OnRetryCancelled -= OnRetryCancelled;
        }

        private void OnReturnClicked()
        {
            // 판이 끝났으므로 남은 부활 기회를 정리한다(사망 팝업의 마을 복귀와 같은 이유 —
            // 안 지우면 마을에서 죽었을 때 이전 판의 기회로 부활하게 된다).
            ReviveBudget.Clear();
            DungeonResultContext.Clear();

            // 파티면 마을 복귀 = 재입장 거절이다. 서버에 알려야 먼저 재입장을 누른 파티원이 대기에서 풀려난다
            // (안 알리면 그 사람은 이미 떠난 나를 계속 기다린다).
            if (RaidFailFlow.IsPartyRaid) RaidFailFlow.VoteRetry(false);

            RequestClose();

            // 멀티(파티 인스턴스)면 서버에 이탈도 알린다 — 씬만 바꾸면 서버에 아바타가 레이드에 남는다.
            PartyInstanceExit.ReturnToVillage();
        }

        private void OnRetryClicked()
        {
            // 파티는 혼자 다시 들어갈 수 없다 — 서버가 파티 전체를 새 인스턴스로 옮긴다(실패 후 재시도와 같은 경로).
            // 전원이 동의하면 서버가 OnRetryStarting을, 누가 마을로 돌아가면 OnRetryCancelled를 돌려준다.
            if (RaidFailFlow.IsPartyRaid)
            {
                if (voted) return;
                voted = true;

                if (retryButton != null) retryButton.gameObject.SetActive(false);
                RaidFailFlow.VoteRetry(true);
                return;
            }

            DungeonResultContext.Clear();

            // 세션이 비어 있으면(직접 씬 테스트 등) 지금 있는 던전을 그대로 다시 연다.
            int dungeonId = GameSession.SelectedDungeonId != 0
                ? GameSession.SelectedDungeonId
                : DungeonContext.CurrentDungeonId;

            RequestClose();

            if (dungeonId <= 0)
            {
                Debug.LogWarning($"{name}: 다시 들어갈 던전 ID를 찾지 못해 재도전하지 못했다.", this);
                return;
            }

            // ★ 레이드(ID 9x)는 레이드 모드로 넘겨야 한다. 일반 던전 모드로 넘기면 DungeonRouter가 던전 번호 9에 맞는
            //   씬을 못 찾아 경고만 남기고 아무 일도 안 해, 재입장 버튼이 무반응이었다(2026-09-28).
            EntryMode mode = DungeonRouter.DungeonNumberOf(dungeonId) == 9 ? EntryMode.Raid : EntryMode.Dungeon;
            DungeonRouter.Enter(mode, dungeonId);
        }

        private void OnVoteChanged(int agreed, int total)
        {
            if (voted && missionNoticeText != null) missionNoticeText.text = string.Format(waitingFormat, agreed, total);
        }

        // 전원 동의 — 서버가 파티를 새 인스턴스로 옮기는 중이다. 결과를 지우고 창을 닫는다(곧 로딩 화면이 덮는다).
        private void OnRetryStarting()
        {
            DungeonResultContext.Clear();
            if (missionNoticeText != null) missionNoticeText.text = retryingMessage;
            RequestClose();
        }

        // 누군가 마을로 돌아가 재입장이 무산됐다. 사유를 보여 주고 마을 복귀만 남긴다.
        private void OnRetryCancelled(string reason)
        {
            voted = true;   // 다시 누를 수 없게
            if (retryButton != null) retryButton.gameObject.SetActive(false);
            if (missionNoticeText != null) missionNoticeText.text = reason;
        }
    }
}
