// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 파티 슬롯(docs/PARTY_WINDOW_UI.md §2)을 프리팹으로 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Party Slot Bar
    ///
    /// 초상화를 중심으로 한 카드 한 장(<c>PartySlot</c>)과, 그것을 두 개 물고 있는
    /// <c>PartySlotBar</c>를 함께 만든다. 구역은 여섯이다 —
    /// ① 레벨 · ② 닉네임 · ③ 클래스 아이콘 · ④ 파티장 아이콘 · ⑤ 초상화 · ⑥ 바이탈 그래프.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>두 칸을 손으로 복사하지 않고 프리팹 인스턴스로 넣는다.</b> 손복사본은 나중에 칸을 고쳐도
    /// 따라오지 않아, 실제로 <c>Skill_01</c>만 프리팹이고 나머지는 사본으로 갈렸던 전례가 있다
    /// (CLAUDE.md 씬·UI 이름 규칙).
    /// </para>
    /// <para>
    /// <b>③④ 는 초상화 위에 겹쳐 올린다.</b> 따로 줄을 만들면 카드가 세로로 더 길어지는데,
    /// 아이콘 두 개가 차지하는 자리는 초상화의 위쪽 모서리로 충분하다.
    /// </para>
    /// <para>
    /// <b>⑥ 바이탈 그래프에는 <c>HPEcg.mat</c>을 물린다.</b> 연출이라 스크립트가 값을 넣지 않아도
    /// 셰이더가 스스로 파형을 흘린다. 머티리얼을 못 찾으면 색만 칠하고 넘어간다.
    /// </para>
    /// <para>
    /// 초상화 스프라이트와 클래스·파티장 아이콘 그림은 넣지 않는다. 아트가 붙는 자리라
    /// 툴이 채우면 다시 실행할 때마다 덮어쓴다.
    /// </para>
    /// (2026-08-31 TH)
    /// </remarks>
    public static class PartySlotGenerator
    {
        private const string PrefabFolder = "Assets/Prefabs/UI";
        private const string SlotPath = PrefabFolder + "/PartySlot.prefab";
        private const string BarPath = PrefabFolder + "/PartySlotBar.prefab";
        private const string EcgMaterialPath = "Assets/Shader/UI/HPEcg.mat";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";

        // 카드는 세로로 긴 형태. 두 장 + 라벨이 한 열(약 868)에 들어가는 크기다.
        private const float SlotWidth = 340f;
        private const float SlotHeight = 400f;
        private const float TopBarHeight = 52f;
        private const float VitalHeight = 56f;
        private const float IconSize = 40f;
        private const float IconInset = 12f;
        private const float Pad = 14f;

        private const float LabelWidth = 52f;
        private const float SlotGap = 12f;

        private static readonly Color SlotColor = new Color32(0x1A, 0x21, 0x30, 0xFF);
        private static readonly Color SelfColor = new Color32(0x24, 0x3A, 0x66, 0xFF);
        private static readonly Color TopBarColor = new Color32(0x12, 0x18, 0x24, 0xFF);
        private static readonly Color PortraitColor = new Color32(0x21, 0x2A, 0x3C, 0xFF);
        private static readonly Color LeaderColor = new Color32(0xF0, 0xB4, 0x29, 0xFF);
        private static readonly Color VitalColor = new Color32(0x43, 0xC9, 0x7B, 0xFF);
        private static readonly Color MutedColor = new Color32(0x8D, 0x99, 0xAC, 0xFF);
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);

        [MenuItem("Tools/ProjectS/Create Party Slot Bar")]
        public static void Create()
        {
            string slotPath = ResolvePath(SlotPath, "파티 슬롯");
            if (slotPath == null) return;

            string barPath = ResolvePath(BarPath, "파티 슬롯 바");
            if (barPath == null) return;

            EnsureFolder();

            // 칸을 먼저 저장해야 바가 그것을 프리팹 인스턴스로 물 수 있다.
            GameObject slotTemp = BuildSlot();
            GameObject slotAsset = PrefabUtility.SaveAsPrefabAsset(slotTemp, slotPath);
            Object.DestroyImmediate(slotTemp);

            GameObject barTemp = BuildBar(slotAsset);
            GameObject barAsset = PrefabUtility.SaveAsPrefabAsset(barTemp, barPath);
            Object.DestroyImmediate(barTemp);

            AssetDatabase.SaveAssets();
            Selection.activeObject = barAsset;
            EditorGUIUtility.PingObject(barAsset);

            Debug.Log($"[PartySlotGenerator] 파티 슬롯을 만들었다.\n· {slotPath}\n· {barPath}\n" +
                      "초상화 스프라이트(portraitsByClass)와 클래스·파티장 아이콘 그림은 비어 있으니 " +
                      "PartySlot 프리팹에서 채운다.");

            TryAttachToDungeonEntry(barAsset);
        }

        /// <summary>
        /// 씬에 던전 입장 창이 있고 파티 슬롯이 아직 비어 있으면, 바를 넣어 줄지 물어본다.
        /// 넣더라도 <b>위치는 왼쪽 아래에 임시로</b> 둔다 — 어느 자리에 놓을지는 창마다 달라
        /// 툴이 정하면 오히려 손이 더 간다.
        /// </summary>
        private static void TryAttachToDungeonEntry(GameObject barAsset)
        {
            DungeonEntryPopup popup = Object.FindAnyObjectByType<DungeonEntryPopup>(FindObjectsInactive.Include);
            if (popup == null) return;

            SerializedObject so = new(popup);
            SerializedProperty slotProp = so.FindProperty("partySlots");
            if (slotProp == null || slotProp.objectReferenceValue != null) return;

            bool attach = EditorUtility.DisplayDialog(
                "던전 입장 창에 붙일까?",
                "씬에서 던전 입장 창을 찾았고 파티 슬롯이 비어 있다.\n\n" +
                "바를 자식으로 넣고 연결해 둘까? (위치는 왼쪽 아래에 임시로 두므로 직접 옮겨야 한다)",
                "넣기",
                "그만두기");

            if (!attach) return;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(barAsset, popup.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Attach Party Slot Bar");

            RectTransform rt = (RectTransform)instance.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(80f, 80f);
            rt.sizeDelta = BarSize();

            slotProp.objectReferenceValue = instance.GetComponent<PartySlotBar>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Debug.Log("[PartySlotGenerator] 던전 입장 창에 파티 슬롯 바를 넣고 연결했다. 위치는 직접 옮긴다.", instance);
        }

        // ── 카드 한 장 ───────────────────────────────────────────────────

        private static GameObject BuildSlot()
        {
            GameObject root = new("PartySlot", typeof(RectTransform), typeof(Image),
                                  typeof(Button), typeof(PartySlotView));

            RectTransform rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(SlotWidth, SlotHeight);

            Image background = root.GetComponent<Image>();
            background.color = SlotColor;
            background.raycastTarget = true;

            Button button = root.GetComponent<Button>();
            button.targetGraphic = background;

            // ── 비었을 때 ────────────────────────────────────────────
            RectTransform emptyRoot = CreateRect(rt, "EmptyRoot");
            Fill(emptyRoot);

            TextMeshProUGUI emptyLabel = CreateText(emptyRoot, "EmptyLabel", "＋ 파티원 초대", 15f);
            emptyLabel.color = MutedColor;
            emptyLabel.alignment = TextAlignmentOptions.Center;
            Fill(emptyLabel.rectTransform);

            // ── 채워졌을 때 ──────────────────────────────────────────
            RectTransform filledRoot = CreateRect(rt, "FilledRoot");
            Fill(filledRoot);

            // ①② 상단 바 — 레벨과 닉네임
            RectTransform topBar = CreateRect(filledRoot, "TopBar");
            StretchTop(topBar, 0f, TopBarHeight, 0f, 0f);
            CreateImage(topBar, "Background", TopBarColor);

            TextMeshProUGUI levelText = CreateText(topBar, "LevelText", "Lv.24", 14f);
            levelText.color = MutedColor;
            levelText.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(levelText.rectTransform, Pad, 58f);

            TextMeshProUGUI nameText = CreateText(topBar, "NameText", "하루", 17f);
            nameText.color = TextColor;
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            StretchLeft(nameText.rectTransform, Pad + 58f + 8f, Pad);

            // ⑤ 초상화 — 상단 바와 바이탈 그래프 사이를 통째로 차지한다.
            Image portrait = CreateImage(filledRoot, "Portrait", PortraitColor);
            StretchMiddle(portrait.rectTransform, TopBarHeight, VitalHeight, 0f, 0f);

            // ③④ 는 초상화 위 모서리에 겹쳐 올린다(따로 줄을 만들면 카드가 더 길어진다).
            RectTransform classRoot = CreateRect(filledRoot, "ClassIcon");
            AnchorTopLeft(classRoot, IconInset, TopBarHeight + IconInset, IconSize, IconSize);

            Image classIcon00 = CreateImage(classRoot, "ClassIcon00", Color.white);
            Fill(classIcon00.rectTransform);

            Image classIcon01 = CreateImage(classRoot, "ClassIcon01", Color.white);
            Fill(classIcon01.rectTransform);
            classIcon01.gameObject.SetActive(false);

            Image leaderIcon = CreateImage(filledRoot, "LeaderIcon", LeaderColor);
            AnchorTopRight((RectTransform)leaderIcon.transform, IconInset, TopBarHeight + IconInset, IconSize, IconSize);
            leaderIcon.gameObject.SetActive(false);   // 파티장일 때만 켜진다

            // ⑥ 바이탈 그래프 — 연출. 머티리얼이 스스로 파형을 흘린다.
            Image vital = CreateImage(filledRoot, "VitalGraph", VitalColor);
            StretchBottom(vital.rectTransform, 0f, VitalHeight, 0f, 0f);

            Material ecg = AssetDatabase.LoadAssetAtPath<Material>(EcgMaterialPath);
            if (ecg != null) vital.material = ecg;
            else Debug.LogWarning($"[PartySlotGenerator] {EcgMaterialPath} 를 찾지 못해 바이탈 그래프가 단색이다.");

            filledRoot.gameObject.SetActive(false);   // 기본은 빈 칸

            PartySlotView view = root.GetComponent<PartySlotView>();
            Wire(view,
                 ("slotButton", button),
                 ("emptyRoot", emptyRoot.gameObject),
                 ("emptyLabel", emptyLabel),
                 ("filledRoot", filledRoot.gameObject),
                 ("levelText", levelText),
                 ("nameText", nameText),
                 ("leaderIcon", leaderIcon.gameObject),
                 ("portraitImage", portrait),
                 ("vitalGraph", vital.gameObject));

            WireList(view, "classIcons", classIcon00.gameObject, classIcon01.gameObject);

            return root;
        }

        // ── 두 칸 묶음 ───────────────────────────────────────────────────

        private static Vector2 BarSize()
            => new(LabelWidth + (SlotWidth * 2f) + (SlotGap * 3f), SlotHeight);

        private static GameObject BuildBar(GameObject slotAsset)
        {
            GameObject root = new("PartySlotBar", typeof(RectTransform),
                                  typeof(PartySlotBar), typeof(DummyPartySource));

            RectTransform rt = (RectTransform)root.transform;
            rt.sizeDelta = BarSize();

            TextMeshProUGUI label = CreateText(rt, "BarLabel", "파티", 14f);
            label.color = MutedColor;
            label.alignment = TextAlignmentOptions.TopLeft;
            AnchorLeft(label.rectTransform, 0f, LabelWidth);

            // 손복사가 아니라 프리팹 인스턴스로 넣는다 — 칸을 고치면 두 칸이 함께 따라온다.
            PartySlotView selfSlot = InstantiateSlot(slotAsset, rt, "SelfSlot", LabelWidth + SlotGap);
            PartySlotView partnerSlot = InstantiateSlot(slotAsset, rt, "PartnerSlot",
                                                        LabelWidth + SlotGap + SlotWidth + SlotGap);

            // 내 칸은 눌러도 할 게 없어 배경만 다르게 둔다(파티원 칸과 구분되도록).
            Image selfBackground = selfSlot.GetComponent<Image>();
            if (selfBackground != null) selfBackground.color = SelfColor;

            Wire(root.GetComponent<PartySlotBar>(),
                 ("partySourceBehaviour", root.GetComponent<DummyPartySource>()),
                 ("selfSlot", selfSlot),
                 ("partnerSlot", partnerSlot));

            return root;
        }

        private static PartySlotView InstantiateSlot(GameObject slotAsset, Transform parent, string name, float left)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(slotAsset, parent);
            instance.name = name;

            RectTransform rt = (RectTransform)instance.transform;
            AnchorLeft(rt, left, SlotWidth);

            return instance.GetComponent<PartySlotView>();
        }

        // ── 만들기 도우미 ────────────────────────────────────────────────

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

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;   // 클릭은 카드 배경 한 장만 받는다
            Fill((RectTransform)go.transform);
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

            // 긴 닉네임이 두 줄로 흘러 카드 높이가 깨지는 것을 막는다.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            // TMP 기본값(LiberationSans)에는 한글 글리프가 없어 그대로 두면 전부 □로 나온다.
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

        private static void AnchorLeft(RectTransform rt, float left, float width)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
            rt.sizeDelta = new Vector2(width, 0f);
        }

        /// <summary>왼쪽에서 떨어진 자리부터 오른쪽 여백까지 늘린다.</summary>
        private static void StretchLeft(RectTransform rt, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        private static void StretchTop(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void StretchBottom(RectTransform rt, float bottom, float top, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, top);
        }

        private static void StretchMiddle(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorTopLeft(RectTransform rt, float left, float top, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void AnchorTopRight(RectTransform rt, float right, float top, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -top);
            rt.sizeDelta = new Vector2(width, height);
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
                else Debug.LogWarning($"[PartySlotGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없다. 이름이 바뀌었는지 확인한다.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireList(Component comp, string prop, params Object[] elements)
        {
            SerializedObject so = new(comp);
            SerializedProperty p = so.FindProperty(prop);

            if (p != null && p.isArray)
            {
                p.arraySize = elements.Length;
                for (int i = 0; i < elements.Length; i++)
                {
                    p.GetArrayElementAtIndex(i).objectReferenceValue = elements[i];
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
