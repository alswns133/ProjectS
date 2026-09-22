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
    /// 레이드 등장 연출 전 <b>파티원 대기 화면</b>(<see cref="RaidWaitView"/>)을 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Raid Wait Screen
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>어디에 만드나.</b> 열린 씬의 <c>UIManager</c> 아래(보통 Bootstrap). 대기 신호는 씬을 넘어 오므로 DDoL인
    /// UIManager 아래에 있어야 레이드 씬 로딩 중에도 살아 있다. UIManager가 없으면 씬 루트에 만들고 알린다.
    /// </para>
    /// <para>
    /// <b>구성.</b> 전체 검은 배경 + 가운데 원형 로딩(트랙·회전 호·가운데 구멍) + 바로 아래 준비 현황 "0/2".
    /// 원형은 스프라이트 아트 없이 Unity 기본 원(Knob)으로 만든다 — 호는 원을 Radial 채우기로 일부만 칠하고,
    /// 가운데를 배경색 원으로 덮어 고리처럼 보이게 한다. 아트가 생기면 스프라이트만 갈아끼우면 된다.
    /// </para>
    /// <para>
    /// <b>정렬 순서 90.</b> 로딩 화면(LoadingPanel=100)보다 아래, 팝업들(40~53)보다 위. 로딩 중 미리 깔려 있다가
    /// 로딩이 걷히는 순간 이미 덮고 있어야 로딩→씬 사이 빈 프레임이 안 보인다.
    /// </para>
    /// <para>
    /// 이름은 CLAUDE.md 씬·UI 이름 규칙을 따른다(PascalCase, 배경은 <c>Background</c>).
    /// </para>
    /// </remarks>
    public static class RaidWaitScreenGenerator
    {
        private const string RootName = "RaidWaitScreen";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";
        private const string CircleSpritePath = "UI/Skin/Knob.psd";

        private const int SortingOrder = 90;
        private const float SpinnerSize = 96f;
        private const float RingThickness = 10f;
        private const float CountFontSize = 40f;
        private const float MessageFontSize = 26f;
        private const float MessageToSpinnerGap = 40f;
        private const float SpinnerToCountGap = 28f;

        private static readonly Color BackgroundColor = Color.black;
        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color ArcColor = Color.white;
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);

        [MenuItem("Tools/ProjectS/Create Raid Wait Screen")]
        public static void Create()
        {
            UIManager uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            Transform parent = uiManager != null ? uiManager.transform : null;

            if (uiManager == null &&
                !EditorUtility.DisplayDialog("UIManager를 찾지 못했다",
                    "열린 씬에 UIManager가 없다. 대기 화면은 씬을 넘어 살아 있어야 해 보통 Bootstrap의 UIManager 아래에 둔다.\n\n" +
                    "그래도 이 씬 루트에 만들까?", "씬 루트에 만들기", "취소"))
                return;

            if (!ConfirmReplaceExisting()) return;

            GameObject root = BuildRoot(parent);

            Undo.RegisterCreatedObjectUndo(root, "Create Raid Wait Screen");
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Debug.Log($"[RaidWaitScreenGenerator] '{root.name}' 생성 완료 — 부모: {(parent != null ? parent.name : "씬 루트")}. " +
                      "씬을 저장하세요.", root);
        }

        // 이미 있으면 교체 여부를 묻는다. 둘이 되면 같은 신호에 화면이 두 겹 뜬다.
        private static bool ConfirmReplaceExisting()
        {
            RaidWaitView existing = Object.FindAnyObjectByType<RaidWaitView>(FindObjectsInactive.Include);
            if (existing == null) return true;

            if (!EditorUtility.DisplayDialog("대기 화면이 이미 있다",
                    $"'{existing.name}'가 이미 씬에 있다. 지우고 새로 만들면 수동으로 바꾼 색·배치가 사라진다.\n\n교체할까?",
                    "교체", "취소"))
                return false;

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        private static GameObject BuildRoot(Transform parent)
        {
            GameObject root = new(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                  typeof(GraphicRaycaster), typeof(CanvasGroup));
            if (parent != null) root.transform.SetParent(parent, false);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // 프로젝트 다른 캔버스와 같은 기준 해상도.
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 처음엔 안 보이게 — View.Awake도 같은 처리를 하지만, 에디터에서 씬을 볼 때 화면이 검게 가려지지 않게 한다.
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // 전체 검은 배경. 대기 중 뒤의 UI를 누르지 못하게 레이캐스트를 받는다.
            Image background = CreateImage(root.transform, "Background", BackgroundColor, null);
            background.raycastTarget = true;
            Fill(background.rectTransform);

            RectTransform center = CreateRect(root.transform, "Center");
            Centered(center, Vector2.zero, new Vector2(900f, SpinnerSize + SpinnerToCountGap + CountFontSize * 1.5f + MessageToSpinnerGap + MessageFontSize * 1.5f));

            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>(CircleSpritePath);

            // 원형 로딩: 트랙(희미한 전체 고리) → 호(회전) → 구멍(배경색으로 가운데를 덮어 고리로 보이게).
            RectTransform spinner = CreateRect(center, "Spinner");
            Centered(spinner, new Vector2(0f, (SpinnerToCountGap + CountFontSize) * 0.5f), new Vector2(SpinnerSize, SpinnerSize));

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

            TextMeshProUGUI count = CreateText(center, "Count", "0 / 0 준비 완료", CountFontSize);
            Centered(count.rectTransform,
                     new Vector2(0f, -(SpinnerSize + SpinnerToCountGap) * 0.5f),
                     new Vector2(900f, CountFontSize * 1.5f));

            // 숫자만 있으면 "무엇을 기다리는지"가 읽히지 않는다. 진입 화면(EntryVeil)과 같은 형식으로
            // 안내 한 줄을 스피너 위에 둔다 — 문구 자체는 View의 인스펙터 값이 채운다.
            TextMeshProUGUI message = CreateText(center, "Message", "파티원이 모두 도착하기를 기다리는 중입니다", MessageFontSize);
            Centered(message.rectTransform,
                     new Vector2(0f, (SpinnerToCountGap + CountFontSize) * 0.5f + SpinnerSize * 0.5f + MessageToSpinnerGap),
                     new Vector2(900f, MessageFontSize * 1.5f));

            RaidWaitView view = root.AddComponent<RaidWaitView>();
            Wire(view, ("spinnerArc", arc.rectTransform), ("countText", count), ("messageText", message));

            RaidWaitPresenter presenter = root.AddComponent<RaidWaitPresenter>();
            Wire(presenter, ("view", view));

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

            // TMP 기본값(LiberationSans)에는 한글 글리프가 없다. 지금은 숫자뿐이지만 문구가 붙을 때를 대비해 맞춘다.
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
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[RaidWaitScreenGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없다. 이름이 바뀌었는지 확인한다.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
