using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 캐릭터 선택 페이지(Page_Select)를 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Page Select
    ///
    /// 슬롯 카드는 <c>Assets/Prefabs/UI/CharacterSlot.prefab</c>을 인스턴스로 6장 배치하므로,
    /// Create Character Slot Prefab을 먼저 실행해야 한다.
    ///
    /// 모델 프리뷰가 보이려면 카메라가 RenderTexture로 그려줘야 해서 <c>CharacterStage</c>
    /// 리그와 <c>RT_CharacterPreview</c>도 함께 만든다(없을 때만). 스테이지는 다른 씬 오브젝트와
    /// 섞이지 않게 원점에서 멀리 떨어뜨려 둔다 — 나중에 전용 레이어로 가르는 편이 확실하다.
    /// </summary>
    public static class CharacterSelectPageGenerator
    {
        private const string UndoLabel = "Create Page Select";
        private const string SlotPrefabPath = "Assets/Prefabs/UI/CharacterSlot.prefab";

        private const int SlotCount = 6;
        private const float SlotSpacing = 12f;

        // 우측 패널. 화면 오른쪽에 붙고 위아래로 늘어난다.
        private const float PanelWidth = 460f;
        private const float PanelRightMargin = 30f;
        private const float PanelVerticalMargin = 40f;
        private const float PanelPadding = 16f;

        private const float TitleHeight = 90f;
        private const float NoteHeight = 32f;

        private static readonly Color BackgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        private static readonly Color PanelColor = new Color(0.09f, 0.11f, 0.14f, 0.75f);
        private static readonly Color ButtonColor = new Color(0.18f, 0.21f, 0.27f, 1f);
        private static readonly Color MutedTextColor = new Color(0.62f, 0.66f, 0.74f, 1f);

        [MenuItem("Tools/ProjectS/Create Page Select")]
        public static void CreatePageSelect()
        {
            GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
            if (slotPrefab == null)
            {
                EditorUtility.DisplayDialog("CharacterSlot 프리팹이 없습니다",
                    $"{SlotPrefabPath} 를 찾지 못했습니다.\n\n" +
                    "Tools ▸ ProjectS ▸ Create Character Slot Prefab 을 먼저 실행하세요.", "확인");
                return;
            }

            EntryUIBuilder.EnsureEventSystem(UndoLabel);
            Canvas canvas = EntryUIBuilder.EnsureCanvas(UndoLabel);
            EnsureBackground(canvas.transform);

            Transform existing = canvas.transform.Find("Page_Select");
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Page_Select 다시 만들기",
                    "이미 Page_Select가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·색이 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            RenderTexture preview = EntryUIBuilder.EnsurePreviewTexture();
            EntryUIBuilder.EnsureCharacterStage(preview, UndoLabel);

            GameObject page = BuildPage(canvas.transform, slotPrefab, preview);

            Undo.RegisterCreatedObjectUndo(page, UndoLabel);
            Selection.activeGameObject = page;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[CharacterSelectPageGenerator] Page_Select 생성 완료. 슬롯 6칸은 빈 상태로 시작합니다.");
        }

        // ── 페이지 ─────────────────────────────────────────────────

        private static GameObject BuildPage(Transform canvas, GameObject slotPrefab, RenderTexture preview)
        {
            GameObject page = new GameObject("Page_Select", typeof(RectTransform));
            page.transform.SetParent(canvas, false);
            EntryUIBuilder.Fill(page.GetComponent<RectTransform>());

            RawImage viewport = BuildModelViewport(page.transform, preview);
            RectTransform panel = BuildRightPanel(page.transform);

            BuildTitleGroup(panel);
            RectTransform list = BuildSlotList(panel);
            BuildSlotNote(panel);

            CharacterSlotView[] slots = new CharacterSlotView[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, list);
                instance.name = $"Slot_{i}";
                slots[i] = instance.GetComponent<CharacterSlotView>();
            }

            (Button quit, Button option, TextMeshProUGUI version) = BuildBottomBar(page.transform);

            CharacterSelectPageView view = page.AddComponent<CharacterSelectPageView>();
            EntryUIBuilder.Wire(view,
                ("modelViewport", viewport),
                ("quitButton", quit),
                ("optionButton", option),
                ("versionText", version));
            EntryUIBuilder.WireList(view, "slots", slots);

            return page;
        }

        private static RawImage BuildModelViewport(Transform parent, RenderTexture preview)
        {
            GameObject go = new GameObject("ModelViewport", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.Fill(rt);
            rt.offsetMin = new Vector2(60f, 120f);
            rt.offsetMax = new Vector2(-(PanelWidth + PanelRightMargin + 30f), -60f);

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = preview;
            raw.color = Color.white;
            raw.raycastTarget = false;
            return raw;
        }

        private static RectTransform BuildRightPanel(Transform parent)
        {
            GameObject go = new GameObject("RightPanel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            // 레이아웃 그룹을 일부러 쓰지 않는다. 타이틀은 위, 노트는 아래에 고정하고
            // 목록만 사이를 채우게 하면 배치가 씬에 그대로 남아 에디터에서 최종 모습이 보인다.
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, -PanelVerticalMargin * 2f);
            rt.anchoredPosition = new Vector2(-PanelRightMargin, 0f);

            Image img = go.GetComponent<Image>();
            img.color = PanelColor;
            img.raycastTarget = false;
            return rt;
        }

        private static void BuildTitleGroup(RectTransform panel)
        {
            GameObject group = new GameObject("TitleGroup", typeof(RectTransform));
            group.transform.SetParent(panel, false);
            EntryUIBuilder.StretchTop(group.GetComponent<RectTransform>(), -TitleHeight, 0f, PanelPadding);

            TextMeshProUGUI en = EntryUIBuilder.CreateTMP(group.transform, "TitleEn", "PLAY CHARACTER", 34f);
            en.alignment = TextAlignmentOptions.MidlineLeft;
            en.color = Color.white;
            EntryUIBuilder.StretchTop(en.rectTransform, -48f, -4f);

            TextMeshProUGUI ko = EntryUIBuilder.CreateTMP(group.transform, "TitleKo", "플레이 캐릭터 선택", 20f);
            ko.alignment = TextAlignmentOptions.MidlineLeft;
            ko.color = MutedTextColor;
            EntryUIBuilder.StretchTop(ko.rectTransform, -82f, -50f);
        }

        // 슬롯 6칸이 쌓이는 세로 목록. 스크롤 없이 다 들어가므로 Mask·ScrollRect·ContentSizeFitter를
        // 붙이지 않는다 — 붙이면 카드 밖으로 나가는 요소가 잘리고 리빌드가 늘어난다.
        private static RectTransform BuildSlotList(RectTransform panel)
        {
            GameObject go = new GameObject("SlotList", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(panel, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            EntryUIBuilder.Fill(rt);
            rt.offsetMin = new Vector2(PanelPadding, NoteHeight + 14f);
            rt.offsetMax = new Vector2(-PanelPadding, -(TitleHeight + 10f));

            VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = SlotSpacing;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;       // 카드의 LayoutElement.preferredHeight(120)를 따른다
            vlg.childForceExpandHeight = false;  // 남는 공간을 카드가 나눠 갖지 않게(높이 고정 유지)

            return rt;
        }

        private static void BuildSlotNote(RectTransform panel)
        {
            TextMeshProUGUI note = EntryUIBuilder.CreateTMP(panel, "SlotNote", $"※ 캐릭터 슬롯 총 {SlotCount}개", 18f);
            note.alignment = TextAlignmentOptions.MidlineLeft;
            note.color = MutedTextColor;
            EntryUIBuilder.StretchBottom(note.rectTransform, 8f, 8f + NoteHeight, PanelPadding);
        }

        // 게임 시작 버튼은 카드 안으로 갔으므로 하단은 종료·환경설정·버전 셋만 남는다.
        private static (Button quit, Button option, TextMeshProUGUI version) BuildBottomBar(Transform parent)
        {
            GameObject bar = new GameObject("BottomBar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(parent, false);

            RectTransform rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(60f, 40f);
            rt.sizeDelta = new Vector2(760f, 56f);

            HorizontalLayoutGroup hlg = bar.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = false;      // 각 버튼의 sizeDelta를 그대로 쓴다
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandHeight = false;

            Button quit = EntryUIBuilder.CreateButton(bar.transform, "QuitButton", "게임 종료", 22f, ButtonColor).button;
            quit.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 56f);

            Button option = EntryUIBuilder.CreateButton(bar.transform, "OptionButton", "환경설정", 22f, ButtonColor).button;
            option.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 56f);

            TextMeshProUGUI version = EntryUIBuilder.CreateTMP(bar.transform, "VersionText", "v0.1.0", 18f);
            version.alignment = TextAlignmentOptions.MidlineLeft;
            version.color = MutedTextColor;
            version.rectTransform.sizeDelta = new Vector2(220f, 56f);

            return (quit, option, version);
        }

        // ── 씬 구성 요소(없을 때만 만든다) ──────────────────────────

        private static void EnsureBackground(Transform canvas)
        {
            if (canvas.Find("Background") != null) return;

            Image bg = EntryUIBuilder.CreateFullScreenImage(canvas, "Background", BackgroundColor, false);
            bg.transform.SetAsFirstSibling();   // 페이지들보다 뒤에
        }

    }
}
