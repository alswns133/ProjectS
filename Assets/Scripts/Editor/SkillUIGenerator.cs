using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using ProjectS.UI.Framework;
using ProjectS.Managers;
using ProjectS.Skills;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 스킬 등록 UI(<see cref="SkillContextMenu"/> 우클릭 슬롯 메뉴 · <see cref="SkillTooltip"/> hover 설명창)를
    /// 계층·배선까지 한 번에 만드는 에디터 툴. 최상위 렌더링을 위해 OverlayCanvas(없으면 생성)에 붙인다.
    /// <see cref="ItemContextMenuGenerator"/>와 같은 방식이며, 툴팁은 데이터 UI가 확정되기 전 임시 버전이다.
    ///
    /// 메뉴: Tools ▸ ProjectS ▸ Create Skill Context Menu / Create Skill Tooltip
    /// </summary>
    public static class SkillUIGenerator
    {
        private const string UndoName = "Create Skill UI";
        private static TMP_FontAsset s_font;

        private static readonly Color ButtonBlue = new Color(0.25f, 0.45f, 0.85f, 1f);
        private static readonly Color BoxColor = new Color(0.13f, 0.14f, 0.18f, 0.96f);

        [MenuItem("Tools/ProjectS/Create Skill Context Menu")]
        public static void CreateSkillContextMenu()
        {
            s_font = ResolveKoreanFont();
            EnsureEventSystem();
            Canvas overlay = GetOrCreateOverlayCanvas();

            SkillContextMenu existing = Object.FindAnyObjectByType<SkillContextMenu>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[SkillUIGenerator] 이미 SkillContextMenu가 있어 선택만 했습니다. 다시 만들려면 지우고 재실행하세요.");
            }
            else
            {
                BuildContextMenu(overlay.transform);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SkillUIGenerator] SkillContextMenu 생성·배선 완료. OverlayCanvas가 UIManager 밖이면 자식으로 옮기세요. " +
                "★ 씬을 저장해야 반영됩니다(Ctrl+S).");
        }

        [MenuItem("Tools/ProjectS/Create Skill Tooltip")]
        public static void CreateSkillTooltip()
        {
            s_font = ResolveKoreanFont();
            EnsureEventSystem();
            Canvas overlay = GetOrCreateOverlayCanvas();

            SkillTooltip existing = Object.FindAnyObjectByType<SkillTooltip>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[SkillUIGenerator] 이미 SkillTooltip이 있어 선택만 했습니다. 다시 만들려면 지우고 재실행하세요.");
            }
            else
            {
                BuildTooltip(overlay.transform);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[SkillUIGenerator] SkillTooltip(임시) 생성·배선 완료. ★ 씬을 저장해야 반영됩니다(Ctrl+S).");
        }

        // ── 우클릭 컨텍스트 메뉴(블로커 + 세로 레이아웃 박스 + 슬롯 버튼 4개) ──
        private static void BuildContextMenu(Transform canvasParent)
        {
            GameObject root = CreateFullScreen(canvasParent, "SkillContextMenu");
            SkillContextMenu menu = root.AddComponent<SkillContextMenu>();

            // 전체화면 투명 블로커(바깥 클릭 시 닫기).
            GameObject blockerGo = CreateFullScreen(root.transform, "BackgroundBlocker");
            Image blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0f);
            Button blocker = blockerGo.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.targetGraphic = blockerImg;

            // 메뉴 박스: 버튼 수만큼 높이가 맞는 세로 레이아웃.
            GameObject boxGo = new GameObject("MenuBox",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            boxGo.transform.SetParent(root.transform, false);

            RectTransform box = boxGo.GetComponent<RectTransform>();
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(160f, 0f);

            Image boxImg = boxGo.GetComponent<Image>();
            boxImg.type = Image.Type.Sliced;
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            boxImg.color = BoxColor;

            VerticalLayoutGroup vlg = boxGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = boxGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // 단축키 1~SlotCount 버튼. 배열 순서(인덱스 0 = 슬롯 1)로 컴포넌트가 등록한다.
            var buttons = new Button[SkillState.SlotCount];
            for (int i = 0; i < buttons.Length; i++)
                buttons[i] = CreateLayoutButton(box, $"Slot{i + 1}Button", $"슬롯 {i + 1}", ButtonBlue);

            SerializedObject so = new SerializedObject(menu);
            SetProp(so, "menuRect", box);
            SetProp(so, "backgroundBlocker", blocker);
            WireArray(so, "slotButtons", buttons);
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = root;
        }

        // ── 임시 툴팁(세로 레이아웃 박스: 아이콘 · 이름 · 레벨 · 설명) ──
        private static void BuildTooltip(Transform canvasParent)
        {
            // 루트 = 이동하는 툴팁 박스(전체화면이 아니라 커서로 옮겨지는 박스). SkillTooltip.tooltipRect = self.
            GameObject root = new GameObject("SkillTooltip",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            root.transform.SetParent(canvasParent, false);

            RectTransform rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.sizeDelta = new Vector2(300f, 0f);

            Image bg = root.GetComponent<Image>();
            bg.type = Image.Type.Sliced;
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            bg.color = new Color(0.10f, 0.11f, 0.15f, 0.97f);
            bg.raycastTarget = false;   // 툴팁은 클릭을 막지 않는다

            VerticalLayoutGroup vlg = root.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter fitter = root.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            SkillTooltip tooltip = root.AddComponent<SkillTooltip>();

            // 아이콘(64x64, 레이아웃 상단).
            GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(root.transform, false);
            Image icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.enabled = false;
            LayoutElement iconLe = iconGo.GetComponent<LayoutElement>();
            iconLe.preferredWidth = 64f; iconLe.preferredHeight = 64f;
            iconLe.minHeight = 64f;

            TextMeshProUGUI nameText = CreateTMP(root.transform, "NameText", "스킬 이름", 24f);
            nameText.color = Color.white;
            nameText.raycastTarget = false;

            TextMeshProUGUI levelText = CreateTMP(root.transform, "LevelText", "Lv. 0 / 5", 18f);
            levelText.color = new Color(0.7f, 0.85f, 1f, 1f);
            levelText.raycastTarget = false;

            // 설명 묶음(SkillTooltip이 통째로 켜고 끈다).
            GameObject descSection = new GameObject("DescSection", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            descSection.transform.SetParent(root.transform, false);
            VerticalLayoutGroup dvlg = descSection.GetComponent<VerticalLayoutGroup>();
            dvlg.childControlWidth = true; dvlg.childControlHeight = true; dvlg.childForceExpandWidth = true; dvlg.childForceExpandHeight = false;
            ContentSizeFitter dcsf = descSection.GetComponent<ContentSizeFitter>();
            dcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI descText = CreateTMP(descSection.transform, "DescText", "스킬 설명", 18f);
            descText.color = new Color(0.85f, 0.85f, 0.9f, 1f);
            descText.raycastTarget = false;

            SerializedObject so = new SerializedObject(tooltip);
            SetProp(so, "tooltipRect", rootRt);
            SetProp(so, "nameText", nameText);
            SetProp(so, "icon", icon);
            SetProp(so, "levelText", levelText);
            SetProp(so, "descSection", descSection);
            SetProp(so, "descText", descText);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Awake에서 자기 숨김하므로 켠 채 둔다(ItemTooltip과 동일).
            Selection.activeGameObject = root;
        }

        // ── 유틸(ItemContextMenuGenerator와 동일 패턴) ──

        private static Canvas GetOrCreateOverlayCanvas()
        {
            foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.name == "OverlayCanvas") return c;

            GameObject go = new GameObject("OverlayCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            UIManager um = Object.FindAnyObjectByType<UIManager>();
            if (um != null) go.transform.SetParent(um.transform, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[SkillUIGenerator] OverlayCanvas(Sort Order 100) 생성.");
            return canvas;
        }

        private static Button CreateLayoutButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 40f);

            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.minHeight = 40f;

            Image img = go.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(go.transform, "Text", label, 20f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            FillParent(text.rectTransform);

            return btn;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            GameObject es = new GameObject("EventSystem", typeof(EventSystem));
            var newModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newModule != null) es.AddComponent(newModule);
            else es.AddComponent<StandaloneInputModule>();

            Undo.RegisterCreatedObjectUndo(es, UndoName);
        }

        private static GameObject CreateFullScreen(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            FillParent(go.GetComponent<RectTransform>());
            return go;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, string content, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            if (s_font != null) tmp.font = s_font;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        private static TMP_FontAsset ResolveKoreanFont()
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (string guid in guids)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font.name.Contains("Paperlogy")) return font;
            }
            return TMP_Settings.defaultFontAsset;
        }

        private static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void SetProp(SerializedObject so, string prop, Object value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = value;
        }

        // 배열 [SerializeField] 참조를 SerializedObject로 채운다(slotButtons처럼).
        private static void WireArray(SerializedObject so, string prop, Object[] values)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p == null) return;

            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
