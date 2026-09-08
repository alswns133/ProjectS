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
    /// 파티 결성창과 초대 수락 팝업(docs/PARTY_WINDOW_UI.md §7·§8)을 현재 씬에 만드는 에디터 툴.
    /// 메뉴: Tools ▸ ProjectS ▸ Create Party Roster Popup / Create Party Invite Accept Popup
    ///
    /// 결성창은 세 화면(파티 없음 · 파티 결성 · 출발 카운트다운)의 계층을 만들고
    /// <see cref="PartyRosterPopup"/>의 인스펙터 연결까지 끝낸다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>파티 데이터원을 UIManager 아래에 만든다.</b> 던전 입장 창 안에 두면 창이 닫힐 때 꺼져
    /// 타이머가 끊기고 파티가 사라진다. 씬에 이미 <see cref="DummyPartySource"/>가 있으면 그것을 쓰고,
    /// 없으면 새로 만든다. <b>슬롯 바가 다른 것을 보고 있으면 손으로 이쪽으로 옮겨야 한다</b> —
    /// 둘이 다른 데이터원을 보면 슬롯과 결성창이 서로 다른 파티를 그린다.
    /// </para>
    /// <para>
    /// <b>초대 수락 화면은 여기서 만들지 않는다(2026-09-07).</b> 받는 쪽은 판단에 필요한 것이
    /// 초대자·던전·남은 시간 셋뿐이라 별도 팝업으로 뺐다 — 같은 파일의
    /// <b>Create Party Invite Accept Popup</b> 메뉴로 만든다.
    /// </para>
    /// <para>
    /// 로스터 두 칸은 <c>PartySlot</c> 프리팹 인스턴스로 문다. 카드를 고치면 슬롯 바와 결성창이
    /// 함께 따라온다.
    /// </para>
    /// (2026-09-07 TH)
    /// </remarks>
    public static class PartyRosterUIGenerator
    {
        private const string UndoLabel = "Create Party Roster Popup";
        private const string PopupName = "PartyRosterPopup";
        private const string SourceName = "PartySource";
        private const string AcceptPopupName = "PartyInviteAcceptPopup";
        private const string SlotPrefabPath = "Assets/Prefabs/UI/PartySlot.prefab";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/Paperlogy-5Medium SDF.asset";

        // 사망 팝업(50)·상점(51)보다 위. 초대가 오면 최상위로 떠야 한다(§7).
        // 컨텍스트 메뉴가 사는 OverlayCanvas는 이보다 더 위에 둔다.
        private const int SortingOrder = 52;

        // 수락 팝업은 결성창 위에 뜬다 — 초대는 무엇을 보고 있든 끊고 들어와야 한다.
        private const int AcceptSortingOrder = 53;

        private static readonly Vector2 WindowSize = new(560f, 760f);
        private static readonly Vector2 AcceptWindowSize = new(440f, 300f);
        private const float Pad = 24f;
        private const float HeaderHeight = 64f;
        private const float DungeonRowHeight = 48f;
        private const float RosterHeight = 260f;
        private const float FooterHeight = 56f;
        private const float CountdownHeight = 76f;
        private const float Gap = 12f;
        private const float SlotWidth = 210f;

        private static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
        private static readonly Color WindowColor = new Color32(0x13, 0x19, 0x24, 0xFF);
        private static readonly Color PanelColor = new Color32(0x1A, 0x21, 0x30, 0xFF);
        private static readonly Color AccentColor = new Color32(0x2F, 0x5F, 0xD0, 0xFF);
        private static readonly Color CyanColor = new Color32(0x4F, 0xD8, 0xE8, 0xFF);
        private static readonly Color AmberColor = new Color32(0xF0, 0xB4, 0x29, 0xFF);
        private static readonly Color MutedColor = new Color32(0x8D, 0x99, 0xAC, 0xFF);
        private static readonly Color TextColor = new Color32(0xDC, 0xE3, 0xEE, 0xFF);

        [MenuItem("Tools/ProjectS/Create Party Roster Popup")]
        public static void Create()
        {
            Transform parent = FindUIManagerRoot();
            if (parent == null)
            {
                EditorUtility.DisplayDialog("UIManager가 없다",
                    "현재 씬에 UIManager가 없어 팝업을 붙일 곳이 없다.\nUIManager가 있는 씬을 열고 다시 실행한다.",
                    "확인");
                return;
            }

            if (!ConfirmRebuild(parent)) return;

            DummyPartySource source = EnsurePartySource(parent);
            PartyRosterPopup popup = Build(parent, source);

            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Selection.activeObject = popup.gameObject;
            EditorGUIUtility.PingObject(popup.gameObject);

            Debug.Log($"[PartyRosterUIGenerator] 결성창을 만들었다.\n" +
                      $"· 데이터원: {source.name} (UIManager 아래 — 창이 닫혀도 살아 있어야 한다)\n" +
                      "· 슬롯 바(PartySlotBar)가 다른 데이터원을 보고 있으면 이쪽으로 다시 연결한다.\n" +
                      "· 초대 수락 화면은 Create Party Invite Accept Popup 으로 따로 만든다.");
        }

        private static bool ConfirmRebuild(Transform parent)
        {
            Transform existing = parent.Find(PopupName);
            if (existing == null) return true;

            bool rebuild = EditorUtility.DisplayDialog("결성창 다시 만들기",
                $"'{PopupName}'이 이미 있다. 새로 만들면 지금 계층과 손으로 손본 배치가 사라진다.\n\n계속할까?",
                "다시 만들기", "취소");

            if (!rebuild) return false;

            Undo.DestroyObjectImmediate(existing.gameObject);
            return true;
        }

        /// <summary>
        /// 씬에 이미 있는 데이터원을 찾고, 없으면 UIManager 아래에 만든다.
        /// 창을 여는 <see cref="PartyWindowOpener"/>도 같은 오브젝트에 함께 붙인다 —
        /// 창들은 닫혀 있는 동안 꺼져 있어 스스로 초대를 감시할 수 없다.
        /// </summary>
        private static DummyPartySource EnsurePartySource(Transform parent)
        {
            DummyPartySource source = Object.FindAnyObjectByType<DummyPartySource>(FindObjectsInactive.Include);

            if (source == null)
            {
                GameObject go = new(SourceName, typeof(DummyPartySource));
                Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                go.transform.SetParent(parent, false);
                source = go.GetComponent<DummyPartySource>();
            }

            EnsureOpener(source);
            return source;
        }

        private static void EnsureOpener(DummyPartySource source)
        {
            PartyWindowOpener opener = source.GetComponent<PartyWindowOpener>();
            if (opener == null)
            {
                opener = Undo.AddComponent<PartyWindowOpener>(source.gameObject);
            }

            Wire(opener, ("partySourceBehaviour", source));
        }

        private static PartyRosterPopup Build(Transform parent, DummyPartySource source)
        {
            GameObject go = new(PopupName,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PartyRosterPopup));

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
            Fill(screen);

            CreateFilledImage(screen, "Dim", DimColor, blocksRaycast: true);

            Image window = CreateFilledImage(screen, "Window", WindowColor, blocksRaycast: true);
            RectTransform win = window.rectTransform;
            SetCenter(win, Vector2.zero, WindowSize);

            // ── 헤더 ────────────────────────────────────────────────
            TextMeshProUGUI title = CreateText(win, "TitleText", "파티", 24f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
            StretchTop(title.rectTransform, Pad, Pad + HeaderHeight - 16f, Pad, Pad + 60f);

            (Button closeButton, TextMeshProUGUI closeLabel) = CreateButton(win, "CloseButton", "✕", 16f, PanelColor);
            closeLabel.color = MutedColor;
            AnchorTopRight(closeButton.GetComponent<RectTransform>(), Pad, Pad, 32f, 32f);

            // ── 던전 줄(표시 전용) ──────────────────────────────────
            float rowTop = Pad + HeaderHeight;
            RectTransform dungeonRow = CreateRect(win, "DungeonRow");
            StretchTop(dungeonRow, rowTop, rowTop + DungeonRowHeight, Pad, Pad);
            CreateFilledImage(dungeonRow, "Background", PanelColor, blocksRaycast: false);

            TextMeshProUGUI rowLabel = CreateText(dungeonRow, "Label", "던전", 12f);
            rowLabel.color = MutedColor;
            rowLabel.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(rowLabel.rectTransform, 14f, 44f);

            TextMeshProUGUI dungeonName = CreateText(dungeonRow, "DungeonNameText", "폐기된 연구시설 1-1", 15f);
            dungeonName.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(dungeonName.rectTransform, 66f, 300f);

            TextMeshProUGUI difficulty = CreateText(dungeonRow, "DifficultyText", "노말", 13f);
            difficulty.color = CyanColor;
            difficulty.alignment = TextAlignmentOptions.MidlineRight;
            AnchorRight(difficulty.rectTransform, 14f, 100f);

            // ── 로스터 두 칸 ────────────────────────────────────────
            float rosterTop = rowTop + DungeonRowHeight + Gap;
            RectTransform roster = CreateRect(win, "Roster");
            StretchTop(roster, rosterTop, rosterTop + RosterHeight, Pad, Pad);

            GameObject slotAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
            if (slotAsset == null)
            {
                Debug.LogWarning($"[PartyRosterUIGenerator] {SlotPrefabPath} 를 찾지 못해 로스터 칸이 비어 있다. " +
                                 "Tools ▸ ProjectS ▸ Create Party Slot Bar 를 먼저 실행한다.");
            }

            float slotGap = 16f;
            float slotsWidth = SlotWidth * 2f + slotGap;
            float slotLeft = ((WindowSize.x - Pad * 2f) - slotsWidth) * 0.5f;

            PartySlotView selfSlot = InstantiateSlot(slotAsset, roster, "SelfSlot", slotLeft);
            PartySlotView partnerSlot = InstantiateSlot(slotAsset, roster, "PartnerSlot", slotLeft + SlotWidth + slotGap);

            // ── 파티 없음 ───────────────────────────────────────────
            float bodyTop = rosterTop + RosterHeight + Gap;
            float bodyBottom = Pad + FooterHeight + Gap;

            RectTransform emptyRoot = CreateRect(win, "EmptyRoot");
            StretchMiddle(emptyRoot, bodyTop, bodyBottom, Pad, Pad);

            TextMeshProUGUI emptyText = CreateText(emptyRoot, "EmptyText", "던전 입구에서 파티를 만들 수 있습니다", 14f);
            emptyText.color = MutedColor;
            emptyText.alignment = TextAlignmentOptions.Center;
            Fill(emptyText.rectTransform);

            // ── 파티 결성 · 출발 ────────────────────────────────────
            RectTransform formedRoot = CreateRect(win, "FormedRoot");
            StretchMiddle(formedRoot, bodyTop, Pad, Pad, Pad);

            CountdownView departCountdown = CreateCountdown(formedRoot, "DepartCountdown", "파티원 대기 중", CyanColor);
            StretchTop(departCountdown.GetComponent<RectTransform>(), 0f, CountdownHeight, 0f, 0f);

            TextMeshProUGUI departHint = CreateText(formedRoot, "DepartHintText", "파티장이 출발을 시작하면 활성화됩니다", 13f);
            departHint.color = MutedColor;
            departHint.alignment = TextAlignmentOptions.Center;
            StretchTop(departHint.rectTransform, CountdownHeight + 8f, CountdownHeight + 36f, 0f, 0f);

            (Button leaveButton, TextMeshProUGUI leaveLabel) =
                CreateButton(formedRoot, "LeaveButton", "파티 나가기", 15f, PanelColor);
            leaveLabel.color = TextColor;
            AnchorBottomLeft(leaveButton.GetComponent<RectTransform>(), 0f, 0f, HalfWidth(), FooterHeight);

            (Button departButton, TextMeshProUGUI departLabel) =
                CreateButton(formedRoot, "DepartButton", "던전 입장", 16f, AccentColor);
            departLabel.color = Color.white;
            AnchorBottomRight(departButton.GetComponent<RectTransform>(), 0f, 0f, HalfWidth(), FooterHeight);

            // ── 인스펙터 연결 ───────────────────────────────────────
            PartyRosterPopup popup = go.GetComponent<PartyRosterPopup>();
            Wire(popup,
                ("partySourceBehaviour", source),
                ("closeButton", closeButton),
                ("dungeonRow", dungeonRow.gameObject),
                ("dungeonNameText", dungeonName),
                ("difficultyText", difficulty),
                ("selfSlot", selfSlot),
                ("partnerSlot", partnerSlot),
                ("emptyRoot", emptyRoot.gameObject),
                ("emptyText", emptyText),
                ("formedRoot", formedRoot.gameObject),
                ("leaveButton", leaveButton),
                ("departButton", departButton),
                ("departLabel", departLabel),
                ("departHintText", departHint),
                ("departCountdown", departCountdown));

            go.SetActive(false);   // 팝업은 닫힌 채로 시작한다(UIManager가 연다)
            return popup;
        }

        // ── 초대 수락 팝업 ───────────────────────────────────────────────

        [MenuItem("Tools/ProjectS/Create Party Invite Accept Popup")]
        public static void CreateAcceptPopup()
        {
            Transform parent = FindUIManagerRoot();
            if (parent == null)
            {
                EditorUtility.DisplayDialog("UIManager가 없다",
                    "현재 씬에 UIManager가 없어 팝업을 붙일 곳이 없다.", "확인");
                return;
            }

            Transform existing = parent.Find(AcceptPopupName);
            if (existing != null)
            {
                bool rebuild = EditorUtility.DisplayDialog("수락 팝업 다시 만들기",
                    $"'{AcceptPopupName}'이 이미 있다. 새로 만들면 손으로 손본 배치가 사라진다.계속할까?",
                    "다시 만들기", "취소");

                if (!rebuild) return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            DummyPartySource source = EnsurePartySource(parent);
            PartyInviteAcceptPopup popup = BuildAcceptPopup(parent, source);

            EditorSceneManager.MarkSceneDirty(popup.gameObject.scene);
            Selection.activeObject = popup.gameObject;
            EditorGUIUtility.PingObject(popup.gameObject);

            Debug.Log("[PartyRosterUIGenerator] 초대 수락 팝업을 만들었다." +
                      "데이터원의 ⋮ 메뉴 '초대 받은 것처럼'으로 확인한다.");
        }

        private static PartyInviteAcceptPopup BuildAcceptPopup(Transform parent, DummyPartySource source)
        {
            GameObject go = new(AcceptPopupName,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PartyInviteAcceptPopup));

            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = AcceptSortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform screen = (RectTransform)go.transform;
            Fill(screen);
            CreateFilledImage(screen, "Dim", DimColor, blocksRaycast: true);

            Image window = CreateFilledImage(screen, "Window", WindowColor, blocksRaycast: true);
            RectTransform win = window.rectTransform;
            SetCenter(win, Vector2.zero, AcceptWindowSize);

            TextMeshProUGUI message = CreateText(win, "MessageText", "시온 님이 파티에 초대했습니다", 18f);
            message.alignment = TextAlignmentOptions.Center;
            StretchTop(message.rectTransform, Pad, Pad + 32f, Pad, Pad);

            TextMeshProUGUI meta = CreateText(win, "InviterMetaText", "Lv.31 검사", 13f);
            meta.color = MutedColor;
            meta.alignment = TextAlignmentOptions.Center;
            StretchTop(meta.rectTransform, Pad + 34f, Pad + 56f, Pad, Pad);

            // 무엇에 동의하는지가 보여야 한다 — 파티 수락이 아니라 '이 던전에 같이 가기' 수락이다.
            float rowTop = Pad + 68f;
            RectTransform dungeonRow = CreateRect(win, "DungeonRow");
            StretchTop(dungeonRow, rowTop, rowTop + DungeonRowHeight, Pad, Pad);
            CreateFilledImage(dungeonRow, "Background", PanelColor, blocksRaycast: false);

            TextMeshProUGUI rowLabel = CreateText(dungeonRow, "Label", "던전", 12f);
            rowLabel.color = MutedColor;
            rowLabel.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(rowLabel.rectTransform, 14f, 44f);

            TextMeshProUGUI dungeonName = CreateText(dungeonRow, "DungeonNameText", "폐기된 연구시설 1-1", 15f);
            dungeonName.alignment = TextAlignmentOptions.MidlineLeft;
            AnchorLeft(dungeonName.rectTransform, 66f, 220f);

            TextMeshProUGUI difficulty = CreateText(dungeonRow, "DifficultyText", "노말", 13f);
            difficulty.color = CyanColor;
            difficulty.alignment = TextAlignmentOptions.MidlineRight;
            AnchorRight(difficulty.rectTransform, 14f, 90f);

            CountdownView countdown = CreateCountdown(win, "Countdown", "응답 제한", AmberColor);
            StretchTop(countdown.GetComponent<RectTransform>(),
                       rowTop + DungeonRowHeight + Gap, rowTop + DungeonRowHeight + Gap + CountdownHeight, Pad, Pad);

            float half = (AcceptWindowSize.x - Pad * 2f - Gap) * 0.5f;

            (Button declineButton, TextMeshProUGUI declineLabel) =
                CreateButton(win, "DeclineButton", "거절", 15f, PanelColor);
            declineLabel.color = TextColor;
            AnchorBottomLeft(declineButton.GetComponent<RectTransform>(), Pad, Pad, half, FooterHeight);

            (Button acceptButton, TextMeshProUGUI acceptLabel) =
                CreateButton(win, "AcceptButton", "수락", 16f, AccentColor);
            acceptLabel.color = Color.white;
            AnchorBottomRight(acceptButton.GetComponent<RectTransform>(), Pad, Pad, half, FooterHeight);

            PartyInviteAcceptPopup popup = go.GetComponent<PartyInviteAcceptPopup>();
            Wire(popup,
                ("partySourceBehaviour", source),
                ("messageText", message),
                ("inviterMetaText", meta),
                ("dungeonNameText", dungeonName),
                ("difficultyText", difficulty),
                ("countdown", countdown),
                ("acceptButton", acceptButton),
                ("declineButton", declineButton));

            go.SetActive(false);
            return popup;
        }

        private static float HalfWidth() => (WindowSize.x - Pad * 2f - Gap) * 0.5f;

        private static PartySlotView InstantiateSlot(GameObject slotAsset, Transform parent, string name, float left)
        {
            if (slotAsset == null) return null;

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(slotAsset, parent);
            instance.name = name;

            RectTransform rt = (RectTransform)instance.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(left, 0f);
            rt.sizeDelta = new Vector2(SlotWidth, RosterHeight);

            return instance.GetComponent<PartySlotView>();
        }

        /// <summary>제목 · 숫자 · 진행 바를 갖춘 카운트다운 위젯 하나.</summary>
        private static CountdownView CreateCountdown(Transform parent, string name, string title, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(CountdownView));
            go.transform.SetParent(parent, false);

            RectTransform rt = (RectTransform)go.transform;
            CreateFilledImage(rt, "Background", PanelColor, blocksRaycast: false);

            TextMeshProUGUI titleText = CreateText(rt, "TitleText", title, 12f);
            titleText.color = MutedColor;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
            StretchTop(titleText.rectTransform, 10f, 30f, 14f, 100f);

            TextMeshProUGUI numberText = CreateText(rt, "NumberText", "00:30", 20f);
            numberText.color = color;
            numberText.alignment = TextAlignmentOptions.MidlineRight;
            StretchTop(numberText.rectTransform, 8f, 34f, 100f, 14f);

            // 진행 바는 Filled 이미지 한 장. 남은 비율만큼 가로로 채운다.
            Image fill = CreateFilledImage(rt, "FillImage", color, blocksRaycast: false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            StretchBottom(fill.rectTransform, 12f, 18f, 14f, 14f);

            CountdownView view = go.GetComponent<CountdownView>();
            Wire(view,
                ("titleText", titleText),
                ("numberText", numberText),
                ("fillImage", fill));

            return view;
        }

        // ── 만들기 도우미 ────────────────────────────────────────────────

        private static TMP_FontAsset KoreanFont
        {
            get
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (font != null) return font;

                Debug.LogWarning($"[PartyRosterUIGenerator] 한글 폰트를 찾지 못했다: {FontPath}");
                return TMP_Settings.defaultFontAsset;
            }
        }

        private static Sprite UISprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = TextColor;
            text.raycastTarget = false;

            TMP_FontAsset font = KoreanFont;
            if (font != null) text.font = font;
            return text;
        }

        private static (Button button, TextMeshProUGUI label) CreateButton(
            Transform parent, string name, string text, float size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.sprite = UISprite;
            image.type = Image.Type.Sliced;
            image.color = color;

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;

            TextMeshProUGUI label = CreateText(go.transform, "Label", text, size);
            label.alignment = TextAlignmentOptions.Center;
            Fill(label.rectTransform);

            return (button, label);
        }

        private static Image CreateFilledImage(Transform parent, string name, Color color, bool blocksRaycast)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = blocksRaycast;
            Fill((RectTransform)go.transform);
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        private static void SetCenter(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
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

        private static void AnchorTopRight(RectTransform rt, float right, float top, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void AnchorBottomLeft(RectTransform rt, float left, float bottom, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void AnchorBottomRight(RectTransform rt, float right, float bottom, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-right, bottom);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static Transform FindUIManagerRoot()
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            return manager != null ? manager.transform : null;
        }

        // private [SerializeField] 참조를 SerializedObject로 연결한다(필드를 public으로 열지 않기 위함).
        private static void Wire(Component comp, params (string prop, Object value)[] refs)
        {
            SerializedObject so = new(comp);
            foreach ((string prop, Object value) in refs)
            {
                SerializedProperty p = so.FindProperty(prop);
                if (p != null) p.objectReferenceValue = value;
                else Debug.LogWarning($"[PartyRosterUIGenerator] {comp.GetType().Name}에 '{prop}' 필드가 없다. 이름이 바뀌었는지 확인한다.");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
