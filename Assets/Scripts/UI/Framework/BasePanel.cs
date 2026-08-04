using UnityEngine;

namespace ProjectS.UI.Framework
{
    // 패널: 화면을 점유하는 큰 UI. 스택으로 관리해 한 번에 하나만 활성이고 뒤로가기(Back)로 되돌린다
    //        (새 패널을 열면 이전 패널은 Pause되어 뒤에 남고, 닫으면 Resume). 서로 공존하지 않는 화면 전환용.
    // ex) HUD, 상점, 강화창
    // cf) 인벤토리·장비창처럼 다른 창과 동시에 띄우는 창은 스택이 아니라 리스트로 공존하는 BasePopup을 쓴다.
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
        private bool isInitialized = false;

        internal void Show()
        {
            // 최초 1회만 초기화
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
}
