// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 던전 입장 창 전체를 프리팹으로 만드는 에디터 툴. 파티 슬롯까지 함께 넣는다.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Dungeon Entry Popup
    ///
    /// 기획서 3-1의 UI_DG_001~006 + 입장/닫기에 파티 슬롯(docs/PARTY_WINDOW_UI.md §2)을 더한 구성이며,
    /// <see cref="DungeonEntryPopup"/>의 인스펙터 연결까지 끝낸다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>창이 곧 화면이다.</b> 가운데 상자를 띄우지 않고 화면 전체를 덮는다 — 그래서 닫는 수단이
    /// 하단에 명시돼 있어야 하고, 우상단 ✕는 만들지 않는다(둘 다 같은 <c>RequestClose</c>로 간다).
    /// </para>
    /// <para>
    /// <b>목록 : 사이드 비율(66:34)을 픽셀이 아니라 앵커로 잡는다.</b> 목업에서 정한 비율이며,
    /// 픽셀 폭으로 박으면 해상도나 창 크기가 바뀔 때 비율이 무너진다.
    /// </para>
    /// <para>
    /// <b>한글 폰트를 직접 지정한다.</b> TMP 기본값(LiberationSans)에는 한글 글리프가 없어,
    /// 그대로 두면 만들어 놓고 실행했을 때 모든 한글이 □로 나온다.
    /// </para>
    /// <para>
    /// <b>기존 프리팹을 덮어쓴다.</b> 경로가 같으므로 GUID는 유지되어 이 프리팹을 쓰는 씬들의 참조는
    /// 끊기지 않는다. 다만 내부 fileID가 새로 발급되어 <b>씬 인스턴스에 걸어 둔 개별 오버라이드는
    /// 어긋난다</b> — 실행 전 확인 대화상자를 띄우는 이유다.
    /// </para>
    /// <para>
    /// 에피소드 카드와 파티 슬롯 바는 프리팹 인스턴스로 문다. 이 툴이 그리는 것은 창의 뼈대뿐이다.
    /// 스프라이트·미리보기 그림은 넣지 않는다 — 아트가 붙는 자리라 다시 실행할 때마다 덮어쓴다.
    /// </para>
    /// (2026-08-31 TH)
    /// </remarks>
    public static class DungeonEntryUIGenerator
    {
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/DungeonEntryPopup.prefab";
        private const string EpisodePrefabPath = PrefabFolder + "/EpisodeEntry.prefab";
        private const string SlotBarPrefabPath = PrefabFolder + "/PartySlotBar.prefab";

        // 이 창의 에피소드 카드가 쓰는 폰트와 같은 것을 쓴다.
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";

        // 던전 결과창(40)·사망 팝업(50)보다 아래. 초대 목록 팝업(45)이 이 위에 뜬다.
        private const int SortingOrder = 30;

        private const float PadX = 80f;
        private const float PadTop = 40f;
        private const float PadBottom = 40f;
        private const float HeaderHeight = 96f;
        private const float TabRowHeight = 56f;
        private const float FooterHeight = 88f;
        private const float Gap = 24f;

        // 목업의 목록:사이드 = 66:34 (1fr : 200px, gap 12).
        private const float SideRatio = 0.34f;
        private const float TrackerHeaderHeight = 44f;
        private const float TrackerBodyHeight = 120f;
        private const float PreviewCaptionHeight = 44f;
        private const int DifficultyTabCount = 3;
        private const float TabWidth = 140f;

        private static readonly Color WindowColor = new Color32(0x0D, 0x12, 0x1B, 0xFF);
        private static readonly Color PanelColor = new Color32(0x1A, 0x21, 0x30, 0xFF);
        private static readonly Color AccentColor = new Color32(0x2F, 0x5F, 0xD0, 0xFF);
        private static readonly Color CyanColor = new Color32(0x4F, 0xD8, 0xE8, 0xFF);
        private static readonly Color MutedColor = new Color32(0x8D, 0x99, 0xAC, 0xFF);
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);

        [MenuItem("Tools/ProjectS/Create Dungeon Entry Popup")]
        public static void Create()
        {
            if (!Confirm()) return;

            EnsureFolder();

            GameObject temp = Build();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);

            AssetDatabase.SaveAssets();
            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);

            Debug.Log($"[DungeonEntryUIGenerator] 던전 입장 창을 만들었다: {PrefabPath}\n" +
                      "스프라이트·미리보기 그림은 비어 있으니 프리팹에서 채운다.");
        }

        /// <summary>덮어쓸지 물어본다. 이 프리팹은 여러 씬이 참조하고 있어 조용히 새로 뽑으면 안 된다.</summary>
        private static bool Confirm()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return true;

            return EditorUtility.DisplayDialog(
                "던전 입장 창을 새로 만든다",
                $"{PrefabPath} 를 덮어쓴다.\n\n" +
                "· 경로가 같아 GUID는 유지되므로 이 프리팹을 쓰는 씬의 참조는 끊기지 않는다.\n" +
                "· 다만 내부 ID가 새로 발급되어 씬 인스턴스의 개별 오버라이드는 어긋난다.\n" +
                "· 지금 프리팹의 아트와 수동 배치는 사라진다(git으로 되돌릴 수 있다).\n\n계속할까?",
                "덮어쓰기",
                "취소");
        }

        private static GameObject Build()
        {
            // ── 화면 루트(자기 Canvas) ──────────────────────────────────
            GameObject go = new("DungeonEntryPopup",
                typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(DungeonEntryPopup));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform win = (RectTransform)go.transform;
            Fill(win);

            // 창이 곧 화면이라 따로 딤을 깔지 않는다 — 배경 한 장이 그 역할까지 한다.
            CreateFilledImage(win, "Background", WindowColor, blocksRaycast: true);

            // ── ① 헤더 ─────────────────────────────────────────────────
            TextMeshProUGUI title = CreateText(win, "TitleText", "폐기된 연구시설", 36f);
            title.alignment = TextAlignmentOptions.BottomLeft;
            StretchTop(title.rectTransform, PadTop, PadTop + 48f, PadX, PadX + 500f);

            TextMeshProUGUI progress = CreateText(win, "ProgressText", "0 / 8", 16f);
            progress.color = MutedColor;
            progress.alignment = TextAlignmentOptions.TopLeft;
            StretchTop(progress.rectTransform, PadTop + 50f, PadTop + 74f, PadX, PadX + 500f);

            (Button rewardButton, TextMeshProUGUI rewardLabel) =
                CreateButton(win, "RewardButton", "보상", 16f, PanelColor);
            rewardLabel.color = TextColor;
            AnchorTopRight(rewardButton.GetComponent<RectTransform>(), PadX, PadTop + 8f, 140f, 44f);

            // ── ⑤ 난이도 탭 ────────────────────────────────────────────
            float tabTop = PadTop + HeaderHeight;
            RectTransform tabRow = CreateRect(win, "DifficultyTabs");
            StretchTop(tabRow, tabTop, tabTop + TabRowHeight, PadX, PadX);

            ToggleGroup group = tabRow.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            List<Toggle> tabs = new();
            string[] tabNames = { "노말", "하드", "매니악" };
            for (int i = 0; i < DifficultyTabCount; i++)
            {
                Toggle tab = CreateTab(tabRow, $"DifficultyTab{i:00}", tabNames[i], group, on: i == 0);
                AnchorLeft(tab.GetComponent<RectTransform>(), i * (TabWidth + 8f), TabWidth);
                tabs.Add(tab);
            }

            // ── ④ 에피소드 목록 · 사이드 ────────────────────────────────
            float bodyTop = tabTop + TabRowHeight + Gap;
            float bodyBottom = PadBottom + FooterHeight + Gap;

            RectTransform body = CreateRect(win, "Body");
            StretchMiddle(body, bodyTop, bodyBottom, PadX, PadX);

            // 비율을 앵커로 잡는다. 픽셀 폭으로 박으면 해상도가 바뀔 때 비율이 무너진다.
            RectTransform listArea = CreateRect(body, "EpisodeList");
            listArea.anchorMin = new Vector2(0f, 0f);
            listArea.anchorMax = new Vector2(1f - SideRatio, 1f);
            listArea.pivot = new Vector2(0.5f, 0.5f);
            listArea.offsetMin = Vector2.zero;
            listArea.offsetMax = new Vector2(-Gap * 0.5f, 0f);

            RectTransform episodeContent = CreateScroll(listArea, out ScrollRect _);

            RectTransform side = CreateRect(body, "Side");
            side.anchorMin = new Vector2(1f - SideRatio, 0f);
            side.anchorMax = new Vector2(1f, 1f);
            side.pivot = new Vector2(0.5f, 0.5f);
            side.offsetMin = new Vector2(Gap * 0.5f, 0f);
            side.offsetMax = Vector2.zero;

            // ③ 퀘스트 트래커 — 미리보기 위. 접어도 손잡이는 남는다(통째로 끄면 다시 열 방법이 없다).
            RectTransform trackerHeader = CreateRect(side, "TrackerHeader");
            StretchTop(trackerHeader, 0f, TrackerHeaderHeight, 0f, 0f);
            CreateFilledImage(trackerHeader, "Background", PanelColor, blocksRaycast: true);

            (Button trackerToggle, TextMeshProUGUI trackerToggleLabel) =
                CreateButton(trackerHeader, "TrackerToggleButton", "◆ 퀘스트   ▲", 15f, new Color(0f, 0f, 0f, 0f));
            trackerToggleLabel.color = CyanColor;
            trackerToggleLabel.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(trackerToggle.GetComponent<RectTransform>());
            trackerToggleLabel.rectTransform.offsetMin = new Vector2(14f, 0f);
            trackerToggleLabel.rectTransform.offsetMax = new Vector2(-14f, 0f);

            RectTransform trackerBody = CreateRect(side, "TrackerBody");
            StretchTop(trackerBody, TrackerHeaderHeight, TrackerHeaderHeight + TrackerBodyHeight, 0f, 0f);
            CreateFilledImage(trackerBody, "Background", PanelColor, blocksRaycast: false);

            TextMeshProUGUI trackerText = CreateText(trackerBody, "TrackerText", "· 진행 중 퀘스트 없음", 14f);
            trackerText.color = MutedColor;
            trackerText.alignment = TextAlignmentOptions.TopLeft;
            Fill(trackerText.rectTransform);
            trackerText.rectTransform.offsetMin = new Vector2(14f, 12f);
            trackerText.rectTransform.offsetMax = new Vector2(-14f, -12f);

            Image preview = CreateFilledImage(side, "PreviewImage", PanelColor, blocksRaycast: false);
            StretchMiddle(preview.rectTransform,
                          TrackerHeaderHeight + TrackerBodyHeight + Gap, PreviewCaptionHeight, 0f, 0f);

            TextMeshProUGUI previewCaption = CreateText(side, "PreviewCaption", "스테이지 설명", 14f);
            previewCaption.color = MutedColor;
            previewCaption.alignment = TextAlignmentOptions.TopLeft;
            StretchBottom(previewCaption.rectTransform, 0f, PreviewCaptionHeight - 8f, 0f, 0f);

            // ── ⑥⑦⑧ + 파티 슬롯 ───────────────────────────────────────
            RectTransform footer = CreateRect(win, "Footer");
            StretchBottom(footer, PadBottom, PadBottom + FooterHeight, PadX, PadX);

            PartySlotBar slotBar = InstantiateSlotBar(footer);

            TextMeshProUGUI keyGuide = CreateText(footer, "KeyGuideText", "W/S 에피소드   A/D 난이도", 13f);
            keyGuide.color = MutedColor;
            keyGuide.alignment = TextAlignmentOptions.MidlineRight;
            AnchorRight(keyGuide.rectTransform, 380f, 320f);

            (Button cancelButton, TextMeshProUGUI cancelLabel) =
                CreateButton(footer, "CancelButton", "닫기   ESC", 16f, PanelColor);
            cancelLabel.color = TextColor;
            AnchorRight(cancelButton.GetComponent<RectTransform>(), 190f, 180f);

            (Button enterButton, TextMeshProUGUI enterLabel) =
                CreateButton(footer, "EnterButton", "입장   SPACE", 17f, AccentColor);
            enterLabel.color = Color.white;
            AnchorRight(enterButton.GetComponent<RectTransform>(), 0f, 180f);
            enterButton.interactable = false;   // 던전을 고르기 전에는 못 누른다

            // ── 인스펙터 연결 ───────────────────────────────────────────
            DungeonEntryPopup popup = go.GetComponent<DungeonEntryPopup>();
            GameObject episodeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EpisodePrefabPath);
            EpisodeEntryView episodePrefab = episodeAsset != null ? episodeAsset.GetComponent<EpisodeEntryView>() : null;

            if (episodePrefab == null)
            {
                Debug.LogWarning($"[DungeonEntryUIGenerator] {EpisodePrefabPath} 를 찾지 못해 episodeEntryPrefab이 비어 있다.");
            }

            Wire(popup,
                ("titleText", title),
                ("progressText", progress),
                ("rewardButton", rewardButton),
                ("episodeListRoot", episodeContent),
                ("episodeEntryPrefab", episodePrefab),
                ("previewImage", preview),
                ("previewCaption", previewCaption),
                ("trackerHeader", trackerHeader.gameObject),
                ("trackerBody", trackerBody.gameObject),
                ("trackerToggleButton", trackerToggle),
                ("trackerToggleLabel", trackerToggleLabel),
                ("trackerText", trackerText),
                ("partySlots", slotBar),
                ("keyGuideText", keyGuide),
                ("enterButton", enterButton),
                ("cancelButton", cancelButton));

            Object[] tabObjects = new Object[tabs.Count];
            for (int i = 0; i < tabs.Count; i++) tabObjects[i] = tabs[i];
            WireList(popup, "difficultyTabs", tabObjects);

            go.SetActive(false);   // 팝업은 닫힌 채로 시작한다(UIManager가 연다)
            return go;
        }

        /// <summary>파티 슬롯 바를 프리팹 인스턴스로 문다. 없으면 경고만 하고 비워 둔다.</summary>
        private static PartySlotBar InstantiateSlotBar(Transform footer)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(SlotBarPrefabPath);
            if (asset == null)
            {
                Debug.LogWarning($"[DungeonEntryUIGenerator] {SlotBarPrefabPath} 가 없어 파티 슬롯을 넣지 못했다.\n" +
                                 "Tools ▸ ProjectS ▸ Create Party Slot Bar 를 먼저 실행한 뒤 다시 만든다.");
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, footer);
            instance.name = "PartySlotBar";

            RectTransform rt = (RectTransform)instance.transform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            return instance.GetComponent<PartySlotBar>();
        }

        // ── 만들기 도우미 ────────────────────────────────────────────────

        /// <summary>세로 스크롤 목록을 만들고 카드가 쌓일 Content를 돌려준다.</summary>
        private static RectTransform CreateScroll(Transform parent, out ScrollRect scroll)
        {
            GameObject scrollGo = new("ScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            Fill((RectTransform)scrollGo.transform);
            scroll = scrollGo.GetComponent<ScrollRect>();

            // RectMask2D는 Mask+Image 조합보다 가볍고 추가 드로우콜이 없다.
            GameObject viewportGo = new("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            RectTransform viewport = (RectTransform)viewportGo.transform;
            Fill(viewport);

            RectTransform content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;    // 카드 높이는 프리팹이 정한다
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            scroll.content = content;

            return content;
        }

        private static Toggle CreateTab(Transform parent, string name, string label, ToggleGroup group, bool on)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);

            Image bg = go.GetComponent<Image>();
            bg.sprite = UISprite;
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;

            Image onMark = CreateFilledImage(go.transform, "On", CyanColor, blocksRaycast: false);
            onMark.sprite = UISprite;
            onMark.type = Image.Type.Sliced;

            TextMeshProUGUI text = CreateText(go.transform, "Label", label, 16f);
            text.alignment = TextAlignmentOptions.Center;
            Fill(text.rectTransform);

            Toggle toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = onMark;      // 켜졌을 때만 보이는 그래픽
            toggle.group = group;
            toggle.isOn = on;

            return toggle;
        }

        // ── 공용 UI 헬퍼 ────────────────────────────────────────────────
        // 예전에는 EntryUIBuilder를 함께 썼지만 그 파일이 정리되면서 사라져, 이 툴이 쓰는 것만 여기 둔다.

        /// <summary>
        /// 이 창이 쓰는 한글 폰트. 못 찾으면 TMP 기본값으로 떨어지는데, 그때는 한글이 전부 □로 나온다.
        /// </summary>
        private static TMP_FontAsset KoreanFont
        {
            get
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (font != null) return font;

                Debug.LogWarning($"[DungeonEntryUIGenerator] 한글 폰트를 찾지 못했다: {FontPath}\n" +
                                 "한글이 □로 나오면 프리팹에서 폰트를 직접 지정한다.");
                return TMP_Settings.defaultFontAsset;
            }
        }

        /// <summary>버튼·탭 배경에 쓰는 유니티 기본 9슬라이스 스프라이트.</summary>
        private static Sprite UISprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = TextColor;
            text.raycastTarget = false;

            TMP_FontAsset font = KoreanFont;
            if (font != null) text.font = font;
            return text;
        }

        /// <summary>배경 이미지 + 가운데 라벨을 가진 버튼. RectTransform은 호출부가 잡는다.</summary>
        private static (Button button, TextMeshProUGUI label) CreateButton(
            Transform parent, string name, string text, float size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.sprite = UISprite;
            image.type = Image.Type.Sliced;
            image.color = color;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI label = CreateText(go.transform, "Label", text, size);
            label.alignment = TextAlignmentOptions.Center;
            Fill(label.rectTransform);

            return (button, label);
        }

        /// <summary>부모를 가득 채우는 이미지(배경·패널).</summary>
        private static Image CreateFilledImage(Transform parent, string name, Color color, bool blocksRaycast)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = blocksRaycast;
            Fill((RectTransform)go.transform);
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        private static void StretchTop(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void StretchBottom(RectTransform rt, float bottom, float top, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, top);
        }

        private static void StretchMiddle(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorLeft(RectTransform rt, float left, float width)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
            rt.sizeDelta = new Vector2(width, 0f);
        }

        private static void AnchorRight(RectTransform rt, float right, float width)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, 0f);
        }

        private static void AnchorTopRight(RectTransform rt, float right, float top, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(PrefabFolder)) return;

            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        }

        // private [SerializeField] 단일 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[DungeonEntryUIGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없다. 이름이 바뀌었는지 확인한다.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireList(Component comp, string prop, params Object[] elements)
        {
            SerializedObject so = new(comp);
            SerializedProperty p = so.FindProperty(prop);

            if (p != null && p.isArray)
            {
                p.arraySize = elements.Length;
                for (int i = 0; i < elements.Length; i++)
                {
                    p.GetArrayElementAtIndex(i).objectReferenceValue = elements[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
