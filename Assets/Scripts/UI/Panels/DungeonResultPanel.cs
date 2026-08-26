using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전 결과창의 1·2페이즈 — 성과 화면과 보상 화면. 기획서 5-1(UI_RS_001~003) ·
    /// 5-2(UI_RS_011~014)에 해당한다. 3페이즈(퇴장 선택)는 <see cref="DungeonExitPopup"/>이 맡는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>한 프리팹에 페이지 둘.</b> 두 페이즈는 하단 3분할(좌 정보 · 중앙 원형 · 우 정보) 골격이 같고
    /// 배경 연출도 공유한다. 패널을 두 벌로 나누면 그 골격과 연출을 두 번 만들게 된다.
    /// </para>
    /// <para>
    /// <b>반드시 <see cref="Open"/>으로 연다.</b> 이 패널은 <c>ClearPanelStack</c> 뒤에 열려
    /// <b>패널 스택의 유일한 패널</b>이 된다. UIManager의 뒤로가기는 "마지막 패널 1개는 닫지 않는다"라서,
    /// 그래야 ESC가 결과 화면을 통째로 닫아 클리어된 던전에 플레이어만 남는 사고를 막을 수 있다.
    /// HUD 위에 그냥 쌓아 열면 ESC 한 번에 이 화면이 사라진다.
    /// </para>
    /// <para>
    /// 표시할 값은 <see cref="DungeonResultContext"/>에서 읽는다. UIManager에는 패널 인스턴스를
    /// 타입으로 꺼내는 통로가 없어(팝업만 <c>GetPopup</c>) 여는 쪽이 값을 직접 먹일 수 없기 때문이다.
    /// </para>
    /// (2026-08-24 TH)
    /// </remarks>
    public class DungeonResultPanel : BasePanel
    {
        [Header("페이지")]
        [SerializeField] private GameObject pageScore;      // 1페이즈
        [SerializeField] private GameObject pageReward;     // 2페이즈

        [Tooltip("페이지 전체를 덮는 투명 버튼. 화면 아무 곳이나 눌러 다음으로 넘어가는 용도.")]
        [SerializeField] private Button advanceButton;

        [Header("성과 — 좌 (UI_RS_001)")]
        [SerializeField] private ScoreCountUpFx playScoreNum;

        [Tooltip("0 클리어 점수 · 1 클리어 시간 · 2 메이즈 난이도 · 3 최대 콤보 순서.")]
        [SerializeField] private StatRowView[] statRows;

        [Header("성과 — 중앙 (UI_RS_002)")]
        [Tooltip("원형 퍼포먼스 게이지 프리팹 인스턴스. 안쪽 조각은 뷰가 들고 있어 여기선 참조 하나만 잡는다.")]
        [SerializeField] private PerformanceGaugeView performanceGauge;

        [Header("성과 — 우 (UI_RS_003)")]
        [SerializeField] private TMP_Text dungeonNameText;
        [SerializeField] private TMP_Text stageText;
        [SerializeField] private SegmentGaugeView achieveBar;
        [SerializeField] private TMP_Text achieveNum;

        [Header("보상 — 좌 (UI_RS_011)")]
        [Tooltip("0 기본 보상 · 1 확정 획득 · 2 랜덤 획득 순서.")]
        [SerializeField] private ResultRewardSlot[] rewardSlots;

        [Header("보상 — 중앙 (UI_RS_012)")]
        [SerializeField] private TMP_Text gradeText;
        [SerializeField] private Image itemPreview;

        [Header("보상 — 우 (UI_RS_013 · 014)")]
        [SerializeField] private TMP_Text expNum;
        [SerializeField] private TMP_Text goldNum;
        [SerializeField] private Button closeButton;

        [Header("3페이즈")]
        [SerializeField] private DungeonExitPopup exitPopup;

        private DungeonResultData data;
        private int page;

        /// <summary>
        /// 결과를 싣고 결과 화면을 연다. 클리어 판정을 내린 쪽이 부르는 유일한 진입점이다.
        /// </summary>
        /// <param name="result">이번 판의 결과 스냅샷</param>
        public static void Open(DungeonResultData result)
        {
            DungeonResultContext.Set(result);

            UIManager manager = UIManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[DungeonResultPanel] UIManager가 없어 결과 화면을 열지 못했다.");
                return;
            }

            // ★ 스택을 비우고 여는 것이 핵심이다(위 remarks 참고). HUD 위에 쌓으면 ESC로 닫힌다.
            manager.ClearPanelStack();
            manager.ShowPanel<DungeonResultPanel>();
        }

        protected override void OnInit()
        {
            if (advanceButton != null) advanceButton.onClick.AddListener(Advance);
            if (closeButton != null) closeButton.onClick.AddListener(Advance);
        }

        protected override void OnShow()
        {
            data = DungeonResultContext.Current;

            BindScore();
            BindReward();
            GoToPage(0);
        }

        protected override void OnHide()
        {
            // 다음에 열 때 이전 판의 카운트업이 이어지지 않게 끊는다.
            if (playScoreNum != null) playScoreNum.SetImmediate(0);
        }

        private void Update()
        {
            if (!IsVisible) return;

            // 3페이즈가 떠 있으면 그쪽이 입력의 주인이다. 여기서 또 받으면 팝업 뒤에서 페이지가 넘어간다.
            if (UIManager.Instance != null && UIManager.Instance.IsPopupOpen<DungeonExitPopup>()) return;

            if (!AdvancePressed()) return;

            // 첫 입력은 굴러가는 점수를 끊는 데 쓴다(연출 건너뛰기). 그다음 입력부터 페이지가 넘어간다.
            if (page == 0 && playScoreNum != null && playScoreNum.IsCounting)
            {
                playScoreNum.Skip();
                return;
            }

            Advance();
        }

        // ESC는 UIManager의 뒤로가기로도 흘러가지만, 이 패널이 스택의 마지막 하나라 그쪽은 아무 일도 하지 않는다.
        private static bool AdvancePressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return false;

            return keyboard.spaceKey.wasPressedThisFrame
                || keyboard.enterKey.wasPressedThisFrame
                || keyboard.escapeKey.wasPressedThisFrame;
        }

        private void Advance()
        {
            if (page == 0)
            {
                GoToPage(1);
                return;
            }

            OpenExitPopup();
        }

        private void GoToPage(int index)
        {
            page = index;

            if (pageScore != null) pageScore.SetActive(index == 0);
            if (pageReward != null) pageReward.SetActive(index == 1);

            // 성과 페이지에 들어설 때마다 점수 카운트업과 게이지 잠금 연출을 처음부터 돌린다.
            if (index != 0) return;

            if (playScoreNum != null) playScoreNum.Play(data.playScore);
            if (performanceGauge != null) performanceGauge.PlayLock();
        }

        private void OpenExitPopup()
        {
            // 3페이즈에서는 하단 정보 블록이 사라지고, 뒤의 클리어 연출 위에 선택창만 남는다.
            if (pageScore != null) pageScore.SetActive(false);
            if (pageReward != null) pageReward.SetActive(false);

            if (exitPopup == null || UIManager.Instance == null)
            {
                Debug.LogWarning($"{name}: 퇴장 선택창을 열지 못했다(팝업 또는 UIManager 없음).", this);
                return;
            }

            // ★ 값 주입은 반드시 ShowPopup 전에. 뒤에 부르면 이미 옛 수량으로 그려진 뒤다.
            exitPopup.SetMissionCount(data.remainingMissions);
            UIManager.Instance.ShowPopup<DungeonExitPopup>();
        }

        private void BindScore()
        {
            if (playScoreNum != null) playScoreNum.SetImmediate(0);

            SetStatRow(0, "클리어 점수", data.clearScore.ToString("N0", CultureInfo.InvariantCulture));
            SetStatRow(1, "클리어 시간", ClearTimeLabel(data.clearTime));
            SetStatRow(2, "메이즈 난이도", DifficultyLabel(data.difficulty));
            SetStatRow(3, "최대 콤보", data.maxCombo.ToString(CultureInfo.InvariantCulture));

            if (performanceGauge != null) performanceGauge.SetRatio(data.performanceRatio);

            if (dungeonNameText != null)
                dungeonNameText.text = string.IsNullOrEmpty(data.dungeonName) ? "-" : data.dungeonName;
            if (stageText != null) stageText.text = $"{data.stage} 단계";

            float achieve = Mathf.Clamp01(data.achieveRatio);
            if (achieveBar != null) achieveBar.SetRatio(achieve);
            if (achieveNum != null) achieveNum.text = $"{Mathf.RoundToInt(achieve * 100f)}%";
        }

        private void BindReward()
        {
            // 드랍 테이블이 아직 없다(2026-08-24). 슬롯 자리만 잡아 두고 랜덤 칸만 '?'로 표시한다.
            if (rewardSlots != null)
            {
                for (int i = 0; i < rewardSlots.Length; i++)
                {
                    if (rewardSlots[i] == null) continue;

                    bool isRandom = i == rewardSlots.Length - 1;
                    if (isRandom) rewardSlots[i].Set(null, "?", 0, true);
                    else rewardSlots[i].Clear();
                }
            }

            if (gradeText != null) gradeText.text = string.IsNullOrEmpty(data.grade) ? "-" : data.grade;
            if (itemPreview != null) itemPreview.enabled = itemPreview.sprite != null;

            if (expNum != null) expNum.text = data.exp.ToString("N0", CultureInfo.InvariantCulture);
            if (goldNum != null) goldNum.text = data.gold.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void SetStatRow(int index, string label, string value)
        {
            if (statRows == null || index >= statRows.Length) return;
            if (statRows[index] == null) return;

            statRows[index].Set(label, value);
        }

        private static string ClearTimeLabel(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60}분 {total % 60}초";
        }

        private static string DifficultyLabel(int difficulty) => difficulty switch
        {
            1 => "Normal",
            2 => "Hard",
            3 => "Maniac",
            _ => "-"
        };
    }
}
