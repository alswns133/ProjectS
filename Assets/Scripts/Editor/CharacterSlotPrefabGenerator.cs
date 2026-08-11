using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 캐릭터 선택 화면의 슬롯 카드 프리팹을 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Character Slot Prefab
    ///
    /// 손으로 계층을 쌓으면 앵커·피벗·오프셋을 매번 다시 맞춰야 하고, 카드 높이가 어긋나면
    /// 6칸이 패널을 넘긴다. 수치를 코드에 박아두고 재실행으로 다시 뽑을 수 있게 한다.
    /// 배치를 손본 뒤 다시 실행하면 프리팹을 덮어쓰므로, 실행 전 확인 대화상자를 띄운다.
    /// </summary>
    public static class CharacterSlotPrefabGenerator
    {
        private const string PrefabPath = "Assets/Prefabs/UI/CharacterSlot.prefab";

        // 카드 크기. 높이는 선택 여부와 무관하게 고정이다 — 액션 영역은 내용만 바뀐다.
        // 6장 기준 6*120 + 간격 5*12 = 780으로, 타이틀·노트를 더해도 1080 안에 들어간다.
        private const float CardWidth = 420f;
        private const float CardHeight = 120f;

        // 구분선 위=정보, 아래=액션 영역. 바닥에서의 거리로 잡아 카드 높이를 바꿔도 아래가 안 밀린다.
        private const float DividerFromBottom = 46f;
        private const float ActionBottom = 8f;
        private const float ActionTop = 42f;

        private static readonly Color CardColor = new Color(0.12f, 0.14f, 0.18f, 0.92f);
        private static readonly Color FrameColor = new Color(0.35f, 0.85f, 0.72f, 1f);
        private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color PortraitColor = new Color(0.25f, 0.28f, 0.34f, 1f);
        private static readonly Color StartColor = new Color(0.16f, 0.5f, 0.42f, 1f);
        private static readonly Color DeleteColor = new Color(0.42f, 0.18f, 0.2f, 1f);
        private static readonly Color SubInfoColor = new Color(0.68f, 0.71f, 0.78f, 1f);

        private static TMP_FontAsset font;

        [MenuItem("Tools/ProjectS/Create Character Slot Prefab")]
        public static void CreateCharacterSlotPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "CharacterSlot 프리팹 덮어쓰기",
                    $"{PrefabPath} 가 이미 있습니다.\n덮어쓰면 인스펙터에서 손본 배치·색이 사라집니다.",
                    "덮어쓰기", "취소");
                if (!overwrite) return;
            }

            font = ResolveKoreanFont();
            EnsureFolder("Assets/Prefabs/UI");

            GameObject root = BuildCard();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            Selection.activeObject = saved;
            EditorGUIUtility.PingObject(saved);
            Debug.Log($"[CharacterSlotPrefabGenerator] 생성 완료: {PrefabPath}");
        }

        // ── 카드 구성 ──────────────────────────────────────────────

        private static GameObject BuildCard()
        {
            GameObject root = new GameObject("CharacterSlot",
                typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

            RectTransform rt = root.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(CardWidth, CardHeight);

            Image bg = root.GetComponent<Image>();
            bg.color = CardColor;
            bg.type = Image.Type.Sliced;
            bg.sprite = BuiltinSprite("UI/Skin/UISprite.psd");

            Button cardButton = root.GetComponent<Button>();
            cardButton.targetGraphic = bg;

            // VerticalLayoutGroup(childControlHeight=true)이 이 값으로 카드 높이를 잡는다.
            // 선택 여부와 무관하게 고정이라 목록 전체 높이가 런타임에 변하지 않는다.
            LayoutElement le = root.GetComponent<LayoutElement>();
            le.minHeight = CardHeight;
            le.preferredHeight = CardHeight;
            le.flexibleWidth = 1f;

            Image frame = BuildSelectedFrame(root.transform);
            Image portrait = BuildPortrait(root.transform);
            TextMeshProUGUI info = BuildInfoText(root.transform);
            TextMeshProUGUI empty = BuildEmptyLabel(root.transform);
            BuildDivider(root.transform);

            RectTransform action = BuildActionArea(root.transform);
            TextMeshProUGUI subInfo = BuildSubInfoText(action);
            Button start = BuildStartButton(action);
            Button delete = BuildDeleteButton(action);

            CharacterSlotView view = root.AddComponent<CharacterSlotView>();
            Wire(view,
                ("cardButton", cardButton),
                ("selectedFrame", frame),
                ("portrait", portrait),
                ("infoText", info),
                ("emptyLabel", empty),
                ("subInfoText", subInfo),
                ("startButton", start),
                ("deleteButton", delete));

            // 프리팹 기본 모습은 "빈 슬롯". 채워진 모습은 런타임에 SetCharacter가 만든다.
            empty.gameObject.SetActive(true);
            info.gameObject.SetActive(false);
            portrait.gameObject.SetActive(false);
            frame.gameObject.SetActive(false);
            subInfo.gameObject.SetActive(false);
            start.gameObject.SetActive(false);
            delete.gameObject.SetActive(false);

            return root;
        }

        // 선택 강조 테두리. Sliced + fillCenter=false 라 가운데가 뚫려 카드 내용을 가리지 않는다.
        private static Image BuildSelectedFrame(Transform parent)
        {
            GameObject go = new GameObject("SelectedFrame", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Fill(go.GetComponent<RectTransform>());

            Image img = go.GetComponent<Image>();
            img.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
            img.color = FrameColor;
            img.raycastTarget = false;
            return img;
        }

        private static Image BuildPortrait(Transform parent)
        {
            GameObject go = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(12f, -10f);
            rt.sizeDelta = new Vector2(56f, 56f);

            Image img = go.GetComponent<Image>();
            img.color = PortraitColor;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI BuildInfoText(Transform parent)
        {
            TextMeshProUGUI tmp = CreateTMP(parent, "InfoText", "Lv.12  캐릭터A", 24f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;

            // 이름이 길어도 액션 영역을 침범하지 않게 한 줄 + 말줄임으로 고정한다.
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(80f, -50f);
            rt.offsetMax = new Vector2(-12f, -18f);
            return tmp;
        }

        private static TextMeshProUGUI BuildEmptyLabel(Transform parent)
        {
            TextMeshProUGUI tmp = CreateTMP(parent, "EmptyLabel", "+ 신규 캐릭터", 24f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.75f, 0.78f, 0.85f, 1f);

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12f, 12f);
            rt.offsetMax = new Vector2(-12f, -12f);
            return tmp;
        }

        private static void BuildDivider(Transform parent)
        {
            GameObject go = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(12f, DividerFromBottom);
            rt.offsetMax = new Vector2(-12f, DividerFromBottom + 1f);

            Image img = go.GetComponent<Image>();
            img.color = DividerColor;
            img.raycastTarget = false;
        }

        // 액션 영역. rect는 프리팹에 고정이고 자식(보조문구/시작/삭제)만 켜고 끈다.
        // 레이아웃 컴포넌트를 붙이면 자식 토글마다 리빌드가 돌므로 붙이지 않는다.
        private static RectTransform BuildActionArea(Transform parent)
        {
            GameObject go = new GameObject("ActionArea", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(12f, ActionBottom);
            rt.offsetMax = new Vector2(-12f, ActionTop);
            return rt;
        }

        private static TextMeshProUGUI BuildSubInfoText(Transform parent)
        {
            TextMeshProUGUI tmp = CreateTMP(parent, "SubInfoText", "전사 · 시작 마을", 18f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = SubInfoColor;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            Fill(tmp.rectTransform);
            return tmp;
        }

        private static Button BuildStartButton(Transform parent)
        {
            Button btn = CreateButton(parent, "StartButton", "게임 시작 [ENTER]", 20f, StartColor);

            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(200f, 30f);
            return btn;
        }

        private static Button BuildDeleteButton(Transform parent)
        {
            Button btn = CreateButton(parent, "DeleteButton", "×", 22f, DeleteColor);

            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(36f, 30f);
            return btn;
        }

        // ── 유틸 ───────────────────────────────────────────────────

        private static Button CreateButton(Transform parent, string name, string label, float fontSize, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = color;

            Button btn = go.GetComponent<Button>();
            btn.targetGraphic = img;

            TextMeshProUGUI text = CreateTMP(go.transform, "Text", label, fontSize);
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            Fill(text.rectTransform);

            return btn;
        }

        private static TextMeshProUGUI CreateTMP(Transform parent, string name, string content, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.raycastTarget = false;
            if (font != null) tmp.font = font;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            return tmp;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite BuiltinSprite(string path)
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // 프로젝트의 한글 TMP 폰트(Paperlogy)를 찾는다. 없으면 기본 폰트로 떨어져 한글이 깨질 수 있다.
        private static TMP_FontAsset ResolveKoreanFont()
        {
            const string target = "Paperlogy-5Medium SDF";
            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");

            foreach (string guid in guids)
            {
                TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name == target) return asset;
            }
            foreach (string guid in guids)
            {
                TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name.Contains("Paperlogy")) return asset;
            }

            Debug.LogWarning("[CharacterSlotPrefabGenerator] Paperlogy TMP 폰트를 못 찾아 기본 폰트를 씁니다. 한글이 깨지면 폰트를 지정하세요.");
            return TMP_Settings.defaultFontAsset;
        }

        // private [SerializeField] 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new SerializedObject(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[CharacterSlotPrefabGenerator] 필드를 못 찾음: {prop}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
