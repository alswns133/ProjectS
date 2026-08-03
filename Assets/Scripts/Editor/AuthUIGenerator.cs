using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using ProjectS.UI;
using ProjectS.Managers;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 로그인/가입 + 캐릭터 선택 UI를 현재 씬에 한 번에 생성하고, 키보드 내비게이션·비번 토글·
    /// Caps Lock 경고·포커스 하이라이트까지 배선하는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Auth UI  (구조가 바뀌면 기존 Canvas 삭제 후 재실행)
    /// </summary>
    public static class AuthUIGenerator
    {
        // 생성되는 모든 TMP 텍스트에 쓸 한글 폰트(Paperlogy). CreateAuthUI 시작에서 1회 해석한다.
        private static TMP_FontAsset s_font;

        [MenuItem("Tools/ProjectS/Create Auth UI")]
        public static void CreateAuthUI()
        {
            s_font = ResolveKoreanFont();

            EnsureEventSystem();
            EnsureFirebaseManager();

            Canvas canvas = CreateCanvas();

            // 인증 패널 묶음(로그인 성공/Esc 로그아웃으로 통째 토글). 키보드 내비·Caps 경고가 여기 붙는다.
            GameObject authRoot = CreateFullScreen(canvas.transform, "AuthRoot");

            // Caps Lock 경고(상단, 기본 숨김).
            TextMeshProUGUI caps = CreateLabel(authRoot.transform, "CapsWarning", "Caps Lock이 켜져 있습니다", new Vector2(0f, 470f), 22f, TextAlignmentOptions.Center);
            caps.rectTransform.sizeDelta = new Vector2(700f, 44f);
            caps.color = new Color(1f, 0.75f, 0.2f);
            caps.gameObject.SetActive(false);

            var login = BuildLoginPanel(authRoot.transform);
            var signup = BuildSignupPanel(authRoot.transform);

            // 캐릭터 선택 패널(처음엔 비활성 — 로그인 성공 시 켜짐).
            GameObject selectPanel = BuildCharacterSelectPanel(canvas.transform, authRoot);
            selectPanel.SetActive(false);

            // 로그인/가입 성공 → authRoot 숨김 + selectPanel 표시.
            Wire(login.ui, ("authRoot", authRoot), ("characterSelectRoot", selectPanel));
            Wire(signup.ui, ("authRoot", authRoot), ("characterSelectRoot", selectPanel));

            // 키보드 내비게이션: 하나가 두 폼 전체를 담당(포커스 충돌 방지). 순서 = 로그인 → 가입.
            FormNavigation nav = authRoot.AddComponent<FormNavigation>();
            WireList(nav, "order",
                login.email, login.pw, login.button,
                signup.email, signup.pw, signup.confirm, signup.button);

            // Caps Lock 경고: 비번 필드 포커스 + 캡스락 켜짐일 때만 표시.
            CapsLockIndicator capsInd = authRoot.AddComponent<CapsLockIndicator>();
            Wire(capsInd, ("warning", caps.gameObject));
            WireList(capsInd, "passwordFields", login.pw, signup.pw, signup.confirm);

            // 포커스 하이라이트(키보드 위치 표시).
            foreach (Selectable s in new Selectable[] { login.email, login.pw, login.button, signup.email, signup.pw, signup.confirm, signup.button })
                ApplyFocusColors(s);

            Selection.activeGameObject = canvas.gameObject;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[AuthUIGenerator] 로그인/가입/캐릭터선택 + 키보드 내비 생성 완료. Play로 테스트하세요.");
        }

        // ── 상위 구성 ──────────────────────────────────────────────

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            GameObject es = new GameObject("EventSystem", typeof(EventSystem));
            var newModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newModule != null) es.AddComponent(newModule);
            else es.AddComponent<StandaloneInputModule>();

            Undo.RegisterCreatedObjectUndo(es, "Create Auth UI");
        }

        private static void EnsureFirebaseManager()
        {
            if (Object.FindAnyObjectByType<FirebaseManager>() != null) return;

            GameObject go = new GameObject("FirebaseManager", typeof(FirebaseManager));
            Undo.RegisterCreatedObjectUndo(go, "Create Auth UI");
        }

        private static Canvas CreateCanvas()
        {
            GameObject go = new GameObject("AuthCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Auth UI");
            return canvas;
        }

        private static GameObject CreateFullScreen(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            FillParent(go.GetComponent<RectTransform>());
            return go;
        }

        // ── 패널 ───────────────────────────────────────────────────

        private static (LoginUI ui, TMP_InputField email, TMP_InputField pw, Button button) BuildLoginPanel(Transform parent)
        {
            RectTransform panel = CreatePanel(parent, "LoginPanel", new Vector2(290f, 0f), new Vector2(500f, 500f));

            CreateLabel(panel, "TitleText", "로그인", new Vector2(0f, 190f), 32f, TextAlignmentOptions.Center);
            TMP_InputField email = CreateInputField(panel, "EmailField", new Vector2(0f, 110f), new Vector2(440f, 60f), "이메일", false);
            TMP_InputField pw = CreatePasswordFieldWithToggle(panel, "Password", new Vector2(0f, 30f), "비밀번호");
            Toggle autoLogin = CreateToggle(panel, "AutoLoginToggle", "자동 로그인", new Vector2(-40f, -40f), false);
            Button button = CreateButton(panel, "LoginButton", "로그인", new Vector2(0f, -110f), new Vector2(440f, 60f));
            TextMeshProUGUI message = CreateLabel(panel, "MessageText", "", new Vector2(0f, -185f), 20f, TextAlignmentOptions.Center);
            message.color = new Color(0.95f, 0.4f, 0.4f);

            LoginUI ui = panel.gameObject.AddComponent<LoginUI>();
            Wire(ui,
                ("emailField", email),
                ("passwordField", pw),
                ("loginButton", button),
                ("messageText", message),
                ("autoLoginToggle", autoLogin));
            return (ui, email, pw, button);
        }

        private static (SignupUI ui, TMP_InputField email, TMP_InputField pw, TMP_InputField confirm, Button button) BuildSignupPanel(Transform parent)
        {
            RectTransform panel = CreatePanel(parent, "SignupPanel", new Vector2(-290f, 0f), new Vector2(500f, 540f));

            CreateLabel(panel, "TitleText", "회원가입", new Vector2(0f, 230f), 32f, TextAlignmentOptions.Center);
            TMP_InputField email = CreateInputField(panel, "EmailField", new Vector2(0f, 150f), new Vector2(440f, 60f), "이메일", false);
            TMP_InputField pw = CreatePasswordFieldWithToggle(panel, "Password", new Vector2(0f, 70f), "비밀번호(6자 이상)");
            TMP_InputField confirm = CreatePasswordFieldWithToggle(panel, "Confirm", new Vector2(0f, -10f), "비밀번호 확인");
            Button button = CreateButton(panel, "SignupButton", "회원가입", new Vector2(0f, -90f), new Vector2(440f, 60f));
            TextMeshProUGUI message = CreateLabel(panel, "MessageText", "", new Vector2(0f, -170f), 20f, TextAlignmentOptions.Center);
            message.color = new Color(0.95f, 0.4f, 0.4f);

            SignupUI ui = panel.gameObject.AddComponent<SignupUI>();
            Wire(ui,
                ("emailField", email),
                ("passwordField", pw),
                ("confirmField", confirm),
                ("signupButton", button),
                ("messageText", message));
            return (ui, email, pw, confirm, button);
        }

        private static GameObject BuildCharacterSelectPanel(Transform parent, GameObject authRoot)
        {
            RectTransform panel = CreatePanel(parent, "CharacterSelectPanel", Vector2.zero, new Vector2(760f, 640f));

            CreateLabel(panel, "TitleText", "캐릭터 선택 (Esc: 로그아웃)", new Vector2(0f, 270f), 32f, TextAlignmentOptions.Center);

            RectTransform list = CreateListContainer(panel, "ListContainer", new Vector2(0f, 70f), new Vector2(680f, 320f));

            TMP_InputField nameField = CreateInputField(panel, "NameField", new Vector2(0f, -140f), new Vector2(680f, 60f), "캐릭터 이름(2~12자)", false);
            Button swordsman = CreateButton(panel, "CreateSwordsmanButton", "검사 생성", new Vector2(-175f, -220f), new Vector2(320f, 60f));
            Button gunner = CreateButton(panel, "CreateGunnerButton", "거너 생성", new Vector2(175f, -220f), new Vector2(320f, 60f));
            TextMeshProUGUI message = CreateLabel(panel, "MessageText", "", new Vector2(0f, -285f), 20f, TextAlignmentOptions.Center);
            message.rectTransform.sizeDelta = new Vector2(680f, 50f);
            message.color = new Color(0.95f, 0.85f, 0.4f);

            CharacterSelectUI ui = panel.gameObject.AddComponent<CharacterSelectUI>();
            Wire(ui,
                ("listContainer", list),
                ("nameField", nameField),
                ("createSwordsmanButton", swordsman),
                ("createGunnerButton", gunner),
                ("messageText", message),
                ("rowFont", s_font),
                ("authRoot", authRoot));   // Esc 로그아웃 시 되돌아갈 패널

            return panel.gameObject;
        }

        // ── 요소 빌더 ──────────────────────────────────────────────

        // 비밀번호 필드 + 오른쪽 "표시/숨김" 토글 버튼을 한 세트로 만든다.
        private static TMP_InputField CreatePasswordFieldWithToggle(Transform panel, string namePrefix, Vector2 pos, string placeholder)
        {
            TMP_InputField field = CreateInputField(panel, namePrefix + "Field", new Vector2(pos.x - 30f, pos.y), new Vector2(390f, 60f), placeholder, true);

            Button eye = CreateButton(panel, namePrefix + "ToggleButton", "표시", new Vector2(pos.x + 200f, pos.y), new Vector2(56f, 60f));
            eye.GetComponent<Image>().color = new Color(0.32f, 0.32f, 0.38f, 1f);
            TextMeshProUGUI eyeLabel = eye.GetComponentInChildren<TextMeshProUGUI>();
            if (eyeLabel != null) eyeLabel.fontSize = 18f;

            PasswordVisibilityToggle toggle = eye.gameObject.AddComponent<PasswordVisibilityToggle>();
            Wire(toggle, ("passwordField", field), ("label", eyeLabel));

            return field;
        }

        // 체크박스 토글(배경 + 체크마크 + 라벨). Toggle.targetGraphic=배경, graphic=체크마크.
        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 pos, bool isOn)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), pos, new Vector2(300f, 36f));

            // 체크박스 배경(루트 왼쪽)
            GameObject box = new GameObject("Background", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(root.transform, false);
            RectTransform boxRt = box.GetComponent<RectTransform>();
            boxRt.anchorMin = new Vector2(0f, 0.5f);
            boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.anchoredPosition = Vector2.zero;
            boxRt.sizeDelta = new Vector2(30f, 30f);
            Image boxImg = box.GetComponent<Image>();
            boxImg.color = Color.white;
            boxImg.type = Image.Type.Sliced;
            boxImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

            // 체크마크(켜짐 시 표시 — Toggle이 graphic으로 on/off)
            GameObject check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(box.transform, false);
            FillParent(check.GetComponent<RectTransform>());
            Image checkImg = check.GetComponent<Image>();
            checkImg.color = new Color(0.2f, 0.5f, 0.9f, 1f);
            checkImg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

            // 라벨(체크박스 오른쪽)
            TextMeshProUGUI text = CreateTMP(root.transform, "Label", label, 20f);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
            RectTransform trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(0f, 0.5f);
            trt.offsetMin = new Vector2(40f, -18f);
            trt.offsetMax = new Vector2(0f, 18f);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = isOn;

            return toggle;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            SetRect(rt, pos, size);

            Image img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.35f);
            return rt;
        }

        private static RectTransform CreateListContainer(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            SetRect(go.GetComponent<RectTransform>(), pos, size);

            VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            return go.GetComponent<RectTransform>();
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, Vector2 pos, Vector2 size, string placeholder, bool isPassword)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), pos, size);

            Image bg = root.GetComponent<Image>();
            bg.color = Color.white;
            bg.type = Image.Type.Sliced;
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");

            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.targetGraphic = bg;

            GameObject textArea = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            textArea.transform.SetParent(root.transform, false);
            RectTransform ta = textArea.GetComponent<RectTransform>();
            ta.anchorMin = Vector2.zero;
            ta.anchorMax = Vector2.one;
            ta.offsetMin = new Vector2(12f, 6f);
            ta.offsetMax = new Vector2(-12f, -6f);

            TextMeshProUGUI ph = CreateTMP(textArea.transform, "Placeholder", placeholder, 22f);
            ph.fontStyle = FontStyles.Italic;
            ph.color = new Color(0.35f, 0.35f, 0.35f, 0.75f);
            FillParent(ph.rectTransform);

            TextMeshProUGUI text = CreateTMP(textArea.transform, "Text", "", 22f);
            text.color = Color.black;
            FillParent(text.rectTransform);

            input.textViewport = ta;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (isPassword) input.contentType = TMP_InputField.ContentType.Password;

            return input;
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), pos, size);

            Image img = root.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.color = new Color(0.25f, 0.5f, 0.9f, 1f);

            Button btn = root.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(root.transform, "Text", label, 24f);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            FillParent(text.rectTransform);

            return btn;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string content, Vector2 pos, float fontSize, TextAlignmentOptions align)
        {
            TextMeshProUGUI tmp = CreateTMP(parent, name, content, fontSize);
            tmp.alignment = align;
            SetRect(tmp.rectTransform, pos, new Vector2(440f, 60f));
            return tmp;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, string content, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = Color.black;
            if (s_font != null) tmp.font = s_font;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        // 프로젝트에서 'Paperlogy-5Medium SDF' TMP 폰트 에셋을 찾는다(한글 글리프 포함).
        private static TMP_FontAsset ResolveKoreanFont()
        {
            const string target = "Paperlogy-5Medium SDF";
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

            foreach (string guid in guids)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font.name == target) return font;
            }
            foreach (string guid in guids)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (font != null && font.name.Contains("Paperlogy")) return font;
            }

            Debug.LogWarning("[AuthUIGenerator] 'Paperlogy-5Medium SDF' TMP 폰트를 못 찾아 기본 폰트를 씁니다. " +
                "Font Asset Creator로 만들었는지·이름이 맞는지 확인하세요.");
            return TMP_Settings.defaultFontAsset;
        }

        // ── 유틸 ───────────────────────────────────────────────────

        // 포커스(선택) 시 눈에 띄는 색으로 하이라이트한다(키보드로 어디 있는지 보이게).
        private static void ApplyFocusColors(Selectable s)
        {
            if (s == null) return;

            ColorBlock cb = s.colors;
            cb.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
            cb.selectedColor = new Color(1f, 0.92f, 0.55f, 1f);
            cb.fadeDuration = 0.05f;
            s.colors = cb;
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

        // private [SerializeField] 리스트/배열 필드를 SerializedObject로 연결한다.
        private static void WireList(Component comp, string prop, params Object[] elements)
        {
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty p = so.FindProperty(prop);
            if (p != null && p.isArray)
            {
                p.arraySize = elements.Length;
                for (int i = 0; i < elements.Length; i++)
                    p.GetArrayElementAtIndex(i).objectReferenceValue = elements[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
