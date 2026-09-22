// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
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
    /// 진입 베일(<see cref="EntryVeil"/>) 프리팹을 만들고 열린 씬에 배치하는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Entry Veil
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 프리팹인가.</b> 베일은 로그인 씬과 캐릭터 선택 씬에 <b>각각 하나씩</b> 필요하고, 씬이 바뀌는
    /// 순간 두 베일이 교대한다. 배경색이나 로고 위치가 조금이라도 다르면 그 한 프레임에 차이가 튄다.
    /// 두 씬이 같은 프리팹 인스턴스를 쓰면 한쪽을 고칠 때 다른 쪽이 따라오므로, 둘이 갈라지는 사고 자체가
    /// 일어나지 않는다. (손으로 복사한 사본이 프리팹을 고쳐도 안 따라와 갈라졌던 전례가 이 프로젝트에 있다.)
    /// </para>
    /// <para>
    /// <b>쓰는 법.</b> 로그인 씬을 열고 한 번, 캐릭터 선택 씬을 열고 한 번 실행한다. 프리팹은 처음 한 번만
    /// 만들어지고 두 번째부터는 같은 것을 배치한다. 같은 씬의 <c>LoginUI</c>·<c>SignupUI</c>·
    /// <c>EntryFlowController</c>를 찾아 <c>veil</c> 칸까지 자동으로 물린다.
    /// </para>
    /// <para>
    /// <b>구성.</b> 전체 배경 + 가운데 원형 스피너(트랙·회전 호·가운데 구멍) + 문구 한 줄, 그리고 꺼 둔
    /// 로고 자리. 스피너는 아트 없이 Unity 기본 원(Knob)으로 만든다 — 아트가 생기면 스프라이트만 갈아끼운다.
    /// 로고는 스프라이트를 물린 뒤 켜면 된다.
    /// </para>
    /// <para>
    /// <b>정렬 순서 200.</b> 진입 씬의 다른 캔버스보다 확실히 위. 베일은 "판정이 끝나기 전에는 아무것도
    /// 보여주지 않는" 것이 목적이라 무엇에도 가려지면 안 된다.
    /// </para>
    /// <para>
    /// 이름은 CLAUDE.md 씬·UI 이름 규칙을 따른다(PascalCase, 배경은 <c>Background</c>).
    /// </para>
    /// </remarks>
    public static class EntryVeilGenerator
    {
        private const string RootName = "EntryVeil";
        private const string PrefabPath = "Assets/Prefabs/UI/EntryVeil.prefab";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";
        private const string CircleSpritePath = "UI/Skin/Knob.psd";

        private const int SortingOrder = 200;
        private const float SpinnerSize = 72f;
        private const float RingThickness = 7f;
        private const float MessageFontSize = 24f;
        private const float LogoSize = 200f;

        // SessionReboot.VeilColor와 같은 값. 인게임 복귀 때 뜨는 단색 베일과 색이 어긋나지 않게 맞춰 둔다.
        private static readonly Color BackgroundColor = new(0.02f, 0.02f, 0.03f, 1f);
        private static readonly Color TrackColor = new(1f, 1f, 1f, 0.15f);
        private static readonly Color ArcColor = Color.white;
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);

        [MenuItem("Tools/ProjectS/Create Entry Veil")]
        public static void Create()
        {
            GameObject prefab = LoadOrCreatePrefab();
            if (prefab == null) return;

            if (!ConfirmReplaceExisting()) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RootName;
            instance.transform.SetAsLastSibling();

            Undo.RegisterCreatedObjectUndo(instance, "Create Entry Veil");

            EntryVeil veil = instance.GetComponent<EntryVeil>();
            string wired = WireSceneReferences(veil);

            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;

            Debug.Log($"[EntryVeilGenerator] '{RootName}' 배치 완료 ({PrefabPath}). {wired}\n" +
                      "· LoginUI의 formRoot(로그인/회원가입 UI 루트)는 자동으로 못 찾으니 직접 물려 주세요(선택).\n" +
                      "· 편집 중 화면이 가려지면 꺼 두셔도 됩니다 — LoginUI·EntryFlowController가 Awake에서 다시 켭니다.\n" +
                      "  (다만 부모 오브젝트가 꺼져 있으면 되살릴 수 없어 경고만 남습니다.)\n" +
                      "· 씬을 저장하세요.", instance);
        }

        // 프리팹이 없으면 만들고, 있으면 그대로 쓴다. 두 씬이 같은 에셋을 공유해야 모양이 갈라지지 않는다.
        private static GameObject LoadOrCreatePrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI"))
            {
                Debug.LogError("[EntryVeilGenerator] Assets/Prefabs/UI 폴더가 없습니다. 폴더를 먼저 만들어 주세요.");
                return null;
            }

            GameObject temp = BuildRoot();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);

            if (saved == null)
            {
                Debug.LogError($"[EntryVeilGenerator] 프리팹 저장 실패: {PrefabPath}");
                return null;
            }

            Debug.Log($"[EntryVeilGenerator] 프리팹을 새로 만들었습니다: {PrefabPath}", saved);
            return saved;
        }

        // 이미 있으면 교체 여부를 묻는다. 둘이 되면 걷히는 시점이 서로 달라 한 장이 남는다.
        private static bool ConfirmReplaceExisting()
        {
            EntryVeil existing = Object.FindAnyObjectByType<EntryVeil>(FindObjectsInactive.Include);
            if (existing == null) return true;

            if (!EditorUtility.DisplayDialog("베일이 이미 있습니다",
                    $"'{existing.name}'가 이미 이 씬에 있습니다. 지우고 새로 놓으면 수동으로 바꾼 배치가 사라집니다.\n\n교체할까요?",
                    "교체", "취소"))
                return false;

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        // 같은 씬의 진입 컴포넌트들에 방금 놓은 베일을 물린다. 어떤 씬이냐에 따라 있는 것만 연결된다
        // (로그인 씬 = LoginUI·SignupUI, 캐릭터 선택 씬 = EntryFlowController).
        private static string WireSceneReferences(EntryVeil veil)
        {
            int count = 0;

            foreach (LoginUI login in Object.FindObjectsByType<LoginUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                count += Wire(login, "veil", veil) ? 1 : 0;

            foreach (SignupUI signup in Object.FindObjectsByType<SignupUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                count += Wire(signup, "veil", veil) ? 1 : 0;

            foreach (EntryFlowController flow in Object.FindObjectsByType<EntryFlowController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                count += Wire(flow, "veil", veil) ? 1 : 0;

            return count > 0
                ? $"{count}개 컴포넌트의 veil 칸을 자동으로 물렸습니다."
                : "이 씬에서 연결할 진입 컴포넌트를 찾지 못했습니다 — veil 칸을 직접 물려 주세요.";
        }

        private static GameObject BuildRoot()
        {
            GameObject root = new(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                  typeof(GraphicRaycaster), typeof(CanvasGroup));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // 프로젝트 다른 캔버스와 같은 기준 해상도.
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 덮인 상태로 저장한다. 런타임에는 EntryVeil.Awake가 어차피 다시 1로 못박지만,
            // 씬을 열었을 때 보이는 모습이 실제 시작 모습과 같아야 오해가 없다.
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;
            group.interactable = true;

            // 전체 배경. 베일 아래 UI가 눌리지 않게 이 한 장만 레이캐스트를 받는다.
            Image background = CreateImage(root.transform, "Background", BackgroundColor, null);
            background.raycastTarget = true;
            Fill(background.rectTransform);

            RectTransform center = CreateRect(root.transform, "Center");
            Centered(center, Vector2.zero, new Vector2(900f, 500f));

            // 로고 자리. 아트가 없으면 흰 사각형이 뜨므로 꺼 둔 채로 만든다 — 스프라이트를 물리고 켜면 된다.
            Image logo = CreateImage(center, "Logo", Color.white, null);
            Centered(logo.rectTransform, new Vector2(0f, 160f), new Vector2(LogoSize, LogoSize));
            logo.gameObject.SetActive(false);

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircleSpritePath);

            // 원형 스피너: 트랙(희미한 전체 고리) → 호(회전) → 구멍(배경색으로 가운데를 덮어 고리로 보이게).
            RectTransform spinner = CreateRect(center, "Spinner");
            Centered(spinner, Vector2.zero, new Vector2(SpinnerSize, SpinnerSize));

            Image track = CreateImage(spinner, "SpinnerTrack", TrackColor, circle);
            Fill(track.rectTransform);

            Image arc = CreateImage(spinner, "SpinnerArc", ArcColor, circle);
            arc.type = Image.Type.Filled;
            arc.fillMethod = Image.FillMethod.Radial360;
            arc.fillOrigin = (int)Image.Origin360.Top;
            arc.fillClockwise = true;
            arc.fillAmount = 0.25f;
            Fill(arc.rectTransform);

            Image hole = CreateImage(spinner, "SpinnerHole", BackgroundColor, circle);
            Centered(hole.rectTransform, Vector2.zero,
                     new Vector2(SpinnerSize - RingThickness * 2f, SpinnerSize - RingThickness * 2f));

            TextMeshProUGUI message = CreateText(center, "Message", "접속 중...", MessageFontSize);
            Centered(message.rectTransform, new Vector2(0f, -(SpinnerSize * 0.5f + 46f)), new Vector2(900f, 48f));

            EntryVeil veil = root.AddComponent<EntryVeil>();

            // 회전은 호에만 건다. 스피너 묶음 전체를 돌리면 구멍·트랙까지 같이 돌아 의미가 없다.
            Wire(veil, "messageText", message);
            Wire(veil, "spinner", arc.transform);

            return root;
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
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;   // 클릭은 배경 한 장만 받는다
            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            // TMP 기본값(LiberationSans)에는 한글 글리프가 없다. 문구가 전부 한글이라 반드시 갈아끼운다.
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

        // 부모 중앙 기준으로 배치한다. 회전하는 호도 가운데 피벗이라 제자리에서 돈다.
        private static void Centered(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        // private [SerializeField] 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static bool Wire(Component comp, string prop, Object value)
        {
            if (comp == null) return false;

            SerializedObject so = new(comp);
            SerializedProperty p = so.FindProperty(prop);

            if (p == null)
            {
                Debug.LogWarning($"[EntryVeilGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없습니다. 이름이 바뀌었는지 확인하세요.", comp);
                return false;
            }

            p.objectReferenceValue = value;
            so.ApplyModifiedProperties();
            return true;
        }
    }
}
