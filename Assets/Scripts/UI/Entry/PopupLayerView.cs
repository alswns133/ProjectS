using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 진입 화면의 팝업 계층. 페이지(선택·클래스·생성) 바깥 최상단에 두어 어느 페이지에서든 쓴다.
    ///
    /// 딤(뒷배경 어둡게 + 클릭 차단)은 팝업 각자가 아니라 여기서 관리한다.
    /// 팝업이 서로를 이어 열 때(삭제 1단계 → 2단계) 각자 딤을 껐다 켜면 한 프레임 번쩍이기 때문이다.
    ///
    /// 서버 왕복 중 입력 차단(<see cref="SetBusy"/>)은 팝업보다 위에 있어야 한다.
    /// 아래에 두면 팝업 버튼이 그대로 눌려 같은 요청이 두 번 나간다.
    /// </summary>
    public class PopupLayerView : MonoBehaviour
    {
        [SerializeField] private Image dimmer;
        [SerializeField] private ConfirmPopupView confirmPopup;
        [SerializeField] private GameObject busyBlocker;

        /// <summary>지금 팝업이 하나라도 열려 있는지.</summary>
        public bool IsAnyOpen => confirmPopup.IsOpen;

        private void Awake()
        {
            confirmPopup.OnClosed += RefreshDimmer;

            confirmPopup.gameObject.SetActive(false);
            busyBlocker.SetActive(false);
            RefreshDimmer();
        }

        private void OnDestroy()
        {
            confirmPopup.OnClosed -= RefreshDimmer;
        }

        /// <summary>
        /// 예/아니오 확인 팝업을 연다. 삭제 2단계는 확인 콜백에서 이 메서드를 한 번 더 부르면 된다.
        /// </summary>
        /// <param name="message">본문 문구</param>
        /// <param name="subMessage">강조 보조 문구(복구 불가 경고 등). 비우면 표시하지 않는다</param>
        /// <param name="confirm">확인 버튼 라벨. 비우면 "예"</param>
        /// <param name="cancel">취소 버튼 라벨. 비우면 "아니오"</param>
        /// <param name="confirmed">확인 시 실행할 동작</param>
        /// <param name="cancelled">취소·ESC 시 실행할 동작(선택)</param>
        public void ShowConfirm(string message, string subMessage, string confirm, string cancel,
            Action confirmed, Action cancelled = null)
        {
            confirmPopup.Show(message, subMessage, confirm, cancel, confirmed, cancelled);
            RefreshDimmer();
        }

        /// <summary>확인 하나짜리 알림 팝업을 연다(이름 중복·생성 실패 등).</summary>
        public void ShowAlert()
        {
            RefreshDimmer();
        }

        /// <summary>
        /// 서버 왕복 중 화면 전체 입력을 막는다. 목록 로드·생성·삭제를 시작할 때 켜고
        /// 응답을 받으면 반드시 끈다(끄지 않으면 화면이 영영 안 눌린다).
        /// </summary>
        /// <param name="busy">차단할지</param>
        public void SetBusy(bool busy)
        {
            busyBlocker.SetActive(busy);
            if (busy) busyBlocker.transform.SetAsLastSibling();
        }

        /// <summary>열려 있는 팝업을 모두 닫는다(페이지 전환·흐름 취소).</summary>
        public void CloseAll()
        {
            confirmPopup.Close();
            RefreshDimmer();
        }

        // 팝업이 하나라도 열려 있으면 딤을 켜고, 팝업들 바로 아래로 내린다.
        private void RefreshDimmer()
        {
            dimmer.gameObject.SetActive(IsAnyOpen);
            if (IsAnyOpen) dimmer.transform.SetAsFirstSibling();
        }
    }
}
