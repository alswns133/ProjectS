using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 캐릭터 생성 페이지(Page_Create)를 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Page Create
    ///
    /// 캐릭터 선택 페이지와 같은 프리뷰 텍스처·스테이지를 공유한다(없으면 만든다).
    /// 회전은 드래그와 화살표 둘 다 지원하며, 둘 모두 <see cref="ModelViewportRotator"/> 하나를
    /// 거치게 배선해 서로 다른 각도를 들고 있지 않도록 한다.
    /// </summary>
    public static class CharacterCreatePageGenerator
    {
        private const string UndoLabel = "Create Page Create";

        // 모델 뷰포트. 클래스 선택과 달리 화면 가운데를 크게 차지한다.
        private const float ViewportWidth = 880f;
        private const float ViewportHeight = 800f;
        private const float ViewportOffsetY = 60f;

        // 회전 화살표는 뷰포트 rect 바깥에 둔다. 겹치면 그 자리에서 드래그를 시작할 때
        // 버튼이 입력을 먼저 먹어 회전이 걸리지 않는다.
        private const float ArrowGap = 40f;
        private const float ArrowWidth = 72f;
        private const float ArrowHeight = 96f;

        private const float NameRowY = -380f;
        private const float NameRowWidth = 900f;
        private const float NameRowHeight = 72f;
        private const float HintY = -434f;

        private static readonly Color ButtonColor = new Color(0.18f, 0.21f, 0.27f, 1f);
        private static readonly Color AccentButtonColor = new Color(0.16f, 0.5f, 0.42f, 1f);
        private static readonly Color HintColor = new Color(0.95f, 0.62f, 0.45f, 1f);

        [MenuItem("Tools/ProjectS/Create Page Create")]
        public static void CreatePageCreate()
        {
            EntryUIBuilder.EnsureEventSystem(UndoLabel);
            Canvas canvas = EntryUIBuilder.EnsureCanvas(UndoLabel);

            Transform existing = canvas.transform.Find("Page_Create");
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("Page_Create 다시 만들기",
                    "이미 Page_Create가 있습니다.\n지우고 새로 만들면 인스펙터에서 손본 배치·문구가 사라집니다.",
                    "지우고 다시 만들기", "취소");
                if (!rebuild) return;

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            RenderTexture preview = EntryUIBuilder.EnsurePreviewTexture();
            Transform modelRoot = EntryUIBuilder.EnsureCharacterStage(preview, UndoLabel);

            GameObject page = BuildPage(canvas.transform, preview, modelRoot);

            Undo.RegisterCreatedObjectUndo(page, UndoLabel);
            Selection.activeGameObject = page;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[CharacterCreatePageGenerator] Page_Create 생성 완료. 회전은 드래그·화살표 둘 다 붙었습니다.");
        }

        private static GameObject BuildPage(Transform canvas, RenderTexture preview, Transform modelRoot)
        {
            GameObject page = new GameObject("Page_Create", typeof(RectTransform));
            page.transform.SetParent(canvas, false);
            EntryUIBuilder.Fill(page.GetComponent<RectTransform>());

            (RawImage viewport, ModelViewportRotator rotator) = BuildViewport(page.transform, preview, modelRoot);

            Button rotateLeft = BuildArrow(page.transform, "RotateLeft", -(ViewportWidth * 0.5f + ArrowGap), "<");
            Button rotateRight = BuildArrow(page.transform, "RotateRight", ViewportWidth * 0.5f + ArrowGap, ">");

            (TMP_InputField nameField, Button createButton) = BuildNameRow(page.transform);
            TextMeshProUGUI hint = BuildHint(page.transform);
            Button prev = BuildPrevButton(page.transform);

            CharacterCreatePageView view = page.AddComponent<CharacterCreatePageView>();
            EntryUIBuilder.Wire(view,
                ("modelViewport", viewport),
                ("rotator", rotator),
                ("rotateLeftButton", rotateLeft),
                ("rotateRightButton", rotateRight),
                ("nameField", nameField),
                ("createButton", createButton),
                ("nameHintText", hint),
                ("prevButton", prev));

            createButton.interactable = false;   // 이름이 규칙에 맞기 전까지 잠금

            return page;
        }

        // 드래그 회전을 받으려면 뷰포트 그래픽의 raycastTarget이 켜져 있어야 한다.
        // 선택 페이지의 뷰포트는 클릭을 받을 일이 없어 꺼두지만, 여기는 반대다.
        private static (RawImage viewport, ModelViewportRotator rotator) BuildViewport(
            Transform parent, RenderTexture preview, Transform modelRoot)
        {
            GameObject go = new GameObject("ModelViewport",
                typeof(RectTransform), typeof(RawImage), typeof(ModelViewportRotator));
            go.transform.SetParent(parent, false);

            EntryUIBuilder.SetCenter(go.GetComponent<RectTransform>(),
                new Vector2(0f, ViewportOffsetY), new Vector2(ViewportWidth, ViewportHeight));

            RawImage raw = go.GetComponent<RawImage>();
            raw.texture = preview;
            raw.color = Color.white;
            raw.raycastTarget = true;

            ModelViewportRotator rotator = go.GetComponent<ModelViewportRotator>();
            EntryUIBuilder.Wire(rotator, ("target", modelRoot));

            return (raw, rotator);
        }

        private static Button BuildArrow(Transform parent, string name, float offsetX, string label)
        {
            Button btn = EntryUIBuilder.CreateButton(parent, name, label, 32f, ButtonColor).button;
            EntryUIBuilder.SetCenter(btn.GetComponent<RectTransform>(),
                new Vector2(offsetX, ViewportOffsetY), new Vector2(ArrowWidth, ArrowHeight));
            return btn;
        }

        // 입력창과 생성 버튼을 나란히 둔다. 버튼이 필드 바로 옆이라 "이름을 정하고 만든다"가
        // 한 덩어리로 읽힌다.
        private static (TMP_InputField field, Button create) BuildNameRow(Transform parent)
        {
            GameObject row = new GameObject("NameRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            EntryUIBuilder.SetCenter(row.GetComponent<RectTransform>(),
                new Vector2(0f, NameRowY), new Vector2(NameRowWidth, NameRowHeight));

            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 20f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;      // 각자의 sizeDelta를 그대로 쓴다
            hlg.childForceExpandWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandHeight = false;

            TMP_InputField field = EntryUIBuilder.CreateInputField(row.transform, "NameField",
                $"캐릭터 이름 ({CharacterCreatePageView.MinNameLength}~{CharacterCreatePageView.MaxNameLength}자)", 26f);
            field.GetComponent<RectTransform>().sizeDelta = new Vector2(600f, NameRowHeight);

            // 12자를 넘겨 치는 것 자체를 막는다 → 힌트가 다뤄야 할 경우가 하나 줄어든다.
            field.characterLimit = CharacterCreatePageView.MaxNameLength;

            Button create = EntryUIBuilder.CreateButton(row.transform, "CreateButton", "캐릭터 생성", 24f, AccentButtonColor).button;
            create.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, NameRowHeight);

            return (field, create);
        }

        private static TextMeshProUGUI BuildHint(Transform parent)
        {
            // SetActive로 껐다 켜지 않고 문자열만 비운다(줄 높이가 오가면 아래 배치가 흔들린다).
            TextMeshProUGUI hint = EntryUIBuilder.CreateTMP(parent, "NameHintText", string.Empty, 20f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = HintColor;
            EntryUIBuilder.SetCenter(hint.rectTransform, new Vector2(0f, HintY), new Vector2(NameRowWidth, 32f));
            return hint;
        }

        private static Button BuildPrevButton(Transform parent)
        {
            GameObject bar = new GameObject("BottomBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            EntryUIBuilder.StretchBottom(bar.GetComponent<RectTransform>(), 40f, 116f, 60f);

            Button prev = EntryUIBuilder.CreateButton(bar.transform, "PrevButton", "이전", 24f, ButtonColor).button;
            RectTransform rt = prev.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(200f, 64f);
            return prev;
        }
    }
}
