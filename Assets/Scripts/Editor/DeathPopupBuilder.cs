// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 사망/부활 팝업(<see cref="DeathPopup"/>)과 그것을 여는 <see cref="DeathPresenter"/>를
    /// <b>UIManager 직속</b>에 만들고 인스펙터 참조까지 연결하는 에디터 도구.
    /// 메뉴: Tools ▸ ProjectS ▸ 사망·부활 팝업 생성
    /// </summary>
    /// <remarks>
    /// 배치 규칙은 이 프로젝트의 기존 선례(<c>DungeonSelectPopup</c>, <c>LoadingPanel</c>)를 따른다 —
    /// <b>HUD와 독립적으로 떠야 하는 UI는 UIManager 직속에 자기 Canvas를 갖고 비활성으로 대기한다.</b>
    ///
    /// HUD 안에 두면 두 가지가 동시에 깨진다.
    /// <list type="bullet">
    /// <item>HUD는 BasePanel이라 Hide 시 GameObject째 꺼진다 → 프리젠터의 OnDisable이 돌아 구독이 끊기고,
    ///       사망해도 아무도 듣지 못한다(경고조차 안 남아 원인 추적이 어렵다).</item>
    /// <item>부모가 꺼져 있으면 팝업을 Show해도 화면에 나오지 않는다.</item>
    /// </list>
    /// 반대로 Canvas는 HUD에 붙어 있어서, 팝업만 UIManager 직속으로 옮기면 Canvas 밖으로 나가 렌더링이 안 된다.
    /// 그래서 <b>자기 Canvas를 함께 만든다</b>. 이 세 가지가 맞물려 있어 손으로 옮기면 틀리기 쉬워, 툴로 고정한다.
    ///
    /// UIManager 자식이 되면 UIManager가 Awake에서 자동 수집하므로 RegisterPopup 없이도 열린다
    /// (DeathPresenter의 RegisterPopup 호출은 안전망으로만 남는다).
    ///
    /// 스프라이트·색은 임시값이다. 다시 실행하면 씬의 기존 DeathPopup/DeathPresenter를 지우고 새로 만든다 —
    /// 아트를 입힌 뒤에는 다시 돌리지 않는다.
    /// </remarks>
    public static class DeathPopupBuilder
    {
        private const string PopupName = "DeathPopup";
        private const string PresenterName = "DeathPresenter";

        // HUD(0)보다 위, LoadingPanel(100)보다 아래. 로딩 화면은 사망 팝업을 덮어야 하고
        // (마을 복귀 전환), 사망 팝업은 HUD를 덮어야 한다. DungeonSelectPopup(1)과도 겹치지 않는다.
        private const int SortingOrder = 50;

        // 생성되는 모든 TMP 텍스트에 쓸 한글 폰트. Build 시작에서 1회 해석한다(AuthUIGenerator와 같은 방식).
        private static TMP_FontAsset s_font;

        [MenuItem("Tools/ProjectS/사망·부활 팝업 생성", false, 110)]
        public static void Build()
        {
            Transform parent = ResolveParent();
            if (parent == null)
            {
                EditorUtility.DisplayDialog(
                    "사망·부활 팝업 생성",
                    "UIManager를 찾지 못했습니다.\n\n" +
                    "UIManager가 있는 씬(Bootstrap 등)을 열고 다시 실행하세요.\n" +
                    "사망 팝업은 HUD와 독립적으로 떠야 해서 UIManager 직속에 만들어야 합니다.",
                    "확인");
                return;
            }

            s_font = ResolveKoreanFont();

            // 씬 전체에서 이전 결과를 지운다(HUD 안에 잘못 들어간 것 포함).
            // parent 아래만 뒤지면 예전에 HUD 밑에 만들어둔 것이 남아 팝업이 둘이 되고,
            // UIManager가 어느 쪽을 등록할지 알 수 없어진다.
            int removed = RemoveExisting();

            DeathPopup popup = BuildPopup(parent);
            DeathPresenter presenter = BuildPresenter(parent, popup);

            Selection.activeGameObject = popup.gameObject;
            EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);

            Debug.Log($"[DeathPopupBuilder] '{parent.name}' 직속에 사망 팝업을 만들었습니다" +
                      (removed > 0 ? $" (기존 {removed}개 제거)" : "") + ".\n" +
                      $"· 팝업: 자기 Canvas(sortingOrder {SortingOrder}) 보유, 비활성으로 대기\n" +
                      $"· 프리젠터: '{presenter.name}' (항상 활성 — 여기서 사망을 듣는다)\n" +
                      "· ★ 씬을 저장해야 반영됩니다(Ctrl+S).", popup);
        }

        // UIManager를 부모로 삼는다. RectTransform이 아니어도 된다 — 팝업이 자기 Canvas를 갖기 때문에
        // 부모가 UI 계층일 필요가 없다(DungeonSelectPopup·LoadingPanel과 같은 조건).
        private static Transform ResolveParent()
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            return manager != null ? manager.transform : null;
        }

        private static int RemoveExisting()
        {
            int count = 0;

            foreach (DeathPopup old in Object.FindObjectsByType<DeathPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(old.gameObject);
                count++;
            }

            foreach (DeathPresenter old in Object.FindObjectsByType<DeathPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.DestroyObjectImmediate(old.gameObject);
                count++;
            }

            return count;
        }

        private static DeathPopup BuildPopup(Transform parent)
        {
            // 루트: 자기 Canvas를 가진 전체 화면 딤. 클릭이 뒤로 새지 않게 Image의 raycastTarget은 켠 채 둔다.
            GameObject root = new GameObject(PopupName,
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(CanvasGroup), typeof(Image), typeof(DeathPopup));

            Undo.RegisterCreatedObjectUndo(root, "Create DeathPopup");
            root.transform.SetParent(parent, false);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            // 기존 팝업들(DungeonSelectPopup·LoadingPanel)과 같은 스케일 규칙. 다르면 같은 화면에서
            // 폰트·여백 크기가 서로 어긋나 보인다.
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            RectTransform rootRect = (RectTransform)root.transform;
            FillParent(rootRect);

            Image dim = root.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);

            TextMeshProUGUI title = CreateLabel(root.transform, "TitleText", "사망", new Vector2(0f, 180f), 72f);
            title.color = new Color(0.86f, 0.22f, 0.22f);

            TextMeshProUGUI message = CreateLabel(root.transform, "MessageText", "부활하시겠습니까?", new Vector2(0f, 80f), 30f);
            message.rectTransform.sizeDelta = new Vector2(720f, 100f);

            TextMeshProUGUI count = CreateLabel(root.transform, "ReviveCountText", "1 / 1", new Vector2(0f, 20f), 28f);
            count.color = new Color(0.85f, 0.85f, 0.85f);

            // 선택 버튼 묶음. 기회가 없을 때 이 오브젝트만 꺼서 자동 복귀임을 드러낸다.
            GameObject choice = new GameObject("ChoiceGroup", typeof(RectTransform));
            choice.transform.SetParent(root.transform, false);
            SetRect((RectTransform)choice.transform, new Vector2(0f, -80f), new Vector2(720f, 120f));

            Button revive = CreateButton(choice.transform, "ReviveButton", "부활", new Vector2(-140f, 0f));
            Button giveUp = CreateButton(choice.transform, "GiveUpButton", "포기", new Vector2(140f, 0f));
            giveUp.targetGraphic.color = new Color(0.35f, 0.35f, 0.4f);

            TextMeshProUGUI timer = CreateLabel(root.transform, "ReturnTimerText", "5", new Vector2(0f, -200f), 40f);
            timer.color = new Color(0.9f, 0.8f, 0.4f);
            timer.gameObject.SetActive(false);

            DeathPopup popup = root.GetComponent<DeathPopup>();
            Wire(popup, revive, giveUp, count, message, timer, choice);

            // 꺼진 상태로 시작한다. BasePopup은 UIManager가 Show할 때 켜는 것을 전제로 하고,
            // 켠 채 두면 게임 시작하자마자 사망 창이 화면을 덮는다.
            root.SetActive(false);
            return popup;
        }

        // private [SerializeField]는 밖에서 대입할 수 없으므로 SerializedObject로 채운다.
        // 필드 이름이 바뀌면 여기도 같이 고쳐야 한다(이름 문자열 참조라 컴파일러가 안 잡아준다).
        private static void Wire(DeathPopup popup, Button revive, Button giveUp,
                                 TMP_Text count, TMP_Text message, TMP_Text timer, GameObject choice)
        {
            SerializedObject so = new SerializedObject(popup);
            so.FindProperty("reviveButton").objectReferenceValue = revive;
            so.FindProperty("giveUpButton").objectReferenceValue = giveUp;
            so.FindProperty("reviveCountText").objectReferenceValue = count;
            so.FindProperty("messageText").objectReferenceValue = message;
            so.FindProperty("returnTimerText").objectReferenceValue = timer;
            so.FindProperty("choiceGroup").objectReferenceValue = choice;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 프리젠터는 팝업이 아니라 UIManager 직속의 별도 오브젝트다. 팝업 자식으로 넣으면 팝업이 꺼져 있는
        // 동안 사망을 못 듣고, HUD 안에 넣으면 HUD가 닫힐 때 같은 문제가 생긴다. 뷰가 아니라 Canvas는 필요 없다.
        private static DeathPresenter BuildPresenter(Transform parent, DeathPopup popup)
        {
            GameObject host = new GameObject(PresenterName, typeof(DeathPresenter));
            Undo.RegisterCreatedObjectUndo(host, "Create DeathPresenter");
            host.transform.SetParent(parent, false);

            DeathPresenter presenter = host.GetComponent<DeathPresenter>();

            SerializedObject so = new SerializedObject(presenter);
            so.FindProperty("deathPopup").objectReferenceValue = popup;
            so.ApplyModifiedPropertiesWithoutUndo();

            return presenter;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            SetRect((RectTransform)go.transform, pos, new Vector2(220f, 72f));

            Image img = go.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.color = new Color(0.25f, 0.5f, 0.9f);

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(go.transform, "Text", label, 28f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            FillParent(text.rectTransform);

            return btn;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string content, Vector2 pos, float fontSize)
        {
            TextMeshProUGUI tmp = CreateTMP(parent, name, content, fontSize);
            tmp.alignment = TextAlignmentOptions.Center;
            SetRect(tmp.rectTransform, pos, new Vector2(720f, 90f));
            return tmp;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, string content, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.raycastTarget = false;   // 텍스트가 버튼 클릭을 먹지 않게.

            if (s_font != null) tmp.font = s_font;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;

            return tmp;
        }

        // 화면 중앙 기준 배치. 앵커를 중앙으로 모아 해상도가 바뀌어도 가운데를 유지한다.
        private static void SetRect(RectTransform rect, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private static void FillParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // 프로젝트에서 'Paperlogy' TMP 폰트 에셋을 찾는다(한글 글리프 포함). AuthUIGenerator와 같은 규칙.
        private static TMP_FontAsset ResolveKoreanFont()
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

            foreach (string guid in guids)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font.name == "Paperlogy-5Medium SDF") return font;
            }
            foreach (string guid in guids)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font.name.Contains("Paperlogy")) return font;
            }
            return null;
        }
    }
}
