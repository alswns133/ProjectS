using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 결과창의 3페이즈 — 클리어 후 다음 행동을 고르는 선택창. 기획서 5-3의
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

        private int remainingMissions;

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
        }

        private void OnReturnClicked()
        {
            // 판이 끝났으므로 남은 부활 기회를 정리한다(사망 팝업의 마을 복귀와 같은 이유 —
            // 안 지우면 마을에서 죽었을 때 이전 판의 기회로 부활하게 된다).
            ReviveBudget.Clear();
            DungeonResultContext.Clear();

            RequestClose();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
            else
                Debug.LogWarning($"{name}: GameSceneManager가 없어 마을로 돌아가지 못했다.", this);
        }

        private void OnRetryClicked()
        {
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

            DungeonRouter.Enter(EntryMode.Dungeon, dungeonId);
        }
    }
}
