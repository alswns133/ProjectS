using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ProjectS.Data;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.Debugging;

namespace ProjectS.UI
{
    /// <summary>
    /// 대화창을 재생하는 대화 시스템(싱글톤). 대사를 한 줄씩 보여주고
    /// 다음(Space)/이전(Alt)/처음으로(Z)/스킵(Tab)/닫기(Esc)로 진행한다.
    /// 끝까지 보거나 스킵하면 onComplete를 호출하고, 닫기(Esc)는 취소한다(onComplete 안 부름).
    ///
    /// 레이아웃은 좌=플레이어 / 우=NPC(또는 제3자) 고정이다. 이름표(leftNameText/rightNameText)와
    /// 초상화(leftPortrait/rightPortrait)는 <b>지금 말하는 쪽만</b> 표시하고 반대쪽은 숨긴다.
    /// 말하는 쪽 초상화가 없거나 로드 실패하면 그 슬롯은 숨기고 이름·대사만 나온다.
    ///
    /// 대화 중에는 PlayerInputHandler·카메라 회전을 끄고 마우스 커서를 보여준다(버튼 클릭용).
    /// 배치: 대화창 UI에 붙이고 root/텍스트/좌우 이름·초상화/버튼/입력 바인딩을 연결한다. 씬에 하나만 둔다.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        /// <summary>전역 접근점. NPC 상호작용이 대화를 요청할 때 쓴다.</summary>
        public static DialogueManager Instance { get; private set; }

        [Header("UI")]
        [Tooltip("대화창 루트. 재생 중에만 켠다.")]
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text questNameText;   // 퀘스트명(없으면 숨김)
        [SerializeField] private TMP_Text lineText;        // 현재 대사

        [Header("좌: 플레이어 / 우: NPC·제3자")]
        [SerializeField] private TMP_Text leftNameText;    // 플레이어 이름
        [SerializeField] private TMP_Text rightNameText;   // NPC(또는 제3자) 이름
        [SerializeField] private Image leftPortrait;       // 플레이어 초상화
        [SerializeField] private Image rightPortrait;      // NPC·제3자 초상화

        [Header("버튼")]
        [SerializeField] private Button nextButton;   // 다음
        [SerializeField] private Button prevButton;   // 이전
        [SerializeField] private Button firstButton;  // 처음으로
        [SerializeField] private Button skipButton;   // 스킵
        [SerializeField] private Button closeButton;  // 닫기

        [Header("플레이어 (임시 기본값 — 나중에 세이브 캐릭터로 교체)")]
        [SerializeField] private string playerName = "나";
        [SerializeField] private Sprite playerPortrait;

        [Header("입력 키")]
        [SerializeField] private InputAction nextAction = new InputAction("DialogueNext", InputActionType.Button, "<Keyboard>/space");
        [SerializeField] private InputAction prevAction = new InputAction("DialoguePrev", InputActionType.Button, "<Keyboard>/leftAlt");
        [SerializeField] private InputAction firstAction = new InputAction("DialogueFirst", InputActionType.Button, "<Keyboard>/z");
        [SerializeField] private InputAction skipAction = new InputAction("DialogueSkip", InputActionType.Button, "<Keyboard>/tab");
        [SerializeField] private InputAction closeAction = new InputAction("DialogueClose", InputActionType.Button, "<Keyboard>/escape");

        private IReadOnlyList<DialogueLine> lines;
        private int index;
        private Action onComplete;

        // 닫기(Esc/버튼)로 취소될 때 호출. 상호작용 컨트롤러가 "대화 중 취소 = 상호작용 종료"로 받는다.
        private Action onCancel;

        // 이번 대화에서 스킵을 허용할지. 인사말=false, 퀘스트 내용=true(퀘스트 대화부터 스킵 활성).
        private bool allowSkip;

        // 재생 중인 NPC의 이름·초상화(오른쪽 기본값).
        private string npcName;
        private Sprite npcPortrait;

        // Other 초상화 어드레서블 핸들. 한 대화 동안 모아뒀다가 끝날 때 일괄 해제한다.
        private readonly List<AsyncOperationHandle<Sprite>> otherHandles = new();

        // 대화 중 플레이어 입력·카메라를 얼리기 위한 참조(1회 조회 후 캐싱).
        private PlayerInputHandler playerInput;
        private CameraPivotController cameraPivot;

        // 대화 전 커서 상태(끝나면 되돌린다).
        private CursorLockMode savedLockState;
        private bool savedCursorVisible;

