using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 진입 화면(캐릭터 선택·클래스·생성) UI 생성기들이 공유하는 조립 헬퍼.
    ///
    /// 페이지마다 생성기를 따로 두되, 캔버스 규격·한글 폰트 해석·앵커 배치·private 필드 배선처럼
    /// 페이지가 달라도 똑같은 부분을 여기 모은다. 각 생성기가 복사본을 갖고 있으면
    /// 캔버스 해상도나 폰트 규칙을 바꿀 때 한 곳을 빠뜨려 페이지마다 다르게 렌더된다.
    /// </summary>
    public static class EntryUIBuilder
    {
        /// <summary>UI 캔버스 오브젝트 이름. 생성기들이 이 이름으로 기존 캔버스를 찾는다.</summary>
        public const string CanvasName = "UICanvas";

        /// <summary>캐릭터 프리뷰 RenderTexture 경로. 선택·생성 페이지가 공유한다.</summary>
        public const string PreviewTexturePath = "Assets/Textures/UI/RT_CharacterPreview.renderTexture";

        /// <summary>프리뷰 카메라 리그의 씬 오브젝트 이름.</summary>
        public const string StageName = "CharacterStage";

        private const string KoreanFontName = "Paperlogy-5Medium SDF";
        private const string PreviewTextureFolder = "Assets/Textures/UI";

        // 스테이지를 원점에서 이만큼 내려 배치한다(다른 씬 오브젝트가 프리뷰에 찍히지 않게).
        private const float StageOffsetY = -1000f;

        private static TMP_FontAsset cachedFont;
        private static Sprite cachedSprite;

        /// <summary>
        /// 프로젝트의 한글 TMP 폰트(Paperlogy). 못 찾으면 경고 후 기본 폰트로 떨어진다
        /// — 그 경우 생성된 UI의 한글이 깨져 보인다.
        /// </summary>
        public static TMP_FontAsset KoreanFont
        {
            get
            {
                if (cachedFont != null) return cachedFont;
                cachedFont = FindKoreanFont();
                return cachedFont;
            }
        }

        /// <summary>버튼·팝업 배경에 쓰는 유니티 기본 9-slice 스프라이트.</summary>
        public static Sprite UISprite
        {
            get
            {
                if (cachedSprite != null) return cachedSprite;
                cachedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                return cachedSprite;
            }
        }

        // ── 씬 구성 요소 ───────────────────────────────────────────

        /// <summary>EventSystem이 없으면 만든다. 없으면 버튼이 하나도 눌리지 않는다.</summary>
        /// <param name="undoLabel">Undo 기록에 남길 작업 이름</param>
        public static void EnsureEventSystem(string undoLabel)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem));

            // 프로젝트가 Input System을 쓰므로 그쪽 모듈을 우선한다(구 모듈과 섞이면 입력이 안 먹는다).
            var newModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newModule != null) go.AddComponent(newModule);
            else go.AddComponent<StandaloneInputModule>();

            Undo.RegisterCreatedObjectUndo(go, undoLabel);
        }

        /// <summary>
        /// <see cref="CanvasName"/> 캔버스를 찾고, 없으면 1920×1080 기준으로 만든다.
        /// 페이지 생성기들이 각각 호출하므로 규격은 반드시 여기 한 곳에서만 정한다.
        /// </summary>
        /// <param name="undoLabel">Undo 기록에 남길 작업 이름</param>
        /// <returns>찾았거나 새로 만든 캔버스</returns>
        public static Canvas EnsureCanvas(string undoLabel)
        {
            foreach (Canvas existing in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (existing.name == CanvasName) return existing;
            }

            GameObject go = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, undoLabel);
            return canvas;
        }

        /// <summary>에셋 폴더가 없으면 상위부터 차례로 만든다.</summary>
        /// <param name="folder">"Assets/..." 형태의 폴더 경로</param>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ── 요소 조립 ──────────────────────────────────────────────

        /// <summary>
        /// 한글 폰트가 물린 TMP 텍스트를 만든다. RectTransform은 호출부가 잡는다.
        /// raycastTarget은 꺼둔다 — 라벨이 클릭을 먹으면 뒤의 버튼이 안 눌린다.
        /// </summary>
        /// <param name="parent">붙일 부모</param>
        /// <param name="name">오브젝트 이름</param>
        /// <param name="content">표시할 문구</param>
        /// <param name="fontSize">폰트 크기</param>
        /// <returns>만들어진 텍스트</returns>
        public static TextMeshProUGUI CreateTMP(Transform parent, string name, string content, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.raycastTarget = false;

            if (KoreanFont != null) tmp.font = KoreanFont;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;

            return tmp;
        }

        /// <summary>
        /// 배경 이미지 + 가운데 라벨을 가진 버튼을 만든다. RectTransform은 호출부가 잡는다.
        /// 라벨을 함께 돌려주는 이유: 확인 팝업처럼 라벨을 런타임에 바꾸는 곳이 참조를 필요로 한다.
        /// </summary>
        /// <param name="parent">붙일 부모</param>
        /// <param name="name">오브젝트 이름</param>
        /// <param name="text">버튼 문구</param>
        /// <param name="fontSize">라벨 폰트 크기</param>
        /// <param name="color">버튼 배경색</param>
        /// <returns>버튼과 그 라벨</returns>
        public static (Button button, TextMeshProUGUI label) CreateButton(
            Transform parent, string name, string text, float fontSize, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = UISprite;
            img.type = Image.Type.Sliced;
            img.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI label = CreateTMP(go.transform, "Text", text, fontSize);
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            Fill(label.rectTransform);

            return (btn, label);
        }

        /// <summary>부모를 가득 채우는 이미지를 만든다(배경·딤·입력 차단막).</summary>
        /// <param name="parent">붙일 부모</param>
        /// <param name="name">오브젝트 이름</param>
        /// <param name="color">채울 색</param>
        /// <param name="blocksRaycast">true면 뒤쪽 클릭을 막는다</param>
        /// <returns>만들어진 이미지</returns>
        public static Image CreateFullScreenImage(Transform parent, string name, Color color, bool blocksRaycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Fill(go.GetComponent<RectTransform>());

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = blocksRaycast;
            return img;
        }

        /// <summary>
        /// 배경 + 텍스트 영역 + 플레이스홀더를 갖춘 TMP 입력 필드를 만든다.
        /// RectTransform은 호출부가 잡는다.
        /// </summary>
        /// <param name="parent">붙일 부모</param>
        /// <param name="name">오브젝트 이름</param>
        /// <param name="placeholder">비었을 때 보여줄 안내 문구</param>
        /// <param name="fontSize">글자 크기</param>
        /// <returns>만들어진 입력 필드</returns>
        public static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, float fontSize)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);

            Image bg = root.GetComponent<Image>();
            bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;

            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.targetGraphic = bg;

            // 텍스트가 필드 밖으로 흘러나오지 않게 잘라 주는 영역. 없으면 긴 이름이 옆으로 삐져나온다.
            GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(root.transform, false);
            RectTransform areaRt = area.GetComponent<RectTransform>();
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(16f, 8f);
            areaRt.offsetMax = new Vector2(-16f, -8f);

            TextMeshProUGUI hint = CreateTMP(area.transform, "Placeholder", placeholder, fontSize);
            hint.fontStyle = FontStyles.Italic;
            hint.color = new Color(0.35f, 0.35f, 0.35f, 0.75f);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(hint.rectTransform);

            TextMeshProUGUI text = CreateTMP(area.transform, "Text", string.Empty, fontSize);
            text.color = Color.black;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            Fill(text.rectTransform);

            input.textViewport = areaRt;
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = TMP_InputField.LineType.SingleLine;

            return input;
        }

        // ── 3D 프리뷰 리그 ─────────────────────────────────────────

        /// <summary>
        /// 캐릭터 프리뷰용 RenderTexture를 찾고, 없으면 만든다.
        /// 선택 페이지와 생성 페이지가 같은 텍스처를 공유하므로 경로를 한 곳에서만 정한다.
        /// </summary>
        /// <returns>프리뷰 RenderTexture</returns>
        public static RenderTexture EnsurePreviewTexture()
        {
            RenderTexture existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(PreviewTexturePath);
            if (existing != null) return existing;

            EnsureFolder(PreviewTextureFolder);

            RenderTexture rt = new RenderTexture(1024, 688, 24, RenderTextureFormat.ARGB32)
            {
                name = "RT_CharacterPreview",
                antiAliasing = 2,
            };
            AssetDatabase.CreateAsset(rt, PreviewTexturePath);
            return rt;
        }

        /// <summary>
        /// 프리뷰를 그릴 카메라 리그(CharacterStage)를 찾고, 없으면 만든다.
        /// 다른 씬 오브젝트가 프리뷰에 찍히지 않도록 원점에서 멀리 떨어뜨려 둔다 —
        /// 임시 방편이라 나중에 전용 레이어와 컬링 마스크로 가르는 편이 확실하다.
        /// </summary>
        /// <param name="preview">카메라가 그릴 대상 텍스처</param>
        /// <param name="undoLabel">Undo 기록에 남길 작업 이름</param>
        /// <returns>캐릭터 프리팹이 붙을 ModelRoot</returns>
        public static Transform EnsureCharacterStage(RenderTexture preview, string undoLabel)
        {
            GameObject existing = GameObject.Find(StageName);
            if (existing != null)
            {
                Transform found = existing.transform.Find("ModelRoot");
                if (found != null) return found;
            }

            GameObject stage = existing != null ? existing : new GameObject(StageName);
            stage.transform.position = new Vector3(0f, StageOffsetY, 0f);

            if (stage.transform.Find("StageCamera") == null)
            {
                GameObject cameraGo = new GameObject("StageCamera", typeof(Camera));
                cameraGo.transform.SetParent(stage.transform, false);
                cameraGo.transform.localPosition = new Vector3(0f, 1.1f, -3.2f);
                cameraGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

                Camera cam = cameraGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.1f, 0.12f, 0.15f, 1f);
                cam.fieldOfView = 30f;
                cam.targetTexture = preview;
                cam.depth = -10f;   // 메인 카메라보다 먼저 그려 프리뷰가 한 프레임 늦지 않게
            }

            if (stage.transform.Find("StageLight") == null)
            {
                GameObject lightGo = new GameObject("StageLight", typeof(Light));
                lightGo.transform.SetParent(stage.transform, false);
                lightGo.transform.localRotation = Quaternion.Euler(35f, -35f, 0f);

                Light light = lightGo.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
            }

            GameObject modelRoot = new GameObject("ModelRoot");
            modelRoot.transform.SetParent(stage.transform, false);

            if (existing == null) Undo.RegisterCreatedObjectUndo(stage, undoLabel);
            return modelRoot.transform;
        }

        // ── 배치 ───────────────────────────────────────────────────

        /// <summary>부모를 가득 채우도록 앵커를 편다.</summary>
        /// <param name="rt">배치할 RectTransform</param>
        public static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>부모 위쪽에 붙이고 가로로 늘린다(제목 줄·본문 줄).</summary>
        /// <param name="rt">배치할 RectTransform</param>
        /// <param name="bottom">위 모서리에서 아래 끝까지의 거리(음수)</param>
        /// <param name="top">위 모서리에서 위 끝까지의 거리(음수)</param>
        /// <param name="inset">좌우 여백</param>
        public static void StretchTop(RectTransform rt, float bottom, float top, float inset = 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(inset, bottom);
            rt.offsetMax = new Vector2(-inset, top);
        }

        /// <summary>부모 아래쪽에 붙이고 가로로 늘린다(하단 노트 줄).</summary>
        /// <param name="rt">배치할 RectTransform</param>
        /// <param name="bottom">아래 모서리에서 아래 끝까지의 거리</param>
        /// <param name="top">아래 모서리에서 위 끝까지의 거리</param>
        /// <param name="inset">좌우 여백</param>
        public static void StretchBottom(RectTransform rt, float bottom, float top, float inset = 0f)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(inset, bottom);
            rt.offsetMax = new Vector2(-inset, top);
        }

        /// <summary>부모 중앙 기준으로 위치와 크기를 잡는다.</summary>
        /// <param name="rt">배치할 RectTransform</param>
        /// <param name="position">중앙에서의 오프셋</param>
        /// <param name="size">크기</param>
        public static void SetCenter(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        /// <summary>부모 아래 모서리 기준으로 위치와 크기를 잡는다(팝업 하단 버튼).</summary>
        /// <param name="rt">배치할 RectTransform</param>
        /// <param name="position">아래 모서리 중앙에서의 오프셋</param>
        /// <param name="size">크기</param>
        public static void SetBottomCenter(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        // ── 배선 ───────────────────────────────────────────────────

        /// <summary>
        /// private [SerializeField] 단일 참조를 연결한다.
        /// 필드를 public으로 열지 않으려고 SerializedObject를 거친다.
        /// </summary>
        /// <param name="comp">대상 컴포넌트</param>
        /// <param name="refs">(필드 이름, 넣을 값) 쌍</param>
        public static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new SerializedObject(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[EntryUIBuilder] {comp.GetType().Name}에 '{prop}' 필드가 없습니다. 이름이 바뀌었는지 확인하세요.");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>private [SerializeField] 배열/리스트 필드를 연결한다.</summary>
        /// <param name="comp">대상 컴포넌트</param>
        /// <param name="prop">필드 이름</param>
        /// <param name="elements">채울 요소들</param>
        public static void WireList(Component comp, string prop, params Object[] elements)
        {
            SerializedObject so = new SerializedObject(comp);
            SerializedProperty p = so.FindProperty(prop);

            if (p == null || !p.isArray)
            {
                Debug.LogWarning($"[EntryUIBuilder] {comp.GetType().Name}의 '{prop}'가 배열 필드가 아닙니다.");
                return;
            }

            p.arraySize = elements.Length;
            for (int i = 0; i < elements.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = elements[i];

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_FontAsset FindKoreanFont()
        {
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

            foreach (string guid in guids)
            {
                TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name == KoreanFontName) return asset;
            }
            foreach (string guid in guids)
            {
                TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name.Contains("Paperlogy")) return asset;
            }

            Debug.LogWarning($"[EntryUIBuilder] '{KoreanFontName}' TMP 폰트를 못 찾아 기본 폰트를 씁니다. 한글이 깨지면 폰트를 확인하세요.");
            return TMP_Settings.defaultFontAsset;
        }
    }
}
