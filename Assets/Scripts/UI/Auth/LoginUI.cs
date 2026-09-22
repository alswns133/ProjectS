using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ProjectS.Core;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 이메일/비밀번호 로그인 화면. 입력을 받아 <see cref="FirebaseManager.Login"/>을 호출하고
    /// 결과를 메시지로 표시한다. Firebase 타입은 매니저 뒤에 있고, 여기선 LoginResult 열거형만 다룬다.
    ///
    /// 화면 전환 설계(2026-09-22): 이 씬은 <b>베일(<see cref="EntryVeil"/>)로 덮인 채 시작</b>한다.
    /// 자동 로그인 여부는 Firebase 초기화가 끝나야 알 수 있는데, 그전까지 로그인 폼을 띄워 두면
    /// 자동 로그인일 때 폼이 한 번 번쩍였다가 씬이 넘어갔다. 그래서 순서를 뒤집었다 —
    /// 덮은 채 판정하고 <b>로그인이 필요할 때만</b> 베일을 걷는다. 자동 로그인이면 베일을 걷지 않고
    /// 그대로 캐릭터 선택 씬으로 넘어가, 그쪽 베일이 로스터 로드가 끝날 때까지 이어받는다.
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

        [Header("진입 연출")]
        [Tooltip("판정이 끝나기 전 화면을 덮는 베일. 비우면 예전처럼 폼이 바로 보인다(단독 테스트용).")]
        [SerializeField] private EntryVeil veil;

        [Tooltip("로그인/회원가입 UI 루트(선택). 물리면 판정이 끝날 때까지 꺼 두어 베일 뒤에서도 완전히 잠긴다.")]
        [SerializeField] private GameObject formRoot;

        [Tooltip("Firebase 초기화를 기다릴 최대 시간(초). 넘기면 베일을 걷고 수동 로그인을 받는다.")]
        [SerializeField, Min(0f)] private float authTimeoutSeconds = 8f;

        [Tooltip("에디터에서만 쓰는 대기 시간(초). 에디터는 Firebase 네이티브 초기화가 처음에 훨씬 느려 " +
                 "빌드 기준으로 재면 자동 로그인이 매번 시간 초과로 떨어진다.")]
        [SerializeField, Min(0f)] private float editorAuthTimeoutSeconds = 30f;

        // 에디터와 빌드의 대기 시간을 가른다. 빌드 값을 늘려 에디터를 맞추면, 실제 사용자가 연결이
        // 막혔을 때 그만큼 로고 화면에 잡혀 있게 되므로 값을 하나로 합치지 않는다.
        private float AuthTimeout =>
#if UNITY_EDITOR
            editorAuthTimeoutSeconds;
#else
            authTimeoutSeconds;
