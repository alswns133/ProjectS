using UnityEngine;

// 팝업: 패널 위에 뜨는 작은 UI, 여러 개 동시에 가능
// 닫아도 뒤에 패널은 살아있어야 함!
// ex) 아이템 상세, 확인창, 레벨업
public abstract class BasePopup : MonoBehaviour
{
    public bool IsVisible { get; private set; }

    // 팀원들이 필요하면 override해서 쓰는 것들
    protected virtual void OnShow() { } // 열릴때 연출
    protected virtual void OnHide() { } // 닫힐때 연출
    // 처음 한 번만 실행
    protected virtual void OnInit() { }


    // UIManager만 호출하는 메서드들 ( UIManager에서만 호출하기 때문에 아래에 있는 메서드들은 따로 호출하지 않음)
    private bool _isInitialized = false;

    internal void Show()
    {
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

    // 팝업은 본인이 닫기 요청할 수 있어야 함
    // ex) 확인 버튼 눌렀을 때
    protected void RequestClose()
    {
        UIManager.Instance.ClosePopup(this);
    }
}
