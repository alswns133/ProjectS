using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 확인 버튼 하나짜리 알림 팝업. 되물을 게 없고 "이래서 안 됐다"만 알리면 되는 경우에 쓴다
    /// (이름 중복, 생성 실패, 네트워크 오류).
    ///
    /// 2버튼 <see cref="ConfirmPopupView"/>와 나눠 둔 이유: 선택지가 없는데 취소 버튼이 있으면
    /// 무엇을 취소하는 건지 모호해진다. 이름 중복처럼 서버에 물어봐야 아는 조건은
    /// 생성 버튼 비활성으로 미리 막을 수 없어서 이 팝업이 반드시 필요하다.
    /// </summary>
    public class AlertPopupView : MonoBehaviour
    {
        /// <summary>팝업이 닫혔다. PopupLayer가 딤을 끄는 신호로 쓴다.</summary>
        public event Action OnClosed;

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button okButton;
        [SerializeField] private TMP_Text okLabel;

        private Action onClose;

        /// <summary>지금 열려 있는지.</summary>
        public bool IsOpen => gameObject.activeSelf;

        private void OnEnable() => okButton.onClick.AddListener(HandleOk);

        private void OnDisable() => okButton.onClick.RemoveListener(HandleOk);

        // ESC = 확인. 선택지가 하나뿐이라 어느 쪽으로 닫아도 결과가 같다.
        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) HandleOk();
        }

        /// <summary>문구를 띄운다.</summary>
        /// <param name="message">본문 문구</param>
        /// <param name="label">확인 버튼 라벨. 비우면 "확인"</param>
        /// <param name="closed">닫힌 뒤 실행할 동작(선택)</param>
        public void Show(string message, string label = null, Action closed = null)
        {
            onClose = closed;

            messageText.text = message;
            okLabel.text = string.IsNullOrEmpty(label) ? "확인" : label;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>콜백 없이 닫는다.</summary>
        public void Close()
        {
            onClose = null;
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        private void HandleOk()
        {
            Action callback = onClose;
            Close();
            callback?.Invoke();
        }
    }
}
