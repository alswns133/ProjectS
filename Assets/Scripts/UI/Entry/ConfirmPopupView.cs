using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 예/아니오를 묻는 2버튼 확인 팝업. 진입 화면의 되돌릴 수 없는 행동 앞에 끼운다
    /// (캐릭터 삭제 1·2단계, 캐릭터 생성 확인).
    ///
    /// 문구와 버튼 라벨을 <see cref="Show"/>로 받으므로 팝업을 종류마다 만들지 않는다.
    /// 삭제 2단계는 1단계의 확인 콜백에서 <see cref="Show"/>를 한 번 더 부르면 되고,
    /// 그래서 인스턴스도 하나면 충분하다.
    ///
    /// Bootstrap 씬의 <see cref="ConfirmDialog"/>와 역할이 같지만 별개다 —
    /// 그쪽은 게임 씬 전용 오버레이 싱글톤이라 진입 화면엔 존재하지 않는다.
    /// </summary>
    public class ConfirmPopupView : MonoBehaviour
    {
        /// <summary>팝업이 닫혔다(확인·취소·ESC 모두 포함). PopupLayer가 딤을 끄는 신호로 쓴다.</summary>
        public event Action OnClosed;

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text subText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private TMP_Text cancelLabel;

        // Hide가 곧 지우므로 실행 직전에 지역 변수로 캡처한 뒤 부른다.
        private Action onConfirm;
        private Action onCancel;

        /// <summary>지금 열려 있는지.</summary>
        public bool IsOpen => gameObject.activeSelf;

        private void OnEnable()
        {
            confirmButton.onClick.AddListener(HandleConfirm);
            cancelButton.onClick.AddListener(HandleCancel);
        }

        private void OnDisable()
        {
            confirmButton.onClick.RemoveListener(HandleConfirm);
            cancelButton.onClick.RemoveListener(HandleCancel);
        }

        // ESC = 취소. 파괴적 행동을 묻는 팝업이라 ESC는 언제나 안전한 쪽(취소)으로 붙인다.
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) HandleCancel();
        }

        /// <summary>
        /// 문구와 콜백을 받아 연다. 같은 인스턴스를 재사용하므로 이전 호출의 콜백은 덮어써진다.
        /// </summary>
        /// <param name="message">본문 문구</param>
        /// <param name="subMessage">강조 보조 문구(복구 불가 경고 등). 비우면 줄 자체가 사라진다</param>
        /// <param name="confirm">확인 버튼 라벨. 비우면 "예"</param>
        /// <param name="cancel">취소 버튼 라벨. 비우면 "아니오"</param>
        /// <param name="confirmed">확인 시 실행할 동작</param>
        /// <param name="cancelled">취소·ESC 시 실행할 동작(선택)</param>
        public void Show(string message, string subMessage, string confirm, string cancel,
            Action confirmed, Action cancelled = null)
        {
            onConfirm = confirmed;
            onCancel = cancelled;

            messageText.text = message;

            bool hasSub = !string.IsNullOrEmpty(subMessage);
            subText.text = hasSub ? subMessage : string.Empty;
            subText.gameObject.SetActive(hasSub);

            confirmLabel.text = string.IsNullOrEmpty(confirm) ? "예" : confirm;
            cancelLabel.text = string.IsNullOrEmpty(cancel) ? "아니오" : cancel;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();   // 다른 팝업 위로
        }

        /// <summary>콜백 없이 닫는다(외부에서 흐름을 접을 때).</summary>
        public void Close()
        {
            onConfirm = null;
            onCancel = null;
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        // 콜백이 또 팝업을 열 수 있으므로(삭제 1단계 → 2단계) 먼저 닫고 나서 실행한다.
        private void HandleConfirm()
        {
            Action callback = onConfirm;
            Close();
            callback?.Invoke();
        }

        private void HandleCancel()
        {
            Action callback = onCancel;
            Close();
            callback?.Invoke();
        }
    }
}
