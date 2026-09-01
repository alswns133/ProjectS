// 'Editor' 폴더 필수(UnityEditor 참조). 네임스페이스는 UnityEditor.Editor 가림 회피로 EditorTools.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ProjectS.Managers;
using ProjectS.UI;
using Object = UnityEngine.Object;

namespace ProjectS.EditorTools
{
    /// <summary>
    /// 초대 목록 팝업(docs/PARTY_WINDOW_UI.md §3)의 계층을 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Party Invite Popup
    ///
    /// ①~⑦ 요소를 모두 만들고 <see cref="PartyInvitePopup"/>의 인스펙터 연결까지 끝낸다.
    /// 카드 프리팹이 있으면 함께 물리고, 더미 데이터원을 붙여 <b>실행하면 바로 목록이 뜨는 상태</b>로 남긴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UIManager 직속으로 붙인다.</b> UIManager는 Awake에서 자기 자식만 훑어 팝업을 수집하므로
    /// 다른 곳에 두면 <c>RegisterPopup</c>을 누군가 짝 맞춰 불러야 한다. 대신 UIManager 아래는
    /// HUD Canvas 밖이라 자기 Canvas가 없으면 렌더링되지 않아, Canvas+Scaler+Raycaster를 함께 붙인다
    /// (던전 결과창과 같은 구조).
    /// </para>
    /// <para>
    /// <b>가이드 행을 스크롤 Content 안에 넣지 않는다.</b> 목록이 비어도 남아 있어야 하는데,
    /// Content 안에 두면 카드와 함께 스크롤되고 빈 상태에서 같이 사라진다.
    /// </para>
    /// <para>
    /// <b>빈 상태 안내는 스크롤 영역 위에 겹쳐 둔다.</b> Content 안의 한 줄로 만들면 레이아웃이
    /// 그것도 항목으로 세어 스크롤이 생긴다.
    /// </para>
    /// <para>
    /// 수치는 1920×1080 기준으로 코드에 박아 둔다. 배치를 손본 뒤 다시 실행하면 기존 계층을 지우고
    /// 새로 뽑으므로, 실행 전 확인 대화상자를 띄운다.
    /// </para>
    /// (2026-08-31 TH)
    /// </remarks>
    public static class PartyInvitePopupGenerator
    {
        private const string UndoLabel = "Create Party Invite Popup";
        private const string PopupName = "PartyInvitePopup";
        private const string CardPrefabPath = "Assets/Prefabs/UI/PartyPlayerCard.prefab";

        // 던전 입장 창 위에 뜨지만 사망 팝업(50)보다는 아래.
        private const int SortingOrder = 45;

        private static readonly Vector2 WindowSize = new(640f, 760f);
        private const float HeaderHeight = 56f;
        private const float TabRowHeight = 44f;
        private const float GuideRowHeight = 28f;
        private const float FooterHeight = 56f;
        private const float Pad = 16f;
        private const float RowGap = 8f;

        // 카드와 같은 열 폭을 써야 가이드 행의 라벨이 카드 구역과 맞아떨어진다.
        private const float DotColumnWidth = 35f;
        private const float StateWidth = 79f;

        private static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
        private static readonly Color WindowColor = new Color32(0x13, 0x19, 0x24, 0xFF);
        private static readonly Color PanelColor = new Color32(0x1A, 0x21, 0x30, 0xFF);
        private static readonly Color TabOnColor = new Color32(0x4F, 0xD8, 0xE8, 0xFF);
        private static readonly Color AccentColor = new Color32(0x2F, 0x5F, 0xD0, 0xFF);
        private static readonly Color MutedColor = new Color32(0x8D, 0x99, 0xAC, 0xFF);

