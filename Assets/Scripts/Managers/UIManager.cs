using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Debugging;
using ProjectS.UI.Framework;
using ProjectS.UI;

namespace ProjectS.Managers
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        // 뒤로가기 버튼 (Esc)
        public InputAction backAction;

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
            }

            foreach (var popup in GetComponentsInChildren<BasePopup>(true))
            {
                popupMap[popup.GetType()] = popup;
                basePopups.Add(popup);
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

            panelStack.Push(panel);
            panel.Show();
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
        /// </summary>
        public void Back()
        {
            // 팝업이 있으면 팝업 먼저 닫기
            if (activePopups.Count > 0)
            {
                CloseTopPopup();
                return;
            }

            // 패널이 1개 이하면 종료 (마지막 패널은 안 닫음)
            if (panelStack.Count <= 1) return;

            panelStack.Pop().Hide();
            panelStack.Peek().Resume();
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
            popup.Show();
        }

        /// <summary>
        /// 지정한 팝업 하나를 닫고 활성 목록에서 제거한다. 목록에 없는 팝업이면 아무것도 하지 않는다.
        /// </summary>
        /// <param name="popup">닫을 팝업</param>
        internal void ClosePopup(BasePopup popup)
        {
            if (!activePopups.Contains(popup)) return;

            popup.Hide();
            activePopups.Remove(popup);
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
            Back();
            DevLog.Log("[UIManager] Back");
        }


        public void ShowLoading()
        => loadingPanel.Show();

        public void HideLoading()
            => loadingPanel.Hide();

        public void SetLoadingProgress(float progress)
            => loadingPanel.SetProgress(progress);

    }
}
