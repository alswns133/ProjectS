// 이 스크립트는 반드시 'Editor' 폴더 안에 있어야 한다(UnityEditor 참조가 플레이어 빌드로 새지 않게).
// 네임스페이스 세그먼트로 'Editor'를 쓰면 UnityEditor.Editor를 가려 컴파일 에러가 나므로
// (CLAUDE.md 단순명 가림 회피 규칙) 여기서는 EditorTools를 쓴다. 폴더명은 Unity 매직 폴더라 'Editor' 유지.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using ProjectS.UI;
using ProjectS.UI.Framework;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 확정 시안(사이버펑크 리액터형)에 맞춰 강화창(EnhancePanel) 계층을 현재 씬에 1회 생성하는 에디터 툴.
    /// 계층·컴포넌트·색·RectTransform 앵커를 코드로 만들고, EnhancePanel/EnhancePresenter의
    /// SerializeField 참조를 자동 배선한다. 좌우 300 고정 + 가운데 stretch는 앵커로, LayoutGroup은
    /// 가변 리스트(MaterialList/StatList) 안쪽에만 둔다. 재료/스탯 샘플 행은 디자인 미리보기용이며
    /// 런타임 SetMaterials/BuildStatRows가 지우고 실제 데이터로 채운다.
    /// (2026-07-23 TH)
    /// </summary>
    public static class EnhancePanelBuilder
    {
        // ── 팔레트(시안 근사치) ───────────────────────────────────────
        private static readonly Color Bg        = new Color(0.035f, 0.066f, 0.117f, 1f);
        private static readonly Color Panel     = new Color(0.047f, 0.086f, 0.149f, 0.98f);
        private static readonly Color HeaderBg  = new Color(0.063f, 0.110f, 0.180f, 1f);
        private static readonly Color Cyan      = new Color(0.31f, 0.72f, 0.90f, 1f);
        private static readonly Color CyanDim   = new Color(0.36f, 0.55f, 0.68f, 1f);
        private static readonly Color Gold      = new Color(0.93f, 0.74f, 0.30f, 1f);
        private static readonly Color TextWhite = new Color(0.90f, 0.94f, 0.98f, 1f);
        private static readonly Color TextDim   = new Color(0.47f, 0.56f, 0.67f, 1f);
        private static readonly Color Warn      = new Color(1f, 0.36f, 0.48f, 1f);
        private static readonly Color BtnBlue   = new Color(0.16f, 0.40f, 0.82f, 1f);
        private static readonly Color Hazard    = new Color(0.80f, 0.66f, 0.14f, 1f);
        private static readonly Color Line      = new Color(0.16f, 0.29f, 0.42f, 1f);
        private static readonly Color BoxFill   = new Color(1f, 1f, 1f, 0.03f);

        private const string GoldHex = "#EDBC4D";

        // ── 치수 ─────────────────────────────────────────────────────
        private const float Margin = 10f;
        private const float HeaderHeight = 64f;
        private const float FooterHeight = 22f;
        private const float SideWidth = 300f;
        private const float PadX = 30f;
        private const float BodyTop = 18f;
        private const float BodyBottom = 14f;

        // 자동 생성 프리팹 저장 위치(기존 UI 프리팹과 같은 폴더).
        private const string PrefabDir = "Assets/Prefabs/UI";
        private const string StatRowPrefabPath = PrefabDir + "/StatRowView.prefab";
        private const string MaterialSlotPrefabPath = PrefabDir + "/MaterialSlotView.prefab";

        [MenuItem("Tools/ProjectS/Build Enhance Panel")]
        public static void Build()
        {
            if (Object.FindObjectOfType<EnhancePanel>() != null)
            {
                if (!EditorUtility.DisplayDialog("EnhancePanel 이미 존재",
                        "씬에 EnhancePanel이 이미 있습니다. 하나 더 만들면 UIManager가 타입으로 관리해 충돌합니다. 그래도 만들까요?",
                        "그래도 생성", "취소"))
                {
                    return;
                }
            }

            Transform parent = ResolveParent();

            // 루트: BasePanel + Presenter + CanvasGroup (HUD와 같은 '같은 GameObject' 패턴)
            var root = NewUI("EnhancePanel", parent);
            FullStretch(root);
            root.gameObject.AddComponent<CanvasGroup>();
            var panel = root.gameObject.AddComponent<EnhancePanel>();
            var presenter = root.gameObject.AddComponent<EnhancePresenter>();

            // Dim: 전체화면, 클릭 차단
            var dim = NewUI("Dim", root);
            FullStretch(dim);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.6f);
            dimImg.raycastTarget = true;

            // Window: 거의 전체 화면(시안이 풀스크린형) + 얇은 테두리 톤
            var window = NewUI("Window", root);
            FullStretch(window, Margin, Margin, Margin, Margin);
            window.gameObject.AddComponent<Image>().color = Panel;

            BuildHeader(window, out var closeButton);

            // Body
            var body = NewUI("Body", window);
            FullStretch(body, PadX, HeaderHeight + BodyTop, PadX, FooterHeight + BodyBottom);
            VDivider(body, SideWidth + 14f, false);
            VDivider(body, SideWidth + 14f, true);

            BuildLeft(body, out var costText, out var ownedGoldText, out var materialList);
            BuildCenter(body, out var curLevelText, out var nextLevelText, out var coreIcon,
                        out var coreSlotButton, out var nameText, out var typeText,
                        out var rateText, out var rateGauge, out var gaugeImg, out var enhanceButton);
            BuildRight(body, out var statList);

            // Hazard_Footer (경고 스트라이프 — 실제 줄무늬는 스프라이트, 여기선 색으로 근사)
            var footer = NewUI("Hazard_Footer", window);
            BottomBar(footer, FooterHeight);
            footer.gameObject.AddComponent<Image>().color = Hazard;

            // FX_Overlay: Window 바깥 형제 + overrideSorting Canvas (연출이 창 전체를 덮게)
            var fx = NewUI("FX_Overlay", root);
            FullStretch(fx);
            var fxCanvas = fx.gameObject.AddComponent<Canvas>();
            fxCanvas.overrideSorting = true;
            fxCanvas.sortingOrder = 100;

            WireAll(panel, presenter, rateGauge, gaugeImg, coreIcon, coreSlotButton,
                    nameText, typeText, curLevelText, nextLevelText, rateText, costText,
                    ownedGoldText, enhanceButton, closeButton, materialList, statList);

            // 리스트 행 프리팹(StatRowView / MaterialSlotView)을 자동 생성(없으면)하고 배선.
            var statRowPrefab = EnsureStatRowPrefab();
            var matSlotPrefab = EnsureMaterialSlotPrefab();
            var prefabSO = new SerializedObject(panel);
            if (statRowPrefab != null) Wire(prefabSO, "statRowPrefab", statRowPrefab);
            if (matSlotPrefab != null) Wire(prefabSO, "materialSlotPrefab", matSlotPrefab);
            prefabSO.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Enhance Panel");
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;

            Debug.Log("[EnhancePanelBuilder] EnhancePanel 생성 완료.\n" +
                      "· statRowPrefab / materialSlotPrefab 프리팹은 자동 생성·배선됨 (남은 수동 프리팹 배선 없음)\n" +
                      "· 스프라이트 필요: 코어 육각 프레임(HexFrame), 해저드 줄무늬(Footer), SegmentGauge 확률 셰이더 머티리얼\n" +
                      "· 플레이 전 EnhancePanel을 비활성(SetActive false)으로 두면 ShowPanel 때 처음 켜진다(설계 OnInit 규칙).");
        }

        // ── 헤더 ─────────────────────────────────────────────────────
        private static void BuildHeader(RectTransform window, out Button closeButton)
        {
            var header = NewUI("Header", window);
            TopBar(header, HeaderHeight);
            header.gameObject.AddComponent<Image>().color = HeaderBg;

            var underline = NewUI("Underline", header);
            BottomBar(underline, 1f);
            underline.gameObject.AddComponent<Image>().color = Line;

            var title = AddText(NewUI("Title", header), "장비 강화", 26, TextAlignmentOptions.MidlineLeft, TextWhite, true);
            LeftAnchored(title.rectTransform, 32f, 150f, 40f);
            var sub = AddText(NewUI("Sub", header), "ENHANCE", 15, TextAlignmentOptions.MidlineLeft, Cyan, true);
            sub.characterSpacing = 8f;
            LeftAnchored(sub.rectTransform, 188f, 220f, 30f);

            var id = AddText(NewUI("Id", header), "UI_EN_001", 14, TextAlignmentOptions.MidlineRight, CyanDim, false);
            id.characterSpacing = 3f;
            RightAnchored(id.rectTransform, 84f, 160f, 30f);

            closeButton = AddButton(NewUI("CloseButton", header), "✕", BoxFill, TextWhite, 20f);
            AnchorTopRight(closeButton.GetComponent<RectTransform>(), 34f, 34f, 15f);
        }

        // ── 좌: REQUIRED MATERIAL / COST / OPTION ─────────────────────
        private static void BuildLeft(RectTransform body, out TextMeshProUGUI costText,
            out TextMeshProUGUI ownedGoldText, out RectTransform materialList)
        {
            var left = NewUI("Left_Material", body);
            LeftColumn(left, SideWidth);
            float y = 0f;

            Section(left, "REQUIRED MATERIAL", ref y);
            materialList = NewUI("MaterialList", left);
            Row(materialList, ref y, 160f, 18f);
            var ml = materialList.gameObject.AddComponent<VerticalLayoutGroup>();
            ml.spacing = 12f;
            ml.childControlWidth = true;
            ml.childForceExpandWidth = true;
            ml.childControlHeight = true;
            ml.childForceExpandHeight = false;
            SampleMaterialSlot(materialList, "강화 모듈 II", 12, 3);
            SampleMaterialSlot(materialList, "안정화 코어", 2, 1);

            Section(left, "COST", ref y);
            var cost = NewUI("CostGroup", left);
            Row(cost, ref y, 60f, 18f);
            costText = AddText(NewUI("CostText", cost), "48,600", 30, TextAlignmentOptions.BottomLeft, TextWhite, true);
            AnchorRect(costText.rectTransform, 0f, 0.45f, 0.7f, 1f);
            var costUnit = AddText(NewUI("Unit", cost), "G", 16, TextAlignmentOptions.BottomLeft, TextDim, false);
            AnchorRect(costUnit.rectTransform, 0.55f, 0.45f, 1f, 1f, new Vector2(6f, 4f), Vector2.zero);
            ownedGoldText = AddText(NewUI("OwnedGoldText", cost), "보유 1,204,880 G", 14, TextAlignmentOptions.TopLeft, TextDim, false);
            AnchorRect(ownedGoldText.rectTransform, 0f, 0f, 1f, 0.45f);
            // OPTION(보호 토글) 섹션은 제거됨: 실패 보정은 보호권이 아니라 모든 강화 자동 자비(pity)로 처리한다.
        }

        // ── 중앙: 레벨 / 코어 / 이름 / 확률 / 버튼 ─────────────────────
        private static void BuildCenter(RectTransform body,
            out TextMeshProUGUI curLevelText, out TextMeshProUGUI nextLevelText, out Image coreIcon,
            out Button coreSlotButton, out TextMeshProUGUI nameText, out TextMeshProUGUI typeText,
            out TextMeshProUGUI rateText, out SegmentGaugeView rateGauge, out Image gaugeImg, out Button enhanceButton)
        {
            var center = NewUI("Center_Core", body);
            CenterColumn(center, SideWidth + 24f, SideWidth + 24f);
            float y = 6f;

            var levelGroup = NewUI("LevelGroup", center);
            Row(levelGroup, ref y, 52f, 14f);
            curLevelText = AddText(NewUI("Cur", levelGroup), "+7", 34, TextAlignmentOptions.MidlineRight, Cyan, true);
            AnchorRect(curLevelText.rectTransform, 0f, 0f, 0.44f, 1f, Vector2.zero, new Vector2(-10f, 0f));
            var arrow = AddText(NewUI("Arrow", levelGroup), "▶", 22, TextAlignmentOptions.Center, Cyan, false);
            AnchorRect(arrow.rectTransform, 0.44f, 0f, 0.56f, 1f);
            nextLevelText = AddText(NewUI("Next", levelGroup), "+8", 34, TextAlignmentOptions.MidlineLeft, Gold, true);
            AnchorRect(nextLevelText.rectTransform, 0.56f, 0f, 1f, 1f, new Vector2(10f, 0f), Vector2.zero);

            var coreSlot = NewUI("CoreSlot", center);
            Row(coreSlot, ref y, 220f, 10f);
            coreSlot.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.32f, 0.45f, 0.25f); // HexFrame 자리
            coreSlotButton = coreSlot.gameObject.AddComponent<Button>();
            coreIcon = NewUI("ItemIcon", coreSlot).gameObject.AddComponent<Image>();
            AnchorRect(coreIcon.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f);
            coreIcon.rectTransform.sizeDelta = new Vector2(150f, 150f);
            coreIcon.color = new Color(1f, 1f, 1f, 0.12f);
            coreIcon.raycastTarget = false;

            nameText = AddText(NewUI("NameText", center), "벨트라인 리퍼 +7", 22, TextAlignmentOptions.Center, Gold, true);
            Row(nameText.rectTransform, ref y, 32f, 2f);
            typeText = AddText(NewUI("TypeText", center), "TWO-HANDED / RARE", 13, TextAlignmentOptions.Center, CyanDim, false);
            typeText.characterSpacing = 3f;
            Row(typeText.rectTransform, ref y, 24f, 18f);

            var rateHead = NewUI("RateHead", center);
            Row(rateHead, ref y, 26f, 6f);
            var rateLabel = AddText(NewUI("Label", rateHead), "SUCCESS RATE", 13, TextAlignmentOptions.MidlineLeft, Cyan, true);
            rateLabel.characterSpacing = 5f;
            AnchorRect(rateLabel.rectTransform, 0f, 0f, 0.6f, 1f);
            rateText = AddText(NewUI("RateText", rateHead), "62%", 28, TextAlignmentOptions.MidlineRight, Gold, true);
            AnchorRect(rateText.rectTransform, 0.6f, 0f, 1f, 1f);

            var gaugeGO = NewUI("SegmentGauge", center);
            Row(gaugeGO, ref y, 26f, 20f);
            gaugeImg = gaugeGO.gameObject.AddComponent<Image>();
            gaugeImg.color = Cyan;
            rateGauge = gaugeGO.gameObject.AddComponent<SegmentGaugeView>();

            enhanceButton = AddButton(NewUI("EnhanceButton", center), "강화 실행", BtnBlue, TextWhite, 22f);
            Row(enhanceButton.GetComponent<RectTransform>(), ref y, 60f, 8f);
            var space = AddText(NewUI("Hint", enhanceButton.GetComponent<RectTransform>()), "SPACE", 14, TextAlignmentOptions.MidlineRight, new Color(1f, 1f, 1f, 0.6f), true);
            AnchorRect(space.rectTransform, 0.6f, 0f, 1f, 1f, Vector2.zero, new Vector2(-24f, 0f));
        }

        // ── 우: STAT PREVIEW / 마일스톤 / 실패 경고 ────────────────────
        private static void BuildRight(RectTransform body, out RectTransform statList)
        {
            var right = NewUI("Right_Preview", body);
            RightColumn(right, SideWidth);
            float y = 0f;

            Section(right, "STAT PREVIEW", ref y);
            statList = NewUI("StatList", right);
            Row(statList, ref y, 170f, 18f);
            var sl = statList.gameObject.AddComponent<VerticalLayoutGroup>();
            sl.spacing = 10f;
            sl.childControlWidth = true;
            sl.childForceExpandWidth = true;
            sl.childControlHeight = true;
            sl.childForceExpandHeight = false;
            SampleStatRow(statList, "공격력", "1,482", "1,606", "+124");
            SampleStatRow(statList, "명중률", "18.4%", "19.7%", "+1.3");
            SampleStatRow(statList, "치명 확률", "7.2%", "7.9%", "+0.7");
            SampleStatRow(statList, "폭딜 관통", "96", "104", "+8");

            Section(right, "+9 달성 시", ref y);
            var card = NewUI("MilestoneCard", right);
            Row(card, ref y, 76f, 16f);
            card.gameObject.AddComponent<Image>().color = BoxFill;
            var cardTitle = AddText(NewUI("Title", card), "무기 오라 개방", 17, TextAlignmentOptions.TopLeft, TextWhite, true);
            AnchorRect(cardTitle.rectTransform, 0f, 0.45f, 1f, 1f, new Vector2(14f, 0f), new Vector2(-14f, -12f));
            var cardDesc = AddText(NewUI("Desc", card), "외형 이펙트 + 스킬 피해 3% 추가", 13, TextAlignmentOptions.TopLeft, TextDim, false);
            AnchorRect(cardDesc.rectTransform, 0f, 0f, 1f, 0.45f, new Vector2(14f, 0f), new Vector2(-14f, 0f));

            // 문서 규칙: 실패해도 단계는 유지(하락 없음), 대신 다음 시도 성공률에 +5%p 자비 보너스가 쌓인다.
            var fail = AddText(NewUI("FailWarnText", right), "실패 시 단계 유지 · 다음 시도 성공률 +5%p", 13, TextAlignmentOptions.MidlineLeft, TextDim, false);
            Row(fail.rectTransform, ref y, 30f, 8f);
        }

        // ── 샘플 행(디자인 미리보기용; 런타임에 지워짐) ─────────────────
        private static void SampleMaterialSlot(Transform parent, string name, int owned, int required)
        {
            var row = NewUI("MatSample_" + name, parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 68f;

            var box = NewUI("IconBox", row);
            LeftAnchored(box, 0f, 56f, 56f);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.gameObject.AddComponent<Image>().color = BoxFill;
            var boxLine = NewUI("Border", box);
            FullStretch(boxLine);
            var bl = boxLine.gameObject.AddComponent<Image>();
            bl.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f);
            bl.raycastTarget = false;
            var badge = AddText(NewUI("Badge", box), owned.ToString(), 13, TextAlignmentOptions.BottomRight, Cyan, false);
            AnchorRect(badge.rectTransform, 0f, 0f, 1f, 0.4f, Vector2.zero, new Vector2(-4f, 0f));

            var nm = AddText(NewUI("Name", row), name, 17, TextAlignmentOptions.BottomLeft, TextWhite, false);
            AnchorRect(nm.rectTransform, 0f, 0.45f, 1f, 1f, new Vector2(70f, 0f), Vector2.zero);
            var sub = AddText(NewUI("Sub", row), $"보유 {owned} / <color={GoldHex}>필요 {required}</color>", 14, TextAlignmentOptions.TopLeft, TextDim, false);
            AnchorRect(sub.rectTransform, 0f, 0f, 1f, 0.45f, new Vector2(70f, 0f), Vector2.zero);
        }

        private static void SampleStatRow(Transform parent, string label, string cur, string next, string delta)
        {
            var row = NewUI("StatSample_" + label, parent);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var lb = AddText(NewUI("Label", row), label, 13, TextAlignmentOptions.MidlineLeft, TextDim, false);
            AnchorRect(lb.rectTransform, 0f, 0f, 0.26f, 1f);
            var cr = AddText(NewUI("Cur", row), cur, 15, TextAlignmentOptions.MidlineRight, TextDim, false);
            AnchorRect(cr.rectTransform, 0.26f, 0f, 0.52f, 1f);
            var ar = AddText(NewUI("Arrow", row), "›", 15, TextAlignmentOptions.Center, CyanDim, false);
            AnchorRect(ar.rectTransform, 0.52f, 0f, 0.6f, 1f);
            var nx = AddText(NewUI("Next", row), next, 16, TextAlignmentOptions.MidlineRight, TextWhite, true);
            AnchorRect(nx.rectTransform, 0.6f, 0f, 0.82f, 1f);
            var dl = AddText(NewUI("Delta", row), delta, 14, TextAlignmentOptions.MidlineRight, Gold, false);
            AnchorRect(dl.rectTransform, 0.82f, 0f, 1f, 1f);
        }

        // ── 배선 ─────────────────────────────────────────────────────
        private static void WireAll(EnhancePanel panel, EnhancePresenter presenter, SegmentGaugeView rateGauge,
            Image gaugeImg, Image coreIcon, Button coreSlotButton, TextMeshProUGUI nameText, TextMeshProUGUI typeText,
            TextMeshProUGUI curLevelText, TextMeshProUGUI nextLevelText, TextMeshProUGUI rateText, TextMeshProUGUI costText,
            TextMeshProUGUI ownedGoldText, Button enhanceButton, Button closeButton, RectTransform materialList, RectTransform statList)
        {
            var so = new SerializedObject(panel);
            Wire(so, "coreIcon", coreIcon);
            Wire(so, "coreSlotButton", coreSlotButton);
            Wire(so, "nameText", nameText);
            Wire(so, "typeText", typeText);
            Wire(so, "curLevelText", curLevelText);
            Wire(so, "nextLevelText", nextLevelText);
            Wire(so, "rateText", rateText);
            Wire(so, "rateGauge", rateGauge);
            Wire(so, "costText", costText);
            Wire(so, "ownedGoldText", ownedGoldText);
            Wire(so, "enhanceButton", enhanceButton);
            Wire(so, "closeButton", closeButton);
            Wire(so, "materialListRoot", materialList);
            Wire(so, "statListRoot", statList);
            so.ApplyModifiedPropertiesWithoutUndo();

            var pso = new SerializedObject(presenter);
            Wire(pso, "view", panel);
            pso.ApplyModifiedPropertiesWithoutUndo();

            var gso = new SerializedObject(rateGauge);
            Wire(gso, "barImage", gaugeImg);
            gso.ApplyModifiedPropertiesWithoutUndo();
        }

        // 선택 오브젝트(RectTransform)가 있으면 그 아래, 없으면 씬의 첫 Canvas, 그것도 없으면 새 Canvas.
        private static Transform ResolveParent()
        {
            if (Selection.activeTransform != null && Selection.activeTransform.GetComponent<RectTransform>() != null)
                return Selection.activeTransform;

            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas != null) return canvas.transform;

            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
            }
            return canvasGO.transform;
        }

        // ── 생성/앵커 헬퍼 ────────────────────────────────────────────
        private static RectTransform NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        private static TextMeshProUGUI AddText(RectTransform rt, string content, float size, TextAlignmentOptions align, Color color, bool bold)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static Button AddButton(RectTransform rt, string label, Color bg, Color labelColor, float labelSize)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            var btn = rt.gameObject.AddComponent<Button>();
            if (!string.IsNullOrEmpty(label))
            {
                var labelRt = NewUI("Label", rt);
                FullStretch(labelRt);
                AddText(labelRt, label, labelSize, TextAlignmentOptions.Center, labelColor, true);
            }
            return btn;
        }

        private static void FullStretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void TopBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void BottomBar(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void LeftColumn(RectTransform rt, float width)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void RightColumn(RectTransform rt, float width)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void CenterColumn(RectTransform rt, float leftMargin, float rightMargin)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(leftMargin, 0f);
            rt.offsetMax = new Vector2(-rightMargin, 0f);
        }

        // 컬럼 안에서 위→아래로 고정 높이 행을 쌓는다(가변 리스트가 아닌 고정 그룹용).
        private static void Row(RectTransform rt, ref float y, float height, float gap = 8f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -y);
            y += height + gap;
        }

        private static void AnchorRect(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            AnchorRect(rt, xMin, yMin, xMax, yMax, Vector2.zero, Vector2.zero);
        }

        private static void AnchorRect(RectTransform rt, float xMin, float yMin, float xMax, float yMax, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private static void AnchorTopRight(RectTransform rt, float width, float height, float margin)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-margin, -margin);
        }

        private static void LeftAnchored(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        private static void RightAnchored(RectTransform rt, float x, float width, float height)
        {
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-x, 0f);
        }

        private static void Section(Transform parent, string text, ref float y)
        {
            var rt = NewUI("Section_" + text, parent);
            Row(rt, ref y, 20f, 10f);
            var t = AddText(rt, text, 13, TextAlignmentOptions.MidlineLeft, Cyan, true);
            t.characterSpacing = 5f;
        }

        private static void VDivider(Transform parent, float fromEdge, bool fromRight)
        {
            var rt = NewUI("Divider", parent);
            rt.anchorMin = new Vector2(fromRight ? 1f : 0f, 0.04f);
            rt.anchorMax = new Vector2(fromRight ? 1f : 0f, 0.96f);
            rt.pivot = new Vector2(fromRight ? 1f : 0f, 0.5f);
            rt.sizeDelta = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(fromRight ? -fromEdge : fromEdge, 0f);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = Line;
            img.raycastTarget = false;
        }

        // StatRowView 프리팹을 생성하거나(없으면) 기존 것을 로드해 돌려준다.
        // 런타임에 statListRoot(VerticalLayoutGroup)로 인스턴스화되므로 LayoutElement로 높이를 준다.
        private static StatRowView EnsureStatRowPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StatRowPrefabPath);
            if (existing != null)
            {
                var comp = existing.GetComponent<StatRowView>();
                if (comp != null) return comp;
            }

            EnsureFolder(PrefabDir);

            var root = NewUI("StatRowView", null); // 씬 루트에 임시 생성 → 저장 후 파기
            root.sizeDelta = new Vector2(280f, 30f);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            var label = AddText(NewUI("Label", root), "스탯", 13, TextAlignmentOptions.MidlineLeft, TextDim, false);
            AnchorRect(label.rectTransform, 0f, 0f, 0.26f, 1f);
            var cur = AddText(NewUI("Cur", root), "0", 15, TextAlignmentOptions.MidlineRight, TextDim, false);
            AnchorRect(cur.rectTransform, 0.26f, 0f, 0.52f, 1f);
            var arrow = AddText(NewUI("Arrow", root), "›", 15, TextAlignmentOptions.Center, CyanDim, false);
            AnchorRect(arrow.rectTransform, 0.52f, 0f, 0.6f, 1f);
            var next = AddText(NewUI("Next", root), "0", 16, TextAlignmentOptions.MidlineRight, TextWhite, true);
            AnchorRect(next.rectTransform, 0.6f, 0f, 0.82f, 1f);
            var delta = AddText(NewUI("Delta", root), "", 14, TextAlignmentOptions.MidlineRight, Gold, false);
            AnchorRect(delta.rectTransform, 0.82f, 0f, 1f, 1f);

            var view = root.gameObject.AddComponent<StatRowView>();
            var so = new SerializedObject(view);
            Wire(so, "labelText", label);
            Wire(so, "valueText", cur);
            Wire(so, "nextText", next);
            Wire(so, "deltaText", delta);
            so.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root.gameObject, StatRowPrefabPath);
            Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets();

            Debug.Log($"[EnhancePanelBuilder] StatRowView 프리팹 생성: {StatRowPrefabPath}");
            return saved != null ? saved.GetComponent<StatRowView>() : null;
        }

        // MaterialSlotView 프리팹을 생성하거나(없으면) 기존 것을 로드해 돌려준다.
        // 아이콘 박스(장식) + 실제 아이콘 Image(wired) + 이름/수량 텍스트 구성. 런타임 SetMaterials가 복제한다.
        private static MaterialSlotView EnsureMaterialSlotPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MaterialSlotPrefabPath);
            if (existing != null)
            {
                var comp = existing.GetComponent<MaterialSlotView>();
                if (comp != null) return comp;
            }

            EnsureFolder(PrefabDir);

            var root = NewUI("MaterialSlotView", null); // 씬 루트에 임시 생성 → 저장 후 파기
            root.sizeDelta = new Vector2(270f, 68f);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 68f;

            // 아이콘 박스(장식) — 배경 + 테두리
            var box = NewUI("IconBox", root);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.sizeDelta = new Vector2(56f, 56f);
            box.gameObject.AddComponent<Image>().color = BoxFill;
            var border = NewUI("Border", box);
            FullStretch(border);
            var bl = border.gameObject.AddComponent<Image>();
            bl.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f);
            bl.raycastTarget = false;

            // 실제 재료 아이콘(런타임에 sprite가 여기 들어감)
            var icon = NewUI("Icon", box).gameObject.AddComponent<Image>();
            FullStretch(icon.rectTransform, 4f, 4f, 4f, 4f);
            icon.color = Color.white;
            icon.raycastTarget = false;

            var nameLabel = AddText(NewUI("Name", root), "재료 이름", 17, TextAlignmentOptions.BottomLeft, TextWhite, false);
            AnchorRect(nameLabel.rectTransform, 0f, 0.45f, 1f, 1f, new Vector2(70f, 0f), Vector2.zero);
            var countLabel = AddText(NewUI("Count", root), "0/0", 14, TextAlignmentOptions.TopLeft, TextDim, false);
            AnchorRect(countLabel.rectTransform, 0f, 0f, 1f, 0.45f, new Vector2(70f, 0f), Vector2.zero);

            var view = root.gameObject.AddComponent<MaterialSlotView>();
            var so = new SerializedObject(view);
            Wire(so, "icon", icon);
            Wire(so, "nameText", nameLabel);
            Wire(so, "countText", countLabel);
            so.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root.gameObject, MaterialSlotPrefabPath);
            Object.DestroyImmediate(root.gameObject);
            AssetDatabase.SaveAssets();

            Debug.Log($"[EnhancePanelBuilder] MaterialSlotView 프리팹 생성: {MaterialSlotPrefabPath}");
            return saved != null ? saved.GetComponent<MaterialSlotView>() : null;
        }

        // 경로의 폴더가 없으면 상위부터 재귀로 만든다.
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void Wire(SerializedObject so, string propertyName, Object value)
        {
            var p = so.FindProperty(propertyName);
            if (p != null)
            {
                p.objectReferenceValue = value;
            }
            else
            {
                Debug.LogWarning($"[EnhancePanelBuilder] '{propertyName}' 프로퍼티를 찾지 못해 배선 실패");
            }
        }
    }
}
