using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// NPC 허브 화면. 인사말 한 줄 + 고유기능 버튼들(상점/강화/…) + [퀘스트목록(Space)]/[닫기(ESC)].
    /// 지금 상호작용 중인 <see cref="NpcInteractionController"/>의 화면이 Hub일 때만 켜진다(공유 뷰, 씬에 하나).
    /// 고유기능 버튼은 그 NPC가 가진 기능만(HasFeature), 퀘스트목록 버튼은 표시할 퀘스트가 있을 때만(HubHasQuests) 보인다.
    /// </summary>
    public class NpcHubView : NpcScreenViewBase
    {
        protected override NpcScreen Screen => NpcScreen.Hub;

        [Header("표시")]
        [Tooltip("NPC 이름(선택 — 월드 이름표를 쓰면 비워도 됨).")]
        [SerializeField] private TMP_Text npcNameText;
        [Tooltip("인사말 한 줄.")]
        [SerializeField] private TMP_Text greetingText;

        [Header("버튼")]
        [Tooltip("허브 고유기능 버튼들(상점/강화/… — 씬에 미리 배치해 드래그). NPC가 가진 기능만 켜진다.")]
        [SerializeField] private NpcHubFeatureButton[] featureButtons;
        [SerializeField] private Button questButton;   // 퀘스트목록
        [SerializeField] private Button closeButton;   // 닫기

        [Header("입력 키")]
        [SerializeField] private InputAction questAction = new InputAction("HubQuest", InputActionType.Button, "<Keyboard>/space");
        [SerializeField] private InputAction closeAction = new InputAction("HubClose", InputActionType.Button, "<Keyboard>/escape");

        protected override void Awake()
        {
            base.Awake();
            if (featureButtons != null)
            {
                foreach (NpcHubFeatureButton featureButton in featureButtons)
                    if (featureButton != null) featureButton.Bind(OnFeature);
            }
            if (questButton != null) questButton.onClick.AddListener(OnQuest);
            if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        }

        protected override void OnShow()
        {
            if (npcNameText != null) npcNameText.text = Controller.NpcName;
            if (greetingText != null) greetingText.text = Controller.GreetingText;

            // 그 NPC가 가진 기능 버튼만 켠다(나머지는 숨김). 씬에 미리 둔 버튼을 활성/비활성만.
            // None(기능 없음/미설정) 버튼은 항상 숨긴다.
            if (featureButtons != null)
            {
                foreach (NpcHubFeatureButton featureButton in featureButtons)
                    if (featureButton != null)
                    {
                        bool show = featureButton.Feature != NpcHubFeature.None
                                    && Controller.HasFeature(featureButton.Feature);
                        featureButton.gameObject.SetActive(show);
                    }
            }

            if (questButton != null) questButton.gameObject.SetActive(Controller.HubHasQuests);
        }

        private void OnFeature(NpcHubFeature feature)
        {
            if (Controller != null) Controller.SelectFeature(feature);
        }

        private void OnQuest()
        {
            if (Controller != null && Controller.HubHasQuests) Controller.OpenQuestList();
        }

        private void OnClose()
        {
            if (Controller != null) Controller.CloseInteraction();
        }

        protected override void EnableInput(bool enable)
        {
            if (enable)
            {
                questAction.Enable();
                closeAction.Enable();
                questAction.performed += OnQuestKey;
                closeAction.performed += OnCloseKey;
            }
            else
            {
                questAction.performed -= OnQuestKey;
                closeAction.performed -= OnCloseKey;
                questAction.Disable();
                closeAction.Disable();
            }
        }

        private void OnQuestKey(InputAction.CallbackContext _) => OnQuest();
        private void OnCloseKey(InputAction.CallbackContext _) => OnClose();
    }
}
