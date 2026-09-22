// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using ProjectS.Managers;
using ProjectS.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 레이드 실패 팝업(<see cref="RaidFailPopup"/>) 프리팹을 만들고 열린 씬의 UIManager 아래에 배치하는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Raid Fail Popup
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>어디에 만드나.</b> UIManager 아래(보통 Bootstrap). UIManager가 자식 <c>BasePopup</c>을 모아 관리하므로
    /// 그 아래 있어야 <c>ShowPopup&lt;RaidFailPopup&gt;()</c>가 이 창을 찾는다. UIManager가 없으면 만들지 않는다 —
    /// 씬 루트에 두면 조용히 안 뜨는 창이 되기 때문이다.
    /// </para>
    /// <para>
    /// <b>트리거도 함께 붙인다.</b> 팝업은 평소 비활성이라 스스로 실패 신호를 못 받는다. UIManager에
    /// <see cref="RaidFailTrigger"/>가 없으면 같이 추가한다 — 이게 빠지면 전멸해도 창이 영영 안 뜬다.
    /// </para>
    /// <para>
    /// <b>정렬 순서 52.</b> 사망 팝업(50) 바로 위. 전멸 순간 두 창이 한 프레임 겹칠 수 있어 실패 창이 위에 와야 한다.
    /// </para>
    /// <para>
    /// 이름은 CLAUDE.md 씬·UI 이름 규칙을 따르되, 기존 사망 팝업의 표기(<c>TitleText</c>·<c>MessageText</c>)에 맞춘다.
    /// </para>
    /// </remarks>
    public static class RaidFailPopupGenerator
    {
        private const string RootName = "RaidFailPopup";
        private const string PrefabPath = "Assets/Prefabs/UI/RaidFailPopup.prefab";
        private const string ButtonPrefabPath = "Assets/Prefabs/UI/DefaultButton.prefab";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";

        private const int SortingOrder = 52;

        private static readonly Vector2 WindowSize = new(760f, 460f);
        private static readonly Vector2 ButtonSize = new(260f, 72f);

        private static readonly Color DimColor = new(0f, 0f, 0f, 0.75f);
        private static readonly Color WindowColor = new(0.07f, 0.07f, 0.09f, 0.96f);
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);
        private static readonly Color TitleColor = new Color32(0xE2, 0x4B, 0x4A, 0xFF);   // 실패 = 경고색

        [MenuItem("Tools/ProjectS/Create Raid Fail Popup")]
        public static void Create()
        {
            UIManager uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                EditorUtility.DisplayDialog("UIManager를 찾지 못했습니다",
                    "레이드 실패 팝업은 UIManager 아래에 있어야 ShowPopup이 찾을 수 있습니다.\n" +
                    "UIManager가 있는 씬(보통 Bootstrap)을 열고 다시 실행하세요.", "확인");
                return;
            }

            GameObject prefab = LoadOrCreatePrefab();
            if (prefab == null) return;

            if (!ConfirmReplaceExisting()) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, uiManager.transform);
            instance.name = RootName;
            instance.SetActive(false);   // 팝업은 평소 꺼져 있고 UIManager가 켠다

            Undo.RegisterCreatedObjectUndo(instance, "Create Raid Fail Popup");

            // 신호를 받아 창을 띄울 주체. 없으면 전멸해도 창이 안 뜬다.
            bool addedTrigger = uiManager.GetComponent<RaidFailTrigger>() == null;
            if (addedTrigger) Undo.AddComponent<RaidFailTrigger>(uiManager.gameObject);

            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;

            Debug.Log($"[RaidFailPopupGenerator] '{RootName}' 배치 완료 ({PrefabPath}). " +
                      $"{(addedTrigger ? "UIManager에 RaidFailTrigger도 추가했습니다." : "RaidFailTrigger는 이미 있었습니다.")}\n" +
                      "· 버튼·배경 아트는 프리팹에서 바꾸면 됩니다(문구는 팝업 컴포넌트의 인스펙터 값).\n" +
                      "· 씬을 저장하세요.", instance);
        }

        private static GameObject LoadOrCreatePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            {
                Debug.LogError("[RaidFailPopupGenerator] Assets/Prefabs/UI 폴더가 없습니다.");
                return null;
            }

            GameObject temp = BuildRoot();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);

            if (saved == null)
            {
                Debug.LogError($"[RaidFailPopupGenerator] 프리팹 저장 실패: {PrefabPath}");
                return null;
            }

            Debug.Log($"[RaidFailPopupGenerator] 프리팹을 새로 만들었습니다: {PrefabPath}", saved);
            return saved;
        }

        private static bool ConfirmReplaceExisting()
        {
            RaidFailPopup existing = Object.FindAnyObjectByType<RaidFailPopup>(FindObjectsInactive.Include);
            if (existing == null) return true;

            if (!EditorUtility.DisplayDialog("실패 팝업이 이미 있습니다",
                    $"'{existing.name}'가 이미 이 씬에 있습니다. 지우고 새로 놓으면 수동으로 바꾼 배치가 사라집니다.\n\n교체할까요?",
                    "교체", "취소"))
                return false;

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        private static GameObject BuildRoot()
        {
            GameObject root = new(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                  typeof(GraphicRaycaster));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 뒤 UI를 가리고 클릭도 막는다. 선택을 반드시 시키는 모달이라 뒤가 눌리면 안 된다.
            Image dim = CreateImage(root.transform, "Background", DimColor, true);
            Fill(dim.rectTransform);

            Image window = CreateImage(root.transform, "Window", WindowColor, false);
            Centered(window.rectTransform, Vector2.zero, WindowSize);

            TextMeshProUGUI title = CreateText(window.transform, "TitleText", "레이드 실패", 54f, TitleColor);
            Centered(title.rectTransform, new Vector2(0f, 150f), new Vector2(WindowSize.x - 80f, 70f));

            TextMeshProUGUI message = CreateText(window.transform, "MessageText", "레이드에 실패했습니다.\n다시 도전할까요?", 28f, TextColor);
            message.textWrappingMode = TextWrappingModes.Normal;
            Centered(message.rectTransform, new Vector2(0f, 60f), new Vector2(WindowSize.x - 100f, 100f));

            TextMeshProUGUI vote = CreateText(window.transform, "VoteText", "0 / 2 동의", 26f, TextColor);
            Centered(vote.rectTransform, new Vector2(0f, -10f), new Vector2(WindowSize.x - 100f, 44f));

            TextMeshProUGUI countdown = CreateText(window.transform, "CountdownText", "30", 44f, TextColor);
            Centered(countdown.rectTransform, new Vector2(0f, -70f), new Vector2(WindowSize.x - 100f, 60f));

            RectTransform choiceGroup = CreateRect(window.transform, "ChoiceGroup");
            Centered(choiceGroup, new Vector2(0f, -160f), new Vector2(WindowSize.x - 80f, ButtonSize.y));

            Button retry = CreateButton(choiceGroup, "RetryButton", "재시도", new Vector2(-150f, 0f));
            Button back = CreateButton(choiceGroup, "ReturnButton", "마을로", new Vector2(150f, 0f));

            RaidFailPopup popup = root.AddComponent<RaidFailPopup>();
            Wire(popup,
                 ("retryButton", retry),
                 ("returnButton", back),
                 ("messageText", message),
                 ("voteText", vote),
                 ("countdownText", countdown),
                 ("choiceGroup", choiceGroup.gameObject));

            return root;
        }

        // 프로젝트 기본 버튼 프리팹이 있으면 그걸 쓰고(룩앤필 통일), 없으면 최소 구성으로 만든다.
        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            GameObject go;

            if (prefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.name = name;
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.24f, 1f);

                TextMeshProUGUI text = CreateText(go.transform, "Label", label, 28f, TextColor);
                Fill(text.rectTransform);
            }

            RectTransform rect = (RectTransform)go.transform;
            Centered(rect, position, ButtonSize);

            // 프리팹 버튼이면 안쪽 라벨을 찾아 문구만 바꾼다.
            TextMeshProUGUI existingLabel = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingLabel != null) existingLabel.text = label;

            Button button = go.GetComponent<Button>();
            if (button == null) button = go.AddComponent<Button>();

            return button;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(Transform parent, string name, Color color, bool raycast)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            // TMP 기본값(LiberationSans)에는 한글 글리프가 없다 — 문구가 전부 한글이라 반드시 갈아끼운다.
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null) text.font = font;
            else if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            return text;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        private static void Centered(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        // private [SerializeField] 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new(comp);

            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[RaidFailPopupGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없습니다. 이름이 바뀌었는지 확인하세요.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
