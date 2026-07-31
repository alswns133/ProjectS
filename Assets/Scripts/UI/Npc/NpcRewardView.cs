using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using ProjectS.NPCs;
using ProjectS.Data;

namespace ProjectS.UI
{
    /// <summary>
    /// NPC 보상 화면. 완료 가능 퀘스트를 반납할 때(반납 대화가 끝난 뒤) 보상 목록을 보여주고,
    /// 수령(Space)으로 반납·지급을, 뒤로(Z)로 목록 복귀를 한다.
    /// 실제 보상 지급은 QuestRewardGranter(QuestEvents.OnQuestCompleted 구독)가 처리한다.
    /// 컨트롤러의 화면이 Reward일 때만 켜진다(공유 뷰, 씬에 하나).
    /// </summary>
    public class NpcRewardView : NpcScreenViewBase
    {
        protected override NpcScreen Screen => NpcScreen.Reward;

        [Header("표시")]
        [Tooltip("퀘스트명(선택).")]
        [SerializeField] private TMP_Text titleText;
        [Tooltip("보상 칸이 담기는 부모(세로/그리드 레이아웃 권장).")]
        [SerializeField] private RectTransform content;
        [Tooltip("복제해 쓸 보상 칸 프리팹.")]
        [SerializeField] private NpcRewardSlot slotPrefab;

        [Header("보상 아이콘(종류별, 선택)")]
        [SerializeField] private Sprite goldIcon;
        [SerializeField] private Sprite expIcon;
        [SerializeField] private Sprite itemIcon;
        [SerializeField] private Sprite skillIcon;

        [Header("버튼")]
        [SerializeField] private Button claimButton;   // 수령(Space) — 반납·지급
        [SerializeField] private Button backButton;    // 뒤로(Z) — 목록으로
        [SerializeField] private Button closeButton;   // 닫기(Esc) — 상호작용 종료

        [Header("입력 키")]
        [SerializeField] private InputAction claimAction = new InputAction("RewardClaim", InputActionType.Button, "<Keyboard>/space");
        [SerializeField] private InputAction backAction = new InputAction("RewardBack", InputActionType.Button, "<Keyboard>/z");
        [SerializeField] private InputAction closeAction = new InputAction("RewardClose", InputActionType.Button, "<Keyboard>/escape");

        // 보상 칸 풀(재사용). 보상 수에 맞춰 켜고 남는 건 끈다.
        private readonly List<NpcRewardSlot> slots = new();

        protected override void Awake()
        {
            base.Awake();
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);
            if (backButton != null) backButton.onClick.AddListener(OnBack);
            if (closeButton != null) closeButton.onClick.AddListener(OnClose);
        }

        protected override void OnShow()
        {
            QuestData quest = Controller.RewardQuest;
            if (titleText != null) titleText.text = quest != null ? quest.Title : string.Empty;
            Populate(quest);
        }

        // 완료 퀘스트의 보상들로 칸을 채운다. 부족하면 늘리고 남으면 끈다.
        private void Populate(QuestData quest)
        {
            IReadOnlyList<QuestRewardData> rewards = quest != null ? quest.Definition.Rewards : null;
            int count = rewards != null ? rewards.Count : 0;

            while (slots.Count < count && slotPrefab != null && content != null)
                slots.Add(Instantiate(slotPrefab, content));

            for (int i = 0; i < slots.Count; i++)
            {
                if (i < count)
                {
                    slots[i].gameObject.SetActive(true);
                    QuestRewardData reward = rewards[i];
                    slots[i].Bind(RewardName(reward), RewardAmount(reward), RewardIcon(reward.Type));
                }
                else
                {
                    slots[i].gameObject.SetActive(false);
                }
            }
        }

        private void OnClaim()
        {
            if (Controller != null) Controller.ClaimReward();
        }

        private void OnBack()
        {
            if (Controller != null) Controller.CloseReward();
        }

        private void OnClose()
        {
            if (Controller != null) Controller.CloseInteraction();
        }

        protected override void EnableInput(bool enable)
        {
            if (enable)
            {
                claimAction.Enable();
                backAction.Enable();
                closeAction.Enable();

                claimAction.performed += OnClaimKey;
                backAction.performed += OnBackKey;
                closeAction.performed += OnCloseKey;
            }
            else
            {
                claimAction.performed -= OnClaimKey;
                backAction.performed -= OnBackKey;
                closeAction.performed -= OnCloseKey;

                claimAction.Disable();
                backAction.Disable();
                closeAction.Disable();
            }
        }

        private void OnClaimKey(InputAction.CallbackContext _) => OnClaim();
        private void OnBackKey(InputAction.CallbackContext _) => OnBack();
        private void OnCloseKey(InputAction.CallbackContext _) => OnClose();

        // ---- 표시 텍스트·아이콘 ----

        private static string RewardName(QuestRewardData reward) => reward.Type switch
        {
            QuestRewardType.Gold => "골드",
            QuestRewardType.Exp => "경험치",
            QuestRewardType.Item => $"아이템 {reward.TargetId}",
            QuestRewardType.SkillUnlock => "스킬 해금",
            _ => reward.Type.ToString(),
        };

        private static string RewardAmount(QuestRewardData reward)
            => reward.Type == QuestRewardType.SkillUnlock ? string.Empty : $"x{reward.Amount}";

        private Sprite RewardIcon(QuestRewardType type) => type switch
        {
            QuestRewardType.Gold => goldIcon,
            QuestRewardType.Exp => expIcon,
            QuestRewardType.Item => itemIcon,
            QuestRewardType.SkillUnlock => skillIcon,
            _ => null,
        };
    }
}
