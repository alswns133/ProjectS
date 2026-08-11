using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 예/아니오를 묻는 재사용 단일 확인 대화상자. 메시지와 확인 콜백을 받아 화면 중앙에 뜬다.
    /// 아이템 파괴처럼 되돌릴 수 없는 행동 앞에 끼워 오조작을 막는다(상점 판매 등에도 재사용).
    ///
    /// UIManager가 관리하는 <see cref="ProjectS.UI.Framework.BasePopup"/>가 아니라
    /// <see cref="ItemContextMenu"/>·<see cref="ProjectS.UI.Framework.ItemTooltip"/>과 같은 <b>오버레이 싱글톤</b>이다 —
    /// 열 때마다 메시지·콜백을 직접 받아야 하는데, ShowPopup 경로는 파라미터를 넘길 수 없기 때문이다.
    /// 최상단 렌더링을 위해 OverlayCanvas(sortingOrder 100)에 둔다.
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        /// <summary>전역 접근점. 파괴 확인 등 호출부가 타입 참조 없이 연다.</summary>
        public static ConfirmDialog Instance { get; private set; }

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [Tooltip("대화상자 뒤 전체화면 블로커. 바깥을 클릭하면 취소로 닫힌다(선택)")]
        [SerializeField] private Button backgroundBlocker;

        // 확인 시 실행할 콜백. Hide가 곧 null로 지우므로 OnConfirm에서 먼저 캡처해 부른다.
        private Action onConfirm;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
            if (backgroundBlocker != null) backgroundBlocker.onClick.AddListener(Hide);

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 메시지와 확인 콜백으로 대화상자를 연다. 확인을 누르면 콜백을 실행한 뒤 닫히고,
        /// 취소·바깥 클릭은 콜백 없이 닫는다.
        /// </summary>
        /// <param name="message">표시할 문구</param>
        /// <param name="confirmed">확인 시 실행할 동작(null이면 확인해도 아무 일 없음)</param>
        public void Show(string message, Action confirmed)
        {
            onConfirm = confirmed;
            if (messageText != null) messageText.text = message;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();   // 컨텍스트 메뉴 등 다른 오버레이보다 위로
        }

        /// <summary>콜백 없이 닫는다(취소·바깥 클릭).</summary>
        public void Hide()
        {
            onConfirm = null;
            if (this != null) gameObject.SetActive(false);
        }

        // 확인: 콜백을 먼저 캡처하고 닫은 뒤 실행한다(콜백이 또 대화상자를 열어도 상태가 꼬이지 않게).
        private void OnConfirm()
        {
            Action callback = onConfirm;
            Hide();
            callback?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
