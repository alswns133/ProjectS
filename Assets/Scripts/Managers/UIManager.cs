using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ProjectS.Core;
using ProjectS.Debugging;
using ProjectS.NPCs;
using ProjectS.UI.Framework;
using ProjectS.UI;

namespace ProjectS.Managers
{
    /// <summary>UI 루트 싱글톤. 자식의 BasePanel/BasePopup을 타입별로 모아 패널 스택·팝업 목록을 관리하고, ESC(뒤로가기)를 <c>Back()</c> 한 곳에서 처리한다.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIManager : MonoBehaviour
    {
        /// <summary>싱글톤 인스턴스.</summary>
        public static UIManager Instance { get; private set; }

        /// <summary>
        /// 연출(보스 등장 등) 때문에 UI 전체가 숨겨져 있는지. raw 키보드를 읽는 UI 핫키(인벤·포션·채팅 등)는
        /// 이 값이 true면 입력을 무시해야 한다 — 숨김은 GameObject를 끄지 않고 알파만 내리므로, 핫키가 계속 살아 있어
        /// 안 보이는 창이 열리거나 포션이 소모되기 때문이다.
        /// </summary>
        public static bool IsHidden => Instance != null && Instance.hidden;

        /// <summary>뒤로가기 버튼 (Esc)</summary>
        public InputAction backAction;

        // UI 루트 전체의 표시/클릭을 한 번에 끄고 켜는 그룹. SetHidden 참고.
        private CanvasGroup rootGroup;
        private bool hidden;

        // 에디터 확인용 리스트 (빌드 시 지울 예정)
        [SerializeField] private List<BasePanel> basePanels;
        [SerializeField] private List<BasePopup> basePopups;

        private LoadingPanel loadingPanel;

        // 패널은 스택으로 (뒤로가기 처리)
        private readonly Stack<BasePanel> panelStack = new Stack<BasePanel>();

        // 팝업은 리스트로 (여러 개 동시에 가능)
        private readonly List<BasePopup> activePopups = new List<BasePopup>();

        // 타입으로 빠르게 찾기 위한 Dictionary
        private readonly Dictionary<Type, BasePanel> panelMap = new();
        private readonly Dictionary<Type, BasePopup> popupMap = new();

        private void Awake()
        {
            if (Instance != null) 
            { 
                Destroy(gameObject); 
                return; 
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // RequireComponent는 이미 씬에 배치된 오브젝트에 소급해 붙지 않으므로, 없으면 여기서 붙인다.
            rootGroup = GetComponent<CanvasGroup>();
            if (rootGroup == null) rootGroup = gameObject.AddComponent<CanvasGroup>();

            // 인스펙터에 일일이 등록 안 해도 됨!
            // 자식 오브젝트에서 자동으로 찾아옴
            foreach (var panel in GetComponentsInChildren<BasePanel>(true))
            {
                // 타입으로 구분!
                if (panel is LoadingPanel loading)
                {
                    loadingPanel = loading;
                    continue;
                }
                panelMap[panel.GetType()] = panel;
                basePanels.Add(panel);
                panel.gameObject.SetActive(false);  // Awake 시점에 켜져 있으면 OnInit이 두 번 돌고, 스택에 없는 패널이 켜진다.
            }

            foreach (var popup in GetComponentsInChildren<BasePopup>(true))
            {
                popupMap[popup.GetType()] = popup;
                basePopups.Add(popup);
                popup.gameObject.SetActive(false); // Awake 시점에 켜져 있으면 OnInit이 두 번 돌고, 스택에 없는 팝업이 켜진다.
            }
        }

        private void OnEnable()
        {
            if (backAction != null)
            {
                backAction.started += OnBack;
                backAction.Enable();
            }
        }

        private void OnDisable()
        {
            if (backAction != null)
            {
                backAction.started -= OnBack;
                backAction.Disable();
            }
        }

        /// <summary>
        /// UI 전체를 숨기거나 되살린다. 보스 등장 연출처럼 화면을 비워야 할 때 부른다.
        /// </summary>
        /// <remarks>
        /// GameObject를 끄지 않고 루트 CanvasGroup의 알파·클릭만 내린다. 끄면 그 아래 코루틴이 강제 종료돼
        /// (스킬 쿨타임 게이지가 연출 뒤 멈춘 채로 남음) Presenter 구독도 풀려 연출 중 이벤트를 놓친다.
        /// 대신 키 입력은 살아 있으므로 ESC는 <see cref="OnBack"/>에서, 나머지 raw 핫키는 각자 <see cref="IsHidden"/>로 거른다.
        /// 하위에 ignoreParentGroups를 켠 CanvasGroup이 있으면 그 요소는 숨겨지지 않는다.
        /// </remarks>
        /// <param name="hide">true면 숨기고, false면 되살린다.</param>
        public void SetHidden(bool hide)
        {
            hidden = hide;
            rootGroup.alpha = hide ? 0f : 1f;
            rootGroup.interactable = !hide;
            rootGroup.blocksRaycasts = !hide;
        }

        /// <summary>
        /// T 타입 패널을 스택에 쌓아 표시한다. 기존 최상단 패널은 Pause시켜,
        /// 뒤로가기(Back) 시 되살릴 수 있게 한다. 등록되지 않은 패널이면 경고만 남기고 무시한다.
        /// </summary>
        /// <typeparam name="T">BasePanel을 상속받은 클래스</typeparam>
        public void ShowPanel<T>() where T : BasePanel
        {
            if (!panelMap.TryGetValue(typeof(T), out var panel))
            {
                Debug.LogWarning($"[UIManager] {typeof(T).Name} 패널이 없음");
                return;
            }

            // 현재 패널 Pause
            if (panelStack.Count > 0)
                panelStack.Peek().Pause();

            // TODO(sound): 패널 전환(메뉴 진입/확정)음 — SoundManager.Instance.PlaySFX(SoundID.SFX_MenuConfirm);
            panelStack.Push(panel);
            panel.Show();
        }

        /// <summary>
        /// 자식이 아닌 패널을 등록한다. <see cref="RegisterPopup"/>의 패널 판이다.
        /// </summary>
        /// <remarks>
        /// UIManager는 Awake에서 <b>자기 자식에서만</b> BasePanel을 수집하므로, 나중에 로드되는 씬에
        /// 따로 배치된 패널(HUD 루트 아래의 HUDPanel 등)은 어느 흐름에서도 panelMap에 들어가지 못한다.
        /// 그 상태로 <see cref="ShowPanel{T}"/>를 부르면 "패널이 없음" 경고만 남고 <c>OnInit</c>이 돌지 않아,
        /// 게이지 같은 내부 요소가 초기화되지 않은 채 남는다(FillGauge는 코루틴 주인을 OnInit에서 받는다).
        ///
        /// 같은 호출을 여러 번 해도 안전하다(멱등). Awake 순서에 기대지 않도록, 여는 쪽에서
        /// 열기 직전에 불러도 되게 만든 것이다.
        /// </remarks>
        /// <param name="panel">등록할 패널</param>
        public void RegisterPanel(BasePanel panel)
        {
            if (panel == null) return;

            Type type = panel.GetType();
            if (panelMap.TryGetValue(type, out var existing) && existing != panel)
                Debug.LogWarning($"[UIManager] {type.Name} 패널이 이미 등록돼 있어 교체함");

            panelMap[type] = panel;

            if (basePanels != null && !basePanels.Contains(panel))
                basePanels.Add(panel);
        }

        /// <summary>
        /// 패널 등록을 해제한다. 씬이 언로드될 때 호출해야 한다 —
        /// UIManager는 씬을 넘어 살아남으므로, 안 하면 panelMap이 파괴된 패널을 계속 들고 있게 된다.
        /// </summary>
        /// <param name="panel">해제할 패널</param>
        public void UnregisterPanel(BasePanel panel)
        {
            if (panel == null) return;

            Type type = panel.GetType();
            if (panelMap.TryGetValue(type, out var existing) && existing == panel)
                panelMap.Remove(type);

            basePanels?.Remove(panel);
        }

        /// <summary>
        /// 씬 전환 시 호출. 열려 있던 모든 패널과 팝업을 닫아 UI 상태를 깨끗이 초기화한다.
        /// (이전 씬의 UI가 다음 씬에 남는 것을 방지)
        /// </summary>
        public void ClearPanelStack()
        {
            // 열려있는 패널 다 닫기
            while (panelStack.Count > 0)
                panelStack.Pop().Hide();

            // 팝업도 정리
            foreach (var popup in activePopups)
                popup.Hide();
            activePopups.Clear();
        }

        /// <summary>
        /// 뒤로가기 처리(Esc/뒤로 버튼). 팝업이 열려 있으면 팝업부터 하나 닫고,
        /// 없으면 패널 스택을 한 단계 되돌린다. 마지막 패널 1개는 닫지 않는다(빈 화면 방지).
        /// 닫을 게 하나도 없으면(인게임 HUD만 떠 있음) 옵션창을 연다.
        /// </summary>
        public void Back()
        {
            // 파괴 확인 대화상자(오버레이 싱글톤, UIManager 스택 밖)가 떠 있으면 ESC는 그 창의 취소로만 쓰고
            // 뒤의 팝업/패널로 흘려보내지 않는다(그러지 않으면 ESC가 열려 있던 다른 팝업을 닫는다).
            if (ConfirmDialog.Instance != null && ConfirmDialog.Instance.IsOpen)
            {
                ConfirmDialog.Instance.Hide();
                return;
            }

            // 팝업이 있으면 팝업 먼저 닫기
            if (activePopups.Count > 0)
            {
                // 모달 팝업(사망 팝업 등)은 뒤로가기를 무시한다. 아래 패널까지 흘려보내지 않고 여기서 끊는 이유는,
                // "닫히면 안 되는 창이 떠 있는 동안"에는 뒤에 있는 패널도 닫히면 안 되기 때문이다.
                if (!activePopups[activePopups.Count - 1].CanCloseByBack) return;

                CloseTopPopup();
                return;
            }

            // 패널이 1개 이하면 닫을 게 없다(마지막 패널은 안 닫음) → 옵션창을 연다.
            // 규칙: "ESC는 열린 걸 하나 닫고, 닫을 게 없으면 옵션창을 연다." 옵션창 닫기는 위 팝업 분기가 맡는다.
            // ESC를 UIManager 한 곳에서만 받기 위해 옵션용 PopupHotkey(Esc)는 두지 않는다 — 두면 한 번의 ESC에
            // 둘이 같이 반응해 "인벤을 닫는 ESC가 옵션을 여는" 식으로 꼬인다.
            if (panelStack.Count <= 1)
            {
                if (CanOpenOptionsByBack()) PopupToggle.Toggle(PopupToggle.PopupKind.Options);
                return;
            }

            // TODO(sound): 뒤로가기(메뉴 취소/이전)음 — SoundManager.Instance.PlaySFX(<메뉴 취소 SFX>);
            panelStack.Pop().Hide();
            panelStack.Peek().Resume();
        }

        // 닫을 게 없을 때 ESC로 옵션창을 열어도 되는지. ESC를 자기 닫기 키로 쓰는 다른 UI(NPC 허브·대화·채팅)와
        // 한 번의 ESC가 겹치지 않게 거른다. Input System 콜백끼리는 실행 순서가 정해져 있지 않으므로
        // "아직 열려 있음"(Back이 먼저 돈 경우)과 "이번 프레임에 방금 닫힘"(Back이 나중에 돈 경우)을 둘 다 본다.
        private bool CanOpenOptionsByBack()
        {
            // 인게임(HUD가 최상단)에서만 연다. 부트스트랩 대기·결과 화면(스택을 비우고 단독으로 뜸)·로딩 중엔 열지 않는다.
            if (panelStack.Count == 0 || !(panelStack.Peek() is HUDPanel)) return false;
            if (loadingPanel != null && loadingPanel.IsVisible) return false;

            // NPC 상호작용(허브·퀘스트 목록·NPC 대화).
            if (NpcInteractionController.Active != null) return false;
            if (NpcInteractionController.LastClosedFrame == Time.frameCount) return false;

            // NPC 없이 도는 대화(튜토리얼·퀘스트 게이트).
            DialogueManager dialogue = DialogueManager.Instance;
            if (dialogue != null && dialogue.IsPlaying) return false;
            if (DialogueManager.LastEndedFrame == Time.frameCount) return false;

            // 채팅 타이핑 중 ESC는 포커스 해제용이다(ChatWindow.Update가 처리하며, 이 콜백보다 늦게 돈다).
            return !IsTextInputFocused();
        }

        // UiTypingGuard("선택된 게 입력창인가")가 아니라 isFocused로 본다. ESC로 포커스를 풀어도 EventSystem의
        // 선택은 입력창에 남을 수 있어, 선택 기준이면 채팅을 한 번 쓴 뒤로 다른 곳을 클릭하기 전까지 ESC가 옵션을 못 연다.
        private static bool IsTextInputFocused()
        {
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            return field != null && field.isFocused;
        }

        /// <summary>
        /// T 타입 팝업을 표시한다. 팝업은 스택이 아닌 리스트로 관리해 여러 개가 동시에 떠 있을 수 있다.
        /// 등록되지 않은 팝업이면 경고만 남기고 무시한다.
        /// </summary>
        /// <typeparam name="T">BasePopup을 상속받은 클래스</typeparam>
        public void ShowPopup<T>() where T : BasePopup
        {
            if (!popupMap.TryGetValue(typeof(T), out var popup))
            {
                Debug.LogWarning($"[UIManager] {typeof(T).Name} 팝업이 없음");
                return;
            }

            activePopups.Add(popup);
            SoundManager.Instance?.PlaySFX(SoundID.SFX_PopupOpen);
            popup.Show();
        }

        /// <summary>
        /// T 타입 팝업이 지금 열려 있는지 확인한다. I키처럼 같은 키로 열고 닫는 토글 입력이
        /// 지금 그 창이 떠 있는지 판단하는 데 쓴다(팝업은 여러 개 공존하므로 스택 최상단이 아니라 존재로 판정).
        /// </summary>
        /// <typeparam name="T">확인할 BasePopup 파생 타입</typeparam>
        /// <returns>해당 타입 팝업이 활성 목록에 있으면 true</returns>
        public bool IsPopupOpen<T>() where T : BasePopup
        {
            foreach (BasePopup popup in activePopups)
            {
                if (popup is T) return true;
            }
            return false;
        }

        /// <summary>
        /// 자식이 아닌 팝업을 등록한다. UIManager는 Bootstrap에서 DontDestroyOnLoad로 살아남으면서
        /// <b>자기 자식에서만</b> BasePopup을 수집하므로, 나중에 로드되는 씬(HUD·던전 등)에 배치된 팝업은
        /// 어느 흐름에서도 popupMap에 들어가지 못한다. 그 팝업들이 스스로 등록할 수 있게 열어 둔다.
        ///
        /// 같은 호출을 여러 번 해도 안전하다(멱등). Awake 순서에 기대지 않도록, 여는 쪽에서
        /// 열기 직전에 불러도 되게 만든 것이다.
        /// </summary>
        /// <param name="popup">등록할 팝업</param>
        public void RegisterPopup(BasePopup popup)
        {
            if (popup == null) return;

            Type type = popup.GetType();
            if (popupMap.TryGetValue(type, out var existing) && existing != popup)
                Debug.LogWarning($"[UIManager] {type.Name} 팝업이 이미 등록돼 있어 교체함");

            popupMap[type] = popup;

            if (basePopups != null && !basePopups.Contains(popup))
                basePopups.Add(popup);
        }

        /// <summary>
        /// 등록을 해제한다. 씬이 언로드될 때 반드시 호출해야 한다 —
        /// UIManager는 씬을 넘어 살아남으므로, 안 하면 popupMap이 파괴된 팝업을 계속 들고 있게 된다.
        /// </summary>
        /// <param name="popup">해제할 팝업</param>
        public void UnregisterPopup(BasePopup popup)
        {
            if (popup == null) return;

            ClosePopup(popup);   // 열려 있으면 닫고 활성 목록에서도 뺀다

            Type type = popup.GetType();
            if (popupMap.TryGetValue(type, out var existing) && existing == popup)
                popupMap.Remove(type);

            basePopups?.Remove(popup);
        }

        /// <summary>
        /// 지정한 팝업 하나를 닫고 활성 목록에서 제거한다. 목록에 없는 팝업이면 아무것도 하지 않는다.
        /// 닫은 팝업이 마지막이고 HUD만 떠 있으면 커서를 잠가 TPS 조작으로 돌려보낸다.
        /// </summary>
        /// <remarks>
        /// 커서 잠금을 각 팝업의 OnHide가 아니라 여기서 판정하는 이유: OnHide 시점엔 닫히는 팝업이 아직 목록에 남아 있고,
        /// 팝업은 다른 팝업이 떠 있는지 모른다. 팝업마다 스스로 잠그면 "인벤을 켠 채 입장창을 닫았더니 커서가 잠기는" 식으로 꼬인다.
        /// </remarks>
        /// <param name="popup">닫을 팝업</param>
        internal void ClosePopup(BasePopup popup)
        {
            if (!activePopups.Contains(popup)) return;

            SoundManager.Instance?.PlaySFX(SoundID.SFX_PopupClose);
            popup.Hide();
            activePopups.Remove(popup);

            // 팝업이 남아 있거나 HUD 위에 다른 패널이 떠 있으면 아직 마우스로 조작 중이라 잠그지 않는다.
            // Count만 보지 않고 HUDPanel인지까지 보는 이유: 결과 화면·캐릭터 선택처럼 HUD 없이 혼자 뜬 패널도
            // 개수는 1개라, 거기서 잠그면 마우스로 쓰는 화면에서 커서가 사라진다.
            // Cursor를 직접 바꾸지 않고 Player를 거쳐야 OnCursorModeChanged가 나가 HUD 위젯(퀘스트 트래커 등)이 따라온다.
            if (activePopups.Count == 0 && panelStack.Count == 1 && panelStack.Peek() is HUDPanel)
            {
                PlayerManager.Instance?.Player?.SetCursorMode(false);
            }
        }

        /// <summary>
        /// T 타입 팝업을 닫는다. 인스턴스를 들고 있지 않은 외부(예: 던전 게이트가 이탈 시 팝업을 닫을 때)에서
        /// 타입만으로 닫을 수 있게 한다. 열려 있지 않으면 아무것도 하지 않는다.
        /// </summary>
        /// <typeparam name="T">BasePopup을 상속받은 클래스</typeparam>
        public void ClosePopup<T>() where T : BasePopup
        {
            if (popupMap.TryGetValue(typeof(T), out var popup))
                ClosePopup(popup);
        }

        /// <summary>
        /// T 타입 팝업 인스턴스를 돌려준다. 여는 쪽이 <b>열기 전에</b> 데이터를 먹여야 하는 팝업
        /// (던전 입장 팝업의 모드·카탈로그 등)을 위해 조회 경로만 연 것이다.
        /// </summary>
        /// <remarks>
        /// <see cref="ShowPopup{T}"/>는 반환값이 없고, 팝업 인스턴스는 UIManager(DontDestroyOnLoad) 아래에 있어
        /// 다른 씬의 오브젝트가 인스펙터로 참조를 꽂을 수 없다(씬 간 참조 불가). 그래서 타입으로 찾는 길이 필요하다.
        /// 열지도 닫지도 않으므로 activePopups는 건드리지 않는다 — 상태를 바꾸지 않는 순수 조회다
        /// (<see cref="ClosePopup{T}"/>가 이미 하고 있는 조회를 밖에서도 쓸 수 있게 연 것).
        /// </remarks>
        /// <typeparam name="T">BasePopup을 상속받은 클래스</typeparam>
        /// <returns>등록된 팝업. 등록돼 있지 않으면 null</returns>
        public T GetPopup<T>() where T : BasePopup
        {
            return popupMap.TryGetValue(typeof(T), out var popup) ? popup as T : null;
        }

        /// <summary>
        /// 등록된 패널을 타입으로 찾아 반환한다(스택 최상단 여부와 무관한 순수 조회).
        /// 아직 열린 적 없거나 수집되지 않은 패널이면 null이므로, 씬 종료 등 켜져 있다는 보장이 없는
        /// 시점에서 부를 때는 반환값을 null 조건 접근(<c>?.</c>)으로 다뤄야 한다.
        /// </summary>
        /// <typeparam name="T">BasePanel을 상속받은 클래스</typeparam>
        /// <returns>등록된 패널. 등록돼 있지 않으면 null</returns>
        public T GetPanel<T>() where T : BasePanel
        {
            return panelMap.TryGetValue(typeof(T), out var panel) ? panel as T : null;
        }

        /// <summary>
        /// 가장 최근에 연(맨 위) 팝업을 닫는다. 뒤로가기가 팝업을 한 번에 하나씩 닫을 때 사용.
        /// </summary>
        private void CloseTopPopup()
        {
            var top = activePopups[activePopups.Count - 1];
            ClosePopup(top);
        }


        // 뒤로가기 버튼
        private void OnBack(InputAction.CallbackContext context)
        {
            // 숨김 중 ESC는 무시한다 — 안 보이는 옵션창이 열리거나 안 보이는 팝업이 닫히는 것을 막는다.
            if (hidden) return;

            Back();
            DevLog.Log("[UIManager] Back");
        }

        /// <summary>
        /// 로딩 화면을 띄운다. 목적지는 켜질 때 기본 표기로 초기화되므로,
        /// 목적지를 아는 호출자는 이어서 <see cref="SetLoadingDestination"/>을 부른다.
        /// </summary>
        public void ShowLoading()
            => loadingPanel.Show();

        /// <summary>로딩 화면을 닫는다.</summary>
        public void HideLoading()
            => loadingPanel.Hide();

        /// <summary>로딩 화면 진행도를 갱신한다.</summary>
        /// <param name="progress">0~1 진행도.</param>
        public void SetLoadingProgress(float progress)
            => loadingPanel.SetProgress(progress);

        /// <summary>
        /// 로딩 화면에 이동할 목적지를 띄운다. 씬 이름으로 목적지 표를 찾아 이름·구역 코드를 표시한다.
        /// </summary>
        /// <param name="sceneName">로드할 씬 이름(씬 클래스 이름)</param>
        public void SetLoadingDestination(string sceneName)
            => loadingPanel.SetDestination(sceneName);

    }
}
