using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Skills;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 스킬 아이콘에 마우스를 올리면 뜨는 재사용 단일 정보창. 아이템 툴팁(<see cref="ItemTooltip"/>)의 스킬 버전으로,
    /// 스킬창(K)·HUD 스킬 슬롯이 <see cref="Instance"/>로 함께 쓴다. 이름·아이콘·현재레벨/최대레벨·설명을 보여준다.
    /// </summary>
    /// <remarks>
    /// 표시 데이터는 <see cref="SkillGrowthTable"/>(이름/설명/아이콘)와 <see cref="SkillState"/>(현재 레벨)에서 온다.
    /// 위치·pivot 규칙은 ItemTooltip과 동일하다(커서 지점에 붙되 화면 밖으로 안 나가게). 배치: 메인/오버레이 캔버스의
    /// 직속 자식(전체화면·pivot 중앙), 자식 Graphic의 raycastTarget은 꺼 둔다.
    /// </remarks>
    public class SkillTooltip : MonoBehaviour
    {
        /// <summary>전역 접근점. 슬롯이 타입 참조 없이 부른다.</summary>
        public static SkillTooltip Instance { get; private set; }

        [SerializeField] private RectTransform tooltipRect;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text levelText;      // "Lv. 3 / 5"
        [SerializeField] private GameObject descSection;
        [SerializeField] private TMP_Text descText;

        private RectTransform parentRect;
        private Canvas canvas;
        private bool initialized;
        private int currentSkillId;   // 아이콘 async 로드 stale 판정용
        private Component owner;       // 이 툴팁을 띄운 슬롯(그 슬롯이 비활성될 때만 닫는다)

        /// <summary>드래그 중 hover 툴팁이 뜨지 않게 하는 전역 억제 플래그(슬롯 드래그 시작/종료에서 토글).</summary>
        public static bool DragSuppressed { get; set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsureInit();
            gameObject.SetActive(false);
        }

        private void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            if (tooltipRect == null) tooltipRect = (RectTransform)transform;
            parentRect = tooltipRect.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        }

        /// <summary>스킬 정보를 커서 지점에 띄운다(성장행이 없으면 무시).</summary>
        /// <param name="skillId">표시할 스킬ID</param>
        /// <param name="screenPos">커서 스크린 좌표</param>
        /// <param name="owner">이 툴팁을 띄운 슬롯(창이 닫힐 때 이 주인만 닫게 한다)</param>
        public void Show(int skillId, Vector2 screenPos, Component owner = null)
        {
            if (DragSuppressed) return;

            SkillGrowthTable row = JsonManager.Instance != null ? JsonManager.Instance.Get<SkillGrowthTable>(skillId) : null;
            if (row == null) return;

            EnsureInit();
            this.owner = owner;
            currentSkillId = skillId;

            if (nameText != null) nameText.text = row.Name;
            if (levelText != null) levelText.text = $"Lv. {SkillState.GetLevel(skillId)} / {row.MaxLevel}";

            bool hasDesc = !string.IsNullOrWhiteSpace(row.Description);
            if (descSection != null) descSection.SetActive(hasDesc);
            if (hasDesc && descText != null) descText.text = row.Description;

            LoadIcon(skillId, row.IconAddress);

            gameObject.SetActive(true);
            Position(screenPos);
        }

        /// <summary>툴팁을 숨긴다.</summary>
        public void Hide()
        {
            owner = null;
            currentSkillId = 0;
            if (this != null) gameObject.SetActive(false);
        }

        /// <summary>요청한 슬롯이 이 툴팁의 주인일 때만 닫는다(다른 창의 슬롯이 띄운 툴팁은 유지).</summary>
        public void Hide(Component requester)
        {
            if (requester != null && !ReferenceEquals(requester, owner)) return;
            Hide();
        }

        private async void LoadIcon(int skillId, string address)
        {
            if (icon == null) return;

            icon.enabled = false;
            if (string.IsNullOrEmpty(address)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(address);
            if (this == null || icon == null) return;
            if (currentSkillId != skillId) return;   // 다른 스킬로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        // 커서가 있는 화면 구역에 맞춰 pivot을 정하고 커서 지점에 붙인다(ItemTooltip과 동일 규칙).
        private void Position(Vector2 screenPos)
        {
            bool left = screenPos.x < Screen.width * 0.5f;
            bool top = screenPos.y > Screen.height * 0.5f;
            tooltipRect.pivot = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

            if (parentRect != null)
            {
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera
                    : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, cam, out Vector2 local))
                    tooltipRect.anchoredPosition = local;
            }

            tooltipRect.SetAsLastSibling();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            DragSuppressed = false;
        }
    }
}
