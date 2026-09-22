using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 이메일/비밀번호 회원가입 화면. 인라인 검증(이메일 형식·비번 길이·비번 확인 일치) 후
    /// <see cref="FirebaseManager.Register"/>를 호출하고, 가입 성공 시 곧바로 자동 로그인해
    /// 캐릭터 선택 화면으로 넘긴다(다시 로그인할 필요 없음).
    /// </summary>
    public class SignupUI : MonoBehaviour
    {
        [SerializeField] private TMP_InputField emailField;
        [SerializeField] private TMP_InputField passwordField;
        [SerializeField] private TMP_InputField confirmField;   // 비밀번호 확인(오타 방지)
        [SerializeField] private Button signupButton;
        [SerializeField] private TMP_Text messageText;

        [Header("가입 성공 후 전환 (비우면 전환 생략)")]
        [Tooltip("가입→자동 로그인 성공 시 이동할 캐릭터 선택 씬 이름. Build Settings에 등록돼 있어야 한다.")]
        [SerializeField] private string characterSelectScene = "CharacterSelect";

        [Header("진입 연출")]
        [Tooltip("전환을 덮는 베일. 로그인 화면과 같은 씬에 있으므로 LoginUI와 같은 것을 물린다(비워도 동작).")]
        [SerializeField] private EntryVeil veil;

        [SerializeField] private string enteringMessage = "캐릭터 정보를 불러오는 중...";

        private void OnEnable() => signupButton.onClick.AddListener(OnClickSignup);
        private void OnDisable() => signupButton.onClick.RemoveListener(OnClickSignup);

        private async void OnClickSignup()
        {
            string email = emailField.text.Trim();
            string password = passwordField.text;
            string confirm = confirmField != null ? confirmField.text : password;

            // 인라인 검증: Firebase 왕복 전에 즉시 피드백.
            if (string.IsNullOrEmpty(email)) { SetMessage("이메일을 입력하세요."); return; }
            if (!AuthValidation.IsValidEmail(email)) { SetMessage("이메일 형식이 올바르지 않습니다."); return; }
            if (!AuthValidation.IsValidPassword(password)) { SetMessage("비밀번호는 6자 이상이어야 합니다."); return; }
            if (password != confirm) { SetMessage("비밀번호가 일치하지 않습니다."); return; }

            signupButton.interactable = false;
            SetMessage("가입 중...");

            RegisterResult result = await FirebaseManager.Instance.Register(email, password);
            if (this == null) return;

            if (result != RegisterResult.Success)
            {
                signupButton.interactable = true;
                SetMessage(DescribeResult(result));
                return;
            }

            // 가입 성공 → 곧바로 자동 로그인 → 캐릭터 선택으로.
            SetMessage("로그인 중...");
            LoginResult login = await FirebaseManager.Instance.Login(email, password);
            if (this == null) return;

            signupButton.interactable = true;

            if (login == LoginResult.Success)
            {
                passwordField.text = string.Empty;
                if (confirmField != null) confirmField.text = string.Empty;

                // 로그인 경로와 같은 베일로 덮고 넘어간다 — 캐릭터 선택 씬의 베일이 그대로 이어받아
                // 로스터 로드가 끝날 때까지 덮고 있으므로, 어느 쪽으로 들어와도 화면이 같게 흐른다.
                if (veil != null) await veil.CoverAsync(enteringMessage);
                if (this == null) return;

                GoToCharacterSelect();
            }
            else
            {
                // 가입은 됐으나 자동 로그인 실패(드묾) → 로그인 화면에서 직접 로그인하도록 안내.
                SetMessage("가입은 완료됐어요. 로그인해 주세요.");
            }
        }

        // 가입→자동 로그인 성공 → 캐릭터 선택 씬으로 이동(별도 씬 로드, 로그인 씬은 언로드).
        private void GoToCharacterSelect()
        {
            if (string.IsNullOrEmpty(characterSelectScene)) return;
            SceneManager.LoadScene(characterSelectScene);
        }

        private static string DescribeResult(RegisterResult result)
        {
            switch (result)
            {
                case RegisterResult.Success: return "회원가입 성공";
                case RegisterResult.EmailAlreadyInUse: return "이미 사용 중인 이메일입니다.";
                case RegisterResult.InvalidEmail: return "이메일 형식이 올바르지 않습니다.";
                case RegisterResult.WeakPassword: return "비밀번호가 너무 약합니다(6자 이상).";
                default: return "알 수 없는 오류가 발생했습니다.";
            }
        }

        private void SetMessage(string message)
        {
            if (messageText != null) messageText.text = message;
        }
    }
}
