using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.UI.Framework;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 트래커의 카드를 클릭했을 때 뜨는 상세 팝업. 제목·상세 스토리·보상을 보여준다.
    ///
    /// 배치: 트래커와 같은 캔버스 아래 아무 곳에나 둔다. UIManager의 <i>자식</i>일 필요가 없다 —
    /// <see cref="QuestTrackerHud"/>가 UIManager.RegisterPopup으로 등록시키기 때문이다
    /// (UIManager는 자기 자식만 수집해서, 나중에 로드되는 씬의 팝업은 스스로 등록해야 한다).
    /// 여는 쪽은 <see cref="Setup"/>으로 내용을 먼저 채운 뒤 ShowPopup을 부른다.
    /// </summary>
    public class QuestDetailPopup : BasePopup
    {
        [Header("내용")]
        [SerializeField] private TMP_Text titleText;     // 퀘스트 제목
        [SerializeField] private TMP_Text storyText;     // 퀘스트 상세 스토리
        [SerializeField] private TMP_Text rewardText;    // 퀘스트 보상

        [Header("닫기")]
        [Tooltip("팝업 우상단 닫기(X) 버튼. 조작을 몰라도 눈으로 보이는 유일한 닫기 수단이라 넣어 둔다.")]
        [SerializeField] private Button closeButton;

        [Tooltip("팝업 바깥을 클릭하면 닫는다. 전체화면 블로커를 깔지 않으므로 다른 UI 클릭은 그대로 통과한다.")]
        [SerializeField] private bool closeOnClickOutside = true;

        // 팝업을 연(또는 다른 카드로 갈아탄) 그 클릭이 곧바로 '바깥 클릭'으로 잡히는 것을 막는다.
        // 카드는 팝업 바깥에 있으므로 이 가드가 없으면 열리는 즉시 닫힌다.
        private int contentFrame = -1;

        // 이 팝업을 연 카드. 바깥 클릭 판정에서 이 영역을 제외해, 같은 카드를 다시 눌렀을 때
        // 카드의 버튼이 토글로 처리할 수 있게 남겨 둔다(연결선이 없어진 뒤에도 이 용도로 필요하다).
        private RectTransform target;

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
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(RequestClose);
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
            if (rewardText != null) rewardText.text = BuildRewards(quest.Definition);

            contentFrame = Time.frameCount;
        }

        // 다음에 열릴 때 이전 카드를 계속 가리키지 않도록 참조를 끊고, 닫혔음을 알린다.
        protected override void OnHide()
        {
            Quest = null;
            target = null;
            Closed?.Invoke();
        }

        // 보상을 종류별로 한 줄씩 쌓는다. 아이템은 ItemData.Name, 스킬은 SkillTable.NameKey를 테이블에서
        // 조회해 이름으로 표시한다(정의가 없거나 로딩 전이면 ID로 폴백).
        private static string BuildRewards(QuestTable definition)
        {
            if (definition.Rewards == null || definition.Rewards.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var reward in definition.Rewards)
            {
                if (sb.Length > 0) sb.Append('\n');

                switch (reward.Type)
                {
                    case QuestRewardType.Gold:
                        sb.Append("골드 ").Append(reward.Amount);
                        break;
                    case QuestRewardType.Exp:
                        sb.Append("경험치 ").Append(reward.Amount);
                        break;
                    case QuestRewardType.Item:
                        // 아이템 정의가 있으면 이름을, 없으면(로딩 전·삭제된 ID) ID로 폴백한다(NRE 방지).
                        string itemName = JsonManager.Instance != null
                                          && JsonManager.Instance.ItemDict.TryGetValue(reward.TargetId, out ItemData itemData)
                            ? itemData.Name
                            : reward.TargetId.ToString();
                        sb.Append("아이템 ").Append(itemName).Append(" x").Append(reward.Amount);
                        break;
                    case QuestRewardType.SkillUnlock:
                        // 스킬 정의가 있으면 이름(NameKey)을, 없으면 ID로 폴백한다.
                        string skillName = JsonManager.Instance != null
                                           && JsonManager.Instance.SkillDict.TryGetValue(reward.TargetId, out SkillTable skill)
                                           && !string.IsNullOrEmpty(skill.NameKey)
                            ? skill.NameKey
                            : reward.TargetId.ToString();
                        sb.Append("스킬 해금 ").Append(skillName);
                        break;
                    case QuestRewardType.ClassWeapon:
                        int charType = PlayerManager.Instance != null
                            ? PlayerManager.Instance.CurrentCharacterId : 0;
                        // 먼저 직업 무기로 변환
                        int weaponId = QuestRewardData.ResolveClassWeaponId(reward.TargetId, charType);
                        string weaponName = JsonManager.Instance != null 
                            && JsonManager.Instance.ItemDict.TryGetValue(weaponId, out ItemData weaponData)
                            ? weaponData.Name
                            : weaponId.ToString();
                        sb.Append("무기 ").Append(weaponName);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