        [MenuItem("Tools/ProjectS/Create Party Invite Popup")]
        public static void Create()
        {
            Transform parent = FindUIManagerRoot();
            if (parent == null)
            {
                EditorUtility.DisplayDialog(
                    "UIManager가 없다",
                    "현재 씬에 UIManager가 없어 팝업을 붙일 곳이 없다.\n" +
                    "UIManager가 있는 씬을 열고 다시 실행한다.",
                    "확인");
                return;
            }

            if (!ConfirmRebuild(parent)) return;

            PartyInvitePopup popup = Build(parent);

            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Selection.activeObject = popup.gameObject;
            EditorGUIUtility.PingObject(popup.gameObject);

            bool hasCard = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath) != null;
            Debug.Log($"[PartyInvitePopupGenerator] 초대 목록 팝업을 만들었다." +
                      (hasCard ? " 카드 프리팹도 연결했다." : $" 카드 프리팹({CardPrefabPath})이 없어 cardPrefab은 비어 있다.") +
                      "\n더미 데이터원(DummyPartyMemberSource)이 함께 붙었다 — 인스펙터에 줄을 적으면 바로 목록이 뜬다.");
        }

        /// <summary>이미 있으면 지우고 새로 만들지 물어본다. 손으로 손본 배치가 사라지기 때문이다.</summary>
        private static bool ConfirmRebuild(Transform parent)
        {
            Transform existing = parent.Find(PopupName);
            if (existing == null) return true;

            bool rebuild = EditorUtility.DisplayDialog(
                "초대 목록 팝업 다시 만들기",
                $"'{PopupName}'이 이미 있다. 새로 만들면 지금 계층과 손으로 손본 배치가 사라진다.\n\n계속할까?",
                "다시 만들기",
                "취소");

            if (!rebuild) return false;

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        private static PartyInvitePopup Build(Transform parent)
        {
            // ── 화면 루트(자기 Canvas) ──────────────────────────────────
            GameObject go = new(PopupName,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(PartyInvitePopup), typeof(DummyPartyMemberSource));

            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform screen = (RectTransform)go.transform;
            EntryUIBuilder.Fill(screen);

            // 뒤쪽(던전 입장 창) 클릭을 막는 딤. 없으면 팝업 뒤의 에피소드 카드가 눌린다.
            EntryUIBuilder.CreateFullScreenImage(screen, "Dim", DimColor, blocksRaycast: true);

            // ── 창 ──────────────────────────────────────────────────────
            Image window = EntryUIBuilder.CreateFullScreenImage(screen, "Window", WindowColor, blocksRaycast: true);
            RectTransform win = window.rectTransform;
            EntryUIBuilder.SetCenter(win, Vector2.zero, WindowSize);

            // ── ① 헤더 ─────────────────────────────────────────────────
            TextMeshProUGUI title = EntryUIBuilder.CreateTMP(win, "TitleText", "파티원 초대", 22f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            StretchTop(title.rectTransform, 0f, HeaderHeight, Pad, Pad + 44f);

            (Button closeButton, _) = EntryUIBuilder.CreateButton(win, "CloseButton", "✕", 18f, PanelColor);
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-Pad, -Pad);
            closeRect.sizeDelta = new Vector2(32f, 32f);

            // ── ②③⑤ 탭 · 검색 · 새로고침 ──────────────────────────────
            RectTransform tabRow = CreateRect(win, "TabRow");
            StretchTop(tabRow, HeaderHeight, HeaderHeight + TabRowHeight, Pad, Pad);

            ToggleGroup group = tabRow.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            Toggle onlineTab = CreateTab(tabRow, "OnlineTab", "접속 중", group, on: true);
            AnchorLeft(onlineTab.GetComponent<RectTransform>(), 0f, 96f);

            Toggle recentTab = CreateTab(tabRow, "RecentTab", "최근", group, on: false);
            AnchorLeft(recentTab.GetComponent<RectTransform>(), 96f + RowGap, 80f);

            TMP_InputField searchInput = EntryUIBuilder.CreateInputField(tabRow, "SearchInput", "닉네임 검색", 15f);
            RectTransform searchRect = searchInput.GetComponent<RectTransform>();
            searchRect.anchorMin = new Vector2(0f, 0f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(0.5f, 0.5f);
            searchRect.offsetMin = new Vector2(96f + 80f + RowGap * 2f, 0f);
            searchRect.offsetMax = new Vector2(-(36f + RowGap), 0f);

            (Button refreshButton, _) = EntryUIBuilder.CreateButton(tabRow, "RefreshButton", "⟳", 18f, PanelColor);
            RectTransform refreshRect = refreshButton.GetComponent<RectTransform>();
            refreshRect.anchorMin = new Vector2(1f, 0f);
            refreshRect.anchorMax = new Vector2(1f, 1f);
            refreshRect.pivot = new Vector2(1f, 0.5f);
            refreshRect.anchoredPosition = Vector2.zero;
            refreshRect.sizeDelta = new Vector2(36f, 0f);

            // ── ④ 가이드 행 (목록 밖) ──────────────────────────────────
            float guideTop = HeaderHeight + TabRowHeight + RowGap;
            RectTransform guideRow = CreateRect(win, "GuideRow");
            StretchTop(guideRow, guideTop, guideTop + GuideRowHeight, Pad, Pad);

            TextMeshProUGUI onlineLabel = EntryUIBuilder.CreateTMP(guideRow, "OnlineLabel", "온라인", 12f);
            onlineLabel.color = MutedColor;
            onlineLabel.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(onlineLabel.rectTransform, 0f, DotColumnWidth + 20f);

            // 정렬 방향을 뒤집는 버튼. 라벨에 ▲▼가 찍히므로 배경 없이 글자만 둔다.
            (Button sortButton, TextMeshProUGUI sortLabel) =
                EntryUIBuilder.CreateButton(guideRow, "SortButton", "플레이어 ▲", 12f, new Color(0f, 0f, 0f, 0f));
            sortLabel.color = MutedColor;
            sortLabel.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform sortRect = sortButton.GetComponent<RectTransform>();
            AnchorLeft(sortRect, DotColumnWidth + 24f, 160f);

            TextMeshProUGUI stateLabel = EntryUIBuilder.CreateTMP(guideRow, "StateLabel", "파티 가능 여부", 12f);
            stateLabel.color = MutedColor;
            stateLabel.alignment = TextAlignmentOptions.MidlineRight;
            AnchorRight(stateLabel.rectTransform, 0f, StateWidth + 40f);

            // ── ④ 목록 ─────────────────────────────────────────────────
            float listTop = guideTop + GuideRowHeight;
            float listBottom = FooterHeight + RowGap;

            RectTransform listArea = CreateRect(win, "ListArea");
            StretchMiddle(listArea, listTop, listBottom, Pad, Pad);

            GameObject scrollGo = new("ScrollView", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(listArea, false);
            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            EntryUIBuilder.Fill((RectTransform)scrollGo.transform);

            // RectMask2D는 Mask+Image 조합보다 가볍고 추가 드로우콜이 없다.
            GameObject viewportGo = new("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            RectTransform viewport = (RectTransform)viewportGo.transform;
            EntryUIBuilder.Fill(viewport);

            RectTransform content = CreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;    // 카드 높이는 프리팹이 정한다
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            scroll.content = content;

            // 빈 상태 안내는 스크롤 위에 겹친다(Content 안에 넣으면 항목으로 세어져 스크롤이 생긴다).
            RectTransform emptyRoot = CreateRect(listArea, "EmptyRoot");
            EntryUIBuilder.Fill(emptyRoot);

            TextMeshProUGUI emptyText = EntryUIBuilder.CreateTMP(emptyRoot, "EmptyText", "접속 중인 다른 플레이어가 없습니다", 14f);
            emptyText.color = MutedColor;
            emptyText.alignment = TextAlignmentOptions.Center;
            EntryUIBuilder.Fill(emptyText.rectTransform);
            emptyRoot.gameObject.SetActive(false);   // 첫 응답 전에는 띄우지 않는다

            // ── ⑥⑦ 하단 ───────────────────────────────────────────────
            RectTransform footer = CreateRect(win, "Footer");
            StretchBottom(footer, 0f, FooterHeight, Pad, Pad);

            (Button acceptButton, TextMeshProUGUI acceptLabel) =
                EntryUIBuilder.CreateButton(footer, "AcceptToggleButton", "파티 초대 허용", 14f, PanelColor);
            AnchorLeft(acceptButton.GetComponent<RectTransform>(), 0f, 180f);

            (Button inviteButton, TextMeshProUGUI inviteLabel) =
                EntryUIBuilder.CreateButton(footer, "InviteButton", "파티 초대", 15f, AccentColor);
            AnchorRight(inviteButton.GetComponent<RectTransform>(), 0f, 180f);
            inviteButton.interactable = false;   // 선택이 없으면 못 누른다

            // ── 인스펙터 연결 ───────────────────────────────────────────
            PartyInvitePopup popup = go.GetComponent<PartyInvitePopup>();
            GameObject cardAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
            PartyPlayerCard cardPrefab = cardAsset != null ? cardAsset.GetComponent<PartyPlayerCard>() : null;

            EntryUIBuilder.Wire(popup,
                ("closeButton", closeButton),
                ("memberSourceBehaviour", go.GetComponent<DummyPartyMemberSource>()),
                ("onlineTab", onlineTab),
                ("recentTab", recentTab),
                ("searchInput", searchInput),
                ("refreshButton", refreshButton),
                ("listRoot", content),
                ("cardPrefab", cardPrefab),
                ("scrollRect", scroll),
                ("sortButton", sortButton),
                ("sortLabel", sortLabel),
                ("emptyRoot", emptyRoot.gameObject),
                ("emptyText", emptyText),
                ("acceptToggleButton", acceptButton),
                ("acceptToggleLabel", acceptLabel),
                ("inviteButton", inviteButton),
                ("inviteButtonLabel", inviteLabel));

            go.SetActive(false);   // 팝업은 닫힌 채로 시작한다(UIManager가 연다)
            return popup;
        }

        // ── 만들기 도우미 ────────────────────────────────────────────────

        /// <summary>탭 하나. 켜지면 배경이 청록으로 바뀌는 On 이미지가 위에 올라온다.</summary>
        private static Toggle CreateTab(Transform parent, string name, string label, ToggleGroup group, bool on)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);

            Image bg = go.GetComponent<Image>();
            bg.sprite = EntryUIBuilder.UISprite;
            bg.type = Image.Type.Sliced;
            bg.color = PanelColor;

            Image onMark = EntryUIBuilder.CreateFullScreenImage(go.transform, "On", TabOnColor, blocksRaycast: false);
            onMark.sprite = EntryUIBuilder.UISprite;
            onMark.type = Image.Type.Sliced;

            TextMeshProUGUI text = EntryUIBuilder.CreateTMP(go.transform, "Label", label, 14f);
            text.alignment = TextAlignmentOptions.Center;
            EntryUIBuilder.Fill(text.rectTransform);

            Toggle toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = onMark;      // 켜졌을 때만 보이는 그래픽
            toggle.group = group;
            toggle.isOn = on;

            return toggle;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>부모 위쪽에서 <paramref name="top"/>~<paramref name="bottom"/> 만큼 떨어진 가로 줄.</summary>
        private static void StretchTop(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>부모 아래쪽 기준 가로 줄.</summary>
        private static void StretchBottom(RectTransform rt, float bottom, float top, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, top);
        }

        /// <summary>위아래를 모두 물려 남은 공간을 차지하는 영역(목록).</summary>
        private static void StretchMiddle(RectTransform rt, float top, float bottom, float left, float right)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private static void AnchorLeft(RectTransform rt, float left, float width)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
            rt.sizeDelta = new Vector2(width, 0f);
        }

        private static void AnchorRight(RectTransform rt, float right, float width)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(width, 0f);
        }

        private static Transform FindUIManagerRoot()
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            return manager != null ? manager.transform : null;
        }
    }
}
