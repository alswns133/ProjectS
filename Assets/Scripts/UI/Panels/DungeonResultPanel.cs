using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Players;
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
        [SerializeField] private SegmentGaugeView achieveBar;
        [SerializeField] private TMP_Text achieveNum;

        [Header("보상 — 좌 (UI_RS_011)")]
        [Tooltip("보상 슬롯들이 생성될 부모. Layout Group을 붙이면 자동 정렬된다.")]
        [SerializeField] private RectTransform root;

        [Tooltip("보상 슬롯 프리팹(ResultRewardSlot). 보상 아이템 수만큼 이 밑에 생성된다.")]
        [SerializeField] private GameObject slotPrefab;

        [Header("보상 — 우 (UI_RS_013 · 014)")]
        [SerializeField] private TMP_Text expNum;
        [SerializeField] private TMP_Text goldNum;
        [SerializeField] private Button closeButton;

        [Header("3페이즈")]
        [SerializeField] private DungeonExitPopup exitPopup;

        private DungeonResultData data;
        private int page;

        // 동적으로 생성한 보상 슬롯. 재열림 때 재사용하고(매번 생성/파괴 회피), 필요한 개수만 활성화한다.
        private readonly List<ResultRewardSlot> spawnedSlots = new();

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

            // 결과창 동안 마우스 커서를 풀고(버튼 클릭용) 플레이어 조작을 막는다. 닫힐 때 OnHide에서 원복한다.
            SetResultInteraction(true);

            // TODO(sound): 던전 클리어 결과 팡파레 — SoundManager.Instance.PlaySFX(SoundID.SFX_Win);
            //   (BGM을 결과 화면용으로 바꾸거나 잠깐 낮출지도 함께 결정. 실패/전멸 결과가 생기면 SFX_GameOver로 분기.)
            BindScore();
            BindReward();
            GoToPage(0);
        }

        protected override void OnHide()
        {
            // 커서 잠금·플레이어 입력을 플레이 상태로 되돌린다(마을 복귀·재도전으로 이 패널이 닫힐 때).
            SetResultInteraction(false);

            // 다음에 열 때 이전 판의 카운트업이 이어지지 않게 끊는다.
            if (playScoreNum != null) playScoreNum.SetImmediate(0);
        }

        /// <summary>
        /// 결과창용 상호작용 상태를 켜고 끈다. 켜면 마우스 커서를 풀고 플레이어 게임플레이 입력을 잠근다.
        /// </summary>
        /// <remarks>
        /// 커서 처리는 사망 팝업(<see cref="DeathPopup"/>)과 같은 방식이다. 입력 잠금은 컷신용
        /// <c>Player.BeginCutscene</c>이 아니라 <see cref="PlayerInputHandler.SetInputSuspended"/>를 직접 쓴다 —
        /// 결과창은 열려 있는 시간이 정해지지 않아, BeginCutscene의 안전 타이머가 도중에 입력을 되살리면
        /// 패널 뒤에서 플레이어가 움직인다. 패널의 진행 키(스페이스/엔터/ESC)는 자체 Update가 직접 읽으므로
        /// 이 잠금과 무관하게 동작한다.
        /// </remarks>
        /// <param name="active">true면 결과창 상호작용(커서 해제·입력 잠금), false면 플레이 상태로 원복.</param>
        private void SetResultInteraction(bool active)
        {
            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;

            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            player?.Input?.SetInputSuspended(active);
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
            // 게이지 채움·숫자는 잠금 애니메이션이 몬다(fill은 클립, 숫자는 뷰가 미러링). 여기선 재생만 건다.
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

            UIManager.Instance.ShowPopup<DungeonExitPopup>();
        }

        private void BindScore()
        {
            if (playScoreNum != null) playScoreNum.SetImmediate(0);

            SetStatRow(0, "클리어 점수", data.clearScore.ToString("N0", CultureInfo.InvariantCulture));
            SetStatRow(1, "클리어 시간", ClearTimeLabel(data.clearTime));
            SetStatRow(2, "메이즈 난이도", DifficultyLabel(data.difficulty));
            SetStatRow(3, "최대 콤보", data.maxCombo.ToString(CultureInfo.InvariantCulture));

            // 등급 내용만 여기서 세팅한다(노출 타이밍은 잠금 애니메이션이 맡음).
            // 게이지 채움은 즉시 세팅하지 않고, 페이지 진입 시 PlayRise로 0→목표까지 서서히 올린다.
            if (performanceGauge != null) performanceGauge.SetRank(data.grade);

            if (dungeonNameText != null)
                dungeonNameText.text = string.IsNullOrEmpty(data.dungeonName) ? "-" : data.dungeonName;

            float achieve = Mathf.Clamp01(data.achieveRatio);
            if (achieveBar != null) achieveBar.SetRatio(achieve);
            if (achieveNum != null) achieveNum.text = $"{Mathf.RoundToInt(achieve * 100f)}%";
        }

        private void BindReward()
        {
            BindRewardSlots();

            if (expNum != null) expNum.text = data.exp.ToString("N0", CultureInfo.InvariantCulture);
            if (goldNum != null) goldNum.text = data.gold.ToString("N0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 이번 판 보상 아이템 수만큼 슬롯을 만들어 그린다. 확정 보상(기본+확정) 뒤에 뽑힌 랜덤 보상을 붙인다.
        /// 즉시 지급·공개 모델이라 '?' 슬롯은 두지 않는다(랜덤도 뽑힌 실제 아이템으로 보인다).
        /// </summary>
        private void BindRewardSlots()
        {
            if (root == null || slotPrefab == null) return;

            DungeonRewardDisplayItem[] confirmed = data.rewards ?? System.Array.Empty<DungeonRewardDisplayItem>();
            int total = confirmed.Length + (data.hasRandomReward ? 1 : 0);

            EnsureSlotCount(total);

            int idx = 0;
            foreach (DungeonRewardDisplayItem item in confirmed)
                FillRewardSlot(spawnedSlots[idx++], item);

            if (data.hasRandomReward)
                FillRewardSlot(spawnedSlots[idx++], data.randomReward);
        }

        /// <summary>
        /// 슬롯을 정확히 <paramref name="count"/>개 활성화한다. 모자라면 <see cref="slotPrefab"/>으로 만들어
        /// <see cref="spawnedSlots"/>에 쌓고, 남으면 비활성화한다 — 재열림 때 재사용해 매번 생성/파괴를 피한다.
        /// </summary>
        private void EnsureSlotCount(int count)
        {
            while (spawnedSlots.Count < count)
            {
                GameObject go = Instantiate(slotPrefab, root);
                ResultRewardSlot slot = go.GetComponent<ResultRewardSlot>();
                if (slot == null)
                    Debug.LogWarning($"{name}: 슬롯 프리팹에 ResultRewardSlot이 없어 보상을 못 그린다.", this);

                spawnedSlots.Add(slot);   // null이어도 자리를 채워 인덱스가 어긋나지 않게 한다
            }

            for (int i = 0; i < spawnedSlots.Count; i++)
                if (spawnedSlots[i] != null) spawnedSlots[i].gameObject.SetActive(i < count);
        }

        /// <summary>
        /// 한 보상 슬롯을 아이템으로 채운다. 이름은 <see cref="ItemData"/>에서, 아이콘은
        /// <see cref="ItemIconLoader"/>로 비동기 로드해 꽂는다(로드가 끝나면 슬롯이 갱신된다).
        /// </summary>
        /// <remarks>
        /// async void로 두는 것은 UI 이벤트 핸들러와 같은 결의 fire-and-forget이다. 로딩 도중 패널이 닫혀
        /// 슬롯이 파괴됐을 수 있어, await 뒤 <c>slot == null</c>을 다시 확인한다(Unity의 == 오버로드로 파괴 감지).
        /// 아이템 행이 없으면 이름 대신 ID를, 아이콘 없으면 아이콘 칸을 비운 채로 둔다.
        /// </remarks>
        private async void FillRewardSlot(ResultRewardSlot slot, DungeonRewardDisplayItem item)
        {
            ItemData row = null;
            if (JsonManager.Instance != null)
                JsonManager.Instance.ItemDict.TryGetValue(item.itemId, out row);

            string itemName = row != null ? row.Name : $"#{item.itemId}";
            Sprite icon = row != null ? await ItemIconLoader.LoadAsync(row.IconAddress) : null;

            if (slot == null) return;   // 로딩 중 패널이 닫혀 슬롯이 파괴된 경우
            slot.Set(icon, itemName, item.count, false);
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
