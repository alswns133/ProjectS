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
    /// 현재 씬의 사망 팝업을 <b>정식 흐름</b>(<see cref="DeathPopup"/> + <see cref="DeathPopupTrigger"/>)으로 전환한다.
    /// 시안 흐름(<see cref="DeathPopupPrototype"/> + <see cref="DeathPresenter"/>)으로 만들어 둔 씬을
    /// 아트/배치를 살린 채 갈아끼우는 것이 목적이다.
    /// 메뉴: Tools ▸ ProjectS ▸ 사망 팝업 → 정식(DeathPopup)으로 전환
    /// </summary>
    /// <remarks>
    /// <para>
    /// 스크립트만 바꿔서는 동작하지 않아 툴로 고정한다. 정식 <see cref="DeathPopupTrigger"/>는
    /// <see cref="DeathPresenter"/>와 달리 <c>RegisterPopup</c>을 부르지 않고 <b>UIManager의 자동 수집</b>
    /// (Awake의 <c>GetComponentsInChildren&lt;BasePopup&gt;</c>)에만 의존한다. 그래서 팝업이 UIManager
    /// 직속이 아니면 죽어도 "팝업이 없음" 경고만 남고 창이 뜨지 않는다.
    /// 반대로 UIManager 아래로 옮기면 HUD의 Canvas 밖으로 나가므로 <b>자기 Canvas가 없으면 렌더링되지 않는다</b>.
    /// 이 두 가지가 맞물려 있어 손으로 옮기면 반드시 한쪽이 깨진다.
    /// </para>
    /// <para>
    /// 하는 일(모두 멱등 — 여러 번 실행해도 안전):
    /// <list type="number">
    /// <item>시안 팝업 컴포넌트를 <see cref="DeathPopup"/>으로 교체하고 참조·문구·대기 시간을 모두 옮긴다
    ///       (포기 버튼 → returnToVillageButton, 카운트다운 텍스트 → countdownText). 자식 UI는 그대로 둔다.</item>
    /// <item>없는 표시 요소(딤 배경·타이틀·안내 문구·잔여 기회·카운트다운)를 만들어 연결한다.
    ///       Bootstrap.unity의 팝업처럼 버튼만 있는 씬을 위한 단계다 — 이미 연결된 것은 건드리지 않는다.</item>
    /// <item>자체 Canvas를 보장하고 UIManager 직속으로 옮긴 뒤 비활성으로 대기시킨다.</item>
    /// <item><see cref="DeathPresenter"/> 오브젝트를 지우고 UIManager에 <see cref="DeathPopupTrigger"/>를 붙인다
    ///       (Bootstrap.unity의 정식 배치와 같은 모양).</item>
    /// </list>
    /// </para>
    /// <para>
    /// 되돌리려면 Ctrl+Z 또는 씬을 저장하지 않고 다시 여는 것으로 충분하다. 시안 스크립트는 지우지 않으므로
    /// 'Tools ▸ ProjectS ▸ 사망·부활 팝업 생성'으로 시안 배치를 다시 만들 수도 있다.
    /// (2026-08-05 TH)
    /// </para>
    /// </remarks>
    public static class DeathPopupOfficialSwitch
    {
        // 시안 빌더(DeathPopupBuilder)와 같은 값. HUD(0)보다 위, LoadingPanel(100)보다 아래.
        private const int SortingOrder = 50;

        private const string TitleName = "TitleText";
        private const string GlitchShaderName = "ProjectS/UI Glitch Text";

        // 만드는 모든 TMP 텍스트에 쓸 한글 폰트. 전환 1회당 한 번만 해석한다.
        private static TMP_FontAsset s_font;

        // 시안 → 정식 필드 대응. 이름이 다른 것만 있으면 되고, 값의 의미는 같다.
        // 문자열 참조라 어느 한쪽 필드 이름이 바뀌면 컴파일러가 잡아주지 않으니 여기도 함께 고쳐야 한다.
        private static readonly (string From, string To)[] FieldMap =
        {
            ("reviveButton",      "reviveButton"),
            ("giveUpButton",      "returnToVillageButton"),
            ("messageText",       "messageText"),
            ("reviveCountText",   "countText"),
            ("returnTimerText",   "countdownText"),
            ("choiceGroup",       "choiceGroup"),
        };

        [MenuItem("Tools/ProjectS/사망 팝업 → 정식(DeathPopup)으로 전환", false, 112)]
        public static void SwitchToOfficial()
        {
            UIManager manager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                EditorUtility.DisplayDialog(
                    "정식 사망 팝업으로 전환",
                    "UIManager를 찾지 못했습니다.\n\n" +
                    "정식 팝업은 UIManager 직속에 있어야 자동 등록됩니다.\n" +
                    "UIManager가 있는 씬을 열고 다시 실행하세요.",
                    "확인");
                return;
            }

            DeathPopupPrototype prototype = Object.FindAnyObjectByType<DeathPopupPrototype>(FindObjectsInactive.Include);
            DeathPopup popup = Object.FindAnyObjectByType<DeathPopup>(FindObjectsInactive.Include);

            if (prototype == null && popup == null)
            {
                EditorUtility.DisplayDialog(
                    "정식 사망 팝업으로 전환",
                    "씬에 사망 팝업이 없습니다.\n\n" +
                    "'Tools ▸ ProjectS ▸ 사망·부활 팝업 생성'으로 먼저 만든 뒤 다시 실행하세요.",
                    "확인");
                return;
            }

            // 전환은 여러 단계로 이뤄지는데, 단계마다 Undo가 따로 쌓이면 Ctrl+Z 한 번이 '마지막 한 단계'만
            // 되돌린다. 그러면 트리거만 빠진 것처럼 겉보기엔 멀쩡한데 죽어도 창이 안 뜨는, 원인 찾기 어려운
            // 반쪽 상태가 만들어진다. 하나의 그룹으로 묶어 Ctrl+Z가 전부를 되돌리게 한다.
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("사망 팝업 정식 전환");

            string report = string.Empty;

            if (prototype != null)
            {
                if (popup != null && popup.gameObject != prototype.gameObject)
                {
                    // 정식 팝업이 이미 따로 있는 씬. 둘 다 살아 있으면 UIManager가 시안까지 수집해
                    // 어느 쪽이 뜰지 알 수 없어지므로 시안 쪽을 끈다(지우지는 않는다).
                    Undo.RecordObject(prototype.gameObject, "Disable prototype death popup");
                    prototype.gameObject.SetActive(false);
                    report += $"· 시안 팝업('{prototype.name}')은 정식과 겹치므로 비활성화했습니다(삭제 아님).\n";
                }
                else
                {
                    popup = ConvertInPlace(prototype, ref report);
                }
            }

            EnsureCanvas(popup.gameObject, ref report);
            EnsureUnderUIManager(popup.gameObject, manager, ref report);
            EnsureVisuals(popup, ref report);

            // BasePopup은 UIManager가 Show할 때 켜지는 것을 전제로 한다. 켠 채 두면 재생하자마자 화면을 덮는다.
            // 활성 상태는 RecordObject로 잡히지 않는 경우가 있어 RegisterCompleteObjectUndo를 쓴다.
            if (popup.gameObject.activeSelf)
            {
                Undo.RegisterCompleteObjectUndo(popup.gameObject, "Deactivate death popup");
                popup.gameObject.SetActive(false);
                report += "· 팝업을 비활성으로 되돌렸습니다(켜져 있으면 시작하자마자 화면을 덮습니다).\n";
            }

            RemovePresenters(ref report);
            EnsureTrigger(manager, ref report);

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = popup.gameObject;

            Undo.CollapseUndoOperations(undoGroup);

            string missing = Verify(popup, manager);

            Debug.Log("[DeathPopupOfficialSwitch] 정식 사망 팝업 흐름으로 전환했습니다.\n" +
                      (report.Length == 0 ? "· 이미 정식 배치라 바꿀 것이 없었습니다.\n" : report) +
                      "· ★ 씬을 저장해야 반영됩니다(Ctrl+S).\n" +
                      "· 부활 기회는 ReviveBudget(던전 한 판 1회)을 따릅니다 — 단독 씬 테스트에서는 " +
                      "DeathTester의 1번(기회 채우기)을 죽기 전에 실행해야 부활 버튼을 볼 수 있습니다.",
                      popup);

            if (missing.Length > 0)
            {
                Debug.LogError("[DeathPopupOfficialSwitch] ★ 전환이 끝났는데 아래가 충족되지 않았습니다. " +
                               "이 상태로 재생하면 죽어도 창이 뜨지 않습니다.\n" + missing, popup);
            }
        }

        /// <summary>
        /// 전환이 실제로 성립했는지 끝나고 다시 확인한다.
        /// </summary>
        /// <remarks>
        /// 중간에 예외가 나거나 Ctrl+Z로 일부만 되돌아가면 <b>겉보기엔 멀쩡한데 죽어도 창이 안 뜨는</b>
        /// 상태가 된다. 특히 트리거가 빠진 경우는 구독자가 없다는 뜻이라 <b>경고조차 나오지 않아</b>
        /// 원인을 찾기가 매우 어렵다. 그래서 결과를 신뢰하지 않고 다시 검사해 실패를 크게 알린다.
        /// </remarks>
        /// <returns>충족되지 않은 항목 목록. 비어 있으면 정상</returns>
        private static string Verify(DeathPopup popup, UIManager manager)
        {
            string result = string.Empty;

            if (Object.FindAnyObjectByType<DeathPopupTrigger>(FindObjectsInactive.Include) == null)
                result += "· DeathPopupTrigger가 씬에 없습니다 → 사망을 듣는 것이 없어 창이 열리지 않습니다.\n";

            if (popup == null)
            {
                result += "· DeathPopup을 찾을 수 없습니다.\n";
                return result;
            }

            if (popup.transform.parent != manager.transform)
                result += "· DeathPopup이 UIManager 직속이 아닙니다 → UIManager가 등록하지 못합니다.\n";

            if (popup.GetComponentInParent<Canvas>(true) == null)
                result += "· DeathPopup 위에 Canvas가 없습니다 → 열려도 화면에 나오지 않습니다.\n";

            if (popup.gameObject.activeSelf)
                result += "· DeathPopup이 켜진 채입니다 → 재생하자마자 화면을 덮습니다.\n";

            if (Object.FindAnyObjectByType<DeathPresenter>(FindObjectsInactive.Include) != null)
                result += "· 시안 DeathPresenter가 남아 있습니다 → 사망 시 두 번 반응합니다.\n";

            return result;
        }

        /// <summary>
        /// 시안 컴포넌트를 같은 오브젝트 위에서 정식 컴포넌트로 교체한다. 자식 UI(딤·타이틀·버튼)는 그대로 살린다.
        /// </summary>
        /// <remarks>
        /// 새로 만들지 않고 제자리에서 바꾸는 이유: 팝업 오브젝트에는 손으로 맞춘 배치·문구가 들어 있고,
        /// 다시 만들면 그게 전부 초기값으로 돌아간다. private [SerializeField]는 밖에서 대입할 수 없어
        /// SerializedObject로 옮긴다(필드 이름 문자열 참조라 이름이 바뀌면 여기도 함께 고쳐야 한다).
        /// </remarks>
        private static DeathPopup ConvertInPlace(DeathPopupPrototype prototype, ref string report)
        {
            SerializedObject src = new SerializedObject(prototype);

            Object[] carried = new Object[FieldMap.Length];
            for (int i = 0; i < FieldMap.Length; i++)
                carried[i] = src.FindProperty(FieldMap[i].From).objectReferenceValue;

            // 문구·대기 시간도 손봐 둔 값을 그대로 살린다. 다시 입력하게 만들면 씬마다 값이 갈린다.
            string chooseMessage = src.FindProperty("chooseMessage").stringValue;
            string autoReturnMessage = src.FindProperty("autoReturnMessage").stringValue;
            float returnDelay = src.FindProperty("returnDelay").floatValue;

            GameObject host = prototype.gameObject;
            Undo.DestroyObjectImmediate(prototype);

            DeathPopup popup = Undo.AddComponent<DeathPopup>(host);

            SerializedObject dst = new SerializedObject(popup);
            for (int i = 0; i < FieldMap.Length; i++)
                dst.FindProperty(FieldMap[i].To).objectReferenceValue = carried[i];

            if (!string.IsNullOrEmpty(chooseMessage)) dst.FindProperty("chooseMessage").stringValue = chooseMessage;
            if (!string.IsNullOrEmpty(autoReturnMessage)) dst.FindProperty("autoReturnMessage").stringValue = autoReturnMessage;
            dst.FindProperty("returnDelay").floatValue = returnDelay;
            dst.ApplyModifiedPropertiesWithoutUndo();

            report += $"· '{host.name}'의 팝업 스크립트를 시안 → 정식(DeathPopup)으로 교체하고 참조·문구를 옮겼습니다" +
                      $"(부활={Describe(carried[0])}, 마을 복귀={Describe(carried[1])}).\n";

            if (carried[0] == null || carried[1] == null)
                report += "· ⚠ 버튼 참조가 비어 있습니다 → 인스펙터에서 직접 연결하세요(안 하면 눌러도 아무 일이 없습니다).\n";

            // 카운트다운은 정식에서도 쓰지만, 대기 중에만 켜지므로 시작 상태는 꺼진 쪽이 맞다.
            if (Deactivate(carried[4]))
                report += "· 카운트다운 텍스트를 껐습니다(대기 중에만 켜집니다).\n";

            return popup;
        }

        /// <summary>
        /// 팝업이 UIManager 밖으로 나가도 그려지도록 자기 Canvas를 보장한다.
        /// </summary>
        /// <remarks>
        /// UIManager는 UI 계층이 아니라 그냥 Transform이다. 그 밑으로 옮기는 순간 부모 Canvas가 사라지므로,
        /// Canvas가 없으면 오브젝트는 켜져 있는데 화면에 아무것도 안 나오는 상태가 된다
        /// (DungeonEntryPopup·LoadingPanel과 같은 조건).
        /// </remarks>
        private static void EnsureCanvas(GameObject popup, ref string report)
        {
            Canvas canvas = popup.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = Undo.AddComponent<Canvas>(popup);
                report += $"· 자체 Canvas를 추가했습니다(sortingOrder {SortingOrder}) — 없으면 팝업이 화면에 나오지 않습니다.\n";
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            if (popup.GetComponent<CanvasScaler>() == null)
            {
                CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(popup);
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0f;
                report += "· CanvasScaler를 추가했습니다(1920x1080 기준 — 다른 창과 글자 크기를 맞추기 위함).\n";
            }

            // 없으면 클릭이 팝업에 닿지 않아 '버튼이 안 눌리는' 증상이 된다.
            if (popup.GetComponent<GraphicRaycaster>() == null)
            {
                Undo.AddComponent<GraphicRaycaster>(popup);
                report += "· GraphicRaycaster를 추가했습니다(없으면 버튼 클릭이 먹지 않습니다).\n";
            }
        }

        /// <summary>
        /// 팝업을 UIManager 직속으로 옮긴다. 정식 트리거가 자동 수집에만 의존하므로 이것이 등록 조건이다.
        /// </summary>
        private static void EnsureUnderUIManager(GameObject popup, UIManager manager, ref string report)
        {
            if (popup.transform.parent == manager.transform) return;

            string before = popup.transform.parent != null ? popup.transform.parent.name : "(루트)";
            Undo.SetTransformParent(popup.transform, manager.transform, "Reparent death popup");

            // 자기 Canvas가 ScreenSpaceOverlay라 RectTransform은 Canvas가 화면에 맞춰 덮어쓴다.
            // 스케일만 초기화해 두지 않으면 이전 부모의 스케일(0 등)이 남아 안 보이는 사고가 난다.
            popup.transform.localScale = Vector3.one;
            popup.transform.localPosition = Vector3.zero;

            report += $"· 팝업을 '{before}' 아래에서 UIManager 직속으로 옮겼습니다 — " +
                      "UIManager는 자기 자식만 수집하므로 이 위치가 아니면 등록되지 않습니다.\n";
        }

        /// <summary>
        /// 시안용 <see cref="DeathPresenter"/>를 걷어낸다. 남겨 두면 사망 시 정식 트리거와 함께 두 번 반응한다.
        /// </summary>
        private static void RemovePresenters(ref string report)
        {
            foreach (DeathPresenter presenter in Object.FindObjectsByType<DeathPresenter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject host = presenter.gameObject;

                // 프리젠터 전용으로 만들어진 빈 오브젝트면 통째로 지운다(빌더가 그렇게 만든다).
                // 다른 컴포넌트가 함께 붙어 있으면 그쪽을 망가뜨리지 않도록 컴포넌트만 뗀다.
                bool hostIsDedicated = host.transform.childCount == 0 && host.GetComponents<Component>().Length <= 2;

                if (hostIsDedicated)
                {
                    Undo.DestroyObjectImmediate(host);
                    report += $"· 시안 프리젠터 오브젝트('{host.name}')를 제거했습니다.\n";
                }
                else
                {
                    Undo.DestroyObjectImmediate(presenter);
                    report += $"· '{host.name}'에서 DeathPresenter 컴포넌트만 제거했습니다.\n";
                }
            }
        }

        /// <summary>
        /// UIManager에 <see cref="DeathPopupTrigger"/>를 붙인다(Bootstrap.unity의 정식 배치와 동일).
        /// </summary>
        /// <remarks>
        /// 항상 켜져 있는 오브젝트여야 한다. 꺼지는 오브젝트에 붙으면 OnDisable로 구독이 풀려
        /// "죽어도 아무 창이 안 뜨고 경고도 없는" 상태가 된다.
        /// </remarks>
        private static void EnsureTrigger(UIManager manager, ref string report)
        {
            if (manager.GetComponent<DeathPopupTrigger>() != null) return;

            // 다른 곳에 이미 붙어 있으면 중복으로 달지 않는다(둘이 되면 사망 때 두 번 반응한다).
            DeathPopupTrigger existing = Object.FindAnyObjectByType<DeathPopupTrigger>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (!existing.gameObject.activeInHierarchy)
                    report += $"· ⚠ DeathPopupTrigger가 꺼진 오브젝트('{existing.name}')에 있습니다 → 켜진 곳으로 옮기세요.\n";
                return;
            }

            Undo.AddComponent<DeathPopupTrigger>(manager.gameObject);
            report += "· UIManager에 DeathPopupTrigger를 붙였습니다(사망 이벤트를 듣는 쪽).\n";
        }

        /// <summary>
        /// 정식 팝업에 없는 표시 요소(딤 배경·타이틀·안내 문구·잔여 기회·카운트다운)를 만들어 연결한다.
        /// </summary>
        /// <remarks>
        /// Bootstrap.unity의 팝업은 버튼 두 개뿐이라 그대로 두면 <b>남은 초와 안내 문구가 화면에 나오지 않는다</b>
        /// — 기능은 돌지만 플레이어는 자기가 왜 기다리는지 알 수 없다.
        /// 이미 연결된 필드와 이미 있는 오브젝트는 건드리지 않으므로, 아트가 붙은 뒤 다시 실행해도 안전하다.
        /// 스프라이트·색·좌표는 시안 빌더(DeathPopupBuilder)와 같은 임시값이다.
        /// </remarks>
        private static void EnsureVisuals(DeathPopup popup, ref string report)
        {
            GameObject root = popup.gameObject;
            s_font = ResolveKoreanFont();

            EnsureDim(root, ref report);
            EnsureTitle(root, ref report);

            SerializedObject so = new SerializedObject(popup);

            // 팝업이 열릴 때 연출을 재생하려면 팝업이 타이틀 연출을 알아야 한다.
            // (비어 있어도 DeathPopup이 자식에서 찾지만, 슬롯에 보이는 편이 배선을 읽기 쉽다.)
            SerializedProperty fxProperty = so.FindProperty("titleFx");
            if (fxProperty != null && fxProperty.objectReferenceValue == null)
            {
                GlitchTextFx fx = root.GetComponentInChildren<GlitchTextFx>(true);
                if (fx != null)
                {
                    fxProperty.objectReferenceValue = fx;
                    report += "· 타이틀 글리치 연출을 팝업의 Title Fx 슬롯에 연결했습니다.\n";
                }
            }

            if (EnsureLabel(so, "messageText", root, "MessageText", "부활하시겠습니까?",
                            new Vector2(0f, 80f), 30f, Color.white, true))
                report += "· 안내 문구(MessageText)를 만들어 연결했습니다.\n";

            if (EnsureLabel(so, "countText", root, "ReviveCountText", "1 / 1",
                            new Vector2(0f, 20f), 28f, new Color(0.85f, 0.85f, 0.85f), true))
                report += "· 잔여 기회 텍스트(ReviveCountText)를 만들어 연결했습니다.\n";

            // 대기 중에만 켜지는 것이라 꺼진 상태로 만든다.
            if (EnsureLabel(so, "countdownText", root, "CountdownText", "3",
                            new Vector2(0f, -200f), 40f, new Color(0.9f, 0.8f, 0.4f), false))
                report += "· 카운트다운 텍스트(CountdownText)를 만들어 연결했습니다.\n";

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // 전체 화면 딤. 뒤에 있는 HUD 버튼이 클릭되지 않게 막는 역할도 겸한다(raycastTarget 기본 true).
        private static void EnsureDim(GameObject root, ref string report)
        {
            if (root.GetComponent<Image>() != null) return;

            Image dim = Undo.AddComponent<Image>(root);
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            report += "· 전체 화면 딤 배경을 추가했습니다(뒤쪽 UI 클릭도 함께 막힙니다).\n";
        }

        /// <summary>
        /// 타이틀 텍스트를 보장하고 글리치 연출(<see cref="GlitchTextFx"/>)을 붙인다.
        /// </summary>
        /// <remarks>
        /// 셰이더는 <see cref="GlitchTextFx"/>가 이름으로도 찾지만, 슬롯에 넣어 두지 않으면 아무도 참조하지 않아
        /// 빌드에서 빠질 수 있다. 그러면 에디터에서만 되고 빌드에서는 글자 연출만 도는 상태가 된다.
        /// </remarks>
        private static void EnsureTitle(GameObject root, ref string report)
        {
            TMP_Text title = FindChildText(root, TitleName);

            if (title == null)
            {
                TextMeshProUGUI created = CreateLabel(root.transform, TitleName, "사망", new Vector2(0f, 180f), 72f);
                created.color = new Color(0.86f, 0.22f, 0.22f);
                title = created;
                report += "· 타이틀(TitleText)을 만들었습니다.\n";
            }

            if (title.GetComponent<GlitchTextFx>() != null) return;

            GlitchTextFx fx = Undo.AddComponent<GlitchTextFx>(title.gameObject);

            Shader shader = Shader.Find(GlitchShaderName);
            if (shader != null)
            {
                SerializedObject so = new SerializedObject(fx);
                so.FindProperty("glitchShader").objectReferenceValue = shader;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                report += $"· ⚠ '{GlitchShaderName}' 셰이더를 찾지 못했습니다 → 글자 연출만 재생됩니다.\n";
            }

            report += "· 타이틀에 글리치 연출(GlitchTextFx)을 붙였습니다.\n";
        }

        // 이미 연결돼 있으면 아무것도 하지 않는다. 같은 이름의 텍스트가 이미 있으면(손으로 만들어 둔 것)
        // 새로 만들지 않고 그것을 연결한다 — 두 번 실행해도 텍스트가 겹쳐 쌓이지 않게 하기 위함이다.
        private static bool EnsureLabel(SerializedObject so, string field, GameObject root, string name,
                                        string content, Vector2 pos, float fontSize, Color color, bool startActive)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null || property.objectReferenceValue != null) return false;

            TMP_Text existing = FindChildText(root, name);
            if (existing != null)
            {
                property.objectReferenceValue = existing;
                return false;
            }

            TextMeshProUGUI label = CreateLabel(root.transform, name, content, pos, fontSize);
            label.color = color;
            if (!startActive) label.gameObject.SetActive(false);

            property.objectReferenceValue = label;
            return true;
        }

        private static TMP_Text FindChildText(GameObject root, string name)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == name) return text;
            }

            return null;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string content, Vector2 pos, float fontSize)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, "Create death popup label");
            go.transform.SetParent(parent, false);

            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;   // 텍스트가 버튼 클릭을 먹지 않게.

            if (s_font != null) tmp.font = s_font;
            else if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;

            // 화면 중앙 기준 배치. 앵커를 중앙으로 모아 해상도가 바뀌어도 가운데를 유지한다.
            RectTransform rect = tmp.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(720f, 90f);

            return tmp;
        }

        // 프로젝트에서 'Paperlogy' TMP 폰트 에셋을 찾는다(한글 글리프 포함). 시안 빌더와 같은 규칙 —
        // 기본 폰트로 두면 한글이 전부 네모로 나온다.
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

        private static bool Deactivate(Object target)
        {
            if (target is Component component && component.gameObject.activeSelf)
            {
                Undo.RecordObject(component.gameObject, "Deactivate death popup countdown");
                component.gameObject.SetActive(false);
                return true;
            }

            return false;
        }

        private static string Describe(Object target) => target != null ? $"'{target.name}'" : "(비어 있음)";
    }
}
