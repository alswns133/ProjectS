using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using ProjectS.Managers;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 아이템 우클릭 컨텍스트 메뉴(<see cref="ItemContextMenu"/>)를 계층·배선까지 한 번에 만드는 에디터 툴.
    /// 최상위 렌더링을 위해 OverlayCanvas(없으면 생성)에 붙이고, 전체화면 블로커 + 메뉴 박스(등록1/등록2/사용)를
    /// 구성해 컴포넌트 필드를 자동 연결한다. 손으로 2겹 구조를 짤 필요가 없다.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Item Context Menu
    /// </summary>
    public static class ItemContextMenuGenerator
    {
        private const string UndoName = "Create Item Context Menu";
        private static TMP_FontAsset s_font;

        [MenuItem("Tools/ProjectS/Create Item Context Menu")]
        public static void CreateItemContextMenu()
        {
            // 이미 있으면 중복 생성하지 않고 그것을 선택해준다.
            ItemContextMenu existing = Object.FindAnyObjectByType<ItemContextMenu>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[ItemContextMenuGenerator] 이미 ItemContextMenu가 있어 선택만 했습니다. 새로 만들려면 기존 것을 지우고 다시 실행하세요.");
                return;
            }

            s_font = ResolveKoreanFont();
            EnsureEventSystem();

            Canvas overlay = GetOrCreateOverlayCanvas();

            // ── 루트(전체화면) : 컴포넌트 + 블로커가 여기 붙는다 ──
            GameObject root = CreateFullScreen(overlay.transform, "ItemContextMenu");
            ItemContextMenu menu = root.AddComponent<ItemContextMenu>();

            // 전체화면 투명 블로커(바깥 클릭 시 닫기). 루트의 첫 자식이라 메뉴 박스보다 뒤에 그려진다.
            GameObject blockerGo = CreateFullScreen(root.transform, "BackgroundBlocker");
            Image blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0f);   // 완전 투명(보이진 않지만 클릭은 받음)
            Button blocker = blockerGo.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.targetGraphic = blockerImg;

            // ── 메뉴 박스(커서로 이동하는 작은 박스) ──
            GameObject boxGo = new GameObject("MenuBox", typeof(RectTransform), typeof(Image));
            boxGo.transform.SetParent(root.transform, false);
            RectTransform box = boxGo.GetComponent<RectTransform>();
            SetRect(box, Vector2.zero, new Vector2(172f, 156f));
            Image boxImg = boxGo.GetComponent<Image>();
            boxImg.type = Image.Type.Sliced;
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            boxImg.color = new Color(0.13f, 0.14f, 0.18f, 0.96f);

            Button register1 = CreateMenuButton(box, "Register1Button", "등록 1", new Vector2(0f, 52f));
            Button register2 = CreateMenuButton(box, "Register2Button", "등록 2", new Vector2(0f, 4f));
            Button use = CreateMenuButton(box, "UseButton", "사용", new Vector2(0f, -44f));

            // ── 컴포넌트 필드 자동 배선 ──
            Wire(menu,
                ("menuRect", box),
                ("register1Button", register1),
                ("register2Button", register2),
                ("useButton", use),
                ("backgroundBlocker", blocker));

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ItemContextMenuGenerator] ItemContextMenu 생성·배선 완료. " +
                "OverlayCanvas가 UIManager 밖에 생성됐다면 UIManager 자식으로 옮겨 씬 전환에도 유지되게 하세요.");
        }

        // OverlayCanvas를 찾거나(이름 기준) 없으면 만든다. UIManager가 있으면 그 자식으로 두어 씬 전환에도 유지.
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
            canvas.sortingOrder = 100;   // 모든 패널·팝업 캔버스보다 위(툴팁·메뉴가 항상 최상단)

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[ItemContextMenuGenerator] OverlayCanvas(Sort Order 100) 생성. 툴팁 등 다른 오버레이도 여기 두세요.");
            return canvas;
        }

        private static Button CreateMenuButton(Transform parent, string name, string label, Vector2 pos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), pos, new Vector2(156f, 44f));

            Image img = go.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.color = new Color(0.25f, 0.45f, 0.85f, 1f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(go.transform, "Text", label, 22f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            FillParent(text.rectTransform);

            return btn;
        }

        // ── 유틸(AuthUIGenerator와 동일 패턴) ──────────────────────────

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

        private static void SetRect(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        private static void FillParent(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // private [SerializeField] 단일 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new SerializedObject(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
