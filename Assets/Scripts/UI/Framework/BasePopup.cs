using UnityEngine;
using ProjectS.Managers;

namespace ProjectS.UI.Framework
{
    // 팝업: 패널 위에 뜨는 작은 UI, 여러 개 동시에 가능
    // 닫아도 뒤에 패널은 살아있어야 함!
    // ex) 아이템 상세, 확인창, 레벨업
    public abstract class BasePopup : MonoBehaviour
    {
        public bool IsVisible { get; private set; }

        /// <summary>
        /// 뒤로가기(ESC)로 닫을 수 있는지. 기본은 true다.
        /// </summary>
        /// <remarks>
        /// 선택을 반드시 시켜야 하는 모달 팝업은 false로 재정의한다(예: 사망 팝업).
        /// 그런 팝업이 ESC로 닫히면 플레이어는 죽은 채 아무 UI도 없이 조작 불가 상태에 갇힌다 —
        /// 창을 다시 부를 방법이 없어 재시작 말고는 빠져나올 수 없다.
        /// </remarks>
        public virtual bool CanCloseByBack => true;

        // 팀원들이 필요하면 override해서 쓰는 것들
        protected virtual void OnShow() { } // 열릴때 연출
        protected virtual void OnHide() { } // 닫힐때 연출
        // 처음 한 번만 실행
        protected virtual void OnInit() { }


        // UIManager만 호출하는 메서드들 ( UIManager에서만 호출하기 때문에 아래에 있는 메서드들은 따로 호출하지 않음)
        private bool isInitialized = false;

        internal void Show()
        {
            if (!isInitialized)
            {
                OnInit();
                isInitialized = true;
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
}
