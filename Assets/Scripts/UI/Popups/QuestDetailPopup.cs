using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 퀘스트 트래커의 카드를 클릭했을 때 뜨는 상세 팝업. 제목·상세 스토리·보상을 보여준다.
    /// 어느 카드에서 열렸는지 보이도록, 팝업과 그 카드를 잇는 연결선을 함께 그린다.
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

        [Header("연결선")]
        [Tooltip("팝업과 카드를 잇는 가로선 이미지. pivot을 (0, 0.5)로 두면 오른쪽으로 자란다.")]
        [SerializeField] private RectTransform connector;

        [Tooltip("연결선이 시작될 지점(보통 팝업 오른쪽 가장자리의 빈 오브젝트).")]
        [SerializeField] private RectTransform connectorOrigin;

        [Header("닫기")]
        [Tooltip("팝업 우상단 닫기(X) 버튼. 조작을 몰라도 눈으로 보이는 유일한 닫기 수단이라 넣어 둔다.")]
        [SerializeField] private Button closeButton;

        [Tooltip("팝업 바깥을 클릭하면 닫는다. 전체화면 블로커를 깔지 않으므로 다른 UI 클릭은 그대로 통과한다.")]
        [SerializeField] private bool closeOnClickOutside = true;

        // 팝업을 연(또는 다른 카드로 갈아탄) 그 클릭이 곧바로 '바깥 클릭'으로 잡히는 것을 막는다.
        // 카드는 팝업 바깥에 있으므로 이 가드가 없으면 열리는 즉시 닫힌다.
        private int contentFrame = -1;

        // 연결선이 따라갈 카드. 스크롤로 카드가 움직여도 선이 붙어 있게 매 프레임 추적한다.
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

            if (RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)transform, mouse.position.ReadValue(), cam))
                return;

            RequestClose();
        }

        /// <summary>
        /// 표시할 퀘스트와 연결선이 가리킬 카드를 지정한다. ShowPopup 호출 전에 먼저 부른다 —
        /// 켜지는 시점에 내용이 이미 채워져 있어야 첫 프레임에 빈 팝업이 보이지 않는다.
        /// </summary>
        /// <param name="quest">표시할 퀘스트</param>
        /// <param name="cardVisual">연결선이 가리킬 카드 본체(없으면 선을 숨긴다)</param>
        public void Setup(QuestData quest, RectTransform cardVisual)
        {
            if (quest == null) return;

            Quest = quest;
            target = cardVisual;

            if (titleText != null) titleText.text = quest.Title;
            if (storyText != null) storyText.text = quest.Definition.Description;
            if (rewardText != null) rewardText.text = BuildRewards(quest.Definition);

            if (connector != null) connector.gameObject.SetActive(cardVisual != null);
            contentFrame = Time.frameCount;
            UpdateConnector();
        }

        // 다음에 열릴 때 이전 카드를 계속 가리키지 않도록 참조를 끊고, 닫혔음을 알린다.
        protected override void OnHide()
        {
            Quest = null;
            target = null;
            Closed?.Invoke();
        }

        // 카드는 스크롤·선택 연출로 계속 움직이므로 위치를 매 프레임 다시 잡는다.
        // LateUpdate인 이유: 레이아웃과 스크롤이 이번 프레임 위치를 확정한 뒤에 읽어야 선이 한 프레임 밀리지 않는다.
        private void LateUpdate() => UpdateConnector();

        private void UpdateConnector()
        {
            if (connector == null || connectorOrigin == null || target == null) return;

            // 카드 왼쪽 가장자리(세로 중앙)를 연결점으로 삼는다.
            Vector3 cardEdge = target.TransformPoint(new Vector3(target.rect.xMin, 0f, 0f));

            RectTransform parent = (RectTransform)connector.parent;
            Vector2 from = parent.InverseTransformPoint(connectorOrigin.position);
            Vector2 to = parent.InverseTransformPoint(cardEdge);

            Vector2 delta = to - from;
            connector.anchoredPosition = from;
            connector.sizeDelta = new Vector2(delta.magnitude, connector.sizeDelta.y);
            connector.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // 보상을 종류별로 한 줄씩 쌓는다. 아이템·스킬은 ID만 있으므로 이름 조회는 추후 아이템 테이블 연동 시 붙인다.
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
                        sb.Append("아이템 ").Append(reward.TargetId).Append(" x").Append(reward.Amount);
                        break;
                    case QuestRewardType.SkillUnlock:
                        sb.Append("스킬 해금 ").Append(reward.TargetId);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
