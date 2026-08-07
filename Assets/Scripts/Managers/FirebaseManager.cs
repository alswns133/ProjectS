using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectS.Data;

namespace ProjectS.Managers
{
    /// <summary>회원가입 결과. Firebase 오류를 UI가 이해하는 값으로 좁혀 전달한다.</summary>
    public enum RegisterResult
    {
        Success,
        EmailAlreadyInUse,
        InvalidEmail,
        WeakPassword,
        UnknownError,
    }

    /// <summary>
    /// 로그인 결과. Firebase 열거형을 그대로 노출하지 않는 이유:
    /// (1) UI·게임로직이 Firebase를 몰라도 되게(계층 분리: UI → Manager → Firebase),
    /// (2) Firebase 오류 종류가 너무 많아 우리가 쓸 것만 추리기 위함.
    /// 나중에 백엔드를 Steam/AWS로 바꿔도 이 열거형과 반환 계약은 유지된다.
    /// </summary>
    public enum LoginResult
    {
        Success,
        UserNotFound,
        WrongPassword,
        InvalidEmail,
        UserDisabled,
        NetworkError,
        TooManyRequests,
        LoginFailed,
        UnknownError,
    }

    /// <summary>캐릭터 생성 결과. 이름 유니크·형식 검증 실패를 UI가 구분해 표시하도록 좁혀 전달한다.</summary>
    public enum CreateCharacterResult
    {
        Success,
        NameTaken,
        InvalidName,
        Failed,
    }

    /// <summary>
    /// Firebase 인증(Auth)과 캐릭터 세이브(Realtime Database)를 담당하는 매니저.
    /// 캐릭터 슬롯 모델(생성 → 목록 선택 → 접속)이라 캐릭터마다 Users/{uid}/Characters/{uniqueId}에
    /// <see cref="CharacterSaveData"/>를 하나씩 저장한다(재화·인벤토리도 캐릭터별로 분리).
    /// 인증·저장의 구현을 이 클래스 뒤에 숨겨, UI와 게임로직은 결과 열거형/세이브 데이터만 안다
    /// (백엔드를 Steam·AWS로 교체해도 이 클래스만 바꾸면 되도록).
    /// </summary>
    public class FirebaseManager : MonoBehaviour
    {
        /// <summary>매니저 싱글톤. 씬을 넘어 유지된다.</summary>
        public static FirebaseManager Instance { get; private set; }

        private FirebaseAuth auth;
        private FirebaseUser currentUser;
        private DatabaseReference databaseReference;

        // 비워 두면 google-services.json의 기본 DB(DefaultInstance)를 쓴다.
        // 다른 리전/여러 DB를 명시적으로 붙일 때만 URL을 입력한다.
        [SerializeField] private string databaseUrl = "";

        /// <summary>Firebase 의존성 확인·연결이 끝나 사용 가능한 상태인지.</summary>
        public bool IsInitialized { get; private set; }

        /// <summary>현재 로그인된 유저의 UID. 로그인 상태가 아니면 null.</summary>
        public string CurrentUid => auth != null && auth.CurrentUser != null ? auth.CurrentUser.UserId : null;

        /// <summary>이미 로그인된 세션이 있는지(자동 로그인 스킵 판정용). Firebase가 이전 세션을 유지·복원한다.</summary>
        public bool IsLoggedIn => CurrentUid != null;

        // 초기화(성공/실패) 완료 신호. 자동 로그인 스킵이 세션 확인 전에 이걸 기다린다.
        private readonly TaskCompletionSource<bool> readyTcs = new();

        /// <summary>Firebase 초기화가 끝나면(성공/실패 무관) 완료되는 Task. await 후 <see cref="IsLoggedIn"/>으로 세션을 확인한다.</summary>
        public Task ReadyTask => readyTcs.Task;