        /// <summary>대화가 재생 중인지. NPC 상호작용이 중복 실행되지 않게 확인한다.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// 대화가 스스로 플레이어 입력·카메라·커서를 얼릴지. 상호작용 컨트롤러가 인사말→허브→대화 전체를
        /// 한 번에 얼리는 경우, 컨트롤러가 이걸 false로 두어 대화마다 이중으로 얼렸다 푸는 걸 막는다
        /// (대화↔허브 전환 때 커서가 사라져 버튼을 못 누르는 문제 방지).
        /// </summary>
        public bool ManageFreeze { get; set; } = true;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (root != null) root.SetActive(false);

            // UI 버튼을 코드로 연결한다(필드만 연결하면 됨. 인스펙터 onClick에 또 걸면 이중 호출).
            if (nextButton != null) nextButton.onClick.AddListener(Next);
            if (prevButton != null) prevButton.onClick.AddListener(Previous);
            if (firstButton != null) firstButton.onClick.AddListener(First);
            if (skipButton != null) skipButton.onClick.AddListener(Skip);
            if (closeButton != null) closeButton.onClick.AddListener(Cancel);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            EnableInput(false);
            ReleaseOtherHandles();

            // 대화 중 파괴 시엔 즉시 복구(오브젝트가 사라지므로 지연 복구는 못 한다). 컨트롤러 관리 중이면 손 안 댐.
            if (IsPlaying && ManageFreeze)
            {
                if (cameraPivot != null) cameraPivot.enabled = true;
                if (playerInput != null) playerInput.enabled = true;
                Cursor.lockState = savedLockState;
                Cursor.visible = savedCursorVisible;
            }
        }

        /// <summary>
        /// DialogueId로 재생한다(DialogueTable 조회). 데이터가 없으면 대화를 건너뛰고 바로 완료로 본다.
        /// </summary>
        /// <param name="speakerNpcName">오른쪽(NPC) 이름</param>
        /// <param name="speakerNpcPortrait">오른쪽(NPC) 초상화(없으면 null)</param>
        /// <param name="dialogueId">재생할 대화 ID</param>
        /// <param name="questName">상단에 표시할 퀘스트명(없으면 빈 문자열 → 숨김)</param>
        /// <param name="allowSkip">스킵 버튼·입력 활성 여부(인사말=false, 퀘스트 내용=true)</param>
        /// <param name="onComplete">끝까지/스킵 시 호출(취소면 호출 안 됨)</param>
        /// <param name="onCancel">닫기(Esc)로 취소 시 호출(없으면 아무 것도 안 함)</param>
        public void Play(string speakerNpcName, Sprite speakerNpcPortrait, int dialogueId, string questName, bool allowSkip, Action onComplete, Action onCancel = null)
        {
            DialogueTable row = JsonManager.Instance != null ? JsonManager.Instance.Get<DialogueTable>(dialogueId) : null;
            if (row == null)
            {
                DevLog.Warning($"[Dialogue] DialogueId {dialogueId} 행이 없어 대화를 건너뜁니다.");
                onComplete?.Invoke();
                return;
            }
            Play(speakerNpcName, speakerNpcPortrait, row.Lines, questName, allowSkip, onComplete, onCancel);
        }

        /// <summary>
        /// 대사 배열을 직접 재생한다. 이미 재생 중이거나 대사가 없으면 즉시 완료로 본다.
        /// </summary>
        /// <param name="speakerNpcName">오른쪽(NPC) 이름</param>
        /// <param name="speakerNpcPortrait">오른쪽(NPC) 초상화(없으면 null)</param>
        /// <param name="dialogueLines">순서대로 재생할 대사들</param>
        /// <param name="questName">상단에 표시할 퀘스트명(없으면 빈 문자열 → 숨김)</param>
        /// <param name="allowSkip">스킵 버튼·입력 활성 여부(인사말=false, 퀘스트 내용=true)</param>
        /// <param name="onComplete">끝까지/스킵 시 호출(취소면 호출 안 됨)</param>
        /// <param name="onCancel">닫기(Esc)로 취소 시 호출(없으면 아무 것도 안 함)</param>
        public void Play(string speakerNpcName, Sprite speakerNpcPortrait, IReadOnlyList<DialogueLine> dialogueLines, string questName, bool allowSkip, Action onComplete, Action onCancel = null)
        {
            if (IsPlaying) return;

            if (dialogueLines == null || dialogueLines.Count == 0)
            {
                onComplete?.Invoke();
                return;
            }

            this.allowSkip = allowSkip;
            if (skipButton != null) skipButton.gameObject.SetActive(allowSkip);   // 스킵은 퀘스트 내용부터

            npcName = speakerNpcName;
            npcPortrait = speakerNpcPortrait;
            lines = dialogueLines;
            index = 0;
            this.onComplete = onComplete;
            this.onCancel = onCancel;
            IsPlaying = true;

            // 퀘스트명(있을 때만 표시).
            if (questNameText != null)
            {
                bool hasQuest = !string.IsNullOrEmpty(questName);
                questNameText.text = hasQuest ? questName : string.Empty;
                questNameText.gameObject.SetActive(hasQuest);
            }

            // 이름표·초상화 모두 ShowCurrentLine이 '말하는 쪽만' 표시한다.
            ShowCurrentLine();
            if (root != null) root.SetActive(true);

            EnableInput(true);
            FreezePlayer();
        }

        /// <summary>다음 대사로 넘긴다. 마지막이면 대화를 완료한다(Space 또는 버튼).</summary>
        public void Next()
        {
            if (!IsPlaying) return;

            index++;
            if (index >= lines.Count)
                Complete();
            else
                ShowCurrentLine();
        }

        /// <summary>이전 대사로 돌아간다. 첫 줄이면 그대로 있는다(Alt 또는 버튼).</summary>
        public void Previous()
        {
            if (!IsPlaying) return;
            if (index <= 0) return;

            index--;
            ShowCurrentLine();
        }

        /// <summary>대화의 첫 대사로 되돌아간다(Z 또는 버튼).</summary>
        public void First()
        {
            if (!IsPlaying) return;

            index = 0;
            ShowCurrentLine();
        }

        /// <summary>남은 대사를 건너뛰고 바로 완료한다(Tab 또는 버튼).</summary>
        public void Skip()
        {
            if (!IsPlaying) return;
            Complete();
        }

        /// <summary>대화를 취소한다. 정상 완료(onComplete)로는 넘어가지 않고 onCancel만 호출한다(Esc 또는 버튼).</summary>
        public void Cancel()
        {
            if (!IsPlaying) return;

            Action callback = onCancel;   // EndSession이 지우기 전에 잡아둔다
            EndSession();
            callback?.Invoke();
        }

        // 현재 줄의 화자에 맞춰 대사·이름·초상화를 갱신한다. 이름표·초상화 모두 '말하는 쪽만' 보이고 반대쪽은 숨긴다.
        private void ShowCurrentLine()
        {
            DialogueLine line = lines[index];
            if (lineText != null) lineText.text = line.Text;

            if (line.Speaker == DialogueSpeaker.Player)
            {
                // 왼쪽(플레이어)만 표시.
                ShowName(leftNameText, playerName);
                SetPortrait(leftPortrait, playerPortrait);
                HideName(rightNameText);
                HidePortrait(rightPortrait);
            }
            else if (line.Speaker == DialogueSpeaker.Other)
            {
                // 제3자: 오른쪽 자리를 잠시 그 사람으로 바꾼다(초상화는 주소로 비동기 로드).
                ShowName(rightNameText, line.Name);
                SetPortrait(rightPortrait, null);
                if (!string.IsNullOrEmpty(line.PortraitAddress))
                    LoadOtherPortrait(line.PortraitAddress, rightPortrait, index);
                HideName(leftNameText);
                HidePortrait(leftPortrait);
            }
            else // Npc
            {
                // 오른쪽(NPC)만 표시.
                ShowName(rightNameText, npcName);
                SetPortrait(rightPortrait, npcPortrait);
                HideName(leftNameText);
                HidePortrait(leftPortrait);
            }
        }

        // 이름표를 값과 함께 켠다.
        private static void ShowName(TMP_Text text, string value)
        {
            if (text == null) return;
            text.text = value;
            text.gameObject.SetActive(true);
        }

        // 이름표를 숨긴다(말하지 않는 쪽).
        private static void HideName(TMP_Text text)
        {
            if (text != null) text.gameObject.SetActive(false);
        }

        // 정상 종료: 세션을 닫은 뒤 완료 콜백을 부른다(콜백 안에서 다음 대화를 열어도 안전하게).
        private void Complete()
        {
            Action callback = onComplete;
            EndSession();
            callback?.Invoke();
        }

        private void EndSession()
        {
            IsPlaying = false;
            lines = null;
            onComplete = null;
            onCancel = null;
            EnableInput(false);
            UnfreezePlayer();
            ReleaseOtherHandles();
            if (root != null) root.SetActive(false);
        }

        // ---------- 초상화 ----------

        // 스프라이트가 있으면 그 쪽 초상화를 켜고, 없으면 숨긴다(초상화 없어도 대화는 계속).
        private static void SetPortrait(Image image, Sprite sprite)
        {
            if (image == null) return;

            if (sprite != null)
            {
                image.sprite = sprite;
                image.gameObject.SetActive(true);
            }
            else
            {
                image.gameObject.SetActive(false);
            }
        }

        // 초상화 슬롯을 숨긴다(말하지 않는 쪽).
        private static void HidePortrait(Image image)
        {
            if (image != null) image.gameObject.SetActive(false);
        }

        // Other 초상화를 어드레서블로 로드해 채운다. 로드 완료 시 아직 같은 줄일 때만 반영하고,
        // 실패하면 숨긴 채로 둔다. 핸들은 대화가 끝날 때 일괄 해제한다.
        private async void LoadOtherPortrait(string address, Image target, int forIndex)
        {
            AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(address);
            otherHandles.Add(handle);

            await handle.Task;

            if (!IsPlaying || index != forIndex) return;

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                SetPortrait(target, handle.Result);
            else
                DevLog.Warning($"[Dialogue] Other 초상화 로드 실패: {address}");
        }

        private void ReleaseOtherHandles()
        {
            foreach (var handle in otherHandles)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            otherHandles.Clear();
        }

        // ---------- 플레이어 얼리기 ----------

        // 대화 시작: 이동·공격 입력과 카메라 회전을 끄고 커서를 보여준다.
        private void FreezePlayer()
        {
            if (!ManageFreeze) return;   // 컨트롤러가 상호작용 전체를 얼리는 경우 대화는 관여 안 함

            if (playerInput == null) playerInput = FindAnyObjectByType<PlayerInputHandler>();
            if (playerInput != null) playerInput.enabled = false;

            if (cameraPivot == null) cameraPivot = FindAnyObjectByType<CameraPivotController>();
            if (cameraPivot != null) cameraPivot.enabled = false;

            savedLockState = Cursor.lockState;
            savedCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 대화 종료: 카메라·커서는 즉시 복구하되, 플레이어 이동/공격 입력은
        // 대화를 닫은 공유 키(Space/Esc)가 떼어질 때까지 기다렸다 켠다.
        // (마지막 대사의 Space가 그대로 점프로 새는 것을 막는다.)
        private void UnfreezePlayer()
        {
            if (!ManageFreeze) return;   // 컨트롤러가 관리 → 대화 종료로 풀지 않는다

            if (cameraPivot != null) cameraPivot.enabled = true;
            Cursor.lockState = savedLockState;
            Cursor.visible = savedCursorVisible;

            if (playerInput != null)
            {
                StopAllCoroutines();
                StartCoroutine(ReenablePlayerInputWhenReleased());
            }
        }

        private IEnumerator ReenablePlayerInputWhenReleased()
        {
            Keyboard kb = Keyboard.current;
            // 대화 진행/닫기 키(Space, Esc)가 눌려 있으면 놓을 때까지 대기.
            while (kb != null && (kb.spaceKey.isPressed || kb.escapeKey.isPressed))
                yield return null;

            // 그 사이 새 대화가 시작됐으면 복구하지 않는다.
            if (!IsPlaying && playerInput != null)
                playerInput.enabled = true;
        }

        // ---------- 대화 입력 ----------

        // 재생 중에만 입력을 켠다. 짝을 맞춰 구독/해제한다.
        private void EnableInput(bool enable)
        {
            if (enable)
            {
                nextAction.Enable();
                prevAction.Enable();
                firstAction.Enable();
                closeAction.Enable();

                nextAction.performed += OnNext;
                prevAction.performed += OnPrev;
                firstAction.performed += OnFirst;
                closeAction.performed += OnClose;

                // 스킵은 퀘스트 내용 대화에서만(인사말/허브 대화에선 비활성).
                if (allowSkip)
                {
                    skipAction.Enable();
                    skipAction.performed += OnSkip;
                }
            }
            else
            {
                nextAction.performed -= OnNext;
                prevAction.performed -= OnPrev;
                firstAction.performed -= OnFirst;
                skipAction.performed -= OnSkip;
                closeAction.performed -= OnClose;

                nextAction.Disable();
                prevAction.Disable();
                firstAction.Disable();
                skipAction.Disable();
                closeAction.Disable();
            }
        }

        private void OnNext(InputAction.CallbackContext _) => Next();
        private void OnPrev(InputAction.CallbackContext _) => Previous();
        private void OnFirst(InputAction.CallbackContext _) => First();
        private void OnSkip(InputAction.CallbackContext _) => Skip();
        private void OnClose(InputAction.CallbackContext _) => Cancel();
    }
}
