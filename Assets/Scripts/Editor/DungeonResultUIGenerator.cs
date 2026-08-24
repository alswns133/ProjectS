// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.UI;
using ProjectS.UI.Framework;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 던전 결과창(기획서 5장)의 계층을 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Dungeon Result UI
    ///
    /// 만드는 것은 둘이다 — 1·2페이즈를 담은 <see cref="DungeonResultPanel"/>과
    /// 3페이즈 <see cref="DungeonExitPopup"/>. 둘 다 <c>UIManager</c>의 자식으로 붙인다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 UIManager 직속인가</b>: UIManager는 Awake에서 <b>자기 자식만</b> 훑어 패널·팝업을 수집한다.
    /// 다른 곳에 두면 <c>RegisterPanel</c>/<c>RegisterPopup</c>을 누군가 짝 맞춰 불러야 하고, 안 부르면
    /// "패널이 없음" 경고만 남고 화면이 뜨지 않는다. 대신 UIManager 아래는 HUD의 Canvas 밖이라
    /// <b>자기 Canvas가 없으면 렌더링되지 않는다</b> — 그래서 둘 다 Canvas + CanvasScaler +
    /// GraphicRaycaster를 함께 붙인다(사망 팝업이 같은 구조).
    /// </para>
    /// <para>
    /// 정렬 순서는 HUD(0) 위, 사망 팝업(50) 아래에 둔다. 결과 화면과 사망 팝업은 동시에 뜰 일이 없지만,
    /// 순서를 겹쳐 두면 나중에 둘 중 무엇이 위인지 씬마다 달라진다.
    /// </para>
    /// <para>
    /// 수치는 1920×1080 기준으로 코드에 박아 둔다. 배치를 손본 뒤 다시 실행하면 기존 계층을 지우고
    /// 새로 뽑으므로, 실행 전 확인 대화상자를 띄운다.
    /// </para>
    /// (2026-08-24 TH)
    /// </remarks>
    public static class DungeonResultUIGenerator
    {
        private const string UndoLabel = "Create Dungeon Result UI";

        private const string PanelName = "DungeonResultPanel";
        private const string PopupName = "DungeonExitPopup";

        private const int PanelSortingOrder = 40;
        private const int PopupSortingOrder = 45;

        // 하단 정보 영역. 화면 위쪽은 클리어 연출(캐릭터)에 내주고 정보는 아래에만 깐다 — 기획서 와이어프레임 주석.
        private static readonly Vector2 InfoBarSize = new(1800f, 300f);
        private const float InfoBarBottom = 210f;
        private static readonly Vector2 CardSize = new(520f, 300f);
        private static readonly Vector2 CircleSize = new(260f, 260f);
        private const float CardOffsetX = 620f;

        // 원형 게이지는 아트·연출이 붙는 덩어리라 씬에 직접 짓지 않고 프리팹에서 가져온다.
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string GaugePrefabPath = "Assets/Prefabs/UI/PerformanceGauge.prefab";
        private const int PieceCount = 4;
        private const float PieceRadius = 150f;
        private static readonly Vector2 PieceSize = new(96f, 96f);
        private static readonly Color PieceColor = new(0.16f, 0.55f, 0.92f, 1f);

        private static readonly Color ScreenDim = new(0f, 0f, 0f, 0.55f);
        private static readonly Color CardColor = new(0.09f, 0.11f, 0.15f, 0.92f);
        private static readonly Color PopupColor = new(0.11f, 0.13f, 0.17f, 0.98f);
        private static readonly Color SlotColor = new(0.16f, 0.19f, 0.24f, 1f);
        private static readonly Color AccentColor = new(0.93f, 0.55f, 0.18f, 1f);
        private static readonly Color TrackColor = new(0.20f, 0.23f, 0.28f, 1f);
        private static readonly Color NeutralButtonColor = new(0.22f, 0.25f, 0.31f, 1f);
        private static readonly Color MutedTextColor = new(0.72f, 0.76f, 0.83f, 1f);

        [MenuItem("Tools/ProjectS/Create Dungeon Result UI")]
        public static void CreateDungeonResultUI()
        {
            Transform managerRoot = FindUIManagerRoot();
            if (managerRoot == null)
            {
                EditorUtility.DisplayDialog(
                    "던전 결과 UI",
                    "현재 씬에서 UIManager를 찾지 못했습니다.\nHUD(TH) 2처럼 UIManager가 있는 씬을 열고 다시 실행하세요.",
                    "확인");
                return;
            }

            if (!ConfirmOverwrite(managerRoot)) return;

            RemoveExisting(managerRoot, PanelName);
            RemoveExisting(managerRoot, PopupName);

            DungeonExitPopup popup = BuildExitPopup(managerRoot);
            DungeonResultPanel panel = BuildResultPanel(managerRoot, popup);

            // 팝업은 패널이 열어 주므로 평소엔 꺼 둔다. 패널도 UIManager가 ShowPanel로 켜기 전까진 꺼 둔다.
            popup.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(managerRoot.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;

            Debug.Log("[DungeonResultUIGenerator] 던전 결과 UI를 만들었습니다. " +
                      "원형 게이지 연출(RadialFlowGaugeFx)과 세그먼트 바 머티리얼은 아트 에셋이 정해지면 붙이세요.");
        }

        // ── 최상위 구성 ────────────────────────────────────────────

        private static DungeonResultPanel BuildResultPanel(Transform parent, DungeonExitPopup popup)
        {
            RectTransform root = CreateScreen(parent, PanelName, PanelSortingOrder);
            DungeonResultPanel panel = Undo.AddComponent<DungeonResultPanel>(root.gameObject);

            // 배경은 클릭을 먹지 않는다. 바로 뒤 형제인 AdvanceButton이 화면 아무 곳이나 받아야 하기 때문.
            EntryUIBuilder.CreateFullScreenImage(root, "Background", ScreenDim, false);

            (Button advance, TextMeshProUGUI advanceLabel) = EntryUIBuilder.CreateButton(
                root, "AdvanceButton", string.Empty, 1f, new Color(0f, 0f, 0f, 0f));
            EntryUIBuilder.Fill((RectTransform)advance.transform);
            advanceLabel.gameObject.SetActive(false);

            RectTransform pageScore = CreatePage(root, "PageScore");
            RectTransform pageReward = CreatePage(root, "PageReward");

            ScorePageParts score = BuildScorePage(pageScore);
            RewardPageParts reward = BuildRewardPage(pageReward);

            EntryUIBuilder.Wire(panel,
                ("pageScore", pageScore.gameObject),
                ("pageReward", pageReward.gameObject),
                ("advanceButton", advance),
                ("playScoreNum", score.playScoreNum),
                ("performanceGauge", score.gauge),
                ("dungeonNameText", score.dungeonNameText),
                ("stageText", score.stageText),
                ("achieveBar", score.achieveBar),
                ("achieveNum", score.achieveNum),
                ("gradeText", reward.gradeText),
                ("itemPreview", reward.itemPreview),
                ("expNum", reward.expNum),
                ("goldNum", reward.goldNum),
                ("closeButton", reward.closeButton),
                ("exitPopup", popup));

            EntryUIBuilder.WireList(panel, "statRows", score.statRows);
            EntryUIBuilder.WireList(panel, "rewardSlots", reward.slots);

            return panel;
        }

        private static DungeonExitPopup BuildExitPopup(Transform parent)
        {
            RectTransform root = CreateScreen(parent, PopupName, PopupSortingOrder);
            DungeonExitPopup popup = Undo.AddComponent<DungeonExitPopup>(root.gameObject);

            // 딤은 클릭을 막는다. 뒤의 결과 화면 AdvanceButton이 눌리면 팝업 뒤에서 페이지가 넘어간다.
            EntryUIBuilder.CreateFullScreenImage(root, "Dimmer", new Color(0f, 0f, 0f, 0.65f), true);

            Image frame = CreateImage(root, "Frame", PopupColor, EntryUIBuilder.UISprite);
            EntryUIBuilder.SetCenter(frame.rectTransform, Vector2.zero, new Vector2(720f, 360f));

            TextMeshProUGUI title = EntryUIBuilder.CreateTMP(frame.transform, "TitleText", "클리어 후 다음 행동", 32f);
            title.alignment = TextAlignmentOptions.Center;
            EntryUIBuilder.StretchTop(title.rectTransform, -96f, -32f, 40f);

            (Button returnButton, _) = EntryUIBuilder.CreateButton(
                frame.transform, "ReturnButton", "마을 복귀", 26f, AccentColor);
            EntryUIBuilder.SetCenter((RectTransform)returnButton.transform, new Vector2(-170f, 10f), new Vector2(300f, 76f));

            (Button retryButton, _) = EntryUIBuilder.CreateButton(
                frame.transform, "RetryButton", "재도전", 26f, NeutralButtonColor);
            EntryUIBuilder.SetCenter((RectTransform)retryButton.transform, new Vector2(170f, 10f), new Vector2(300f, 76f));

            TextMeshProUGUI notice = EntryUIBuilder.CreateTMP(
                frame.transform, "MissionNotice", "클리어 가능한 미션이 0개 남았습니다.", 22f);
            notice.alignment = TextAlignmentOptions.Center;
            notice.color = MutedTextColor;
            EntryUIBuilder.StretchBottom(notice.rectTransform, 32f, 84f, 40f);

            EntryUIBuilder.Wire(popup,
                ("returnButton", returnButton),
                ("retryButton", retryButton),
                ("missionNoticeText", notice));

            return popup;
        }

        // ── 1페이즈: 성과 ──────────────────────────────────────────

        private struct ScorePageParts
        {
            public ScoreCountUpFx playScoreNum;
            public StatRowView[] statRows;
            public PerformanceGaugeView gauge;
            public TextMeshProUGUI dungeonNameText;
            public TextMeshProUGUI stageText;
            public SegmentGaugeView achieveBar;
            public TextMeshProUGUI achieveNum;
        }

        private static ScorePageParts BuildScorePage(RectTransform page)
        {
            RectTransform infoBar = CreateInfoBar(page);
            ScorePageParts parts = new();

            // ① 좌: 성과 지표
            Image scoreCard = CreateCard(infoBar, "ScoreCard", -CardOffsetX);

            TextMeshProUGUI title = EntryUIBuilder.CreateTMP(scoreCard.transform, "TitleText", "플레이 점수", 24f);
            title.alignment = TextAlignmentOptions.Center;
            title.color = MutedTextColor;
            EntryUIBuilder.StretchTop(title.rectTransform, -60f, -20f, 24f);

            TextMeshProUGUI scoreLabel = EntryUIBuilder.CreateTMP(scoreCard.transform, "PlayScoreNum", "0", 56f);
            scoreLabel.alignment = TextAlignmentOptions.Center;
            scoreLabel.color = AccentColor;
            EntryUIBuilder.SetCenter(scoreLabel.rectTransform, new Vector2(0f, 78f), new Vector2(460f, 72f));

            parts.playScoreNum = Undo.AddComponent<ScoreCountUpFx>(scoreLabel.gameObject);
            EntryUIBuilder.Wire(parts.playScoreNum, ("label", scoreLabel));

            RectTransform statList = CreateRect(scoreCard.transform, "StatList");
            EntryUIBuilder.SetCenter(statList, new Vector2(0f, -66f), new Vector2(460f, 152f));

            VerticalLayoutGroup layout = Undo.AddComponent<VerticalLayoutGroup>(statList.gameObject);
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            string[] labels = { "클리어 점수", "클리어 시간", "메이즈 난이도", "최대 콤보" };
            parts.statRows = new StatRowView[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                parts.statRows[i] = CreateStatRow(statList, $"StatRow{i:00}", labels[i]);

            // ② 중앙: 원형 퍼포먼스 게이지 — 프리팹 인스턴스로만 꽂는다(아래 EnsurePerformanceGaugePrefab 참고).
            parts.gauge = InstantiatePerformanceGauge(infoBar);

            // ③ 우: 달성 현황
            Image progressCard = CreateCard(infoBar, "ProgressCard", CardOffsetX);

            parts.dungeonNameText = EntryUIBuilder.CreateTMP(progressCard.transform, "DungeonNameText", "던전 이름", 28f);
            parts.dungeonNameText.alignment = TextAlignmentOptions.Left;
            EntryUIBuilder.StretchTop(parts.dungeonNameText.rectTransform, -64f, -20f, 28f);

            parts.stageText = EntryUIBuilder.CreateTMP(progressCard.transform, "StageText", "0 단계", 22f);
            parts.stageText.alignment = TextAlignmentOptions.Right;
            parts.stageText.color = MutedTextColor;
            EntryUIBuilder.StretchTop(parts.stageText.rectTransform, -62f, -22f, 28f);

            TextMeshProUGUI achieveLabel = EntryUIBuilder.CreateTMP(progressCard.transform, "AchieveLabel", "달성현황", 20f);
            achieveLabel.alignment = TextAlignmentOptions.Left;
            achieveLabel.color = MutedTextColor;
            EntryUIBuilder.StretchTop(achieveLabel.rectTransform, -104f, -68f, 28f);

            Image bar = CreateImage(progressCard.transform, "AchieveBar", AccentColor, EntryUIBuilder.UISprite);
            EntryUIBuilder.SetCenter(bar.rectTransform, new Vector2(0f, -4f), new Vector2(460f, 40f));
            parts.achieveBar = Undo.AddComponent<SegmentGaugeView>(bar.gameObject);
            EntryUIBuilder.Wire(parts.achieveBar, ("barImage", bar));

            parts.achieveNum = EntryUIBuilder.CreateTMP(progressCard.transform, "AchieveNum", "0%", 34f);
            parts.achieveNum.alignment = TextAlignmentOptions.Center;
            parts.achieveNum.color = AccentColor;
            EntryUIBuilder.StretchBottom(parts.achieveNum.rectTransform, 24f, 74f, 28f);

            return parts;
        }

        // ── 2페이즈: 보상 ──────────────────────────────────────────

        private struct RewardPageParts
        {
            public ResultRewardSlot[] slots;
            public TextMeshProUGUI gradeText;
            public Image itemPreview;
            public TextMeshProUGUI expNum;
            public TextMeshProUGUI goldNum;
            public Button closeButton;
        }

        private static RewardPageParts BuildRewardPage(RectTransform page)
        {
            RectTransform infoBar = CreateInfoBar(page);
            RewardPageParts parts = new();

            // ① 좌: 보상 슬롯
            Image rewardCard = CreateCard(infoBar, "RewardCard", -CardOffsetX);

            string[] groupNames = { "BaseGroup", "FixedGroup", "RandomGroup" };
            string[] groupLabels = { "기본 보상", "확정 획득", "랜덤 획득" };
            parts.slots = new ResultRewardSlot[groupNames.Length];

            for (int i = 0; i < groupNames.Length; i++)
            {
                RectTransform group = CreateRect(rewardCard.transform, groupNames[i]);
                EntryUIBuilder.SetCenter(group, new Vector2(0f, 88f - i * 88f), new Vector2(460f, 84f));

                TextMeshProUGUI label = EntryUIBuilder.CreateTMP(group, "Label", groupLabels[i], 20f);
                label.alignment = TextAlignmentOptions.Left;
                label.color = MutedTextColor;
                EntryUIBuilder.StretchTop(label.rectTransform, -28f, 0f, 0f);

                parts.slots[i] = CreateRewardSlot(group, $"RewardSlot{i:00}");
            }

            // ② 중앙: 등급 엠블럼
            RectTransform emblem = CreateRect(infoBar, "GradeEmblem");
            EntryUIBuilder.SetCenter(emblem, Vector2.zero, CircleSize);

            Image emblemBackground = CreateImage(emblem, "EmblemBackground", TrackColor, CircleSprite);
            EntryUIBuilder.Fill(emblemBackground.rectTransform);

            parts.itemPreview = CreateImage(emblem, "ItemPreview", Color.white, null);
            EntryUIBuilder.SetCenter(parts.itemPreview.rectTransform, Vector2.zero, new Vector2(120f, 120f));
            parts.itemPreview.enabled = false;

            parts.gradeText = EntryUIBuilder.CreateTMP(emblem, "GradeText", "-", 96f);
            parts.gradeText.alignment = TextAlignmentOptions.Center;
            parts.gradeText.color = AccentColor;
            EntryUIBuilder.Fill(parts.gradeText.rectTransform);

            // ③ 우: 완료 보상
            Image completeCard = CreateCard(infoBar, "CompleteCard", CardOffsetX);

            TextMeshProUGUI completeTitle = EntryUIBuilder.CreateTMP(completeCard.transform, "TitleText", "완료보상", 24f);
            completeTitle.alignment = TextAlignmentOptions.Left;
            completeTitle.color = MutedTextColor;
            EntryUIBuilder.StretchTop(completeTitle.rectTransform, -60f, -20f, 28f);

            parts.expNum = CreateRewardValueRow(completeCard.transform, "ExpRow", "EXP", 74f);
            parts.goldNum = CreateRewardValueRow(completeCard.transform, "GoldRow", "재니", 6f);

            (Button close, _) = EntryUIBuilder.CreateButton(
                completeCard.transform, "CloseButton", "닫기 [ESC]", 22f, NeutralButtonColor);
            EntryUIBuilder.SetCenter((RectTransform)close.transform, new Vector2(120f, -96f), new Vector2(200f, 52f));
            parts.closeButton = close;

            return parts;
        }

        // ── 조각 ───────────────────────────────────────────────────

        private static StatRowView CreateStatRow(RectTransform parent, string name, string label)
        {
            RectTransform row = CreateRect(parent, name);

            LayoutElement element = Undo.AddComponent<LayoutElement>(row.gameObject);
            element.preferredHeight = 32f;

            TextMeshProUGUI labelText = EntryUIBuilder.CreateTMP(row, "LabelText", label, 20f);
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.color = MutedTextColor;
            SetHorizontalSlice(labelText.rectTransform, 0f, 0.55f);

            TextMeshProUGUI valueText = EntryUIBuilder.CreateTMP(row, "ValueText", "-", 20f);
            valueText.alignment = TextAlignmentOptions.Right;
            SetHorizontalSlice(valueText.rectTransform, 0.45f, 1f);

            StatRowView view = Undo.AddComponent<StatRowView>(row.gameObject);
            EntryUIBuilder.Wire(view, ("labelText", labelText), ("valueText", valueText));
            return view;
        }

        private static ResultRewardSlot CreateRewardSlot(RectTransform parent, string name)
        {
            Image background = CreateImage(parent, name, SlotColor, EntryUIBuilder.UISprite);
            RectTransform rt = background.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(52f, 52f);

            Image icon = CreateImage(rt, "Icon", Color.white, null);
            EntryUIBuilder.Fill(icon.rectTransform);
            icon.enabled = false;

            TextMeshProUGUI countNum = EntryUIBuilder.CreateTMP(rt, "CountNum", "×1", 18f);
            countNum.alignment = TextAlignmentOptions.BottomRight;
            EntryUIBuilder.Fill(countNum.rectTransform);
            countNum.gameObject.SetActive(false);

            TextMeshProUGUI unknown = EntryUIBuilder.CreateTMP(rt, "UnknownMark", "?", 28f);
            unknown.alignment = TextAlignmentOptions.Center;
            unknown.color = MutedTextColor;
            EntryUIBuilder.Fill(unknown.rectTransform);
            unknown.gameObject.SetActive(false);

            // 이름표는 슬롯 오른쪽에 둔다. 아이템 이름이 붙는 자리라 슬롯보다 넓게 잡는다.
            TextMeshProUGUI itemName = EntryUIBuilder.CreateTMP(rt, "ItemNameText", "-", 20f);
            itemName.alignment = TextAlignmentOptions.Left;
            RectTransform nameRt = itemName.rectTransform;
            nameRt.anchorMin = new Vector2(1f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0f, 0.5f);
            nameRt.anchoredPosition = new Vector2(12f, 0f);
            nameRt.sizeDelta = new Vector2(300f, 0f);

            ResultRewardSlot slot = Undo.AddComponent<ResultRewardSlot>(background.gameObject);
            EntryUIBuilder.Wire(slot,
                ("icon", icon),
                ("itemNameText", itemName),
                ("countNum", countNum),
                ("unknownMark", unknown.gameObject));

            return slot;
        }

        private static TextMeshProUGUI CreateRewardValueRow(Transform parent, string name, string label, float y)
        {
            RectTransform row = CreateRect(parent, name);
            EntryUIBuilder.SetCenter(row, new Vector2(0f, y), new Vector2(460f, 52f));

            TextMeshProUGUI labelText = EntryUIBuilder.CreateTMP(row, "Label", label, 26f);
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.color = MutedTextColor;
            SetHorizontalSlice(labelText.rectTransform, 0f, 0.5f);

            TextMeshProUGUI valueText = EntryUIBuilder.CreateTMP(row, "Num", "0", 30f);
            valueText.alignment = TextAlignmentOptions.Right;
            SetHorizontalSlice(valueText.rectTransform, 0.4f, 1f);

            return valueText;
        }

        // ── 원형 게이지 프리팹 ─────────────────────────────────────

        /// <summary>
        /// 게이지 프리팹을 씬에 인스턴스로 꽂는다. <b>내용은 절대 여기서 만지지 않는다</b> —
        /// 안쪽 조각을 손대면 프리팹 연결이 오버라이드로 지저분해지고, 프리팹을 고쳐도 씬이 안 따라온다.
        /// </summary>
        private static PerformanceGaugeView InstantiatePerformanceGauge(RectTransform parent)
        {
            GameObject prefab = EnsurePerformanceGaugePrefab();
            if (prefab == null) return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(instance, UndoLabel);

            // 위치만 씬이 정하고 크기는 프리팹이 쥔다. 아트가 프리팹에서 게이지를 키워도 씬이 되돌리지 않게.
            PlaceCenter((RectTransform)instance.transform, Vector2.zero);

            PerformanceGaugeView view = instance.GetComponent<PerformanceGaugeView>();
            if (view == null)
                Debug.LogWarning($"[DungeonResultUIGenerator] {GaugePrefabPath} 루트에 PerformanceGaugeView가 없습니다. " +
                                 "게이지 배선이 비어 있게 됩니다.");

            return view;
        }

        /// <summary>
        /// 게이지 프리팹을 가져온다. <b>없을 때만</b> 기본 리그를 한 번 뽑아 저장하고, 이미 있으면 그대로 쓴다.
        /// </summary>
        /// <remarks>
        /// 덮어쓰지 않는 것이 이 함수의 전부다. 프리팹 안에는 아트와 잠금 애니메이션 클립이 들어가는데,
        /// 제너레이터를 다시 돌릴 때마다 갈아엎으면 그 작업이 매번 날아간다.
        /// (씬 계층은 다시 뽑아도 되지만 프리팹은 안 된다 — 이 둘을 가르는 것이 프리팹으로 뺀 이유다.)
        /// </remarks>
        private static GameObject EnsurePerformanceGaugePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GaugePrefabPath);
            if (existing != null) return existing;

            EntryUIBuilder.EnsureFolder(PrefabFolder);

            GameObject temp = BuildPerformanceGaugeContents();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, GaugePrefabPath);
            Object.DestroyImmediate(temp);

            Debug.Log($"[DungeonResultUIGenerator] {GaugePrefabPath} 를 새로 만들었습니다. " +
                      "아트와 잠금 애니메이션은 이 프리팹 안에서 작업하세요(제너레이터가 다시 덮지 않습니다).");
            return saved;
        }

        /// <summary>
        /// 게이지 프리팹의 기본 리그를 만든다. 잠금 연출을 위해 <b>회전축과 반지름을 두 겹으로 분리</b>한다 —
        /// <c>PiecePivotNN</c>이 중심 회전을, 그 자식 <c>PieceNN</c>이 중심에서의 거리를 담당한다.
        /// 한 겹으로 두면 "돌기"와 "조여들기"가 같은 Transform을 놓고 싸운다.
        /// </summary>
        /// <remarks>
        /// 애니메이션 커브 경로는 Animator가 붙은 오브젝트 기준의 <b>이름 경로</b>(<c>PiecePivot00/Piece00</c>)다.
        /// 이 이름들을 바꾸면 에러도 경고도 없이 그 커브만 조용히 죽으므로, 리그 이름은 손대지 않는다.
        /// 피벗의 기본 각도(0·90·180·270)는 클립이 없을 때의 정지 그림용이고, 클립이 돌면 클립 값이 이긴다.
        /// </remarks>
        private static GameObject BuildPerformanceGaugeContents()
        {
            GameObject root = new("PerformanceGauge", typeof(RectTransform), typeof(PerformanceGaugeView));
            RectTransform rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = CircleSize;

            // 조각은 게이지보다 뒤에 깔린다. 둘레 바깥으로 나가는 그림이라 앞에 두면 퍼센트 숫자를 덮는다.
            RectTransform fx = CreateRect(rootRt, "GaugeLockFx");
            EntryUIBuilder.Fill(fx);

            Animator animator = fx.gameObject.AddComponent<Animator>();

            // 결과 화면도 연출로 timeScale이 떨어질 수 있다. scaled로 두면 화면은 떠 있는데 조각만 얼어붙는다.
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            for (int i = 0; i < PieceCount; i++)
            {
                RectTransform pivot = CreateRect(fx, $"PiecePivot{i:00}");
                EntryUIBuilder.SetCenter(pivot, Vector2.zero, CircleSize);
                pivot.localRotation = Quaternion.Euler(0f, 0f, i * (360f / PieceCount));

                Image piece = CreateImage(pivot, $"Piece{i:00}", PieceColor, EntryUIBuilder.UISprite);
                EntryUIBuilder.SetCenter(piece.rectTransform, new Vector2(PieceRadius, 0f), PieceSize);
            }

            Image background = CreateImage(rootRt, "GaugeBackground", TrackColor, CircleSprite);
            EntryUIBuilder.Fill(background.rectTransform);

            Image fill = CreateImage(rootRt, "GaugeFill", AccentColor, CircleSprite);
            EntryUIBuilder.Fill(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = false;   // 강화창 게이지와 같은 방향
            fill.fillAmount = 0.75f;

            TextMeshProUGUI num = EntryUIBuilder.CreateTMP(rootRt, "GaugeNum", "0%", 40f);
            num.alignment = TextAlignmentOptions.Center;
            EntryUIBuilder.SetCenter(num.rectTransform, Vector2.zero, new Vector2(180f, 60f));

            EntryUIBuilder.Wire(root.GetComponent<PerformanceGaugeView>(),
                ("fill", fill),
                ("num", num),
                ("lockAnimator", animator));

            return root;
        }

        private static RectTransform CreateInfoBar(RectTransform page)
        {
            RectTransform infoBar = CreateRect(page, "InfoBar");
            EntryUIBuilder.SetBottomCenter(infoBar, new Vector2(0f, InfoBarBottom), InfoBarSize);
            return infoBar;
        }

        private static Image CreateCard(Transform parent, string name, float offsetX)
        {
            Image card = CreateImage(parent, name, CardColor, EntryUIBuilder.UISprite);
            EntryUIBuilder.SetCenter(card.rectTransform, new Vector2(offsetX, 0f), CardSize);
            return card;
        }

        private static RectTransform CreatePage(Transform parent, string name)
        {
            RectTransform page = CreateRect(parent, name);
            EntryUIBuilder.Fill(page);
            return page;
        }

        /// <summary>UIManager 아래에 자기 Canvas를 가진 화면 루트를 만든다(위 remarks의 렌더링 조건).</summary>
        private static RectTransform CreateScreen(Transform parent, string name, int sortingOrder)
        {
            GameObject go = new(name,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));

            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform rt = (RectTransform)go.transform;
            EntryUIBuilder.Fill(rt);
            return rt;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(Transform parent, string name, Color color, Sprite sprite)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        /// <summary>크기는 건드리지 않고 부모 중앙 기준 위치만 잡는다(프리팹 인스턴스용).</summary>
        private static void PlaceCenter(RectTransform rt, Vector2 position)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
        }

        /// <summary>부모 폭의 일부 구간만 차지하도록 좌우 앵커를 잘라 준다(라벨 | 값 두 칸 나누기).</summary>
        private static void SetHorizontalSlice(RectTransform rt, float min, float max)
        {
            rt.anchorMin = new Vector2(min, 0f);
            rt.anchorMax = new Vector2(max, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>원형 게이지·엠블럼에 쓰는 유니티 기본 원형 스프라이트.</summary>
        private static Sprite CircleSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        private static Transform FindUIManagerRoot()
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            return manager != null ? manager.transform : null;
        }

        private static bool ConfirmOverwrite(Transform parent)
        {
            bool exists = parent.Find(PanelName) != null || parent.Find(PopupName) != null;
            if (!exists) return true;

            return EditorUtility.DisplayDialog(
                "던전 결과 UI",
                $"이미 있는 {PanelName} / {PopupName} 계층을 지우고 다시 만듭니다.\n" +
                "인스펙터에서 손본 값과 아트 연결은 사라집니다. 계속할까요?",
                "다시 만들기", "취소");
        }

        private static void RemoveExisting(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        }
    }
}
