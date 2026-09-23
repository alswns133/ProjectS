using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectS.Cameras;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.UI
{
    /// <summary>
    /// 캐릭터 선택 씬의 컨트롤러. 뷰(<see cref="CharacterSelectPageView"/>)의 이벤트를 받아
    /// Firebase 로스터 로드·<see cref="GameSession"/>·씬 전환으로 잇는 중재자다.
    /// 뷰는 표시만 담당하고, Firebase 접근은 이 한곳에서만 한다(게임플레이 코드가 백엔드를 모르게).
    ///
    /// 현재 범위: 로스터 로드 → 6칸 채우기 → 선택(+프리뷰) → 시작(게임씬) → 종료/버전,
    /// 신규 생성(빈 카드 → 클래스 선택 → 이름 → CreateCharacter → 갱신),
    /// 삭제(× → 확인 팝업 → DeleteCharacter → 갱신).
    /// 환경설정 버튼·로드 실패 재시도 팝업은 다음 단계에서 확장한다.
    /// </summary>
    public class EntryFlowController : MonoBehaviour
    {
        [Header("페이지")]
        [SerializeField] private CharacterSelectPageView selectPage;
        [SerializeField] private ClassSelectPageView classSelectPage;      // 신규 생성 1단계
        [SerializeField] private CharacterCreatePageView createPage;       // 신규 생성 2단계(이름)

        [Header("팝업")]
        [SerializeField] private PopupLayerView popupLayer;                // 확인/알림 + 딤 + 서버 왕복 입력차단

        [Header("클래스 소개 문구")]
        [SerializeField, TextArea] private string warriorIntro = "검사 · 근접 3단 콤보";
        [SerializeField, TextArea] private string gunnerIntro = "거너 · 원거리 사격";

        [Header("클래스 소개 영상 (VideoClip 어드레서블 주소)")]
        [Tooltip("미등록이거나 로드에 실패하면 소개 패널에 대체 이미지가 뜬다.")]
        [SerializeField] private string warriorVideoAddress = "ClassVideo_1";
        [SerializeField] private string gunnerVideoAddress = "ClassVideo_2";

        [Header("접속할 게임 씬 (Build Settings 등록 필요)")]
        [SerializeField] private string gameSceneName = "Bootstrap";

        [Header("로그아웃")]
        [Tooltip("로그아웃 버튼으로 돌아갈 로그인 씬(Esc 단축키는 실수 방지로 제거됨).")]
        [SerializeField] private string loginSceneName = "Login";

        [Header("프리뷰 모델 (클래스별, 씬에 배치된 GameObject)")]
        [Tooltip("슬롯 선택 시 해당 characterType 모델만 켜고 나머지는 끈다. 리그(카메라→RT)는 씬에 이미 있다.")]
        [SerializeField] private ClassModel[] classModels;

        [Header("캐릭터 아트")]
        [Tooltip("슬롯 초상화를 꺼내 쓰는 캐릭터 로스터(Assets/CharacterRoster). 비면 초상화 칸이 빈 채로 뜬다.")]
        [SerializeField] private CharacterRoster characterRoster;

        [Header("시작 연출 (선택)")]
        [Tooltip("캐릭터 시작 시 로딩 전에 재생할 연출(카메라 회전 + 문 열림). 비우면 바로 로딩한다.")]
        [SerializeField] private CharacterStartTransition startTransition;

        [Header("진입 연출")]
        [Tooltip("Firebase 준비 + 로스터 로드가 끝날 때까지 화면을 덮는 베일. 로그인 씬의 베일과 모양을 맞춘다.")]
        [SerializeField] private EntryVeil veil;

        [Tooltip("Firebase 초기화를 기다릴 최대 시간(초). 넘겨도 계속 진행하고, 실패는 재시도 팝업으로 잡는다.")]
        [SerializeField, Min(0f)] private float firebaseTimeoutSeconds = 8f;

        [Tooltip("에디터에서만 쓰는 대기 시간(초). 에디터는 Firebase 네이티브 초기화가 처음에 훨씬 느리다.")]
        [SerializeField, Min(0f)] private float editorFirebaseTimeoutSeconds = 30f;

        // 에디터와 빌드의 대기 시간을 가른다(LoginUI와 같은 이유 — 빌드 값을 에디터에 맞춰 늘리면
        // 실제 사용자가 연결이 막혔을 때 그만큼 로고 화면에 잡혀 있게 된다).
        private float FirebaseTimeout =>
#if UNITY_EDITOR
            editorFirebaseTimeoutSeconds;
#else
            firebaseTimeoutSeconds;
#endif

        [Header("베일 문구")]
        [SerializeField] private string connectingMessage = "접속 중...";
        [SerializeField] private string loadingRosterMessage = "캐릭터 정보를 불러오는 중...";
        [SerializeField] private string returningToLoginMessage = "로그인 화면으로 돌아갑니다...";

        // characterType(검사=1/거너=2 …)과 씬에 놓인 프리뷰 모델을 짝짓는다. 클래스가 늘면 항목만 추가.
        [System.Serializable]
        private struct ClassModel
        {
            public int characterType;
            public GameObject model;
        }

        // 슬롯 인덱스 → 캐릭터 세이브. 로스터를 로드한 순서대로 앞칸부터 채우므로 슬롯 i = 로스터 i.
        // 시작/삭제에서 "어느 캐릭터인지"를 이 목록으로 되찾는다(뷰는 세이브 인스턴스를 들지 않으므로).
        private readonly List<CharacterSaveData> roster = new();

        // 생성 흐름 중 고른 클래스의 characterType(검사=1/거너=2). 0 = 아직 안 고름.
        // 클래스 선택 페이지와 이름 입력 페이지에 걸쳐 유지돼야 해서 필드로 둔다.
        private int pendingClassType;

        // 직전 Refresh가 로스터를 못 읽었는지. "진짜 0개"와 "읽기 실패"는 화면상 똑같이 빈칸이라
        // 구분이 필요하다(실패를 0개로 오해하면 중복 생성·오삭제로 이어진다).
        private bool rosterLoadFailed;

        // 프리뷰 모델은 실플레이 프리팹(Haru/Erwin)을 그대로 배치한 것이라 입력·이동·커서 잠금·시점 조작
        // 스크립트가 전부 붙어 있다. 그대로 두면 모델이 켜지는 순간 WASD로 걸어다니고, Player.Start()가
        // 커서를 잠가(Alt 토글로만 풀림) UI 클릭이 막힌다. 보여주기만 하면 되므로 조작 계열만 끈다.
        // Awake에서 끄는 이유: 모든 Awake가 끝난 뒤 Start가 도므로, 여기서 끄면 Player.Start(커서 잠금)가
        // 아예 실행되지 않는다. Animator·외형(천/헤어 등) 컴포넌트는 건드리지 않는다.
        private void Awake()
        {
            // 로스터가 다 찰 때까지 덮는다. 씬에서 베일이 꺼진 채 저장돼 있어도 CoverImmediate가 켜 주므로
            // "켜 두는 것을 기억해야 하는" 배선이 되지 않는다. Start(비동기)가 아니라 Awake여야
            // 첫 프레임에 빈 슬롯이 비치지 않는다.
            if (veil != null) veil.CoverImmediate();

            if (classModels == null) return;

            foreach (ClassModel entry in classModels)
            {
                if (entry.model == null) continue;

                // 여기서 걸러 내지 않으면 DisableGameplay가 프리팹 에셋 자체를 수정해 디스크에 저장된다.
                // 실제로 Haru·Erwin의 Player·InputHandler·Movement·Combat이 꺼진 채 커밋될 뻔했다(2026-09-22).
                if (!IsSceneModel(entry.model))
                {
                    WarnNotSceneModel(entry.model);
                    continue;
                }

                DisableGameplay(entry.model);
            }
        }

        // 프리뷰 모델은 반드시 "씬에 배치된" 오브젝트여야 한다. 프리팹 에셋(프로젝트 창의 원본)을 물리면
        // DisableGameplay와 ShowModel이 씬이 아니라 에셋을 고치므로, 화면엔 아무 변화가 없으면서
        // 실제 게임플레이 캐릭터만 조용히 망가진다. scene.IsValid()가 그 둘을 가르는 유일한 기준이다.
        //
        // 이 사고는 EntryFlowController가 씬에서 CharacterSelectUI 프리팹 안으로 옮겨질 때 났다.
        // 프리팹 에셋은 씬 오브젝트를 참조할 수 없어 Unity가 참조를 비웠고, 그 빈칸이 이름이 같아 보이는
        // 원본 프리팹으로 채워졌다. 같은 이관을 또 하면 똑같이 재발하므로 런타임에 잡는다.
        private static bool IsSceneModel(GameObject model) => model != null && model.scene.IsValid();

        private void WarnNotSceneModel(GameObject model)
        {
            Debug.LogError(
                $"[EntryFlowController] classModels에 프리팹 에셋 '{model.name}'이 물려 있다. " +
                "씬에 배치된 프리뷰 모델을 물려라 — 이대로 두면 프리뷰가 뜨지 않고 에셋이 수정되어 저장된다.",
                this);
        }

        private static void DisableGameplay(GameObject model)
        {
            DisableAll<Player>(model);
            DisableAll<PlayerInputHandler>(model);
            DisableAll<PlayerMovement>(model);
            DisableAll<PlayerCombat>(model);
            DisableAll<CameraRig>(model);
            DisableAll<CameraPivotController>(model);
        }

        private static void DisableAll<T>(GameObject root) where T : Behaviour
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
                component.enabled = false;
        }

        private void OnEnable()
        {
            selectPage.QuitButton.onClick.AddListener(HandleQuit);
            if (selectPage.LogoutButton != null) selectPage.LogoutButton.onClick.AddListener(HandleLogoutRequested);

            foreach (CharacterSlotView slot in selectPage.Slots)
            {
                slot.OnSelected += HandleSelected;
                slot.OnStartRequested += HandleStart;
                slot.OnCreateRequested += HandleCreateRequested;
                slot.OnDeleteRequested += HandleDeleteRequested;
            }

            if (classSelectPage != null)
            {
                classSelectPage.OnClassClicked += HandleClassClicked;
                classSelectPage.SelectButton.onClick.AddListener(HandleClassConfirmed);
                classSelectPage.PrevButton.onClick.AddListener(GoToSelect);
            }

            if (createPage != null)
            {
                createPage.OnCreateRequested += HandleCreateConfirmed;
                createPage.PrevButton.onClick.AddListener(GoToClassSelect);
            }
        }

        private void OnDisable()
        {
            selectPage.QuitButton.onClick.RemoveListener(HandleQuit);
            if (selectPage.LogoutButton != null) selectPage.LogoutButton.onClick.RemoveListener(HandleLogoutRequested);

            foreach (CharacterSlotView slot in selectPage.Slots)
            {
                slot.OnSelected -= HandleSelected;
                slot.OnStartRequested -= HandleStart;
                slot.OnCreateRequested -= HandleCreateRequested;
                slot.OnDeleteRequested -= HandleDeleteRequested;
            }

            if (classSelectPage != null)
            {
                classSelectPage.OnClassClicked -= HandleClassClicked;
                classSelectPage.SelectButton.onClick.RemoveListener(HandleClassConfirmed);
                classSelectPage.PrevButton.onClick.RemoveListener(GoToSelect);
            }

            if (createPage != null)
            {
                createPage.OnCreateRequested -= HandleCreateConfirmed;
                createPage.PrevButton.onClick.RemoveListener(GoToClassSelect);
            }
        }

        // 진입 순서: (덮인 채로) Firebase 준비 → 로스터 로드 → 슬롯 채우기 → 그제야 베일을 걷는다.
        //
        // 베일을 걷는 기준을 "씬이 로드됐을 때"가 아니라 "데이터가 다 찼을 때"로 둔 덕분에,
        // 이 씬은 어디서 들어오든 똑같이 동작한다 —
        //   · 로그인 씬 경유: ReadyTask가 이미 끝나 있어 로스터만 기다린다(거의 즉시).
        //   · 인게임 복귀(SessionReboot): 이 씬의 FirebaseManager가 새로 초기화되므로 더 기다렸다 걷힌다.
        // 진입 경로를 구분하는 분기가 필요 없다는 뜻이다.
        private async void Start()
        {
            GoToSelect();   // 진입 기본 페이지 = 선택 화면(다른 페이지는 꺼둔다)
            selectPage.SetVersion($"v{Application.version}");

            // Firebase가 로그인 세션·초기화를 끝낼 때까지 기다린 뒤 로스터를 읽는다.
            // 매니저가 없으면(로그인 없이 이 씬만 단독 테스트) 빈 슬롯으로 둔다.
            if (FirebaseManager.Instance != null)
            {
                SetVeilMessage(connectingMessage);

                // 타임아웃을 넘겨도 중단하지 않는다 — 어차피 Refresh가 실패로 떨어지고,
                // 그쪽 재시도 팝업이 사용자에게 선택지를 준다(베일에 갇히는 것만 막으면 된다).
                bool ready = await AsyncTimeout.Wait(FirebaseManager.Instance.ReadyTask, FirebaseTimeout);
                if (this == null) return;

                if (!ready)
                    Debug.LogWarning($"[EntryFlowController] Firebase 초기화가 {FirebaseTimeout}초 안에 끝나지 않았습니다 — 로스터 로드를 그대로 시도합니다.");
            }

            SetVeilMessage(loadingRosterMessage);

            await Refresh();
            if (this == null) return;

            // ★ 성공이든 실패든 반드시 걷는다. 실패 경로에서 빠뜨리면 사용자가 로고 화면에 영영 갇힌다.
            await RevealAsync();
            if (this == null) return;

            if (rosterLoadFailed) ShowRosterRetry();
        }

        // 베일을 걷는다(이미 걷혀 있으면 아무 일도 하지 않는다). 팝업은 반드시 이 뒤에 띄운다 —
        // 베일 밑에서 열린 팝업은 화면에 보이지 않아 "버튼이 안 먹는다"로만 보인다.
        private async Task RevealAsync()
        {
            if (veil == null) return;

            await veil.HideAsync();
        }

        private void SetVeilMessage(string message)
        {
            if (veil != null) veil.SetMessage(message);
        }

        // 로스터를 못 읽었을 때의 선택지. 빈 슬롯을 그대로 두면 "캐릭터가 0개"로 착각해
        // 새로 만들거나(중복 생성) 지울 위험이 있어, 반드시 실패였음을 알리고 재시도를 권한다.
        private void ShowRosterRetry()
        {
            if (popupLayer == null)
            {
                Debug.LogWarning("[EntryFlowController] popupLayer 미배선 — 로스터 로드 실패를 알릴 방법이 없습니다.");
                return;
            }

            popupLayer.ShowConfirm(
                "캐릭터 정보를 불러오지 못했어요.",
                "네트워크 상태를 확인한 뒤 다시 시도해 주세요.",
                "다시 시도", "로그인 화면",
                confirmed: RetryRefresh,
                cancelled: LogoutToLogin);
        }

        private async void RetryRefresh()
        {
            if (veil != null) await veil.CoverAsync(loadingRosterMessage);
            if (this == null) return;

            await Refresh();
            if (this == null) return;

            await RevealAsync();
            if (this == null) return;

            if (rosterLoadFailed) ShowRosterRetry();
        }

        // 로스터를 다시 읽어 6칸을 채운다(진입 시·생성/삭제 후 재호출 예정).
        private async Task Refresh()
        {
            rosterLoadFailed = false;
            roster.Clear();
            selectPage.ClearSelection();
            HideAllModels();   // 갱신 직후엔 선택이 없으니 이전 모델이 남지 않게 전부 끈다

            List<CharacterSaveData> characters = null;
            if (FirebaseManager.Instance != null)
                characters = await FirebaseManager.Instance.LoadAllCharacters();
            if (this == null) return;

            // null = 로딩 실패(권한 전파 지연·네트워크). 빈 슬롯(진짜 0개)과 혼동하면 중복 생성·오삭제
            // 위험이 있어, 실패 시엔 로스터를 확신하지 못한다. 플래그로 올려 두면 호출부가 베일을 걷은 뒤
            // 재시도 팝업(ShowRosterRetry)을 띄운다.
            if (characters == null)
            {
                rosterLoadFailed = true;
                Debug.LogWarning("[EntryFlowController] 캐릭터 로스터 로드 실패 — 재시도 팝업으로 넘깁니다.");
                FillSlots();
                return;
            }

            roster.AddRange(characters);
            FillSlots();
        }

        // roster 순서대로 앞칸부터 채우고, 남는 칸은 "+ 신규 캐릭터"(빈 카드)로 둔다.
        private void FillSlots()
        {
            IReadOnlyList<CharacterSlotView> slots = selectPage.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (i < roster.Count)
                {
                    CharacterSaveData c = roster[i];
                    slots[i].SetCharacter(i, GetPortrait(c.characterType), c.name, c.level, TypeName(c.characterType));
                }
                else
                {
                    slots[i].SetEmpty(i);
                }
            }
        }

        // 다른 UI(장비창·대화창)는 PlayerManager.Instance.Roster로 일러스트를 꺼내지만, 캐릭터 선택 씬에는
        // PlayerManager가 없다(부트스트랩 전이라 아직 생성되지 않음). 그래서 이 화면만 로스터 에셋을
        // 인스펙터로 직접 물린다. 빠지면 초상화가 null로 넘어가 CharacterSlotView가 초상화 칸을 꺼 버린다.
        private Sprite GetPortrait(int characterType)
            => characterRoster != null ? characterRoster.GetPortrait(characterType) : null;

        private void HandleSelected(int index)
        {
            if (IsTransitionPlaying) return;   // 연출 중 다른 슬롯을 눌러 프리뷰 모델이 바뀌지 않게

            selectPage.SetSelectedIndex(index);

            if (index >= 0 && index < roster.Count) ShowModel(roster[index].characterType);
        }

        // 선택된 클래스의 모델만 켜고 나머지는 끈다(씬에 배치된 프리뷰 모델 토글).
        private void ShowModel(int characterType)
        {
            if (classModels == null) return;

            foreach (ClassModel entry in classModels)
            {
                if (!IsSceneModel(entry.model)) continue;   // 프리팹 에셋이면 건드리지 않는다(Awake에서 이미 경고)

                entry.model.SetActive(entry.characterType == characterType);
            }
        }

        private void HideAllModels()
        {
            if (classModels == null) return;

            foreach (ClassModel entry in classModels)
            {
                if (!IsSceneModel(entry.model)) continue;

                entry.model.SetActive(false);
            }
        }

        private void HandleStart(int index)
        {
            if (index < 0 || index >= roster.Count) return;
            if (IsTransitionPlaying) return;   // 연출 중 다른 슬롯 시작 클릭 무시

            // 선택한 세이브를 세션에 담고 게임 씬으로 접속. 이후 PlayerManager/PlayerStats가 여기만 읽는다.
            GameSession.SetSelectedCharacter(roster[index]);

            if (startTransition != null) startTransition.Play(LoadGameScene);
            else LoadGameScene();
        }

        private void LoadGameScene() => SceneManager.LoadScene(gameSceneName);

        private bool IsTransitionPlaying => startTransition != null && startTransition.IsPlaying;

        // ── 신규 생성 흐름 (선택 → 클래스 → 이름) ────────────────────

        // 빈 카드("+ 신규 캐릭터") 클릭 → 클래스 선택 페이지로. 빈 칸에서만 오므로 슬롯이 남았다는 뜻.
        private void HandleCreateRequested(int index) => GoToClassSelect();

        private void GoToSelect() => ShowOnly(selectPage != null ? selectPage.gameObject : null);

        private void GoToClassSelect()
        {
            pendingClassType = 0;
            if (classSelectPage != null) classSelectPage.ClearSelection();
            HideAllModels();
            ShowOnly(classSelectPage != null ? classSelectPage.gameObject : null);
        }

        // 일러스트를 눌러 클래스를 골랐다 → 반대편에 소개 패널을 열고 프리뷰 모델을 켠다(확정 전 미리보기).
        private void HandleClassClicked(int classIndex)
        {
            //pendingClassType = ClassToType(classIndex);
            pendingClassType = classIndex;

            bool warrior = classIndex == ClassSelectPageView.ClassWarrior;
            string intro = warrior ? warriorIntro : gunnerIntro;
            classSelectPage.ShowIntro(classIndex, intro, warrior ? warriorVideoAddress : gunnerVideoAddress);
            ShowModel(pendingClassType);
        }

        // 클래스 확정 → 이름 입력 페이지로. 프리뷰 스테이지는 페이지끼리 공유하므로 모델은 그대로 둔다.
        private void HandleClassConfirmed()
        {
            if (pendingClassType == 0) return;   // 아무 클래스도 안 골랐으면 무시(SelectButton은 그때 꺼져 있음)

            createPage.ClearName();
            ShowOnly(createPage.gameObject);
        }

        // 이름 확정 → 서버에 생성 요청. 성공이면 목록으로 돌아가 갱신, 실패면 힌트로 이유를 인라인 표시.
        private async void HandleCreateConfirmed(string name)
        {
            if (pendingClassType == 0) return;

            if (FirebaseManager.Instance == null)
            {
                createPage.SetHint("로그인이 필요합니다.");
                return;
            }

            createPage.SetHint("생성 중...");
            CreateCharacterResult result = await FirebaseManager.Instance.CreateCharacter(pendingClassType, name,  ProjectS.Core.TutorialState.Undone);
            if (this == null) return;

            if (result == CreateCharacterResult.Success)
            {
                GoToSelect();
                await Refresh();
                return;
            }

            createPage.SetHint(DescribeCreate(result));
        }

        // ClassSelectPageView(0=전사/1=거너) → characterType(1=검사/2=거너). 매핑이 갈리면 여기만 고친다.
        private static int ClassToType(int classIndex) => classIndex + 1;

        private static string DescribeCreate(CreateCharacterResult result)
        {
            switch (result)
            {
                case CreateCharacterResult.NameTaken: return "이미 사용 중인 이름입니다.";
                case CreateCharacterResult.InvalidName:
                    return $"이름은 {CharacterCreatePageView.MinNameLength}~{CharacterCreatePageView.MaxNameLength}자여야 합니다 ( . # $ [ ] / 불가 ).";
                default:
                    // 빌드에선 콘솔을 못 보므로 실제 실패 사유(미로그인·권한 거부·변환 예외 등)를 힌트에 직접 띄운다.
                    string detail = FirebaseManager.Instance != null ? FirebaseManager.Instance.LastCreateError : null;
                    return string.IsNullOrEmpty(detail)
                        ? "생성에 실패했습니다. 잠시 후 다시 시도하세요."
                        : $"생성 실패: {detail}";
            }
        }

        // 한 페이지만 켜고 나머지는 끈다. null 페이지(아직 미배선)는 건너뛰어 slice 1만 쓰던 씬도 안전.
        private void ShowOnly(GameObject page)
        {
            if (selectPage != null) selectPage.gameObject.SetActive(selectPage.gameObject == page);
            if (classSelectPage != null) classSelectPage.gameObject.SetActive(classSelectPage.gameObject == page);
            if (createPage != null) createPage.gameObject.SetActive(createPage.gameObject == page);
        }

        // ── 삭제 흐름 (확인 팝업 → 서버 삭제 → 갱신) ─────────────────

        // 삭제(×) 클릭 → 확인 팝업. 되돌릴 수 없는 행동이라 반드시 확인을 한 번 끼운다.
        // 인덱스가 아니라 세이브 인스턴스를 캡처한다 — 팝업이 열린 사이 목록이 바뀌어도 uniqueId로 정확히 지운다.
        private void HandleDeleteRequested(int index)
        {
            // 아래 가드들은 조용히 빠져나가면 안 된다 — 버튼이 안 눌린 것과 구분이 되지 않아
            // "삭제가 아무 일도 안 한다"로만 보인다(팝업 계층이 꺼져 있던 사고가 그래서 오래 갔다).
            if (index < 0 || index >= roster.Count)
            {
                Debug.LogWarning($"[EntryFlowController] 슬롯 {index}에 대응하는 캐릭터가 로스터에 없다 — 삭제 중단.");
                return;
            }

            if (popupLayer == null)
            {
                Debug.LogWarning("[EntryFlowController] popupLayer 미배선 — 확인 없는 삭제는 막는다.");
                return;
            }

            CharacterSaveData target = roster[index];
            popupLayer.ShowConfirm(
                $"'{target.name}' 캐릭터를 삭제할까요?",
                "삭제하면 되돌릴 수 없습니다.",
                "삭제", "취소",
                confirmed: () => PerformDelete(target));
        }

        private async void PerformDelete(CharacterSaveData target)
        {
            if (FirebaseManager.Instance == null)
            {
                Debug.LogError("[EntryFlowController] FirebaseManager가 없다 — 로그인 씬을 거치지 않고 이 씬을 단독 재생했는지 확인.");
                popupLayer.ShowAlert();
                return;
            }

            popupLayer.SetBusy(true);   // 서버 왕복 중 중복 클릭 차단(끝나면 반드시 해제)
            // 이름도 넘겨 전역 이름 예약(CharacterNames)까지 함께 지운다(안 지우면 그 이름을 다시 못 씀).
            bool ok = await FirebaseManager.Instance.DeleteCharacter(target.uniqueId, target.name);
            if (this == null) return;
            popupLayer.SetBusy(false);

            if (ok)
            {
                await Refresh();
                return;
            }

            // ShowAlert는 아직 빈 껍데기(AlertPopupView 미연결)라 화면엔 아무것도 뜨지 않는다.
            // 그 사이 원인을 잃지 않도록 매니저가 남긴 사유를 콘솔로라도 흘린다.
            string reason = FirebaseManager.Instance.LastDeleteError;
            Debug.LogError($"[EntryFlowController] '{target.name}' 삭제 실패: " +
                (string.IsNullOrEmpty(reason) ? "사유 없음(권한 거부 예외 로그를 함께 확인)" : reason));
            popupLayer.ShowAlert();
        }

        // 로그아웃 버튼 클릭 → 확인 팝업. 실수로 로그인 씬에 튀는 일이 잦아 Esc 단축키를 없앴으므로,
        // 버튼도 한 번 확인을 끼워 같은 사고를 막는다. 팝업 미배선(단독 테스트)이면 바로 로그아웃.
        private void HandleLogoutRequested()
        {
            if (IsTransitionPlaying) return;   // 시작 연출 중에는 로그아웃으로 끊지 않는다

            if (popupLayer == null)
            {
                LogoutToLogin();
                return;
            }

            popupLayer.ShowConfirm(
                "로그아웃할까요?",
                "로그인 화면으로 돌아갑니다.",
                "로그아웃", "취소",
                confirmed: LogoutToLogin);
        }

        // 선택 → 로그인도 반대 방향(로그인 → 선택)과 같은 형식으로 넘긴다 — 베일로 덮은 뒤 비동기 로드.
        // 동기 LoadScene은 그 프레임을 통째로 멈춰 덮어 놓아도 "딱 끊기는" 느낌이 남고, 비동기로 넘기면
        // 로딩 동안 베일의 스피너가 계속 돌아 화면이 살아 있다. 도착한 로그인 씬은 자기 베일로 이어받는다.
        //
        // async void는 UI 콜백(확인 팝업의 confirmed)에서만 예외적으로 허용한다.
        private async void LogoutToLogin()
        {
            if (veil != null) await veil.CoverAsync(returningToLoginMessage);
            if (this == null) return;

            if (FirebaseManager.Instance != null) FirebaseManager.Instance.Logout();
            GameSession.Clear();

            SceneManager.LoadSceneAsync(loginSceneName);
        }

        private void HandleQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string TypeName(int characterType)
        {
            switch (characterType)
            {
                case 1: return "검사";
                case 2: return "거너";
                default: return $"타입{characterType}";
            }
        }
    }
}
