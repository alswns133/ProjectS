using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 이메일/비밀번호 로그인 화면. 입력을 받아 <see cref="FirebaseManager.Login"/>을 호출하고
    /// 결과를 메시지로 표시한다. Firebase 타입은 매니저 뒤에 있고, 여기선 LoginResult 열거형만 다룬다.
    /// </summary>
    public class LoginUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField emailField;
        [SerializeField] private TMP_InputField passwordField;
        [SerializeField] private Button loginButton;
        [SerializeField] private TMP_Text messageText;   // 결과/오류 표시(없어도 동작)

        [Header("로그인 성공 후 전환 (비우면 전환 생략)")]
        [Tooltip("로그인 성공 시 이동할 캐릭터 선택 씬 이름. Build Settings에 등록돼 있어야 한다.")]
        [SerializeField] private string characterSelectScene = "CharacterSelect";

        [Header("자동 로그인")]
        [SerializeField] private Toggle autoLoginToggle;   // 체크 시 다음 실행에 로그인 건너뜀(기본 해제)

        // 자동 로그인 선택을 저장하는 키. 세션은 Firebase가 유지하고, 이 값이 "그 세션을 쓸지"를 가른다.
        private const string AutoLoginKey = "auth.autoLogin";

        private void OnEnable() => loginButton.onClick.AddListener(OnClickLogin);
        private void OnDisable() => loginButton.onClick.RemoveListener(OnClickLogin);

        // 자동 로그인 스킵: 이미 로그인된 세션이면 로그인 화면을 건너뛰고 캐릭터 선택으로 바로 넘긴다.
        // Firebase가 이전 세션을 유지·복원하므로, 초기화(ReadyTask)를 기다린 뒤 CurrentUid를 확인한다.
        private async void Start()
        {
            // 토글 초기값 복원(기본 false = 해제).
            bool autoLogin = PlayerPrefs.GetInt(AutoLoginKey, 0) == 1;
            if (autoLoginToggle != null) autoLoginToggle.isOn = autoLogin;

            if (FirebaseManager.Instance == null) return;

            await FirebaseManager.Instance.ReadyTask;
            if (this == null) return;

            // Firebase가 이전 세션을 복원했더라도, 자동 로그인을 껐으면 그 세션을 폐기하고 로그인을 다시 받는다.
            if (FirebaseManager.Instance.IsLoggedIn)
            {
                if (autoLogin) GoToCharacterSelect();
                else FirebaseManager.Instance.Logout();
            }
        }

        // async void는 UI 이벤트 핸들러에서만 예외적으로 허용한다(진입점 성격).
        private async void OnClickLogin()
        {
            string email = emailField.text.Trim();
            string password = passwordField.text.Trim();

            if (string.IsNullOrEmpty(email)) { SetMessage("이메일을 입력하세요."); return; }
            if (!AuthValidation.IsValidEmail(email)) { SetMessage("이메일 형식이 올바르지 않습니다."); return; }
            if (string.IsNullOrEmpty(password)) { SetMessage("비밀번호를 입력하세요."); return; }

            // 중복 클릭 방지: 요청 중에는 버튼을 잠근다.
            loginButton.interactable = false;
            SetMessage("로그인 중...");

            LoginResult result = await FirebaseManager.Instance.Login(email, password);

            loginButton.interactable = true;
            SetMessage(DescribeResult(result));

            if (result == LoginResult.Success)
            {
                // 자동 로그인 선택 저장(다음 실행 때 이 값으로 스킵 여부 결정).
                PlayerPrefs.SetInt(AutoLoginKey, (autoLoginToggle != null && autoLoginToggle.isOn) ? 1 : 0);
                PlayerPrefs.Save();

                passwordField.text = string.Empty;
                GoToCharacterSelect();
            }
        }

        // 로그인 성공 → 캐릭터 선택 씬으로 이동한다. 선택은 이제 같은 씬의 패널이 아니라 별도 씬이라
        // 씬 전체를 로드한다(로그인 씬은 이때 언로드됨). 씬 이름이 비어 있으면 전환을 생략한다
        // (로그인 씬만 단독으로 테스트할 때를 위한 예외).
        private void GoToCharacterSelect()
        {
            if (string.IsNullOrEmpty(characterSelectScene)) return;
            SceneManager.LoadScene(characterSelectScene);
        }

        // LoginResult → 사용자용 한국어 메시지. Firebase 최신 버전은 계정 유무를 숨기려
        // WrongPassword/UserNotFound 대신 LoginFailed로 뭉뚱그려 주기도 하므로 둘 다 처리한다.
        private static string DescribeResult(LoginResult result)
        {
            switch (result)
            {
                case LoginResult.Success: return "로그인 성공";
                case LoginResult.UserNotFound: return "존재하지 않는 계정입니다.";
                case LoginResult.WrongPassword: return "비밀번호가 올바르지 않습니다.";
                case LoginResult.InvalidEmail: return "이메일 형식이 올바르지 않습니다.";
                case LoginResult.LoginFailed: return "이메일 또는 비밀번호가 올바르지 않습니다.";
                case LoginResult.UserDisabled: return "비활성화된 계정입니다.";
                case LoginResult.NetworkError: return "네트워크 연결을 확인하세요.";
                case LoginResult.TooManyRequests: return "시도가 너무 많습니다. 잠시 후 다시 시도하세요.";
                default: return "알 수 없는 오류가 발생했습니다.";
            }
        }

        private void SetMessage(string message)
        {
            if (messageText != null) messageText.text = message;
        }
    }
}
