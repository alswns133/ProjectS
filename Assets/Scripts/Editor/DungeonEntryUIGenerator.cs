using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 던전(레이드) 입장 화면의 계층을 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Episode Entry Prefab · Create Dungeon Entry Popup
    ///
    /// <para>
    /// 손으로 쌓으면 앵커·오프셋을 매번 다시 맞춰야 하고, 겹침 순서(이미지 아래 · 트래커 위)처럼
    /// 눈으로는 구분이 안 되는 배치가 쉽게 어긋난다. 수치를 코드에 박아 재실행으로 다시 뽑을 수 있게 한다.
    /// 진입 화면 생성기들과 같은 방침이고, 조립 헬퍼도 <see cref="EntryUIBuilder"/>를 그대로 쓴다.
    /// </para>
    /// <para>
    /// 만들어진 팝업 프리팹은 <b>Bootstrap 씬의 UIManager 캔버스 아래에 직접 놓아야 한다</b> —
    /// UIManager가 Awake에서 자기 자식만 훑어 팝업을 수집하기 때문이다.
    /// </para>
    /// </summary>
    public static class DungeonEntryUIGenerator
    {
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string EntryPrefabPath = PrefabFolder + "/EpisodeEntry.prefab";
        private const string PopupPrefabPath = PrefabFolder + "/DungeonEntryPopup.prefab";

        // 캔버스 기준 해상도. 진입 화면과 같은 1920×1080.
        private const float ScreenWidth = 1920f;
        private const float ScreenHeight = 1080f;

        // 카드 크기. 높이는 선택 여부와 무관하게 고정이다(늘리면 아래 카드가 밀려 커서 밑 항목이 바뀐다).
        private const float CardWidth = 760f;
        private const float CardHeight = 92f;
        private const float HexSize = 76f;

        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color PanelColor = new Color(0.07f, 0.09f, 0.13f, 0.98f);
        private static readonly Color BoxColor = new Color(0.12f, 0.15f, 0.21f, 1f);
        private static readonly Color AccentColor = new Color(0.29f, 0.55f, 0.9f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.66f, 0.74f, 1f);
        private static readonly Color MainTagColor = new Color(0.95f, 0.72f, 0.25f, 1f);
        private static readonly Color TabColor = new Color(0.1f, 0.13f, 0.19f, 1f);

        // ── 카드 프리팹 ────────────────────────────────────────

        [MenuItem("Tools/ProjectS/Create Episode Entry Prefab")]
        public static void CreateEpisodeEntryPrefab()
        {
            if (!ConfirmOverwrite(EntryPrefabPath, "EpisodeEntry 프리팹")) return;

            EntryUIBuilder.EnsureFolder(PrefabFolder);

            GameObject root = BuildEpisodeEntry();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, EntryPrefabPath);
            Object.DestroyImmediate(root);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[DungeonEntryUIGenerator] 카드 프리팹 생성: {EntryPrefabPath}");
        }

        private static GameObject BuildEpisodeEntry()
        {
            // 루트가 곧 버튼이다. 헥사곤 배지까지 눌리게 하려고 루트에 투명 Image(raycastTarget)를 둔다.
            GameObject root = new GameObject("EpisodeEntry",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(EpisodeEntryView));

            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(CardWidth, CardHeight);

            Image rootImg = root.GetComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0f);
            rootImg.raycastTarget = true;

            // 높이 고정의 실체. VerticalLayoutGroup이 이 값을 그대로 쓴다.
            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = CardHeight;
            layout.minHeight = CardHeight;

            // 헥사곤 배지 — 왼쪽 바깥
            GameObject hexGo = new GameObject("HexIcon", typeof(RectTransform), typeof(Image));
            hexGo.transform.SetParent(root.transform, false);
            SetRect(hexGo, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(HexSize / 2f, 0f), new Vector2(HexSize, HexSize));
            Image hexImg = hexGo.GetComponent<Image>();
            hexImg.sprite = EntryUIBuilder.UISprite;
            hexImg.type = Image.Type.Sliced;
            hexImg.color = BoxColor;

            TextMeshProUGUI hexLabel = EntryUIBuilder.CreateTMP(hexGo.transform, "HexLabel", "EP.1", 22f);
            hexLabel.alignment = TextAlignmentOptions.Center;
            hexLabel.color = Color.white;
            EntryUIBuilder.Fill(hexLabel.rectTransform);

            // 이름 바 — 배지 오른쪽
            GameObject barGo = new GameObject("NameBar", typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(root.transform, false);
            SetRect(barGo, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(HexSize + 24f, 8f), new Vector2(-8f, -8f), stretch: true);
            Image barImg = barGo.GetComponent<Image>();
            barImg.sprite = EntryUIBuilder.UISprite;
            barImg.type = Image.Type.Sliced;
            barImg.color = BoxColor;
            barImg.raycastTarget = false;

            // 선택 테두리. 기본은 꺼진 상태(SetSelected가 켠다).
            GameObject frameGo = new GameObject("SelectedFrame", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(barGo.transform, false);
            EntryUIBuilder.Fill(frameGo.GetComponent<RectTransform>());
            Image frameImg = frameGo.GetComponent<Image>();
            frameImg.sprite = EntryUIBuilder.UISprite;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = AccentColor;
            frameImg.raycastTarget = false;
            frameImg.enabled = false;

            TextMeshProUGUI nameText = EntryUIBuilder.CreateTMP(barGo.transform, "NameText", "에피소드명", 24f);
            nameText.alignment = TextAlignmentOptions.Left;
            nameText.color = Color.white;
            SetRect(nameText.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 0f), new Vector2(-260f, 0f), stretch: true);

            GameObject mainTag = new GameObject("MainTag", typeof(RectTransform));
            mainTag.transform.SetParent(barGo.transform, false);
            SetRect(mainTag, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-190f, 0f), new Vector2(80f, 32f));
            TextMeshProUGUI mainLabel = EntryUIBuilder.CreateTMP(mainTag.transform, "Label", "MAIN", 18f);
            mainLabel.alignment = TextAlignmentOptions.Center;
            mainLabel.color = MainTagColor;
            EntryUIBuilder.Fill(mainLabel.rectTransform);

            GameObject clearedMark = new GameObject("ClearedMark", typeof(RectTransform));
            clearedMark.transform.SetParent(barGo.transform, false);
            SetRect(clearedMark, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(120f, 32f));
            TextMeshProUGUI clearedLabel = EntryUIBuilder.CreateTMP(clearedMark.transform, "Label", "CLEAR", 18f);
            clearedLabel.alignment = TextAlignmentOptions.Center;
            clearedLabel.color = MutedColor;
            EntryUIBuilder.Fill(clearedLabel.rectTransform);
            clearedMark.SetActive(false);

            GameObject lockedMark = new GameObject("LockedMark", typeof(RectTransform));
            lockedMark.transform.SetParent(barGo.transform, false);
            SetRect(lockedMark, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-90f, 0f), new Vector2(120f, 32f));
            TextMeshProUGUI lockedLabel = EntryUIBuilder.CreateTMP(lockedMark.transform, "Label", "잠김", 18f);
            lockedLabel.alignment = TextAlignmentOptions.Center;
            lockedLabel.color = MutedColor;
            EntryUIBuilder.Fill(lockedLabel.rectTransform);
            lockedMark.SetActive(false);

            Button button = root.GetComponent<Button>();
            button.targetGraphic = barImg;

            EntryUIBuilder.Wire(root.GetComponent<EpisodeEntryView>(),
                ("cardButton", button),
                ("selectedFrame", frameImg),
                ("hexIcon", hexImg),
                ("hexLabel", hexLabel),
                ("nameText", nameText),
                ("mainTag", mainTag),
                ("clearedMark", clearedMark),
                ("lockedMark", lockedMark));

            return root;
        }

        // ── 팝업 프리팹 ────────────────────────────────────────

        [MenuItem("Tools/ProjectS/Create Dungeon Entry Popup")]
        public static void CreateDungeonEntryPopup()
        {
            if (!ConfirmOverwrite(PopupPrefabPath, "DungeonEntryPopup 프리팹")) return;

            EntryUIBuilder.EnsureFolder(PrefabFolder);

            // 카드 프리팹이 없으면 먼저 만든다. 없으면 목록이 텅 빈 채로 뜬다.
            GameObject entryAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EntryPrefabPath);
            if (entryAsset == null)
            {
                CreateEpisodeEntryPrefab();
                entryAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EntryPrefabPath);
            }

            GameObject root = BuildPopup(entryAsset != null ? entryAsset.GetComponent<EpisodeEntryView>() : null);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PopupPrefabPath);
            Object.DestroyImmediate(root);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[DungeonEntryUIGenerator] 팝업 프리팹 생성: {PopupPrefabPath}\n" +
                      "Bootstrap 씬의 UIManager 캔버스 아래에 놓아야 UIManager가 수집합니다(자식만 훑음).");
        }

        private static GameObject BuildPopup(EpisodeEntryView entryPrefab)
        {
            // ★ 팝업 루트가 곧 캔버스다. UIManager는 그냥 Transform이라 캔버스가 아니고,
            //    이 프로젝트의 팝업들은 각자 Canvas + CanvasScaler + GraphicRaycaster를 들고 그 밑에 붙는다.
            //    이 셋이 없으면 UIManager가 수집도 하고 SetActive(true)까지 해도 화면에 아무것도 안 그려진다.
            GameObject root = new GameObject("DungeonEntryPopup",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(DungeonEntryPopup));

            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(ScreenWidth, ScreenHeight);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1;   // 기존 팝업들과 같은 값

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ScreenWidth, ScreenHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;   // 씬의 다른 팝업들이 전부 0이라 맞춘다

            // 뒤쪽 클릭을 막는 딤. 팝업이 화면 전체를 덮으므로 raycast를 먹어야 한다.
            EntryUIBuilder.CreateFullScreenImage(root.transform, "Dimmer", DimColor, blocksRaycast: true);

            GameObject content = new GameObject("Root", typeof(RectTransform), typeof(Image));
            content.transform.SetParent(root.transform, false);
            EntryUIBuilder.Fill(content.GetComponent<RectTransform>());
            content.GetComponent<Image>().color = PanelColor;

            (TextMeshProUGUI title, TextMeshProUGUI progress, Button reward) = BuildHeader(content.transform);
            (RectTransform listRoot, Image preview, TextMeshProUGUI caption, GameObject trackerHeader,
             GameObject trackerBody, Button trackerToggle, TextMeshProUGUI trackerLabel, TextMeshProUGUI trackerText) = BuildBody(content.transform);
            (Toggle[] tabs, TextMeshProUGUI keyGuide, Button enter, Button cancel) = BuildFooter(content.transform);

            EntryUIBuilder.Wire(root.GetComponent<DungeonEntryPopup>(),
                ("titleText", title),
                ("progressText", progress),
                ("rewardButton", reward),
                ("episodeListRoot", listRoot),
                ("episodeEntryPrefab", entryPrefab),
                ("previewImage", preview),
                ("previewCaption", caption),
                ("trackerHeader", trackerHeader),
                ("trackerBody", trackerBody),
                ("trackerToggleButton", trackerToggle),
                ("trackerToggleLabel", trackerLabel),
                ("trackerText", trackerText),
                ("keyGuideText", keyGuide),
                ("enterButton", enter),
                ("cancelButton", cancel));

            EntryUIBuilder.WireList(root.GetComponent<DungeonEntryPopup>(), "difficultyTabs", tabs);

            // 꺼진 채로 저장한다. UIManager는 비활성 팝업도 수집하고(includeInactive), 켜는 주체는 ShowPopup이다.
            // 켠 채로 두면 게임 시작부터 입장 화면이 떠 있다(기존 DungeonSelectPopup도 비활성이었다).
            root.SetActive(false);

            return root;
        }

        private static (TextMeshProUGUI title, TextMeshProUGUI progress, Button reward) BuildHeader(Transform parent)
        {
            GameObject header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(parent, false);
            EntryUIBuilder.StretchTop(header.GetComponent<RectTransform>(), -132f, -48f, 72f);

            GameObject titleGroup = new GameObject("TitleGroup", typeof(RectTransform), typeof(Image));
            titleGroup.transform.SetParent(header.transform, false);
            SetRect(titleGroup, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(760f, 0f), stretch: true);
            Image titleBg = titleGroup.GetComponent<Image>();
            titleBg.sprite = EntryUIBuilder.UISprite;
            titleBg.type = Image.Type.Sliced;
            titleBg.color = BoxColor;

            TextMeshProUGUI title = EntryUIBuilder.CreateTMP(titleGroup.transform, "TitleText", "던전명", 30f);
            title.alignment = TextAlignmentOptions.Left;
            title.color = Color.white;
            SetRect(title.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 0f), new Vector2(-160f, 0f), stretch: true);

            TextMeshProUGUI progress = EntryUIBuilder.CreateTMP(titleGroup.transform, "ProgressText", "0 / 0", 24f);
            progress.alignment = TextAlignmentOptions.Right;
            progress.color = MutedColor;
            SetRect(progress.gameObject, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-100f, 0f), new Vector2(160f, 0f), stretch: true);

            (Button reward, _) = EntryUIBuilder.CreateButton(header.transform, "RewardButton", "REWARD", 22f, BoxColor);
            SetRect(reward.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(220f, 60f));

            return (title, progress, reward);
        }

        private static (RectTransform listRoot, Image preview, TextMeshProUGUI caption, GameObject trackerHeader,
                        GameObject trackerBody, Button trackerToggle, TextMeshProUGUI trackerLabel, TextMeshProUGUI trackerText)
            BuildBody(Transform parent)
        {
            GameObject body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(parent, false);
            SetRect(body, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(72f, 240f), new Vector2(-72f, -160f), stretch: true);

            // ④ 에피소드 목록 — 왼쪽
            GameObject list = new GameObject("EpisodeList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(body.transform, false);
            SetRect(list, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(CardWidth, 0f), stretch: true);

            VerticalLayoutGroup layout = list.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;        // LayoutElement.preferredHeight를 그대로 쓴다
            layout.childForceExpandHeight = false;   // true면 남는 공간을 카드가 나눠 가져 높이 고정이 깨진다
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            // 사이드 패널 — 오른쪽. 이미지와 트래커가 같은 사각형을 공유한다(겹침).
            // ★ LayoutGroup을 붙이면 자식이 줄 세워져 겹칠 수 없다.
            GameObject side = new GameObject("SidePanel", typeof(RectTransform));
            side.transform.SetParent(body.transform, false);
            SetRect(side, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(CardWidth + 48f, 0f), new Vector2(0f, 0f), stretch: true);

            // 아래층 — 던전 이미지
            GameObject previewRoot = new GameObject("DungeonPreview", typeof(RectTransform), typeof(Image));
            previewRoot.transform.SetParent(side.transform, false);
            EntryUIBuilder.Fill(previewRoot.GetComponent<RectTransform>());
            previewRoot.GetComponent<Image>().color = BoxColor;

            GameObject previewGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(Image));
            previewGo.transform.SetParent(previewRoot.transform, false);
            EntryUIBuilder.Fill(previewGo.GetComponent<RectTransform>());
            Image preview = previewGo.GetComponent<Image>();
            preview.preserveAspect = true;
            preview.raycastTarget = false;
            preview.enabled = false;   // 스프라이트가 들어오기 전까지 비워 둔다

            TextMeshProUGUI caption = EntryUIBuilder.CreateTMP(previewRoot.transform, "PreviewCaption", string.Empty, 22f);
            caption.alignment = TextAlignmentOptions.Center;
            caption.color = MutedColor;
            EntryUIBuilder.StretchBottom(caption.rectTransform, 16f, 64f, 24f);

            // 위층 — 퀘스트 트래커. 형제 순서가 곧 겹침 순서라 이미지 뒤에 만든다.
            GameObject overlay = new GameObject("QuestTrackerOverlay", typeof(RectTransform));
            overlay.transform.SetParent(side.transform, false);
            SetRect(overlay, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -520f), new Vector2(0f, 0f), stretch: true);

            GameObject trackerHeader = new GameObject("TrackerHeader", typeof(RectTransform), typeof(Image));
            trackerHeader.transform.SetParent(overlay.transform, false);
            EntryUIBuilder.StretchTop(trackerHeader.GetComponent<RectTransform>(), -56f, 0f);
            Image headerBg = trackerHeader.GetComponent<Image>();
            headerBg.sprite = EntryUIBuilder.UISprite;
            headerBg.type = Image.Type.Sliced;
            headerBg.color = AccentColor;

            TextMeshProUGUI headerLabel = EntryUIBuilder.CreateTMP(trackerHeader.transform, "Label", "퀘스트", 24f);
            headerLabel.alignment = TextAlignmentOptions.Left;
            headerLabel.color = Color.white;
            SetRect(headerLabel.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 0f), new Vector2(-140f, 0f), stretch: true);

            (Button trackerToggle, TextMeshProUGUI trackerLabel) =
                EntryUIBuilder.CreateButton(trackerHeader.transform, "ToggleButton", "펼치기", 20f, BoxColor);
            SetRect(trackerToggle.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-64f, 0f), new Vector2(112f, 40f));

            // ★ 배경은 불투명해야 한다. 아래 이미지가 비치면 퀘스트 글자가 읽히지 않는다.
            GameObject trackerBody = new GameObject("TrackerBody", typeof(RectTransform), typeof(Image));
            trackerBody.transform.SetParent(overlay.transform, false);
            SetRect(trackerBody, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, -56f), stretch: true);
            trackerBody.GetComponent<Image>().color = new Color(0.09f, 0.11f, 0.16f, 1f);

            TextMeshProUGUI trackerText = EntryUIBuilder.CreateTMP(trackerBody.transform, "TrackerText", "추적 중인 퀘스트 없음", 22f);
            trackerText.alignment = TextAlignmentOptions.TopLeft;
            trackerText.color = MutedColor;
            SetRect(trackerText.gameObject, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 24f), new Vector2(-24f, -24f), stretch: true);

            // 접힌 채로 시작한다(팝업의 trackerUserClosed 기본값과 맞춘 모습). 런타임에는 Rebuild가 다시 정한다.
            trackerBody.SetActive(false);

            return (list.GetComponent<RectTransform>(), preview, caption, trackerHeader, trackerBody, trackerToggle, trackerLabel, trackerText);
        }

        private static (Toggle[] tabs, TextMeshProUGUI keyGuide, Button enter, Button cancel) BuildFooter(Transform parent)
        {
            GameObject footer = new GameObject("Footer", typeof(RectTransform));
            footer.transform.SetParent(parent, false);
            EntryUIBuilder.StretchBottom(footer.GetComponent<RectTransform>(), 48f, 216f, 72f);

            // ⑤ 난이도 탭
            GameObject tabBar = new GameObject("DifficultyTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ToggleGroup));
            tabBar.transform.SetParent(footer.transform, false);
            EntryUIBuilder.StretchTop(tabBar.GetComponent<RectTransform>(), -84f, 0f);

            HorizontalLayoutGroup tabLayout = tabBar.GetComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 12f;
            tabLayout.childControlWidth = true;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandHeight = true;

            ToggleGroup group = tabBar.GetComponent<ToggleGroup>();

            string[] labels = { "NORMAL", "HARD", "MANIAC" };
            Toggle[] tabs = new Toggle[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                tabs[i] = BuildDifficultyTab(tabBar.transform, labels[i], group);

            // ⑥⑦⑧ 키 안내 · 입장 · 취소
            TextMeshProUGUI keyGuide = EntryUIBuilder.CreateTMP(footer.transform, "KeyGuide", "W/S 스테이지 · A/D 난이도", 22f);
            keyGuide.alignment = TextAlignmentOptions.Left;
            keyGuide.color = MutedColor;
            SetRect(keyGuide.gameObject, new Vector2(0f, 0f), new Vector2(0.4f, 0f), new Vector2(16f, 0f), new Vector2(0f, 72f), stretchBottom: true);

            (Button enter, _) = EntryUIBuilder.CreateButton(footer.transform, "EnterButton", "입장 [SPACE]", 24f, AccentColor);
            SetRect(enter.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(360f, 72f));

            (Button cancel, _) = EntryUIBuilder.CreateButton(footer.transform, "CancelButton", "취소 [ESC]", 24f, BoxColor);
            SetRect(cancel.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-200f, 36f), new Vector2(360f, 72f));

            return (tabs, keyGuide, enter, cancel);
        }

        private static Toggle BuildDifficultyTab(Transform parent, string label, ToggleGroup group)
        {
            GameObject go = new GameObject($"Tab_{label}", typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);

            Image background = go.GetComponent<Image>();
            background.sprite = EntryUIBuilder.UISprite;
            background.type = Image.Type.Sliced;
            background.color = TabColor;

            // 선택 표시. Toggle.graphic은 켜진 동안에만 보인다.
            GameObject onGo = new GameObject("Selected", typeof(RectTransform), typeof(Image));
            onGo.transform.SetParent(go.transform, false);
            EntryUIBuilder.Fill(onGo.GetComponent<RectTransform>());
            Image onImg = onGo.GetComponent<Image>();
            onImg.sprite = EntryUIBuilder.UISprite;
            onImg.type = Image.Type.Sliced;
            onImg.color = AccentColor;
            onImg.raycastTarget = false;

            TextMeshProUGUI text = EntryUIBuilder.CreateTMP(go.transform, "Label", label, 24f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            EntryUIBuilder.Fill(text.rectTransform);

            Toggle toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = onImg;
            toggle.group = group;
            toggle.isOn = false;

            return toggle;
        }

        // ── 공통 ──────────────────────────────────────────────

        // EntryUIBuilder의 배치 헬퍼로는 표현 못 하는 앵커 조합(좌측 고정 폭, 겹침 등)을 위한 로컬 헬퍼.
        // stretch면 offsetMin/Max로, 아니면 anchoredPosition/sizeDelta로 잡는다.
        private static void SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 a, Vector2 b, bool stretch = false, bool stretchBottom = false)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;

            if (stretch)
            {
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = a;
                rt.offsetMax = b;
                return;
            }

            if (stretchBottom)
            {
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = a;
                rt.offsetMax = new Vector2(b.x, a.y + b.y);
                return;
            }

            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = a;
            rt.sizeDelta = b;
        }

        private static bool ConfirmOverwrite(string path, string label)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return true;

            return EditorUtility.DisplayDialog(
                $"{label} 덮어쓰기",
                $"{path} 가 이미 있습니다.\n덮어쓰면 인스펙터에서 손본 배치·색이 사라집니다.",
                "덮어쓰기", "취소");
        }
    }
}
