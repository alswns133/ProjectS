using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 던전(레이드) 입구에 도착하면 뜨는 입장 화면. "어디를, 어느 난이도로" 들어갈지 고르는 곳이다.
    /// 기획서 3-1의 UI_DG_001~006 + 입장/취소에 해당한다.
    ///
    /// <para>
    /// <b>던전과 레이드가 이 프리팹 하나를 공유한다.</b> 게이트가 <see cref="SetMode"/>로 모드와 카탈로그를
    /// 넘기고, 이 팝업은 그 값으로 타이틀·목록·전환할 씬만 갈라 쓴다.
    /// <see cref="SetMode"/>는 반드시 <c>ShowPopup</c> <b>전에</b> 불러야 한다 — 뒤에 부르면
    /// <c>OnShow</c>가 카탈로그 null 상태로 목록을 그린다.
    /// </para>
    /// <para>
    /// 선택 결과는 2자리 던전 ID(docs/ID_NUMBERING.md §4)로 <c>GameSession</c>에 실려
    /// 던전 씬의 <c>DungeonContext</c>까지 그대로 간다.
    /// </para>
    /// </summary>
    public class DungeonEntryPopup : BasePopup
    {
        [Header("헤더")]
        [SerializeField] private TMP_Text titleText;              // ① UI_DG_001
        [SerializeField] private TMP_Text progressText;           // ① 진행률
        [SerializeField] private Button rewardButton;             // ② UI_DG_002

        [Header("에피소드 목록")]                                  // ④ UI_DG_004
        [SerializeField] private RectTransform episodeListRoot;
        [SerializeField] private EpisodeEntryView episodeEntryPrefab;

        [Header("사이드 패널 — 이미지")]
        [SerializeField] private Image previewImage;
        [SerializeField] private TMP_Text previewCaption;

        [Header("사이드 패널 — 퀘스트 트래커")]                     // ③ UI_DG_003
        [Tooltip("접어도 남는 손잡이. 통째로 끄면 다시 열 방법이 없어진다.")]
        [SerializeField] private GameObject trackerHeader;
        [Tooltip("접히는 본문. 이 오브젝트만 켜고 끈다.")]
        [SerializeField] private GameObject trackerBody;
        [SerializeField] private Button trackerToggleButton;
        [SerializeField] private TMP_Text trackerToggleLabel;

        [Tooltip("진행 중 퀘스트 제목을 한 줄씩 적는 칸. 카드 목록이 아니라 요약 텍스트다.")]
        [SerializeField] private TMP_Text trackerText;

        [Header("난이도 탭")]                                      // ⑤ UI_DG_005
        [Tooltip("프리팹에 미리 놓인 탭들. 카탈로그 난이도 수보다 많으면 남는 탭은 꺼진다.")]
        [SerializeField] private Toggle[] difficultyTabs;

        [Header("하단")]
        [SerializeField] private TMP_Text keyGuideText;           // ⑥ UI_DG_006
        [SerializeField] private Button enterButton;              // ⑦ [SPACE]
        [SerializeField] private Button cancelButton;             // ⑧ [ESC]

        // 게이트가 넘긴 값. 열기 전에 채워진다.
        private EntryMode mode = EntryMode.Dungeon;
        private DungeonCatalog catalog;

        // 목록에 실제로 만들어 둔 카드들. 카탈로그가 바뀌어도 재사용하고, 남는 카드는 꺼 둔다.
        private readonly List<EpisodeEntryView> entries = new();

        private int selectedEpisode = -1;
        private int selectedDifficulty = -1;

        // 난이도 탭 콜백이 초기화 중에도 불려 선택을 덮어쓰지 않게 하는 빗장.
        private bool suppressTabCallback;

        /// <summary>
        /// 유저가 퀘스트 트래커를 접어 둔 상태인가. <b>쓰는 곳은 접기/펼치기 버튼 하나뿐이다.</b>
        /// 에피소드 선택은 이 값을 읽기만 한다 — 선택이 이 값을 건드리면 유저가 접어둔 게 멋대로 펴진다.
        /// <b>기본값이 true라 처음에는 접힌 채로 시작한다</b>(입장 화면의 주인공은 던전 이미지와 목록이고,
        /// 트래커가 처음부터 이미지를 덮고 있으면 무엇을 고르는 화면인지 흐려진다).
        /// 한 번 펼치면 그 뒤로는 에피소드를 바꿔도 펼친 상태가 따라온다.
        /// 팝업 인스턴스가 씬을 넘어 살아남으므로, 게이트를 나갔다 다시 들어와도 마지막 선택이 유지된다.
        /// </summary>
        private bool trackerUserClosed = true;

        /// <summary>
        /// 어느 콘텐츠의 입장 화면으로 열지 정한다. <b><c>ShowPopup</c> 전에</b> 호출해야 한다.
        /// </summary>
        /// <param name="entryMode">던전인지 레이드인지</param>
        /// <param name="entryCatalog">표시할 목록 에셋</param>
        public void SetMode(EntryMode entryMode, DungeonCatalog entryCatalog)
        {
            mode = entryMode;
            catalog = entryCatalog;

            if (catalog != null && catalog.Mode != entryMode)
                Debug.LogWarning($"[DungeonEntryPopup] 게이트 모드({entryMode})와 카탈로그 모드({catalog.Mode})가 다름: {catalog.name}");

            // 이미 떠 있는 상태에서 바꿔치기해도 화면이 따라오게 한다(게이트가 겹쳐 있는 경우).
            if (IsVisible) Rebuild();
        }

        // 버튼 연결은 최초 1회만. BasePopup이 OnInit을 한 번만 호출해 주므로 중복 구독이 쌓이지 않는다.
        protected override void OnInit()
        {
            if (enterButton != null) enterButton.onClick.AddListener(Enter);
            if (cancelButton != null) cancelButton.onClick.AddListener(RequestClose);
            if (trackerToggleButton != null) trackerToggleButton.onClick.AddListener(ToggleTracker);

            for (int i = 0; i < (difficultyTabs?.Length ?? 0); i++)
            {
                int index = i;   // 클로저가 루프 변수를 잡지 않게 복사
                if (difficultyTabs[i] != null)
                    difficultyTabs[i].onValueChanged.AddListener(on => { if (on) SelectDifficulty(index); });
            }
        }

        protected override void OnShow()
        {
            // 마우스로 누르려면 커서가 풀려 있어야 한다(플레이 중엔 잠겨 숨겨져 있음).
            SetCursorFree(true);

            // W/A/S/D가 목록 이동과 플레이어 이동에 동시에 먹히지 않게 게임플레이 입력을 잠근다.
            SetPlayerInputEnabled(false);

            Rebuild();
        }

        protected override void OnHide()
        {
            SetCursorFree(false);
            SetPlayerInputEnabled(true);
        }

        private void Update()
        {
            if (!IsVisible) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // ⑥ W/S = 스테이지 변경 · A/D = 난이도 변경. 방향키도 같이 받는다(같은 뜻의 입력).
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame) MoveEpisode(-1);
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame) MoveEpisode(1);
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) MoveDifficulty(-1);
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) MoveDifficulty(1);

            // ⑦ 입장. ESC(취소)는 BasePopup.CanCloseByBack이 기본 true라 UIManager의 뒤로가기가 이미 처리한다.
            if (keyboard.spaceKey.wasPressedThisFrame) Enter();
        }

        // ── 구성 ──────────────────────────────────────────────

        // 카탈로그 내용을 화면에 다시 그린다. 열 때마다 호출되며, 레벨이 올라 잠금이 풀렸을 수 있어 매번 다시 판정한다.
        private void Rebuild()
        {
            if (catalog == null)
            {
                Debug.LogWarning("[DungeonEntryPopup] 카탈로그가 없다 — 게이트가 SetMode를 ShowPopup 전에 불렀는지 확인할 것");
                return;
            }

            if (titleText != null) titleText.text = catalog.Title;

            BuildEpisodes();
            BuildDifficultyTabs();

            // 선택을 비운 뒤 다시 고른다. 카탈로그가 더 짧은 것으로 바뀌었을 때 예전 인덱스가 남지 않게 한다.
            selectedEpisode = -1;
            selectedDifficulty = -1;

            // 키보드 흐름이라 선택 없는 상태로 시작하면 첫 입력이 '선택'에 소모되고 이미지 자리도 빈 채로 남는다.
            SelectEpisode(FirstSelectableEpisode());
            SelectDifficulty(FirstSelectableDifficulty());

            ApplyTrackerExpanded(!trackerUserClosed);
            RefreshQuestTracker();
            RefreshProgress();
            RefreshEnterButton();
        }

        private void BuildEpisodes()
        {
            if (episodeListRoot == null || episodeEntryPrefab == null) return;

            IReadOnlyList<EpisodeInfo> episodes = catalog.Episodes;

            // 모자라면 만들고, 남으면 끈다. 열고 닫을 때마다 파괴/생성하지 않는다.
            while (entries.Count < episodes.Count)
            {
                EpisodeEntryView view = Instantiate(episodeEntryPrefab, episodeListRoot);
                view.OnClicked += OnEpisodeClicked;
                entries.Add(view);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                bool used = i < episodes.Count;
                entries[i].gameObject.SetActive(used);
                if (!used) continue;

                EpisodeInfo info = episodes[i];
                entries[i].SetEpisode(i, info, cleared: false, locked: PlayerLevel < info.RequiredLevel);
            }
        }

        private void BuildDifficultyTabs()
        {
            if (difficultyTabs == null) return;

            IReadOnlyList<DifficultyInfo> difficulties = catalog.Difficulties;

            // 탭을 세팅하는 동안 onValueChanged가 선택을 덮어쓰지 않게 막는다.
            suppressTabCallback = true;

            for (int i = 0; i < difficultyTabs.Length; i++)
            {
                Toggle tab = difficultyTabs[i];
                if (tab == null) continue;

                bool used = i < difficulties.Count;
                tab.gameObject.SetActive(used);
                if (!used) continue;

                TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = difficulties[i].Label;

                tab.interactable = PlayerLevel >= difficulties[i].RequiredLevel;
            }

            suppressTabCallback = false;
        }

        // ── 선택 ──────────────────────────────────────────────

        private void OnEpisodeClicked(int index) => SelectEpisode(index);

        private void SelectEpisode(int index)
        {
            if (catalog == null || index < 0 || index >= catalog.Episodes.Count) return;

            // 같은 항목을 다시 눌렀을 때 연출이 매번 재생되면 깜빡거려 보인다.
            if (index == selectedEpisode) return;

            selectedEpisode = index;

            for (int i = 0; i < entries.Count; i++)
                entries[i].SetSelected(i == index);

            ApplyPreview(catalog.Episodes[index]);

            // 유저가 접어 뒀으면 접힌 채로 둔다. 이 분기가 trackerUserClosed를 읽기만 하는 유일한 이유다.
            if (!trackerUserClosed) ApplyTrackerExpanded(true);

            RefreshEnterButton();
        }

        private void SelectDifficulty(int index)
        {
            if (suppressTabCallback) return;
            if (catalog == null || index < 0 || index >= catalog.Difficulties.Count) return;
            if (index == selectedDifficulty) return;

            selectedDifficulty = index;

            if (difficultyTabs != null && index < difficultyTabs.Length && difficultyTabs[index] != null)
            {
                suppressTabCallback = true;
                difficultyTabs[index].isOn = true;
                suppressTabCallback = false;
            }

            RefreshEnterButton();
        }

        // 잠긴 항목은 건너뛰며 이동한다. 목록 끝에서는 멈춘다(순환하면 어디까지 봤는지 감을 잃는다).
        private void MoveEpisode(int step)
        {
            if (catalog == null) return;

            for (int i = selectedEpisode + step; i >= 0 && i < catalog.Episodes.Count; i += step)
            {
                if (PlayerLevel < catalog.Episodes[i].RequiredLevel) continue;
                SelectEpisode(i);
                return;
            }
        }

        private void MoveDifficulty(int step)
        {
            if (catalog == null) return;

            for (int i = selectedDifficulty + step; i >= 0 && i < catalog.Difficulties.Count; i += step)
            {
                if (PlayerLevel < catalog.Difficulties[i].RequiredLevel) continue;
                SelectDifficulty(i);
                return;
            }
        }

        private int FirstSelectableEpisode()
        {
            if (catalog == null) return -1;

            for (int i = 0; i < catalog.Episodes.Count; i++)
                if (PlayerLevel >= catalog.Episodes[i].RequiredLevel) return i;

            return -1;
        }

        private int FirstSelectableDifficulty()
        {
            if (catalog == null) return -1;

            for (int i = 0; i < catalog.Difficulties.Count; i++)
                if (PlayerLevel >= catalog.Difficulties[i].RequiredLevel) return i;

            return -1;
        }

        // ── 사이드 패널 ────────────────────────────────────────

        private void ApplyPreview(EpisodeInfo info)
        {
            if (previewImage != null)
            {
                previewImage.sprite = info.PreviewImage;
                previewImage.enabled = info.PreviewImage != null;
            }

            if (previewCaption != null)
            {
                previewCaption.text = info.Caption;
                previewCaption.gameObject.SetActive(!string.IsNullOrWhiteSpace(info.Caption));
            }
        }

        // 접기/펼치기 버튼. trackerUserClosed를 쓰는 유일한 곳이다.
        private void ToggleTracker()
        {
            bool expanded = trackerBody != null && trackerBody.activeSelf;

            trackerUserClosed = expanded;   // 지금 펼쳐져 있었다면 이번 클릭이 '접기'다
            ApplyTrackerExpanded(!expanded);
        }

        // ③ 진행 중 퀘스트 요약. 입장 화면에서는 "지금 뭘 하던 중이었나"만 상기시키면 되므로
        // HUD 트래커처럼 카드를 만들지 않고 제목만 나열한다(핀·상세 팝업도 여기선 필요 없다).
        // 열릴 때 한 번만 당겨 온다 — 게이트 앞에 서 있는 동안 퀘스트가 진행될 일이 없어 구독이 필요 없다.
        private void RefreshQuestTracker()
        {
            if (trackerText == null) return;

            IReadOnlyList<QuestData> quests = QuestManager.Instance != null ? QuestManager.Instance.ActiveQuests : null;
            if (quests == null || quests.Count == 0)
            {
                trackerText.text = "추적 중인 퀘스트 없음";
                return;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < quests.Count; i++)
            {
                if (i > 0) builder.AppendLine();

                builder.Append("· ").Append(quests[i].Title);
                if (quests[i].IsReadyToTurnIn) builder.Append(" (완료 가능)");
            }

            trackerText.text = builder.ToString();
        }

        private void ApplyTrackerExpanded(bool expanded)
        {
            // 손잡이(헤더)는 접힌 상태에서도 남아 있어야 다시 열 수 있다.
            if (trackerHeader != null) trackerHeader.SetActive(true);
            if (trackerBody != null) trackerBody.SetActive(expanded);
            if (trackerToggleLabel != null) trackerToggleLabel.text = expanded ? "접기" : "펼치기";
        }

        // ── 입장 ──────────────────────────────────────────────

        private void Enter()
        {
            // SPACE는 EventSystem의 Submit이기도 해서, 입장 버튼이 선택돼 있으면 한 프레임에 두 번 들어올 수 있다.
            // 닫힌 뒤(IsVisible = false)의 두 번째 호출을 여기서 막지 않으면 씬 전환이 두 번 요청된다.
            if (!IsVisible) return;
            if (!CanEnter()) return;

            EpisodeInfo episode = catalog.Episodes[selectedEpisode];
            int dungeonId = DungeonCatalog.MakeDungeonId(episode.DungeonNumber, catalog.Difficulties[selectedDifficulty].Value);

            // 먼저 팝업을 닫아 커서·입력을 원복(OnHide)하고, 그 다음 전환을 요청한다.
            RequestClose();

            // 어느 씬으로 가는지는 이 화면이 알 필요가 없다 — 세션에 싣는 것도 씬을 고르는 것도 라우터가 한다.
            // 던전 표시 이름은 이 화면만 알고 있어(재도전은 ID만 안다) 여기서 함께 넘긴다.
            DungeonRouter.Enter(mode, dungeonId, episode.DisplayName);
        }

        private bool CanEnter()
        {
            if (catalog == null) return false;
            if (selectedEpisode < 0 || selectedEpisode >= catalog.Episodes.Count) return false;
            if (selectedDifficulty < 0 || selectedDifficulty >= catalog.Difficulties.Count) return false;

            return PlayerLevel >= catalog.Episodes[selectedEpisode].RequiredLevel
                && PlayerLevel >= catalog.Difficulties[selectedDifficulty].RequiredLevel;
        }

        private void RefreshEnterButton()
        {
            if (enterButton != null) enterButton.interactable = CanEnter();
        }

        // ① 진행률. 클리어 기록을 담는 세이브 항목이 아직 없어 지금은 전체 개수만 보여준다.
        private void RefreshProgress()
        {
            if (progressText == null || catalog == null) return;

            progressText.text = $"0 / {catalog.Episodes.Count}";
        }

        // ── 주변 상태 ──────────────────────────────────────────

        // 세션이 비어 있으면(로그인 없이 직접 씬 테스트) 잠금을 걸지 않는다 — 배치 확인이 막히지 않게.
        private static int PlayerLevel => GameSession.SelectedCharacter?.level ?? int.MaxValue;

        // 입력 핸들러를 통째로 껐다 켠다. OnEnable/OnDisable이 InputAction Enable/Disable과 짝이라
        // 이 한 줄로 이동·공격 입력이 함께 멎는다. Player.LockMovement는 전투용이라 쓰지 않는다
        // (안전장치 타이머가 스스로 잠금을 풀어 버려 UI가 떠 있는 동안 유지되지 않는다).
        private static void SetPlayerInputEnabled(bool value)
        {
            Player player = PlayerManager.Instance?.Player;
            if (player == null || player.Input == null) return;

            player.Input.enabled = value;
        }

        private static void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
