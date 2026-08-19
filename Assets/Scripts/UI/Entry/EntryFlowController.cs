using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ProjectS.Data;
using ProjectS.Managers;

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

        [Header("접속할 게임 씬 (Build Settings 등록 필요)")]
        [SerializeField] private string gameSceneName = "Bootstrap";

        [Header("뒤로 (임시)")]
        [Tooltip("TODO(임시): Esc → 로그아웃 후 이 로그인 씬으로. 정식 뒤로/로그아웃 UX가 정해지면 교체.")]
        [SerializeField] private string loginSceneName = "Login";

        [Header("프리뷰 모델 (클래스별, 씬에 배치된 GameObject)")]
        [Tooltip("슬롯 선택 시 해당 characterType 모델만 켜고 나머지는 끈다. 리그(카메라→RT)는 씬에 이미 있다.")]
        [SerializeField] private ClassModel[] classModels;

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

        private void OnEnable()
        {
            selectPage.QuitButton.onClick.AddListener(HandleQuit);

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

        private async void Start()
        {
            GoToSelect();   // 진입 기본 페이지 = 선택 화면(다른 페이지는 꺼둔다)
            selectPage.SetVersion($"v{Application.version}");

            // Firebase가 로그인 세션·초기화를 끝낼 때까지 기다린 뒤 로스터를 읽는다.
            // 매니저가 없으면(로그인 없이 이 씬만 단독 테스트) 빈 슬롯으로 둔다.
            if (FirebaseManager.Instance != null)
            {
                await FirebaseManager.Instance.ReadyTask;
                if (this == null) return;
            }

            await Refresh();
        }

        // 로스터를 다시 읽어 6칸을 채운다(진입 시·생성/삭제 후 재호출 예정).
        private async Task Refresh()
        {
            roster.Clear();
            selectPage.ClearSelection();
            HideAllModels();   // 갱신 직후엔 선택이 없으니 이전 모델이 남지 않게 전부 끈다

            List<CharacterSaveData> characters = null;
            if (FirebaseManager.Instance != null)
                characters = await FirebaseManager.Instance.LoadAllCharacters();
            if (this == null) return;

            // null = 로딩 실패(권한 전파 지연·네트워크). 빈 슬롯(진짜 0개)과 혼동하면 중복 생성·오삭제
            // 위험이 있어, 실패 시엔 로스터를 확신하지 못한다. 지금은 경고만 남기고 빈칸으로 두되,
            // 재시도 팝업은 다음 단계에서 붙인다.
            if (characters == null)
            {
                Debug.LogWarning("[EntryFlowController] 캐릭터 로스터 로드 실패. 재시도 흐름은 다음 단계에서 추가.");
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
                    slots[i].SetCharacter(i, null, c.name, c.level, TypeName(c.characterType));
                }
                else
                {
                    slots[i].SetEmpty(i);
                }
            }
        }

        private void HandleSelected(int index)
        {
            selectPage.SetSelectedIndex(index);

            if (index >= 0 && index < roster.Count) ShowModel(roster[index].characterType);
        }

        // 선택된 클래스의 모델만 켜고 나머지는 끈다(씬에 배치된 프리뷰 모델 토글).
        private void ShowModel(int characterType)
        {
            if (classModels == null) return;

            foreach (ClassModel entry in classModels)
            { if (entry.model != null) entry.model.SetActive(entry.characterType == characterType); Debug.Log($"{entry.characterType}, {characterType} "); }
        }

        private void HideAllModels()
        {
            if (classModels == null) return;

            foreach (ClassModel entry in classModels)
                if (entry.model != null) entry.model.SetActive(false);
        }

        private void HandleStart(int index)
        {
            if (index < 0 || index >= roster.Count) return;

            // 선택한 세이브를 세션에 담고 게임 씬으로 접속. 이후 PlayerManager/PlayerStats가 여기만 읽는다.
            GameSession.SetSelectedCharacter(roster[index]);
            SceneManager.LoadScene(gameSceneName);
        }

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

            string intro = classIndex == ClassSelectPageView.ClassWarrior ? warriorIntro : gunnerIntro;
            classSelectPage.ShowIntro(classIndex, intro);
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
            CreateCharacterResult result = await FirebaseManager.Instance.CreateCharacter(pendingClassType, name);
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
                default: return "생성에 실패했습니다. 잠시 후 다시 시도하세요.";
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
            if (index < 0 || index >= roster.Count) return;

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
                popupLayer.ShowAlert();
                return;
            }

            popupLayer.SetBusy(true);   // 서버 왕복 중 중복 클릭 차단(끝나면 반드시 해제)
            // 이름도 넘겨 전역 이름 예약(CharacterNames)까지 함께 지운다(안 지우면 그 이름을 다시 못 씀).
            bool ok = await FirebaseManager.Instance.DeleteCharacter(target.uniqueId, target.name);
            if (this == null) return;
            popupLayer.SetBusy(false);

            if (ok) await Refresh();
            else popupLayer.ShowAlert();
        }

        // TODO(임시): Esc = 로그아웃하고 로그인 씬으로. 팝업이 열려 있으면 그쪽이 Esc(취소)를 먼저 먹으므로 넘긴다.
        // 지금은 어느 페이지(선택/클래스/생성)에서든 바로 로그인으로 튄다 — 정식 뒤로가기/로그아웃 UX가
        // 정해지면 이 임시 처리를 교체할 것.
        private void Update()
        {
            if (popupLayer != null && popupLayer.IsAnyOpen) return;

            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

            LogoutToLogin();
        }

        private void LogoutToLogin()
        {
            if (FirebaseManager.Instance != null) FirebaseManager.Instance.Logout();
            GameSession.Clear();
            SceneManager.LoadScene(loginSceneName);
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
