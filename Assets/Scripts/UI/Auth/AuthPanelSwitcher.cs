using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 로그인 씬의 로그인↔회원가입 화면 전환을 담당한다. 두 폼을 동시에 띄우던 예전 구조와 달리,
    /// 기본은 로그인 화면이고 "회원가입" 버튼으로 가입 화면, "로그인/뒤로" 버튼(또는 Esc)으로
    /// 다시 로그인 화면으로 돌아온다.
    ///
    /// 폼 각각의 입력 검증·서버 호출은 <see cref="LoginUI"/>/<see cref="SignupUI"/>가 맡고,
    /// 이 컴포넌트는 오직 "지금 어느 화면을 보여줄지"만 관리한다(표시 책임 분리).
    /// AuthRoot에 하나 두고 두 패널과 전환 버튼을 연결한다.
    /// </summary>
    public class AuthPanelSwitcher : MonoBehaviour
    {
        [Header("화면")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private GameObject signupPanel;

        [Header("전환 버튼")]
        [Tooltip("로그인 화면의 '회원가입' 버튼 → 가입 화면으로")]
        [SerializeField] private Button showSignupButton;

        [Tooltip("가입 화면의 '로그인/뒤로' 버튼 → 로그인 화면으로. 없으면 Esc로만 복귀한다.")]
        [SerializeField] private Button showLoginButton;

        private void OnEnable()
        {
            if (showSignupButton != null) showSignupButton.onClick.AddListener(ShowSignup);
            if (showLoginButton != null) showLoginButton.onClick.AddListener(ShowLogin);

            ShowLogin();   // 진입 기본값 = 로그인 화면
        }

        private void OnDisable()
        {
            if (showSignupButton != null) showSignupButton.onClick.RemoveListener(ShowSignup);
            if (showLoginButton != null) showLoginButton.onClick.RemoveListener(ShowLogin);
        }

        // 가입 화면에서 Esc = 로그인 화면으로 되돌아간다(뒤로 버튼을 안 두더라도 복귀 경로를 보장).
        private void Update()
        {
            if (signupPanel == null || !signupPanel.activeSelf) return;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) ShowLogin();
        }

        /// <summary>로그인 화면을 보이고 가입 화면을 감춘다(기본 상태).</summary>
        public void ShowLogin() => SetLoginVisible(true);

        /// <summary>회원가입 화면을 보이고 로그인 화면을 감춘다.</summary>
        public void ShowSignup() => SetLoginVisible(false);

        // 한쪽을 켜면 다른 쪽은 반드시 꺼서 두 폼이 동시에 뜨지 않게 한다.
        // 각 패널에 FormNavigation을 따로 두면, 켜지는 순간 그 패널의 OnEnable이 첫 필드를 자동 포커스한다.
        private void SetLoginVisible(bool login)
        {
            if (loginPanel != null) loginPanel.SetActive(login);
            if (signupPanel != null) signupPanel.SetActive(!login);
        }
    }
}
