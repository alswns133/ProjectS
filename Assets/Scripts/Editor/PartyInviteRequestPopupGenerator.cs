// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.UI;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 초대를 <b>받는</b> 쪽 팝업(<see cref="PartyInviteRequestPopup"/>)을 프리팹으로 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Party Invite Request Popup
    ///
    /// "OO님이 파티에 초대했습니다"·남은 시간·[수락]/[거절] 두 버튼으로 이뤄진 가운데 모달 카드다.
    /// 보내는 쪽(<see cref="PartyInvitePopup"/>)과 같은 루트 구성(독립 Canvas + CanvasGroup)을 따른다
    /// — 팝업마다 캔버스 하나 규칙(ui-canvas-structure).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 툴로 만드나.</b> 팝업은 UIManager 자식으로 들어가야 자동 수집되는데(RegisterPopup 없이),
    /// 캔버스·스케일러·딤·카드·버튼 계층을 손으로 쌓으면 앵커·폰트·이름 규칙을 매번 틀린다.
    /// 툴이 규칙대로(<see cref="PartySlotGenerator"/>와 같은 헬퍼) 한 번에 세운다.
    /// </para>
    /// <para>
    /// <b>씬에 UIManager가 있으면 그 아래에 넣어 준다</b>(자동 수집 경로). 없으면 프리팹만 만든다.
    /// 뜨는 것은 프롬프터(<c>PartyInvitePrompter</c>)가 초대 신호를 받을 때이므로, 기본은 꺼 둔다.
    /// </para>
    /// </remarks>
    public static class PartyInviteRequestPopupGenerator
    {
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string PopupPath = PrefabFolder + "/PartyInviteRequestPopup.prefab";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";

        // 받는 초대는 목록(45)보다 위에 떠야 한다(모달) — 더 높은 정렬 순서를 준다.
        private const int SortingOrder = 60;

        private const float CardWidth = 460f;
        private const float CardHeight = 240f;
        private const float ButtonWidth = 180f;
        private const float ButtonHeight = 56f;
        private const float ButtonGap = 24f;

        private static readonly Color DimColor = new Color32(0x00, 0x00, 0x00, 0x99);   // 뒤를 가리는 반투명 딤
        private static readonly Color CardColor = new Color32(0x1A, 0x21, 0x30, 0xFF);
        private static readonly Color AcceptColor = new Color32(0x2E, 0x7D, 0xFF, 0xFF); // 파랑=수락(실행)
        private static readonly Color DeclineColor = new Color32(0x39, 0x42, 0x52, 0xFF); // 회색=거절
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);
        private static readonly Color MutedColor = new Color32(0x8D, 0x99, 0xAC, 0xFF);

        [MenuItem("Tools/ProjectS/Create Party Invite Request Popup")]
        public static void Create()
        {
            string path = ResolvePath(PopupPath, "받는 초대 팝업");
            if (path == null) return;

            EnsureFolder();

            GameObject temp = Build();
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);

            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);

            Debug.Log($"[PartyInviteRequestPopupGenerator] 받는 초대 팝업을 만들었다.\n· {path}");

            TryAttachToUIManager(asset);
        }

        // ── 계층 만들기 ─────────────────────────────────────────────────

        private static GameObject Build()
        {
            // 루트: 독립 Canvas + CanvasGroup + 팝업 스크립트(보내는 쪽 팝업과 같은 구성).
            GameObject root = new("PartyInviteRequestPopup",
                                  typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                  typeof(GraphicRaycaster), typeof(CanvasGroup),
                                  typeof(PartyInviteRequestPopup));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;   // 목록 위에 확실히 올린다(모달)
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // ① 딤 — 화면을 덮어 뒤 클릭을 막는다(raycast 받는 유일한 배경).
            Image dim = CreateImage(root.transform, "Dim", DimColor);
            dim.raycastTarget = true;
            Fill(dim.rectTransform);

            // ② 가운데 카드
            Image card = CreateImage(root.transform, "Card", CardColor);
            card.raycastTarget = true;   // 카드 밖(딤) 클릭이 카드 안으로 새지 않게
            CenterBox(card.rectTransform, CardWidth, CardHeight);

            // 메시지("OO님이 파티에 초대했습니다") — 카드 위쪽.
            TextMeshProUGUI message = CreateText(card.transform, "MessageText", "OO님이 파티에 초대했습니다", 22f);
            message.color = TextColor;
            message.alignment = TextAlignmentOptions.Center;
            AnchorTop(message.rectTransform, 40f, 44f, 24f);

            // 남은 시간 — 메시지 아래.
            TextMeshProUGUI timer = CreateText(card.transform, "TimerText", "20초", 16f);
            timer.color = MutedColor;
            timer.alignment = TextAlignmentOptions.Center;
            AnchorTop(timer.rectTransform, 92f, 28f, 24f);

            // ③ 버튼 두 개 — 카드 아래쪽에 나란히.
            Button decline = CreateButton(card.transform, "DeclineButton", "거절", DeclineColor);
            AnchorBottomCenter(decline.GetComponent<RectTransform>(), -(ButtonWidth + ButtonGap) * 0.5f, 32f);

            Button accept = CreateButton(card.transform, "AcceptButton", "수락", AcceptColor);
            AnchorBottomCenter(accept.GetComponent<RectTransform>(), (ButtonWidth + ButtonGap) * 0.5f, 32f);

            Wire(root.GetComponent<PartyInviteRequestPopup>(),
                 ("messageText", message),
                 ("timerText", timer),
                 ("acceptButton", accept),
                 ("declineButton", decline));

            // 뜨는 것은 프롬프터가 초대 신호를 받을 때뿐이라 기본은 꺼 둔다(ShowPopup이 켠다).
            root.SetActive(false);
            return root;
        }

        // ── UIManager에 붙이기 ──────────────────────────────────────────

        // 씬에 UIManager가 있으면 그 자식으로 넣어 자동 수집 경로에 태운다(RegisterPopup 불필요).
        private static void TryAttachToUIManager(GameObject asset)
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.Log("[PartyInviteRequestPopupGenerator] 씬에 UIManager가 없어 프리팹만 만들었다. " +
                          "Bootstrap 씬 UIManager 자식으로 직접 넣으면 자동 등록된다.");
                return;
            }

            if (manager.GetComponentInChildren<PartyInviteRequestPopup>(true) != null)
            {
                Debug.Log("[PartyInviteRequestPopupGenerator] UIManager 아래에 이미 받는 초대 팝업이 있어 붙이지 않았다.");
                return;
            }

            bool attach = EditorUtility.DisplayDialog(
                "UIManager에 붙일까?",
                "씬에서 UIManager를 찾았다.\n\n받는 초대 팝업을 자식으로 넣어 자동 등록되게 할까?\n" +
                "(꺼진 상태로 들어가며, 초대가 오면 프롬프터가 켠다)",
                "넣기",
                "그만두기");

            if (!attach) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, manager.transform);
            instance.SetActive(false);
            Undo.RegisterCreatedObjectUndo(instance, "Attach Party Invite Request Popup");

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Debug.Log("[PartyInviteRequestPopupGenerator] UIManager 아래에 받는 초대 팝업을 넣었다(자동 등록).", instance);
        }

        // ── 만들기 도우미(PartySlotGenerator와 같은 규칙) ────────────────

        private static string ResolvePath(string path, string title)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return path;

            int choice = EditorUtility.DisplayDialogComplex(
                $"{title} 프리팹이 이미 있다",
                $"{path} 를 새로 만들면 지금 프리팹에 넣어 둔 스프라이트·색·수동 배치가 사라진다.\n\n어떻게 할까?",
                "덮어쓰기",
                "취소",
                "새 파일로 저장");

            switch (choice)
            {
                case 0:  return path;
                case 2:  return AssetDatabase.GenerateUniqueAssetPath(path);
                default: return null;
            }
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.raycastTarget = false;

            // TMP 기본값(LiberationSans)에는 한글 글리프가 없어 그대로 두면 전부 □로 나온다.
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) text.font = font;
            else if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            return text;
        }

        // 버튼 = Image(타깃 그래픽) + 자식 라벨. 클릭 배선은 팝업 스크립트가 코드로 한다.
        private static Button CreateButton(Transform parent, string name, string label, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image background = go.GetComponent<Image>();
            background.color = color;
            background.raycastTarget = true;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;

            TextMeshProUGUI text = CreateText(go.transform, "Label", label, 18f);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            Fill(text.rectTransform);

            return button;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // 가운데 고정 크기 상자.
        private static void CenterBox(RectTransform rt, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
        }

        // 부모 위에서 top만큼 내려 가로로 늘린 한 줄.
        private static void AnchorTop(RectTransform rt, float top, float height, float sidePad)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(sidePad, -(top + height));
            rt.offsetMax = new Vector2(-sidePad, -top);
        }

        // 부모 아래 중앙에서 x만큼 비켜 놓은 고정 크기 버튼.
        private static void AnchorBottomCenter(RectTransform rt, float offsetX, float bottom)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(offsetX, bottom);
            rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
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
                else Debug.LogWarning($"[PartyInviteRequestPopupGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없다. 이름이 바뀌었는지 확인한다.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
