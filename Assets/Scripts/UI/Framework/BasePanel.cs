using UnityEngine;

// 패널: 전체 화면을 차지하는 큰 UI
// ex) HUD, 인벤토리, 상점
public abstract class BasePanel : MonoBehaviour
{
    /// <summary>
    /// 현재 패널이 열려있는지 외부에서 확인용
    /// </summary>
    public bool IsVisible { get; private set; }

    // 팀원들이 필요하면 override해서 쓰는 것들

    /// <summary>
    /// 열릴때 연출
    /// </summary>
    protected virtual void OnShow() { }

    /// <summary>
    /// 닫힐때 연출
    /// </summary>
    protected virtual void OnHide() { } 

    /// <summary>
    /// 처음 한 번만 실행 (초기화)
    /// </summary>
    protected virtual void OnInit() { }

    /// <summary>
    /// 스택에서 다시 올라올 때 (인벤토리 → 뒤로가기 → HUD 다시 보일 때)
    /// </summary>
    protected virtual void OnResume() { }

    /// <summary>
    /// 스택에서 잠깐 가려질 때
    /// </summary>
    protected virtual void OnPause() { }

    // UIManager만 호출하는 메서드들 ( UIManager에서만 호출하기 때문에 아래에 있는 메서드들은 따로 호출하지 않음)
    private bool _isInitialized = false;

    internal void Show()
    {
        // 최초 1회만 초기화
        if (!_isInitialized)
        {
            OnInit();
            _isInitialized = true;
        }

        gameObject.SetActive(true);
        IsVisible = true;
        OnShow();
    }

    internal void Hide()
    {
        OnHide();
        IsVisible = false;
        gameObject.SetActive(false);
    }

    internal void Pause()
    {
        OnPause();
        IsVisible = false;
    }

    internal void Resume()
    {
        IsVisible = true;
        OnResume();
    }
}
