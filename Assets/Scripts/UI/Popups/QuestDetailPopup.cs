using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.UI.Framework;
using ProjectS.Managers;
using ProjectS.Items;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 트래커의 카드를 클릭했을 때 뜨는 상세 팝업. 제목·상세 스토리·보상을 보여준다.
    ///
    /// 배치: 트래커와 같은 캔버스 아래 아무 곳에나 둔다. UIManager의 <i>자식</i>일 필요가 없다 —
    /// <see cref="QuestTrackerHud"/>가 UIManager.RegisterPopup으로 등록시키기 때문이다
    /// (UIManager는 자기 자식만 수집해서, 나중에 로드되는 씬의 팝업은 스스로 등록해야 한다).
    /// 여는 쪽은 <see cref="Setup"/>으로 내용을 먼저 채운 뒤 ShowPopup을 부른다.
    ///
    /// 보상은 텍스트가 아니라 아이콘 칸(<see cref="NpcRewardSlot"/>)으로 표시한다 — 대화창 보상 미리보기
    /// (<c>DialogueManager</c>)와 같은 방식이라, 아이콘·이름·수량 표기가 두 화면에서 일관된다.
    /// </summary>
    public class QuestDetailPopup : BasePopup
    {
        [Header("내용")]
        [SerializeField] private TMP_Text titleText;     // 퀘스트 제목
        [SerializeField] private TMP_Text storyText;     // 퀘스트 상세 스토리

        [Header("보상")]
        [Tooltip("보상 칸이 담기는 부모.")]
        [SerializeField] private RectTransform rewardContent;
        [Tooltip("복제해 쓸 보상 칸 프리팹.")]
        [SerializeField] private NpcRewardSlot rewardSlotPrefab;
        [SerializeField] private Sprite goldIcon;
        [SerializeField] private Sprite expIcon;
        [SerializeField] private Sprite itemIcon;
        [SerializeField] private Sprite skillIcon;

        [Header("닫기 / 포기")]
        [Tooltip("팝업 우상단 닫기(X) 버튼. 조작을 몰라도 눈으로 보이는 유일한 닫기 수단이라 넣어 둔다.")]
        [SerializeField] private Button closeButton;

        [Tooltip("이 퀘스트를 포기한다. 되돌릴 수 없는 행동이라 확인 대화상자를 한 번 거친 뒤 진행 목록에서 제거된다.")]
        [SerializeField] private Button abandonButton;

        [Tooltip("팝업 바깥을 클릭하면 닫는다. 전체화면 블로커를 깔지 않으므로 다른 UI 클릭은 그대로 통과한다.")]
        [SerializeField] private bool closeOnClickOutside = true;

        // 팝업을 연(또는 다른 카드로 갈아탄) 그 클릭이 곧바로 '바깥 클릭'으로 잡히는 것을 막는다.
        // 카드는 팝업 바깥에 있으므로 이 가드가 없으면 열리는 즉시 닫힌다.
        private int contentFrame = -1;

        // 이 팝업을 연 카드. 바깥 클릭 판정에서 이 영역을 제외해, 같은 카드를 다시 눌렀을 때
        // 카드의 버튼이 토글로 처리할 수 있게 남겨 둔다(연결선이 없어진 뒤에도 이 용도로 필요하다).
        private RectTransform target;

        // 재사용하는 보상 칸 풀(보상 수에 맞춰 켜고, 남으면 끈다).
        private readonly List<NpcRewardSlot> rewardSlots = new();

        // Setup마다 증가시키는 세대 토큰. 아이템 아이콘 비동기 로드가 늦게 끝났을 때, 그 사이 팝업이 닫혔거나
        // 다른 퀘스트로 갈아탔으면(세대 불일치) 지난 슬롯에 아이콘을 덮어쓰지 않게 하는 가드.
        private int rewardGeneration;

        /// <summary>지금 이 팝업이 보여주고 있는 퀘스트. 같은 카드를 다시 눌렀을 때 닫기 판단에 쓴다.</summary>
        public QuestData Quest { get; private set; }

        /// <summary>
        /// 어떤 경로로든 팝업이 닫힐 때 발행(X 버튼·Esc·바깥 클릭·트래커 접기 등).
        /// 여는 쪽이 이것만 구독하면 닫기 수단이 몇 개로 늘어나든 동기화 지점이 하나로 유지된다.
        /// </summary>
        public event Action Closed;

        private void OnEnable()
        {
            if (closeButton != null) closeButton.onClick.AddListener(RequestClose);
            if (abandonButton != null) abandonButton.onClick.AddListener(OnAbandonClicked);
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(RequestClose);
            if (abandonButton != null) abandonButton.onClick.RemoveListener(OnAbandonClicked);
        }

        // 포기 버튼: 진행도가 사라지는 되돌릴 수 없는 행동이라 확인 대화상자를 한 번 거친다.
        // 확인까지 기다리는 사이 다른 카드로 갈아타 Quest가 바뀔 수 있어, 지금 보고 있는 퀘스트를 캡처해 둔다.
        private void OnAbandonClicked()
        {
            QuestData quest = Quest;
            if (quest == null) return;

            if (ConfirmDialog.Instance != null)
                ConfirmDialog.Instance.Show($"'{quest.Title}' 퀘스트를 포기하시겠습니까?", () => Abandon(quest));
            else
                Abandon(quest);   // 확인 대화상자가 없는 씬(직접 테스트 등)에서는 바로 포기한다.
        }

        // 실제 포기: 매니저에서 제거하면 OnQuestAbandoned가 트래커 카드를 지운다. 그 과정에서 이 팝업이 이미
        // 닫혔을 수 있으므로(선택된 카드가 사라지면 트래커가 닫는다), 아직 열려 있을 때만 닫기를 요청한다.
        private void Abandon(QuestData quest)
        {
            QuestManager.Instance?.AbandonQuest(quest);
            if (IsVisible) RequestClose();
        }

        // 바깥 클릭 판정. 전체화면 블로커를 깔면 다른 카드로 갈아탈 때 두 번 클릭해야 하고
        // 미니맵·메뉴 버튼까지 먹히므로, 포인터가 팝업 사각형 안인지만 보고 클릭 자체는 통과시킨다.
        private void Update()
        {
            if (!closeOnClickOutside) return;
            if (Time.frameCount <= contentFrame) return;   // 열린 그 클릭은 무시

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            Vector2 screen = mouse.position.ReadValue();

            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, cam))
                return;

            // 이 팝업을 연 카드 위 클릭은 그 카드의 버튼이 토글로 처리한다.
            // 여기서 먼저 닫아 버리면 순서가 이렇게 꼬인다:
            //   누른 프레임: 바깥 클릭으로 판단 → 닫힘 → 트래커의 선택 해제
            //   뗀 프레임  : 버튼 OnClick → 선택이 이미 비어 있어 '다시 열기'로 처리
            // 그래서 같은 카드를 아무리 눌러도 닫히지 않고 계속 열리기만 한다.
            if (target != null && RectTransformUtility.RectangleContainsScreenPoint(target, screen, cam))
                return;

            RequestClose();
        }

        /// <summary>
        /// 표시할 퀘스트와 이 팝업을 연 카드를 지정한다. ShowPopup 호출 전에 먼저 부른다 —
        /// 켜지는 시점에 내용이 이미 채워져 있어야 첫 프레임에 빈 팝업이 보이지 않는다.
        /// </summary>
        /// <param name="quest">표시할 퀘스트</param>
        /// <param name="cardVisual">이 팝업을 연 카드 본체(바깥 클릭 판정에서 제외된다)</param>
        public void Setup(QuestData quest, RectTransform cardVisual)
        {
            if (quest == null) return;

            Quest = quest;
            target = cardVisual;

            if (titleText != null) titleText.text = quest.Title;
            if (storyText != null) storyText.text = quest.Definition.Description;

            PopulateRewards(quest.Definition);

            contentFrame = Time.frameCount;
        }

        // 다음에 열릴 때 이전 카드를 계속 가리키지 않도록 참조를 끊고, 닫혔음을 알린다.
        protected override void OnHide()
        {
            Quest = null;
            target = null;
            rewardGeneration++;   // 진행 중이던 아이콘 로드가 닫힌 뒤 반영되지 않게 세대를 넘긴다.
            Closed?.Invoke();
        }

        // 보상 칸을 보상 수에 맞춰 켜고 채운다(부족하면 늘리고 남으면 끈다).
        // DialogueManager의 보상 미리보기와 같은 풀링·바인딩 방식이다.
        private void PopulateRewards(QuestTable definition)
        {
            List<QuestRewardData> rewards = definition.Rewards;
            int count = rewards != null ? rewards.Count : 0;
            int generation = ++rewardGeneration;

            while (rewardSlots.Count < count && rewardSlotPrefab != null && rewardContent != null)
                rewardSlots.Add(Instantiate(rewardSlotPrefab, rewardContent));

            for (int i = 0; i < rewardSlots.Count; i++)
            {
                if (i < count)
                {
                    rewardSlots[i].gameObject.SetActive(true);
                    QuestRewardData reward = rewards[i];
                    rewardSlots[i].Bind(RewardName(reward), RewardAmount(reward), RewardIcon(reward.Type));

                    // 어드레서블 아이콘이 있는 보상은 실제 아이콘을 로드해 기본(정적) 아이콘을 덮어쓴다.
                    // (주소가 없는 골드·경험치 등은 no-op — 기본 아이콘 그대로.)
                    LoadRewardIcon(reward, rewardSlots[i], generation);
                }
                else
                {
                    rewardSlots[i].gameObject.SetActive(false);
                }
            }
        }

        // 보상 아이콘을 캐싱 로더(ItemIconLoader)로 로드해 슬롯에 덮어쓴다. 로더가 주소별로 핸들을
        // 1개만 잡아 여러 UI와 공유하므로 여기선 핸들을 직접 관리하지 않는다. 로드가 끝났을 때 팝업이 닫혔거나
        // 다른 퀘스트로 갈아탔으면(세대 불일치) 반영하지 않는다. 주소가 없는 보상은 조용히 빠져나간다.
        private async void LoadRewardIcon(QuestRewardData reward, NpcRewardSlot slot, int generation)
        {
            string address = ResolveRewardIconAddress(reward);
            if (string.IsNullOrEmpty(address)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (generation != rewardGeneration || slot == null) return;

            if (sprite != null) slot.SetIcon(sprite);
        }

        // 보상 아이콘의 어드레서블 주소를 정한다. 우선순위:
        //   1) 보상이 직접 지정한 IconAddress (스킬처럼 테이블에 아이콘이 없는 종류의 유일한 소스이자 보상별 오버라이드)
        //   2) 아이템/직업무기면 아이템 테이블의 정본 아이콘 주소
        //   3) 그 외(골드·경험치·주소 없는 스킬)는 정적 기본 아이콘만 쓰므로 null
        private static string ResolveRewardIconAddress(QuestRewardData reward)
        {
            if (!string.IsNullOrEmpty(reward.IconAddress)) return reward.IconAddress;

            if (reward.Type == QuestRewardType.Item || reward.Type == QuestRewardType.ClassWeapon)
            {
                int itemId = ResolveRewardItemId(reward);
                ItemData item = JsonManager.Instance != null ? JsonManager.Instance.Get<ItemData>(itemId) : null;
                return item != null ? item.IconAddress : null;
            }

            return null;
        }

        // 아이템 아이콘/이름 조회에 쓸 실제 아이템 ID. 직업무기는 현재 캐릭터 직업에 맞는 무기로 먼저 변환한다.
        private static int ResolveRewardItemId(QuestRewardData reward)
        {
            if (reward.Type == QuestRewardType.ClassWeapon)
            {
                int charType = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
                return QuestRewardData.ResolveClassWeaponId(reward.TargetId, charType);
            }

            return reward.TargetId;
        }

        // 보상 이름(칸 라벨). 아이템·직업무기는 테이블 이름, 스킬은 NameKey, 재화는 고정 라벨.
        private static string RewardName(QuestRewardData reward) => reward.Type switch
        {
            QuestRewardType.Gold => "골드",
            QuestRewardType.Exp => "경험치",
            QuestRewardType.Item => ResolveItemName(reward.TargetId),
            QuestRewardType.ClassWeapon => ResolveItemName(ResolveRewardItemId(reward)),
            QuestRewardType.SkillUnlock => ResolveSkillName(reward.TargetId),
            _ => reward.Type.ToString(),
        };

        // 아이템 이름을 테이블에서 조회한다(없거나 로딩 전이면 ID로 폴백).
        private static string ResolveItemName(int itemId)
            => JsonManager.Instance != null && JsonManager.Instance.ItemDict.TryGetValue(itemId, out ItemData item)
                ? item.Name
                : $"아이템 {itemId}";

        // 스킬 해금 보상의 이름을 조회한다. 보상 TargetId는 스킬 번호(2·3·4)일 수 있어 현재 캐릭터 스킬로
        // 환산한 뒤(SkillState.Unlock과 같은 규칙), 표시용 이름은 SkillGrowthTable에서 가져온다
        // (SkillTable의 NameKey는 "SW_SKILL_2" 같은 내부 키라 표시에 부적합). 없으면 NameKey→ID로 폴백.
        private static string ResolveSkillName(int targetId)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null) return $"스킬 {targetId}";

            int skillId = ResolveSkillId(targetId);

            if (json.SkillGrowthDict.TryGetValue(skillId, out SkillGrowthTable row) && !string.IsNullOrEmpty(row.Name))
                return row.Name;

            if (json.SkillDict.TryGetValue(skillId, out SkillTable skill) && !string.IsNullOrEmpty(skill.NameKey))
                return skill.NameKey;

            return $"스킬 {skillId}";
        }

        // 스킬 번호(1~4)면 현재 캐릭터 스킬ID로 환산한다(예: 거너(2) + 3 → 203). 완성 ID(>=100)면 그대로.
        private static int ResolveSkillId(int idOrNumber)
        {
            if (idOrNumber >= 100) return idOrNumber;
            int charId = PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0;
            return charId > 0 ? charId * 100 + idOrNumber : idOrNumber;
        }

        // 수량 표기. 스킬 해금·직업무기는 개수 개념이 없어 비운다(칸에서 자동으로 숨겨진다).
        private static string RewardAmount(QuestRewardData reward) => reward.Type switch
        {
            QuestRewardType.SkillUnlock => string.Empty,
            QuestRewardType.ClassWeapon => string.Empty,
            _ => $"x {reward.Amount}",
        };

        // 종류별 기본 아이콘(아이템·직업무기는 실제 아이콘 로드 전까지 이 아이콘을 보인다).
        private Sprite RewardIcon(QuestRewardType type) => type switch
        {
            QuestRewardType.Gold => goldIcon,
            QuestRewardType.Exp => expIcon,
            QuestRewardType.Item => itemIcon,
            QuestRewardType.ClassWeapon => itemIcon,
            QuestRewardType.SkillUnlock => skillIcon,
            _ => null,
        };
    }
}
