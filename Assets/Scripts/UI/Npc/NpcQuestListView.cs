using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using ProjectS.NPCs;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>
    /// NPC 퀘스트 선택 리스트 화면. 수락 가능(!)·완료 가능(?) 퀘스트만 보인다(진행중 제외).
    /// W/S(또는 마우스)로 선택을 옮기고 Space(또는 클릭)로 실행한다 — 수락 가능이면 도입 대화→수락,
    /// 완료 가능이면 보상 화면으로. 컨트롤러의 화면이 QuestList일 때만 켜진다(공유 뷰, 씬에 하나).
    /// </summary>
    public class NpcQuestListView : NpcScreenViewBase
    {
        protected override NpcScreen Screen => NpcScreen.QuestList;

        [Header("표시")]
        [Tooltip("NPC 이름(선택).")]
        [SerializeField] private TMP_Text npcNameText;
        [Tooltip("NPC 일러스트(선택). 컨트롤러의 npcPortrait를 표시하고, 없으면 숨긴다.")]
        [SerializeField] private Image npcPortrait;
        [Tooltip("행이 담기는 부모(세로 레이아웃 권장).")]
        [SerializeField] private RectTransform content;
        [Tooltip("복제해 쓸 행 프리팹.")]
        [SerializeField] private NpcQuestListRow rowPrefab;

        [Header("버튼")]
        [SerializeField] private Button backButton;     // 뒤로(허브로) — 고유기능은 허브에 모여 있다
        [SerializeField] private Button selectButton;   // 수락/완료(Space) — 선택된 항목 실행
        [SerializeField] private Button closeButton;    // 닫기

        [Header("입력 키")]
        [SerializeField] private InputAction upAction = new InputAction("QuestUp", InputActionType.Button, "<Keyboard>/w");
        [SerializeField] private InputAction downAction = new InputAction("QuestDown", InputActionType.Button, "<Keyboard>/s");
        [SerializeField] private InputAction selectAction = new InputAction("QuestSelect", InputActionType.Button, "<Keyboard>/space");
        [SerializeField] private InputAction backAction = new InputAction("QuestBack", InputActionType.Button, "<Keyboard>/z");
        [SerializeField] private InputAction closeAction = new InputAction("QuestListClose", InputActionType.Button, "<Keyboard>/escape");

        // 행 풀(재사용). 항목 수에 맞춰 켜고 남는 건 끈다.
        private readonly List<NpcQuestListRow> rows = new();
        private int selectedIndex;

        protected override void Awake()
        {
            base.Awake();
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (selectButton != null) selectButton.onClick.AddListener(Select);
            if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        }

        protected override void OnShow()
        {
            if (npcNameText != null) npcNameText.text = Controller.NpcName;
            ApplyPortrait(npcPortrait, Controller.NpcPortrait);
            Populate();
        }

        // 컨트롤러의 항목으로 행을 채운다. 부족하면 늘리고 남으면 끈다.
        private void Populate()
        {
            IReadOnlyList<NpcQuestEntry> entries = Controller.QuestEntries;

            while (rows.Count < entries.Count && rowPrefab != null && content != null)
                rows.Add(Instantiate(rowPrefab, content));

            for (int i = 0; i < rows.Count; i++)
            {
                if (i < entries.Count)
                {
                    rows[i].gameObject.SetActive(true);
                    rows[i].Bind(i, entries[i], OnRowHover, OnRowClick);
                }
                else
                {
                    rows[i].gameObject.SetActive(false);
                }
            }

            selectedIndex = 0;
            RefreshHighlight();
        }

        // ---- 선택 이동/실행 ----

        private void Move(int delta)
        {
            int count = Controller.QuestEntries.Count;
            if (count == 0) return;

            selectedIndex = (selectedIndex + delta + count) % count;   // 위/아래로 순환
            RefreshHighlight();
        }

        // 패널이 활성화된 뒤 선택을 다시 동기화한다. OnShow(활성화 전)에서 건 EventSystem 선택은
        // 행이 아직 비활성이라 안 먹으므로, 여기서 한 번 더 맞춰 초기 선택 색이 뜨게 한다.
        protected override void OnShown() => SyncSelectedButton();

        private void RefreshHighlight()
        {
            int count = Controller.QuestEntries.Count;
            for (int i = 0; i < rows.Count; i++)
                rows[i].SetSelected(i == selectedIndex && i < count);   // 화살표(>) 토글

            SyncSelectedButton();
        }

        // 선택된 행으로 EventSystem 선택을 옮겨 버튼 Selected 색을 켠다(활성 상태여야 먹는다).
        private void SyncSelectedButton()
        {
            int count = Controller.QuestEntries.Count;
            if (selectedIndex < 0 || selectedIndex >= count || EventSystem.current == null) return;

            Selectable sel = rows[selectedIndex].Selectable;
            if (sel != null)
                EventSystem.current.SetSelectedGameObject(sel.gameObject);
        }

        private void Select()
        {
            IReadOnlyList<NpcQuestEntry> entries = Controller.QuestEntries;
            if (selectedIndex < 0 || selectedIndex >= entries.Count) return;

            Controller.SelectQuest(entries[selectedIndex].QuestId);
        }

        private void OnRowHover(int i)
        {
            selectedIndex = i;
            RefreshHighlight();
        }

        private void OnRowClick(int i)
        {
            selectedIndex = i;
            RefreshHighlight();
            Select();
        }

        private void OnBack()
        {
            if (Controller != null) Controller.BackToGreeting();
        }

        private void OnClose()
        {
            if (Controller != null) Controller.CloseInteraction();
        }

        protected override void EnableInput(bool enable)
        {
            if (enable)
            {
                upAction.Enable();
                downAction.Enable();
                selectAction.Enable();
                backAction.Enable();
                closeAction.Enable();

                upAction.performed += OnUp;
                downAction.performed += OnDown;
                selectAction.performed += OnSelectKey;
                backAction.performed += OnBackKey;
                closeAction.performed += OnCloseKey;
            }
            else
            {
                upAction.performed -= OnUp;
                downAction.performed -= OnDown;
                selectAction.performed -= OnSelectKey;
                backAction.performed -= OnBackKey;
                closeAction.performed -= OnCloseKey;

                upAction.Disable();
                downAction.Disable();
                selectAction.Disable();
                backAction.Disable();
                closeAction.Disable();
            }
        }

        private void OnUp(InputAction.CallbackContext _) => Move(-1);
        private void OnDown(InputAction.CallbackContext _) => Move(1);
        private void OnSelectKey(InputAction.CallbackContext _) => Select();
        private void OnBackKey(InputAction.CallbackContext _) => OnBack();
        private void OnCloseKey(InputAction.CallbackContext _) => OnClose();
    }
}
