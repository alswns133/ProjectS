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
    /// 아이템 우클릭 컨텍스트 메뉴(<see cref="ItemContextMenu"/>)와 파괴 확인 대화상자(<see cref="ConfirmDialog"/>)를
    /// 계층·배선까지 한 번에 만드는 에디터 툴. 최상위 렌더링을 위해 OverlayCanvas(없으면 생성)에 붙인다.
    ///
    /// 메뉴 박스는 VerticalLayoutGroup+ContentSizeFitter라, 장비([장착][파괴])·소비품([등록1][등록2][사용][파괴])처럼
    /// 켜지는 버튼 수가 달라도 높이가 자동으로 맞는다(버튼 5개를 다 만들어두고 런타임에 SetActive로 가린다).
    /// 파괴는 되돌릴 수 없어 ConfirmDialog를 거치므로, 이 툴이 대화상자도 함께 만들어 배선을 완결한다.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Item Context Menu
    /// </summary>
    public static class ItemContextMenuGenerator
    {
        private const string UndoName = "Create Item Context Menu";
        private static TMP_FontAsset s_font;

        private static readonly Color ButtonBlue = new Color(0.25f, 0.45f, 0.85f, 1f);
        private static readonly Color ButtonGreen = new Color(0.24f, 0.58f, 0.35f, 1f);   // 장착
        private static readonly Color ButtonRed = new Color(0.72f, 0.26f, 0.26f, 1f);    // 파괴(위험)

        [MenuItem("Tools/ProjectS/Create Item Context Menu")]
        public static void CreateItemContextMenu()
        {
            s_font = ResolveKoreanFont();
            EnsureEventSystem();

            Canvas overlay = GetOrCreateOverlayCanvas();

            // 이미 있으면 컨텍스트 메뉴는 새로 만들지 않는다(선택만). 새 장착/파괴 버튼이 필요하면 기존 것을 지우고 재실행.
            ItemContextMenu existing = Object.FindAnyObjectByType<ItemContextMenu>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.LogWarning("[ItemContextMenuGenerator] 이미 ItemContextMenu가 있어 선택만 했습니다. " +
                    "장착/파괴 버튼이 없는 옛 버전이면 지우고 다시 실행하세요.");
            }
            else
            {
                BuildContextMenu(overlay.transform);
            }

            // 파괴 확인 대화상자도 없으면 함께 만든다(컨텍스트 메뉴의 파괴가 이걸 필요로 한다).
            if (Object.FindAnyObjectByType<ConfirmDialog>(FindObjectsInactive.Include) == null)
                BuildConfirmDialog(overlay.transform);
            else
                Debug.Log("[ItemContextMenuGenerator] ConfirmDialog가 이미 있어 그대로 둡니다.");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ItemContextMenuGenerator] 생성·배선 완료. " +
                "OverlayCanvas가 UIManager 밖에 생성됐다면 UIManager 자식으로 옮겨 씬 전환에도 유지되게 하세요. " +
                "★ 씬을 저장해야 반영됩니다(Ctrl+S).");
        }

        // ── 컨텍스트 메뉴(2겹: 블로커 + 레이아웃 박스) ──────────────────
        private static void BuildContextMenu(Transform canvasParent)
        {
            GameObject root = CreateFullScreen(canvasParent, "ItemContextMenu");
            ItemContextMenu menu = root.AddComponent<ItemContextMenu>();

            // 전체화면 투명 블로커(바깥 클릭 시 닫기). 루트의 첫 자식이라 메뉴 박스보다 뒤에 그려진다.
            GameObject blockerGo = CreateFullScreen(root.transform, "BackgroundBlocker");
            Image blockerImg = blockerGo.AddComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0f);   // 완전 투명(보이진 않지만 클릭은 받음)
            Button blocker = blockerGo.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.targetGraphic = blockerImg;

            // ── 메뉴 박스: 켜진 버튼 수만큼 높이가 늘고 줄게 세로 레이아웃 + 콘텐츠 핏 ──
            GameObject boxGo = new GameObject("MenuBox",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            boxGo.transform.SetParent(root.transform, false);

            RectTransform box = boxGo.GetComponent<RectTransform>();
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(184f, 0f);   // 폭 고정, 높이는 ContentSizeFitter가 채움

            Image boxImg = boxGo.GetComponent<Image>();
            boxImg.type = Image.Type.Sliced;
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            boxImg.color = new Color(0.13f, 0.14f, 0.18f, 0.96f);

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

            // 버튼 5개를 표시 순서대로 만든다. 장비=[장착][파괴], 소비품=[등록1][등록2][사용][파괴].
            // 계층 순서가 곧 표시 순서 — 켜진 것만 레이아웃에 잡혀 위에서부터 빈틈 없이 쌓인다.
            Button equip = CreateLayoutButton(box, "EquipButton", "장착", ButtonGreen);
            Button register1 = CreateLayoutButton(box, "Register1Button", "등록 1", ButtonBlue);
            Button register2 = CreateLayoutButton(box, "Register2Button", "등록 2", ButtonBlue);
            Button use = CreateLayoutButton(box, "UseButton", "사용", ButtonBlue);
            Button discard = CreateLayoutButton(box, "DiscardButton", "파괴", ButtonRed);

            Wire(menu,
                ("menuRect", box),
                ("equipButton", equip),
                ("register1Button", register1),
                ("register2Button", register2),
                ("useButton", use),
                ("discardButton", discard),
                ("backgroundBlocker", blocker));

            Selection.activeGameObject = root;
        }

        // ── 파괴 확인 대화상자(딤 블로커 + 중앙 박스 + 확인/취소) ──────────
        private static void BuildConfirmDialog(Transform canvasParent)
        {
            GameObject root = CreateFullScreen(canvasParent, "ConfirmDialog");
            ConfirmDialog dialog = root.AddComponent<ConfirmDialog>();

            // 전체화면 딤 + 바깥 클릭=취소.
            GameObject blockerGo = CreateFullScreen(root.transform, "DimBlocker");
            Image dim = blockerGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            Button blocker = blockerGo.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;
            blocker.targetGraphic = dim;

            // 중앙 박스.
            GameObject boxGo = new GameObject("Box", typeof(RectTransform), typeof(Image));
            boxGo.transform.SetParent(root.transform, false);
            RectTransform box = boxGo.GetComponent<RectTransform>();
            SetRect(box, Vector2.zero, new Vector2(520f, 260f));
            Image boxImg = boxGo.GetComponent<Image>();
            boxImg.type = Image.Type.Sliced;
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            boxImg.color = new Color(0.13f, 0.14f, 0.18f, 0.98f);

            TextMeshProUGUI message = CreateTMP(box.transform, "MessageText", "정말 파괴하시겠습니까?", 26f);
            message.alignment = TextAlignmentOptions.Center;
            message.color = Color.white;
            SetRect(message.rectTransform, new Vector2(0f, 34f), new Vector2(470f, 130f));

            Button confirm = CreateFixedButton(box.transform, "ConfirmButton", "확인",
                new Vector2(-115f, -78f), new Vector2(200f, 66f), ButtonRed);
            Button cancel = CreateFixedButton(box.transform, "CancelButton", "취소",
                new Vector2(115f, -78f), new Vector2(200f, 66f), new Color(0.35f, 0.37f, 0.44f, 1f));

            SerializedObject so = new SerializedObject(dialog);
            SetProp(so, "messageText", message);
            SetProp(so, "confirmButton", confirm);
            SetProp(so, "cancelButton", cancel);
            SetProp(so, "backgroundBlocker", blocker);
            so.ApplyModifiedPropertiesWithoutUndo();

            // ★ root를 켠 채 둔다 — ConfirmDialog는 Awake에서 static Instance를 세팅하는데, 비활성으로 두면
            // Awake가 안 돌아 Instance가 null이 되고 Show를 부를 길이 없어진다. Awake 끝에서 스스로 SetActive(false)
            // 하므로 런타임엔 알아서 숨는다(에디터에선 딤이 보이지만 무해 — ItemContextMenu와 같은 방식).
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
            canvas.sortingOrder = 100;   // 모든 패널·팝업 캔버스보다 위(툴팁·메뉴·확인창이 항상 최상단)

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[ItemContextMenuGenerator] OverlayCanvas(Sort Order 100) 생성. 툴팁 등 다른 오버레이도 여기 두세요.");
            return canvas;
        }

        // 세로 레이아웃 박스용 버튼(위치는 레이아웃 그룹이 정한다 — LayoutElement로 높이만 지정).
        private static Button CreateLayoutButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = new GameObject(name,
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);

            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 44f;
            le.minHeight = 44f;

            StyleButton(go, label, color);
            return go.GetComponent<Button>();
        }

        // 고정 위치 버튼(확인창처럼 레이아웃 그룹 밖에서 좌표로 배치).
        private static Button CreateFixedButton(Transform parent, string name, string label, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), pos, size);

            StyleButton(go, label, color);
            return go.GetComponent<Button>();
        }

        // 버튼 공통 스타일(슬라이스 배경 + 중앙 흰 글자).
        private static void StyleButton(GameObject go, string label, Color color)
        {
            Image img = go.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(go.transform, "Text", label, 22f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            FillParent(text.rectTransform);
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

        // private [SerializeField] 참조들을 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new SerializedObject(comp);
            foreach ((string prop, Object value) in refs)
                SetProp(so, prop, value);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 이름으로 단일 참조 프로퍼티를 채운다(없는 이름이면 조용히 건너뜀 — 필드명 변경 시 여기도 같이 고칠 것).
        private static void SetProp(SerializedObject so, string prop, Object value)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = value;
        }
    }
}
