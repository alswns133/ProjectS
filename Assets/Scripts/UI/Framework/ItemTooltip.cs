using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 아이템 슬롯에 마우스를 올리면 뜨는 재사용 단일 정보창(툴팁). 토글 창(BasePopup)이 아니라
    /// 어느 슬롯이든 <see cref="Instance"/>로 불러 쓰는 공용 위젯이라 인벤·장비창·상점이 함께 쓴다.
    ///
    /// 레이아웃은 "공통 프레임 + 타입별 섹션 토글"이다.
    ///  - 공통(장비·소모품 모두): 이름 · 아이콘 · 등급 · 설명 · 판매가.
    ///  - 장비 전용 섹션: 직업/요구레벨(equipMeta) · 주스탯(mainStat).
    ///  - 소모품 전용 섹션: 효과(consumable, 회복량/쿨다운).
    /// 스샷에 있으나 아직 데이터가 없는 항목(귀속·세트효과·옵션 실제값)은 이번엔 배제한다
    /// (인스턴스 옵션 롤·세트 데이터가 생기면 섹션을 추가한다).
    ///
    /// 위치 규칙: 커서 지점에 <b>딱 붙되</b>, 커서가 화면 4구역 중 어디에 있느냐에 따라 화면 중앙 쪽으로 펼친다
    /// (가까운 화면 가장자리 반대로 → 화면 밖으로 잘리지 않게). 한 번 뜨면 그 자리에 고정(마우스를 따라가지 않음).
    ///
    /// 배치: 메인 캔버스의 직속 자식(전체화면·pivot 중앙)으로 두고, 자식 Graphic들의 raycastTarget은 꺼 둔다.
    /// </summary>
    public class ItemTooltip : MonoBehaviour
    {
        /// <summary>전역 접근점. 슬롯이 타입 참조 없이 부를 수 있게 한다.</summary>
        public static ItemTooltip Instance { get; private set; }

        [Header("루트")]
        [SerializeField] private RectTransform tooltipRect;   // 이동/피벗 대상(비우면 자기 루트)

        [Header("공통 프레임")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text gradeText;
        [SerializeField] private GameObject levelBadge;       // 우상단 레벨 뱃지(소모품처럼 Level 0이면 숨김)
        [SerializeField] private TMP_Text levelBadgeText;
        [SerializeField] private GameObject descSection;      // 설명 묶음(없으면 숨김)
        [SerializeField] private TMP_Text descText;
        [SerializeField] private TMP_Text sellPriceText;

        [Header("장비 전용 섹션")]
        [SerializeField] private GameObject equipMetaSection; // 직업 + 요구레벨
        [SerializeField] private TMP_Text classText;
        [SerializeField] private TMP_Text reqLevelText;
        [SerializeField] private GameObject mainStatSection;  // 주스탯 + 강화
        [SerializeField] private TMP_Text mainStatText;       // 예: "방어도 900"
        [SerializeField] private TMP_Text enhanceText;        // 예: "(+0)"

        [Header("소모품 전용 섹션")]
        [SerializeField] private GameObject consumableSection;
        [SerializeField] private TMP_Text effectText;         // 회복량/쿨다운

        [Header("등급 색")]
        [SerializeField] private Color normalColor = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color magicColor = new Color(0.35f, 0.6f, 1f);
        [SerializeField] private Color rareColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color relicColor = new Color(1f, 0.5f, 0.15f);

        private RectTransform parentRect;
        private Canvas canvas;
        private ItemData currentItem;   // 아이콘 async 로드 stale 판정용

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (tooltipRect == null) tooltipRect = (RectTransform)transform;
            parentRect = tooltipRect.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();

            // 위치 계산을 부모 중앙 기준으로 단순화하기 위해 앵커를 중앙으로 고정한다.
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);

            gameObject.SetActive(false);
        }

        /// <summary>장비 정보를 커서 지점에 띄운다.</summary>
        /// <param name="equip">표시할 장비 인스턴스</param>
        /// <param name="screenPos">커서 스크린 좌표(PointerEventData.position)</param>
        public void ShowEquipment(EquipmentInstance equip, Vector2 screenPos)
        {
            if (equip?.Item == null) return;

            ItemData item = equip.Item;
            EquipmentData e = equip.Equipment;

            FillCommon(item);

            // 장비 섹션 ON, 소모품 섹션 OFF.
            SetActiveSafe(equipMetaSection, true);
            SetActiveSafe(mainStatSection, e != null);
            SetActiveSafe(consumableSection, false);

            if (e != null)
            {
                if (classText != null) classText.text = ClassLabel(e.EquipSlot, e.WeaponType);
                if (reqLevelText != null) reqLevelText.text = $"요구 레벨 {item.Level}";

                if (mainStatText != null) mainStatText.text = $"{MainStatLabel(e.MainStatType)} {e.MainStatBase}";
                if (enhanceText != null) enhanceText.text = $"(+{equip.EnhanceStep})";
            }

            ShowAt(screenPos);
        }

        /// <summary>스택형 아이템(소비품·재료) 정보를 커서 지점에 띄운다.</summary>
        /// <param name="stack">표시할 스택</param>
        /// <param name="screenPos">커서 스크린 좌표(PointerEventData.position)</param>
        public void ShowStack(ItemStack stack, Vector2 screenPos)
        {
            if (stack?.Item == null) return;

            FillCommon(stack.Item);

            // 장비 섹션 OFF, 소모품이면 효과 섹션 ON(재료는 효과 없음 → OFF).
            SetActiveSafe(equipMetaSection, false);
            SetActiveSafe(mainStatSection, false);
            SetActiveSafe(consumableSection, stack.IsConsumable);

            if (stack.IsConsumable && effectText != null)
            {
                ConsumableData c = stack.Consumable;
                effectText.text = c.DurationSec > 0f
                    ? $"회복: 초당 {c.HealAmount} · {c.DurationSec}초 (총 {c.TotalHeal})" +
                      (c.CooldownSec > 0f ? $"\n쿨다운: {c.CooldownSec}초" : string.Empty)
                    : $"회복: {c.HealAmount} (즉시)" +
                      (c.CooldownSec > 0f ? $"\n쿨다운: {c.CooldownSec}초" : string.Empty);
            }

            ShowAt(screenPos);
        }

        /// <summary>정보창을 숨긴다(슬롯에서 마우스가 벗어나거나 창이 닫힐 때).</summary>
        public void Hide()
        {
            currentItem = null;
            if (this != null) gameObject.SetActive(false);
        }

        // 공통 프레임(이름·등급·레벨뱃지·아이콘·설명·판매가)을 채운다.
        private void FillCommon(ItemData item)
        {
            currentItem = item;
            Color gradeColor = GradeColor(item.Grade);

            if (nameText != null)
            {
                nameText.text = item.Name;
                nameText.color = gradeColor;
            }
            if (gradeText != null)
            {
                gradeText.text = GradeLabel(item.Grade);
                gradeText.color = gradeColor;
            }

            // 레벨 뱃지: 요구 레벨이 있는 아이템(장비)만. 소모품·재료는 Level 0이라 숨긴다.
            bool hasLevel = item.Level > 0;
            SetActiveSafe(levelBadge, hasLevel);
            if (hasLevel && levelBadgeText != null) levelBadgeText.text = item.Level.ToString();

            bool hasDesc = !string.IsNullOrWhiteSpace(item.Description);
            SetActiveSafe(descSection, hasDesc);
            if (hasDesc && descText != null) descText.text = item.Description;

            if (sellPriceText != null) sellPriceText.text = $"판매가 {item.SellPrice}";

            LoadIcon(item);
        }

        // 아이콘을 async 로드. 대기 중 다른 아이템으로 바뀌면(빠른 hover 이동) 늦게 온 스프라이트는 버린다.
        private async void LoadIcon(ItemData item)
        {
            if (icon == null) return;

            icon.enabled = false;
            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);

            if (this == null || icon == null) return;
            if (!ReferenceEquals(currentItem, item)) return;   // 다른 아이템으로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        private void ShowAt(Vector2 screenPos)
        {
            gameObject.SetActive(true);
            Position(screenPos);
        }

        // 커서가 있는 화면 구역에 맞춰 피벗을 정하고(중앙 쪽으로 펼침) 커서 지점에 붙인다.
        private void Position(Vector2 screenPos)
        {
            bool left = screenPos.x < Screen.width * 0.5f;
            bool top = screenPos.y > Screen.height * 0.5f;

            // 커서가 있는 구역 쪽 모서리를 커서에 붙인다 → 반대(중앙) 방향으로 몸통이 펼쳐진다.
            //   좌상단 커서 → pivot(0,1): 아래+오른쪽 / 우하단 커서 → pivot(1,0): 위+왼쪽
            tooltipRect.pivot = new Vector2(left ? 0f : 1f, top ? 1f : 0f);

            if (parentRect != null)
            {
                Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera
                    : null;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, cam, out Vector2 local))
                    tooltipRect.anchoredPosition = local;
            }

            tooltipRect.SetAsLastSibling();   // 다른 UI 위로
        }

        private static void SetActiveSafe(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value) go.SetActive(value);
        }

        // ── 라벨/색 매핑 (플레이어에게 보이는 한글 표기) ──────────────────────────
        private Color GradeColor(ItemGrade grade) => grade switch
        {
            ItemGrade.Magic => magicColor,
            ItemGrade.Rare => rareColor,
            ItemGrade.Relic => relicColor,
            _ => normalColor,
        };

        private static string GradeLabel(ItemGrade grade) => grade switch
        {
            ItemGrade.Magic => "매직",
            ItemGrade.Rare => "레어",
            ItemGrade.Relic => "유물",
            _ => "노말",
        };

        // 직업 표기: 무기는 무기종류에서 직업이 갈리고(검=검사·총=거너), 방어구는 직업 제한이 없어 공용.
        private static string ClassLabel(EquipSlot slot, WeaponType weapon)
        {
            if (slot != EquipSlot.Weapon) return "공용";
            return weapon switch
            {
                WeaponType.Sword => "검사",
                WeaponType.Gun => "거너",
                _ => "공용",
            };
        }

        private static string MainStatLabel(MainStatType type) => type switch
        {
            MainStatType.AttackDamage => "공격력",
            MainStatType.Defense => "방어도",
            _ => "-",
        };

        // 플레이 모드 리로드 후에도 남을 수 있는 static 참조를 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;
    }
}