#endif

        [Header("베일 문구")]
        [SerializeField] private string connectingMessage = "접속 중...";
        [SerializeField] private string enteringMessage = "캐릭터 정보를 불러오는 중...";
        [SerializeField] private string timeoutMessage = "서버 연결이 지연되고 있어요. 다시 로그인해 주세요.";

        // 자동 로그인 선택을 저장하는 키. 세션은 Firebase가 유지하고, 이 값이 "그 세션을 쓸지"를 가른다.
        private const string AutoLoginKey = "auth.autoLogin";

        // 자동 로그인일 때 Firebase 초기화와 겹쳐서 미리 올려 두는 캐릭터 선택 씬.
        // allowSceneActivation=false로 90%까지만 올려 두었다가, 판정이 끝나면 활성화만 켠다.
        // 대기 시간이 두 겹으로 쌓이지 않게 하는 것이 목적이다(초기화 대기 + 씬 로드 → 둘이 겹침).
        //
        // AsyncOperation은 취소가 불가능하다. 그래서 자동 로그인이 켜져 있을 때만 시작하고,
        // 세션이 만료돼 수동 로그인으로 떨어지는 경우에는 버리지 않고 그때 재사용한다.
        private AsyncOperation preload;

        // 폼은 판정이 끝나기 전에 꺼 둔다. Start에 두면 이미 한 프레임 늦어 그 프레임이 그려지므로
        // 반드시 Awake여야 한다(EntryVeil이 자기 Awake에서 화면을 덮는 것과 같은 이유).
        private void Awake()
        {
            if (formRoot != null) formRoot.SetActive(false);

            // 씬에서 베일이 꺼진 채 저장돼 있어도 여기서 켜며 덮는다(CoverImmediate가 SetActive까지 한다).
            // 폼을 끄는 것과 한 세트라 같은 자리에 둔다 — 덮기 전에 폼이 한 프레임 보이면 안 되기 때문이다.
            if (veil != null)
            {
                veil.CoverImmediate();
                veil.SetMessage(connectingMessage);
            }
        }

        private void OnEnable() => loginButton.onClick.AddListener(OnClickLogin);
        private void OnDisable() => loginButton.onClick.RemoveListener(OnClickLogin);

        // 자동 로그인 스킵: 이미 로그인된 세션이면 로그인 화면을 건너뛰고 캐릭터 선택으로 바로 넘긴다.
        // Firebase가 이전 세션을 유지·복원하므로, 초기화(ReadyTask)를 기다린 뒤 CurrentUid를 확인한다.
        private async void Start()
        {
            // 토글 초기값 복원(기본 false = 해제).
            bool autoLogin = PlayerPrefs.GetInt(AutoLoginKey, 0) == 1;
            if (autoLoginToggle != null) autoLoginToggle.isOn = autoLogin;

            if (FirebaseManager.Instance == null)
            {
                // 로그인 씬만 단독 재생한 경우. 기다릴 대상이 없으니 곧바로 폼을 보여 준다.
                await ShowFormAsync(null);
                return;
            }

            // 대기 시간을 로딩과 겹친다: 여기서 미리 올려 두면 판정이 끝나는 순간 전환이 즉시 끝난다.
            if (autoLogin) BeginPreload();

            bool ready = await AsyncTimeout.Wait(FirebaseManager.Instance.ReadyTask, AuthTimeout);
            if (this == null) return;

            if (!ready)
            {
                Debug.LogWarning($"[LoginUI] Firebase 초기화가 {AuthTimeout}초 안에 끝나지 않았습니다 — 베일을 걷고 수동 로그인으로 전환합니다.");
                await ShowFormAsync(timeoutMessage);
                return;
            }

            // Firebase가 이전 세션을 복원했더라도, 자동 로그인을 껐으면 그 세션을 폐기하고 로그인을 다시 받는다.
            if (FirebaseManager.Instance.IsLoggedIn)
            {
                if (autoLogin)
                {
                    // 베일을 걷지 않고 그대로 넘어간다 — 캐릭터 선택 씬의 베일이 이어받는다.
                    if (veil != null) veil.SetMessage(enteringMessage);
                    GoToCharacterSelect();
                    return;
                }

                FirebaseManager.Instance.Logout();
            }

            await ShowFormAsync(null);
        }

        // 판정 결과가 "로그인이 필요함"일 때만 폼을 켜고 베일을 걷는다. 순서가 중요하다 —
        // 베일을 먼저 걷으면 폼이 아직 꺼진 빈 화면이 잠깐 보인다.
        private async Task ShowFormAsync(string message)
        {
            if (formRoot != null) formRoot.SetActive(true);
            SetMessage(message);

            if (veil != null) await veil.HideAsync();
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
            if (this == null) return;

            loginButton.interactable = true;
            SetMessage(DescribeResult(result));

            if (result == LoginResult.Success)
            {
                // 자동 로그인 선택 저장(다음 실행 때 이 값으로 스킵 여부 결정).
                PlayerPrefs.SetInt(AutoLoginKey, (autoLoginToggle != null && autoLoginToggle.isOn) ? 1 : 0);
                PlayerPrefs.Save();

                passwordField.text = string.Empty;

                // 수동 로그인도 같은 베일로 덮고 넘어간다(폼 → 베일 → 선택 화면이 한 흐름으로 보이게).
                if (veil != null) await veil.CoverAsync(enteringMessage);
                if (this == null) return;

                GoToCharacterSelect();
            }
        }

        // 캐릭터 선택 씬을 미리 올려 둔다(활성화는 보류). 이미 시작했거나 씬 이름이 비면 아무것도 하지 않는다.
        //
        // 활성화를 보류한 로드가 떠 있는 동안에는 다른 씬 로드가 그 뒤에 줄을 선다. 지금은 로그인과
        // 회원가입이 같은 씬의 패널이라 문제가 없지만, 이 씬에서 다른 씬으로 나가는 경로가 생기면
        // 그때는 이 프리로드를 먼저 활성화해 정리해야 한다.
        private void BeginPreload()
        {
            if (preload != null || string.IsNullOrEmpty(characterSelectScene)) return;

            preload = SceneManager.LoadSceneAsync(characterSelectScene);
            if (preload != null) preload.allowSceneActivation = false;
        }

        // 로그인 성공 → 캐릭터 선택 씬으로 이동한다. 선택은 이제 같은 씬의 패널이 아니라 별도 씬이라
        // 씬 전체를 로드한다(로그인 씬은 이때 언로드됨). 씬 이름이 비어 있으면 전환을 생략한다
        // (로그인 씬만 단독으로 테스트할 때를 위한 예외).
        //
        // 미리 올려 둔 로드가 있으면 활성화만 켠다 — 이미 90%까지 올라와 있어 전환이 거의 즉시 끝난다.
        // (allowSceneActivation=false인 동안 progress는 0.9에서 멈춘다. 완료 여부를 봐야 한다면
        //  isDone이 아니라 progress >= 0.9f로 판정해야 한다.)
        private void GoToCharacterSelect()
        {
            if (string.IsNullOrEmpty(characterSelectScene)) return;

            if (preload != null)
            {
                preload.allowSceneActivation = true;
                return;
            }

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
            if (messageText != null && message != null) messageText.text = message;
        }
    }
}
