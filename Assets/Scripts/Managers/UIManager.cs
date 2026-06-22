using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
    private readonly Stack<BasePanel> _panelStack = new Stack<BasePanel>();

    // 팝업은 리스트로 (여러 개 동시에 가능)
    private readonly List<BasePopup> _activePopups = new List<BasePopup>();

    // 타입으로 빠르게 찾기 위한 Dictionary
    private readonly Dictionary<Type, BasePanel> _panelMap = new();
    private readonly Dictionary<Type, BasePopup> _popupMap = new();

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
            _panelMap[panel.GetType()] = panel;
            basePanels.Add(panel);
        }

        foreach (var popup in GetComponentsInChildren<BasePopup>(true))
        {
            _popupMap[popup.GetType()] = popup;
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
    /// 패널 관리
    /// </summary>
    /// <typeparam name="T">BasePanel를 상속받은 클래스만 사용</typeparam>
    public void ShowPanel<T>() where T : BasePanel
    {
        if (!_panelMap.TryGetValue(typeof(T), out var panel))
        {
            Debug.LogWarning($"[UIManager] {typeof(T).Name} 패널이 없음");
            return;
        }

        // 현재 패널 Pause
        if (_panelStack.Count > 0)
            _panelStack.Peek().Pause();

        _panelStack.Push(panel);
        panel.Show();
    }

    /// <summary>
    /// 씬 전환할 때 스택 초기화
    /// </summary>
    public void ClearPanelStack()
    {
        // 열려있는 패널 다 닫기
        while (_panelStack.Count > 0)
            _panelStack.Pop().Hide();

        // 팝업도 정리
        foreach (var popup in _activePopups)
            popup.Hide();
        _activePopups.Clear();
    }

    /// <summary>
    /// 닫기
    /// </summary>
    public void Back()
    {
        // 팝업이 있으면 팝업 먼저 닫기
        if (_activePopups.Count > 0)
        {
            CloseTopPopup();
            return;
        }

        // 패널이 1개 이하면 종료 (마지막 패널은 안 닫음)
        if (_panelStack.Count <= 1) return;

        _panelStack.Pop().Hide();
        _panelStack.Peek().Resume();
    }

    /// <summary>
    /// 팝업 관리
    /// </summary>
    /// /// <typeparam name="T">BasePopup 상속받은 클래스만 사용</typeparam>
    public void ShowPopup<T>() where T : BasePopup
    {
        if (!_popupMap.TryGetValue(typeof(T), out var popup))
        {
            Debug.LogWarning($"[UIManager] {typeof(T).Name} 팝업이 없음");
            return;
        }

        _activePopups.Add(popup);
        popup.Show();
    }

    /// <summary>
    /// 팝업 전체 닫기
    /// </summary>
    internal void ClosePopup(BasePopup popup)
    {
        if (!_activePopups.Contains(popup)) return;

        popup.Hide();
        _activePopups.Remove(popup);
    }

    /// <summary>
    /// 팝업 닫기
    /// </summary>
    private void CloseTopPopup()
    {
        var top = _activePopups[_activePopups.Count - 1];
        ClosePopup(top);
    }


    // 뒤로가기 버튼
    private void OnBack(InputAction.CallbackContext context)
    {
        Back();
        Debug.Log("[UIManager] Back");
    }


    public void ShowLoading()
    => loadingPanel.Show();

    public void HideLoading()
        => loadingPanel.Hide();

    public void SetLoadingProgress(float progress)
        => loadingPanel.SetProgress(progress);

}