        private void Awake()
        {
            // 중복 인스턴스 제거 후 씬 넘김 유지(매니저 규칙).
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // async void는 Unity 진입점(Start)에서만 예외적으로 허용한다.
        private async void Start()
        {
            await InitializeFirebaseAsync();
        }

        /// <summary>
        /// Firebase SDK 의존성을 점검·복구하고 Auth/Database 참조를 연결한다.
        /// 실패하면 IsInitialized가 false로 남아 모든 공개 메서드가 방어적으로 빠져나간다.
        /// </summary>
        private async Task InitializeFirebaseAsync()
        {
            DependencyStatus dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (dependencyStatus != DependencyStatus.Available)
            {
                Debug.LogError($"[Firebase] 초기화 실패: {dependencyStatus}");
                readyTcs.TrySetResult(false);
                return;
            }

            auth = FirebaseAuth.DefaultInstance;

            FirebaseDatabase database = string.IsNullOrWhiteSpace(databaseUrl)
                ? FirebaseDatabase.DefaultInstance          // google-services.json의 기본 DB URL 사용
                : FirebaseDatabase.GetInstance(databaseUrl);
            databaseReference = database.RootReference;

            auth.StateChanged += OnAuthStateChanged;

            // 학습본은 여기서 SignOut으로 자동 로그인을 껐다. ProjectS는 세션 유지가 정상이라 끄지 않는다.
            // 부트스트랩에서 CurrentUid가 살아 있으면 캐릭터 선택창으로 바로 보내는 라이프사이클과 맞춘다.

            IsInitialized = true;
            readyTcs.TrySetResult(true);
        }

        private void OnDestroy()
        {
            if (auth != null)
                auth.StateChanged -= OnAuthStateChanged;
        }

        private void OnAuthStateChanged(object sender, EventArgs e)
        {
            currentUser = auth.CurrentUser;
            Debug.Log(currentUser != null ? $"[Firebase] 로그인 상태: {currentUser.Email}" : "[Firebase] 로그아웃 상태");
        }

        #region Auth

        /// <summary>이메일/비밀번호로 새 계정을 만든다.</summary>
        public async Task<RegisterResult> Register(string email, string password)
        {
            if (!IsInitialized) return RegisterResult.UnknownError;

            try
            {
                await auth.CreateUserWithEmailAndPasswordAsync(email, password);
                return RegisterResult.Success;
            }
            catch (FirebaseException ex)
            {
                switch ((AuthError)ex.ErrorCode)
                {
                    case AuthError.EmailAlreadyInUse: return RegisterResult.EmailAlreadyInUse;
                    case AuthError.InvalidEmail: return RegisterResult.InvalidEmail;
                    case AuthError.WeakPassword: return RegisterResult.WeakPassword;
                    default: return RegisterResult.UnknownError;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 회원가입 예외: {ex}");
                return RegisterResult.UnknownError;
            }
        }

        /// <summary>이메일/비밀번호로 로그인한다.</summary>
        public async Task<LoginResult> Login(string email, string password)
        {
            if (!IsInitialized) return LoginResult.UnknownError;

            try
            {
                AuthResult result = await auth.SignInWithEmailAndPasswordAsync(email, password);
                Debug.Log($"[Firebase] 로그인 성공 UID={result.User.UserId}");
                return LoginResult.Success;
            }
            catch (FirebaseException ex)
            {
                return ConvertLoginError(ex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 로그인 예외: {ex}");
                return LoginResult.UnknownError;
            }
        }

        /// <summary>로그아웃한다.</summary>
        public void Logout()
        {
            if (!IsInitialized) return;
            auth.SignOut();
        }

        // Firebase 오류를 우리 열거형으로 좁혀 번역한다.
        private LoginResult ConvertLoginError(FirebaseException ex)
        {
            switch ((AuthError)ex.ErrorCode)
            {
                case AuthError.InvalidEmail: return LoginResult.InvalidEmail;
                case AuthError.WrongPassword: return LoginResult.WrongPassword;
                case AuthError.UserNotFound: return LoginResult.UserNotFound;
                case AuthError.UserDisabled: return LoginResult.UserDisabled;
                case AuthError.NetworkRequestFailed: return LoginResult.NetworkError;
                case AuthError.TooManyRequests: return LoginResult.TooManyRequests;
                case AuthError.Failure: return LoginResult.LoginFailed;
                default: return LoginResult.UnknownError;
            }
        }

        #endregion

        #region Character Save (슬롯 모델: 생성/선택/접속)

        /// <summary>
        /// 캐릭터를 새로 만든다. 이름 예약(CharacterNames)과 캐릭터 노드를 '한 번에'(멀티패스) 써서,
        /// 보안 규칙의 이름 유니크(!data.exists())가 통째로 걸리면 아무것도 안 써지도록 원자성을 보장한다.
        /// </summary>
        /// <param name="characterType">캐릭터 타입(1=검사, 2=거너). PlayerStatTable.CharacterId.</param>
        /// <param name="name">캐릭터 이름(2~12자, 전 서버 유니크).</param>
        public async Task<CreateCharacterResult> CreateCharacter(int characterType, string name)
        {
            if (!IsInitialized || CurrentUid == null) return CreateCharacterResult.Failed;

            name = name != null ? name.Trim() : string.Empty;
            if (!IsValidName(name)) return CreateCharacterResult.InvalidName;

            // 대소문자 무시 유니크를 위해 소문자 키로 예약한다(표시 이름은 원문 그대로 저장).
            string nameKey = name.ToLowerInvariant();

            try
            {
                // 사전 체크(즉시 피드백용). 경합 시 최종 보장은 아래 멀티패스 쓰기 + 규칙이 한다.
                // 전역 미러 CharacterNames로 '타 계정 포함' 중복을 본다.
                if (await IsCharacterNameTaken(nameKey)) return CreateCharacterResult.NameTaken;

                // 전역 미러는 과거 데이터와 어긋날 수 있다(예약 누락). 그래서 내 계정의 '실제' 캐릭터로도
                // 같은 이름을 막는다 — 미러가 낡아 사전 체크를 통과해도, 같은 계정에 같은 이름을 두 번
                // 만드는 사고(실제 발생: 목록이 안 떠 재생성 → 중복)를 미러 상태와 무관하게 차단한다.
                if (await IsNameUsedByOwnCharacter(nameKey)) return CreateCharacterResult.NameTaken;

                long id = DateTime.UtcNow.Ticks;
                CharacterSaveData data = new CharacterSaveData(id, characterType, name);

                // 멀티패스 쓰기는 raw JSON을 못 섞으므로 CharacterSaveData를 중첩 Dictionary로 변환한다.
                // DeserializeObject<Dictionary<string,object>>는 최상위 한 겹만 풀고 중첩 컬렉션
                // (퀘스트·인벤토리 리스트, potionQuickSlots 등)을 JArray/JObject(잎=JValue)로 남기는데,
                // Firebase Variant는 JValue를 변환 못 해 "Invalid type ... JValue" 예외로 터진다.
                // → JToken 트리를 순수 CLR(Dictionary/List/스칼라)로 재귀 변환해 JValue를 제거한다.
                var charDict = (Dictionary<string, object>)ToFirebaseValue(JObject.FromObject(data));

                var updates = new Dictionary<string, object>
                {
                    [$"Users/{CurrentUid}/Characters/{id}"] = charDict,
                    [$"CharacterNames/{nameKey}"] = CurrentUid,
                };

                await databaseReference.UpdateChildrenAsync(updates);
                return CreateCharacterResult.Success;
            }
            catch (Exception ex)
            {
                // 권한 거부(경합으로 이름이 방금 선점됨 등). 사전 체크를 통과했어도 규칙이 최종 방어한다.
                Debug.LogError($"[Firebase] 캐릭터 생성 예외: {ex}");
                return CreateCharacterResult.Failed;
            }
        }

        /// <summary>
        /// 내 계정의 실제 캐릭터 중 같은 이름(대소문자 무시)이 있는지. 전역 미러(CharacterNames)가
        /// 과거 데이터와 어긋나도 '같은 계정 · 같은 이름' 중복을 막는 최종 방어선. 확인 불가(로드 실패)면
        /// 안전하게 true(중복 생성 허용보다 오탐이 낫다).
        /// </summary>
        /// <param name="nameKey">소문자로 정규화된 이름 키.</param>
        private async Task<bool> IsNameUsedByOwnCharacter(string nameKey)
        {
            List<CharacterSaveData> mine = await LoadAllCharacters();
            if (mine == null) return true;   // 로드 실패 → 확인 불가. 막는 쪽으로.

            foreach (CharacterSaveData c in mine)
                if (c != null && !string.IsNullOrEmpty(c.name) && c.name.ToLowerInvariant() == nameKey)
                    return true;

            return false;
        }

        /// <summary>캐릭터 이름이 이미 사용 중인지. nameKey는 소문자 정규화된 키. 오류 시 안전하게 true.</summary>
        public async Task<bool> IsCharacterNameTaken(string nameKey)
        {
            if (!IsInitialized || string.IsNullOrEmpty(nameKey)) return true;

            try
            {
                DataSnapshot snapshot = await databaseReference.Child("CharacterNames").Child(nameKey).GetValueAsync();
                return snapshot.Exists;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 이름 중복검사 예외: {ex}");
                return true;
            }
        }

        // Firebase 키로 쓸 수 없는 문자와 길이 제한(보안 규칙과 동일: 2~12자)을 검사한다.
        private static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 12) return false;

            foreach (char c in name)
                if (c == '.' || c == '#' || c == '$' || c == '[' || c == ']' || c == '/') return false;

            return true;
        }

        /// <summary>
        /// JToken 트리를 Firebase Variant가 받는 순수 CLR 값으로 재귀 변환한다.
        /// 멀티패스(UpdateChildrenAsync) 쓰기에 넣을 Dictionary가 JObject/JArray/JValue를 품으면
        /// Firebase가 "Invalid type ... JValue"로 터지므로, Object→Dictionary·Array→List·잎→스칼라로 내린다.
        /// </summary>
        private static object ToFirebaseValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in (JObject)token)
                        dict[prop.Key] = ToFirebaseValue(prop.Value);
                    return dict;
                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                        list.Add(ToFirebaseValue(item));
                    return list;
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                    return null;
                default:
                    return token.Value<string>();
            }
        }

        /// <summary>
        /// 캐릭터 하나를 저장한다(생성 직후, 그리고 진행 중 세이브 시점마다). uniqueId가 노드 키가 되므로
        /// 같은 uniqueId로 다시 부르면 덮어쓴다. 신규 생성 시 uniqueId는 호출측이 발급한다(예: DateTime.UtcNow.Ticks).
        /// </summary>
        public async Task<bool> SaveCharacter(CharacterSaveData data)
        {
            if (!IsInitialized || CurrentUid == null || data == null) return false;

            try
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                await databaseReference.Child("Users").Child(CurrentUid)
                    .Child("Characters").Child(data.uniqueId.ToString())
                    .SetRawJsonValueAsync(json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 캐릭터 저장 예외: {ex}");
                return false;
            }
        }

        /// <summary>캐릭터 하나를 로드한다(선택창에서 고른 캐릭터로 접속할 때). 없으면 null.</summary>
        public async Task<CharacterSaveData> LoadCharacter(long uniqueId)
        {
            if (!IsInitialized || CurrentUid == null) return null;

            try
            {
                DataSnapshot snapshot = await databaseReference.Child("Users").Child(CurrentUid)
                    .Child("Characters").Child(uniqueId.ToString()).GetValueAsync();

                if (!snapshot.Exists) return null;

                CharacterSaveData data = JsonConvert.DeserializeObject<CharacterSaveData>(snapshot.GetRawJsonValue());
                // 요청한 uniqueId(=노드 키)를 정체성의 진실로 고정한다(저장 필드가 정밀도로 깎였어도 안전).
                if (data != null) data.uniqueId = uniqueId;
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 캐릭터 로드 예외: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 이 계정의 모든 캐릭터를 로드한다. 캐릭터 선택창이 목록을 나열할 때 쓴다.
        /// 반환 규약: 성공 시 목록(진짜 0개면 빈 리스트), <b>실패(미초기화·미로그인·예외)면 null</b>.
        /// "로드 실패"를 "캐릭터 0개"와 반드시 구분해야, 선택창이 실패를 '없음'으로 오해해
        /// 중복 캐릭터 생성을 유도하는 사고를 막는다(빈 리스트로 뭉뚱그리면 그 사고가 난다).
        /// </summary>
        public async Task<List<CharacterSaveData>> LoadAllCharacters()
        {
            if (!IsInitialized || CurrentUid == null) return null;   // 준비 안 됨 → 실패로 신호(빈 목록 아님)

            try
            {
                DataSnapshot snapshot = await databaseReference.Child("Users").Child(CurrentUid)
                    .Child("Characters").GetValueAsync();

                List<CharacterSaveData> list = new();
                if (!snapshot.Exists) return list;   // 조회는 됐고 자식이 없다 → 진짜 0개

                foreach (DataSnapshot child in snapshot.Children)
                {
                    CharacterSaveData data = JsonConvert.DeserializeObject<CharacterSaveData>(child.GetRawJsonValue());
                    if (data == null) continue;

                    // 노드 키를 정체성의 진실로 삼는다. uniqueId(=Ticks)는 2^53을 넘어 JS 계층(콘솔·함수)에서
                    // 정밀도가 깎일 수 있어, 저장/로드/삭제가 uniqueId.ToString()으로 엉뚱한 노드를 치는 사고를 막는다.
                    if (long.TryParse(child.Key, out long key)) data.uniqueId = key;

                    list.Add(data);
                }

                return list;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 캐릭터 목록 로드 예외: {ex}");
                return null;   // 예외 → 실패로 신호. 호출측이 '0개'로 오해하지 않게 한다.
            }
        }

        /// <summary>캐릭터 하나를 삭제한다(캐릭터 삭제 기능). 성공 시 true.</summary>
        public async Task<bool> DeleteCharacter(long uniqueId)
        {
            if (!IsInitialized || CurrentUid == null) return false;

            try
            {
                await databaseReference.Child("Users").Child(CurrentUid)
                    .Child("Characters").Child(uniqueId.ToString()).RemoveValueAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Firebase] 캐릭터 삭제 예외: {ex}");
                return false;
            }
        }

        #endregion
    }
}
