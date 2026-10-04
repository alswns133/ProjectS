using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Scenes;

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
    /// 스샷에 있으나 아직 데이터가 없는 항목(귀속·세트효과)은 이번엔 배제한다(세트 데이터가 생기면 섹션을 추가한다).
    ///
    /// 진입점은 셋이다: 보유 장비(<see cref="ShowEquipment"/>), 보유 스택(<see cref="ShowStack"/>),
    /// 아직 받지 않은 아이템 정의(<see cref="ShowDefinition"/> — 상점 구입 목록·퀘스트 보상 미리보기).
    /// 정의는 롤 전이라 주 스탯은 범위, 옵션은 개수로만 보여준다.
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
        [SerializeField] private GameObject descSection;      // 설명 묶음(없으면 숨김)
        [SerializeField] private TMP_Text descText;
        [SerializeField] private TMP_Text sellPriceText;

        [Header("장비 전용 섹션")]
        [SerializeField] private GameObject equipMetaSection; // 직업 + 요구레벨
        [SerializeField] private TMP_Text classText;
        [SerializeField] private TMP_Text reqLevelText;
        [SerializeField] private GameObject mainStatSection;  // 주스탯 + 강화
        [SerializeField] private TMP_Text mainStatText;       // 예: "방어도 900"
        [SerializeField] private TMP_Text enhanceText;        // 예: "+9" (0강이면 숨김)

        [Header("장비 옵션 섹션")]
        [SerializeField] private GameObject optionSection;    // 옵션 묶음(장비 전용, 옵션 없으면 통째로 끔)
        [SerializeField] private TMP_Text optionText;         // 롤된 옵션 목록

        [Header("소모품 전용 섹션")]
        [SerializeField] private GameObject consumableSection;
        [SerializeField] private TMP_Text effectText;         // 회복량/쿨다운

        [Header("블록체인 표시 섹션 (보기 전용 목업)")]
        [Tooltip("온체인 정보 묶음. 비우면 이 기능 전체가 꺼진다(기존 툴팁 프리팹은 그대로 동작).")]
        [SerializeField] private GameObject chainSection;
        [SerializeField] private TMP_Text chainText;
        [Tooltip("표시용 가짜 컨트랙트 주소. 실제 체인과 연결되지 않는다.")]
        [SerializeField] private string chainContract = "0x5e7a1c90b3d24f8e6a0c7b1d9f2e3a4b5c6d7e8f";
        [SerializeField] private string chainNetwork = "ProjectS Testnet";

        [Header("비교(장착 중 아이템)")]
        [Tooltip("이 툴팁 자체가 비교용 보조 패널이면 체크. 체크된 패널은 싱글톤(Instance)으로 등록되지 않고 또 다른 비교를 띄우지 않는다.")]
        [SerializeField] private bool isComparePanel;
        [Tooltip("메인 툴팁에만 지정. 장비 hover 시 같은 부위 착용 아이템을 옆에 띄우는 보조 툴팁(isComparePanel=true인 복제본).")]
        [SerializeField] private ItemTooltip comparePanel;

        private RectTransform parentRect;
        private Canvas canvas;
        private ItemData currentItem;   // 아이콘 async 로드 stale 판정용
        private bool initialized;       // EnsureInit 1회 가드(비활성으로 시작한 비교 패널도 안전 초기화)
        private Component owner;         // 이 툴팁을 띄운 슬롯. 그 슬롯이 비활성(창 닫힘)될 때만 이 툴팁을 닫게 한다.

        /// <summary>
        /// 드래그 중에는 hover로 툴팁이 뜨지 않게 하는 전역 억제 플래그. 슬롯 드래그 시작/종료에서 켜고 끈다.
        /// (드래그 중 다른 슬롯 위를 지날 때 PointerEnter가 그 아이템 툴팁을 띄우는 것을 막는다.)
        /// </summary>
        public static bool DragSuppressed { get; set; }

        private void Awake()
        {
            // 비교 패널은 싱글톤(Instance)으로 등록하지 않는다 — 메인 툴팁이 참조(comparePanel)로만 부린다.
            if (!isComparePanel)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
            }

            EnsureInit();
            gameObject.SetActive(false);
        }

        // 위치 계산에 필요한 참조를 1회 준비한다. 비활성으로 시작해 Awake를 못 돌린 비교 패널도
        // 첫 사용 시점(FillEquipment/PlaceBeside)에 안전하게 초기화되도록 별도 메서드로 뺐다.
        private void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            if (tooltipRect == null) tooltipRect = (RectTransform)transform;
            parentRect = tooltipRect.parent as RectTransform;
            canvas = GetComponentInParent<Canvas>();

            // 위치 계산을 부모 중앙 기준으로 단순화하기 위해 앵커를 중앙으로 고정한다.
            tooltipRect.anchorMin = tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
        }

        /// <summary>장비 정보를 커서 지점에 띄운다.</summary>
        /// <param name="equip">표시할 장비 인스턴스</param>
        /// <param name="screenPos">커서 스크린 좌표(PointerEventData.position)</param>
        public void ShowEquipment(EquipmentInstance equip, Vector2 screenPos, Component owner = null)
        {
            if (equip?.Item == null) return;
            if (DragSuppressed) return;   // 드래그 중엔 hover 툴팁을 띄우지 않는다

            this.owner = owner;

            FillEquipment(equip);
            ShowAt(screenPos);

            // 장비는 같은 부위 착용 중인 아이템을 옆에 나란히 띄워 비교하게 한다.
            // (비교 패널 자신은 isComparePanel이라 여기서 또 비교를 띄우지 않아 재귀가 없다.)
            if (!isComparePanel) UpdateCompare(equip.Equipment, equip);
        }

        /// <summary>
        /// 인스턴스가 없는 아이템 정의만으로 정보를 띄운다. 상점 구입 목록·퀘스트 보상 미리보기처럼
        /// "아직 받지 않은 아이템"을 보여줄 때 쓴다. 장비는 롤 전이라 주 스탯은 범위로, 옵션은 개수로만 보여주고,
        /// 장비가 아니면(소비품·재료) <see cref="ShowStack"/>과 같은 화면이 된다.
        /// </summary>
        /// <param name="item">표시할 아이템 행</param>
        /// <param name="screenPos">커서 스크린 좌표(PointerEventData.position)</param>
        /// <param name="owner">툴팁을 띄운 슬롯. 그 슬롯이 비활성될 때만 닫히게 한다</param>
        public void ShowDefinition(ItemData item, Vector2 screenPos, Component owner = null)
        {
            if (item == null) return;
            if (DragSuppressed) return;

            JsonManager json = JsonManager.Instance;
            EquipmentData equip = json != null ? json.Get<EquipmentData>(item.Index) : null;

            // 장비가 아니면 스택 화면을 그대로 쓴다. 소비품 행이 있으면 회복/쿨다운까지 뜨고, 재료는 null이라 효과 섹션이 꺼진다.
            if (equip == null)
            {
                ConsumableData consumable = json != null ? json.Get<ConsumableData>(item.Index) : null;
                ShowStack(new ItemStack(item, consumable), screenPos, owner);
                return;
            }

            this.owner = owner;

            FillEquipmentPreview(item, equip);
            ShowAt(screenPos);

            // 상점에서 가장 쓸모 있는 정보라 미리보기에도 착용 중 장비 비교를 띄운다(비교할 인스턴스는 없으므로 null).
            if (!isComparePanel) UpdateCompare(equip, null);
        }

        // 장비 내용을 프레임에 채운다(위치는 잡지 않음). 메인 툴팁과 비교 패널이 공용으로 쓴다.
        private void FillEquipment(EquipmentInstance equip)
        {
            EnsureInit();

            ItemData item = equip.Item;
            EquipmentData e = equip.Equipment;

            FillEquipmentFrame(item, e);

            if (e != null)
            {
                // 롤값(+0 기준)이 아니라 강화 보너스까지 반영한 실제 주 스탯을 보여준다
                // (전투 스탯·강화창과 같은 EquipmentStatCalculator 경로 — 강화하면 이 값이 오른다).
                if (mainStatText != null) mainStatText.text = $"{MainStatLabel(e.MainStatType)} {EquipmentStatCalculator.MainStat(equip)}";

                // 0강은 "+0"을 보여주지 않는다(기본값이라 정보량이 없음). 1강 이상일 때만 켠다.
                if (enhanceText != null)
                {
                    bool showEnhance = equip.EnhanceStep > 0;
                    if (showEnhance) enhanceText.text = $"+{equip.EnhanceStep}";
                    SetActiveSafe(enhanceText.gameObject, showEnhance);
                }
            }

            FillOptions(equip.Options);
            FillChain(equip);
        }

        // 롤 전 장비(정의)를 프레임에 채운다. 실제 장비와 프레임은 같고, 롤에 따라 달라지는 칸만 미리보기 표기로 바꾼다.
        // 강화 수치는 FillCommon이 이미 꺼 두므로(미리보기는 항상 +0) 여기서 다시 켜지 않는다.
        private void FillEquipmentPreview(ItemData item, EquipmentData e)
        {
            EnsureInit();

            FillEquipmentFrame(item, e);

            // 주 스탯은 드랍 시 랜덤이라 한 값을 찍으면 "산 것과 다르다"는 오해가 생긴다. 나올 수 있는 범위를 보여준다.
            if (mainStatText != null)
            {
                ItemOptionRoller.MainStatRange(e, out int min, out int max);
                string label = MainStatLabel(e.MainStatType);
                mainStatText.text = min == max ? $"{label} {min}" : $"{label} {min} ~ {max}";
            }

            FillOptionPreview(e.OptionCount);
            FillChainPreview(item.Index);
        }

        // 실제 장비와 미리보기가 공유하는 장비 프레임: 공통 칸 + 장비 섹션 ON/소모품 섹션 OFF + 직업·요구 레벨.
        private void FillEquipmentFrame(ItemData item, EquipmentData e)
        {
            FillCommon(item);

            // 장비 섹션 ON, 소모품 섹션 OFF.
            SetActiveSafe(equipMetaSection, true);
            SetActiveSafe(mainStatSection, e != null);
            SetActiveSafe(consumableSection, false);

            // 장비는 회복 효과가 없다. effectText가 소모품 섹션 밖에 배치돼 있어도 확실히 끈다
            // (섹션 토글만 믿으면 프리팹 배치에 따라 "회복: 0 (즉시)"가 장비 툴팁에 그대로 남는다).
            SetActiveSafe(effectText != null ? effectText.gameObject : null, false);

            if (e == null) return;

            if (classText != null) classText.text = ClassLabel(e.EquipSlot, e.WeaponType);
            if (reqLevelText != null) reqLevelText.text = $"요구 레벨 {item.Level}";
        }

        /// <summary>스택형 아이템(소비품·재료) 정보를 커서 지점에 띄운다.</summary>
        /// <param name="stack">표시할 스택</param>
        /// <param name="screenPos">커서 스크린 좌표(PointerEventData.position)</param>
        public void ShowStack(ItemStack stack, Vector2 screenPos, Component owner = null)
        {
            if (stack?.Item == null) return;
            if (DragSuppressed) return;   // 드래그 중엔 hover 툴팁을 띄우지 않는다

            this.owner = owner;

            // 스택(소모품·재료)은 비교 대상이 아니다 — 직전 장비 hover에서 뜬 비교 패널을 확실히 닫는다.
            if (comparePanel != null) comparePanel.Hide();

            FillCommon(stack.Item);

            // 장비 섹션 OFF, 소모품이면 효과 섹션 ON(재료는 효과 없음 → OFF).
            SetActiveSafe(equipMetaSection, false);
            SetActiveSafe(mainStatSection, false);
            SetActiveSafe(optionSection, false);           // 옵션은 장비 전용
            SetActiveSafe(chainSection, false);            // 온체인 표시는 장비 전용
            SetActiveSafe(consumableSection, stack.IsConsumable);
            SetActiveSafe(effectText != null ? effectText.gameObject : null, true);

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
        public void Hide() => HideInternal();

        /// <summary>
        /// 요청한 슬롯이 이 툴팁의 주인일 때만 닫는다. 창을 닫을 때 그 창의 슬롯들이 <c>OnDisable</c>에서 부르며,
        /// 다른 창의 슬롯이 띄운 툴팁은 건드리지 않는다(예: 인벤을 닫아도 장비창에서 띄운 툴팁은 유지).
        /// </summary>
        /// <param name="requester">닫기를 요청한 슬롯. 이 툴팁의 주인이 아니면 무시한다.</param>
        public void Hide(Component requester)
        {
            if (requester != null && !ReferenceEquals(requester, owner)) return;
            HideInternal();
        }

        private void HideInternal()
        {
            owner = null;
            currentItem = null;
            if (comparePanel != null) comparePanel.Hide();   // 비교 패널도 함께 닫는다
            if (this != null) gameObject.SetActive(false);
        }

        // 공통 프레임(이름·등급·아이콘·설명·판매가)을 채운다.
        private void FillCommon(ItemData item)
        {
            currentItem = item;

            // 등급 표기와 색은 같은 행에서 나오므로 조회는 한 번만 한다.
            ItemGradeData gradeRow = ItemGradeColors.Row(item.Grade);
            Color gradeColor = gradeRow != null ? gradeRow.DisplayColor : Color.white;

            if (nameText != null)
            {
                nameText.text = item.Name;
                nameText.color = gradeColor;
            }
            if (gradeText != null)
            {
                gradeText.text = gradeRow != null ? gradeRow.Label : item.Grade.ToString();
                gradeText.color = gradeColor;
            }

            bool hasDesc = !string.IsNullOrWhiteSpace(item.Description);
            SetActiveSafe(descSection, hasDesc);
            if (hasDesc && descText != null) descText.text = item.Description;

            if (sellPriceText != null) sellPriceText.text = $"판매가 {item.SellPrice}";

            // 강화 수치는 장비에만 있으므로 공통 경로에서 먼저 끄고, 장비일 때만 ShowEquipment가 다시 켠다.
            // 끄지 않으면 직전에 본 장비의 "(+15)"가 소모품/재료 툴팁 이름 옆에 그대로 남는다.
            // 텍스트만 비우지 않고 오브젝트를 끄는 이유: 이름 옆 가로 레이아웃에서 자리(최소 폭 + 간격)까지
            // 반납해야 이름 박스가 그만큼 넓게 늘어난다.
            SetActiveSafe(enhanceText != null ? enhanceText.gameObject : null, false);

            LoadIcon(item);
        }

        // 장비의 롤된 옵션을 채운다. 섹션은 늘 켜 두고, 옵션이 있으면 목록을, 없으면 "옵션 없음"을 찍는다.
        private void FillOptions(IReadOnlyList<ItemOption> options)
        {
            bool hasOptions = options != null && options.Count > 0;
            SetActiveSafe(optionSection, hasOptions);
            if (optionText == null) return;

            if (!hasOptions)
            {
                optionText.text = "옵션 없음";
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(FormatOption(options[i]));
            }
            optionText.text = sb.ToString();
        }

        // 옵션 텍스트 색(#FFC030). 인스펙터 색이 아니라 TMP 리치텍스트로 입히는 이유: 옵션 칸의 기본 글자색은
        // 프리팹 쪽에 두고, "옵션 줄만" 강조해 "옵션 없음" 같은 안내 문구와 구분하기 위함이다.
        private const string OptionColorHex = "FFC030";

        private static string ColorizeOption(string text) => $"<color=#{OptionColorHex}>{text}</color>";

        // 옵션 한 줄 표기. 퍼센트 옵션 값은 비율(0.05)이라 100을 곱해 "5%"로, 정수 옵션은 반올림해 보여준다.
        private static string FormatOption(ItemOption opt)
        {
            string line = opt.IsPercent
                ? $"{opt.Label} +{opt.Value * 100f:0.#}%"
                : $"{opt.Label} +{Mathf.RoundToInt(opt.Value)}";
            return ColorizeOption(line);
        }

        // 롤 전 장비의 옵션 칸. 어떤 옵션이 붙을지는 드랍 때 정해지므로 개수만 보여준다.
        // 개수는 등급 상한(ItemGradeData.MaxOptionCount)이 아니라 아이템별 실제 개수(EquipmentData.OptionCount)다 —
        // 상한을 쓰면 "옵션 없는 Lv30 유물 무기" 같은 예외가 옵션 4개로 거짓 표기된다(롤러도 OptionCount만큼 뽑는다).
        // 실제 장비(FillOptions)와 달리 0개여도 섹션을 켜서 "옵션 없음"을 보여준다 — 사기 전에 알아야 하는 정보라서다.
        private void FillOptionPreview(int optionCount)
        {
            SetActiveSafe(optionSection, true);
            if (optionText == null) return;

            optionText.text = optionCount > 0 ? ColorizeOption($"랜덤 옵션 {optionCount}개") : "옵션 없음";
        }

        // 롤 전 장비의 온체인 칸. 토큰 ID·소유자 주소는 롤값으로 만드는데(FillChain) 미리보기엔 롤값이 없으므로,
        // 대상 여부(레이드 보상인지)만 알리고 값은 획득 후 보이게 한다.
        private void FillChainPreview(int itemId)
        {
            bool show = chainSection != null && IsRaidReward(itemId);
            SetActiveSafe(chainSection, show);
            if (!show || chainText == null) return;

            chainText.text =
                "온체인 아이템 (보기 전용)\n" +
                "획득하면 토큰 ID가 부여됩니다\n" +
                $"네트워크  {chainNetwork}";
        }

        // ── 블록체인 표시 (보기 전용 목업) ─────────────────────────────────────
        // 실제 지갑·RPC·컨트랙트 호출은 전혀 없다. 시연용으로 "이 장비가 온체인 자산이라면 이렇게 보인다"만 보여준다.
        // 토큰 ID·소유자 주소는 인스턴스의 롤값(아이템 Index·주스탯·옵션)에서 결정적으로 만든다 —
        // 난수로 만들면 같은 장비를 hover할 때마다 값이 바뀌어 가짜 티가 나고, 롤값은 세이브되므로 재접속해도 같다.
        // 나중에 실제 연동을 붙이면 이 메서드의 데이터 출처만 교체하면 된다(섹션/레이아웃은 그대로).
        private void FillChain(EquipmentInstance equip)
        {
            bool show = chainSection != null && IsRaidReward(equip.Item.Index);
            SetActiveSafe(chainSection, show);
            if (!show || chainText == null) return;

            uint seed = ChainSeed(equip);
            chainText.text =
                "온체인 아이템 (보기 전용)\n" +
                $"토큰 ID  #{seed % 1000000:000000}\n" +
                $"컨트랙트  {ShortAddress(chainContract)}\n" +
                $"소유자  {ShortAddress(FakeAddress(seed))}\n" +
                $"네트워크  {chainNetwork}";
        }

        // "블록체인 아이템 = 레이드 클리어 보상"이다. 별도 플래그 컬럼을 두지 않고 레이드 보상 행(DungeonRewardTable,
        // 던전 번호 9 = DungeonContext.RaidDungeonNumber)의 지급 목록에 든 아이템인지로 판정한다 —
        // 보상 풀을 시트에서 바꾸면 온체인 표시 대상도 저절로 따라가게 하기 위함(목록을 두 군데서 관리하지 않는다).
        private static bool IsRaidReward(int itemId)
        {
            if (JsonManager.Instance == null || !JsonManager.Instance.IsReady) return false;

            foreach (DungeonRewardTable row in JsonManager.Instance.DungeonRewardDict.Values)
            {
                if (row == null || row.DungeonId / 10 != DungeonContext.RaidDungeonNumber) continue;
                if (Contains(row.BaseRewards, itemId) || Contains(row.FixedRewards, itemId)) return true;
                if (row.RandomRewards != null)
                    foreach (RandomRewardEntry e in row.RandomRewards)
                        if (e != null && e.ItemId == itemId) return true;
            }
            return false;
        }

        private static bool Contains(List<RewardItemEntry> list, int itemId)
        {
            if (list == null) return false;
            foreach (RewardItemEntry e in list)
                if (e != null && e.ItemId == itemId) return true;
            return false;
        }

        // FNV-1a. string.GetHashCode는 런타임마다 달라질 수 있어 쓰지 않는다.
        private static uint ChainSeed(EquipmentInstance equip)
        {
            uint h = 2166136261;
            void Mix(int v) { unchecked { h = (h ^ (uint)v) * 16777619; } }

            Mix(equip.Item.Index);
            Mix(equip.RolledMainStat);
            foreach (ItemOption opt in equip.Options) Mix(Mathf.RoundToInt(opt.Value * 10000f));
            return h;
        }

        // seed로 40자리 hex 주소를 만든다(xorshift로 늘림).
        private static string FakeAddress(uint seed)
        {
            var sb = new System.Text.StringBuilder("0x", 42);
            uint x = seed == 0 ? 1u : seed;
            for (int i = 0; i < 5; i++)
            {
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                sb.Append(x.ToString("x8"));
            }
            return sb.ToString();
        }

        // 0x1234abcd...ef56 형태. 말줄임은 폰트에 없을 수 있는 '…' 대신 "..."을 쓴다.
        private static string ShortAddress(string address)
        {
            if (string.IsNullOrEmpty(address) || address.Length <= 12) return address;
            return $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}";
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

        // 호버한 장비와 같은 부위에 착용 중인 장비가 있으면 비교 패널에 띄운다. 같은 인스턴스(장비창에서 착용품 hover)면
        // 비교할 게 없어 닫는다. 비교 대상은 인벤/장비창/상점 어디서 hover하든 항상 "지금 그 부위에 착용 중인 것"이다.
        // 부위는 장비 행(e)으로 정하고, hovered는 "같은 인스턴스인가" 판정에만 쓴다 — 미리보기(상점·보상)는
        // 인스턴스가 없어 null을 넘기며, 그때는 착용품이 있으면 늘 비교를 띄운다.
        private void UpdateCompare(EquipmentData e, EquipmentInstance hovered)
        {
            if (comparePanel == null) return;

            EquipmentInstance equipped = (e != null && InventoryManager.Instance != null)
                ? InventoryManager.Instance.GetEquipped(e.EquipSlot)
                : null;

            if (equipped?.Item == null || ReferenceEquals(equipped, hovered))
            {
                comparePanel.Hide();
                return;
            }

            comparePanel.gameObject.SetActive(true);
            comparePanel.FillEquipment(equipped);
            comparePanel.PlaceBeside(tooltipRect);
        }

        // 이 (비교) 패널을 메인 툴팁 옆(몸통이 펼쳐지는 방향으로 이어)에 나란히 붙인다.
        // 커서가 화면 좌측이면 메인이 오른쪽으로 펼쳐지므로 비교 패널을 그 오른쪽에, 우측이면 왼쪽에 둔다.
        private void PlaceBeside(RectTransform main)
        {
            EnsureInit();

            // main의 실제 폭을 얻으려 레이아웃을 즉시 갱신한다(ContentSizeFitter가 다음 프레임까지 안 갱신될 수 있음).
            LayoutRebuilder.ForceRebuildLayoutImmediate(main);
            float mainWidth = main.rect.width;

            const float gap = 8f;
            bool bodyRight = main.pivot.x < 0.5f;   // main 몸통이 오른쪽으로 펼쳐짐(커서가 화면 좌측)

            tooltipRect.pivot = new Vector2(bodyRight ? 0f : 1f, main.pivot.y);
            float x = bodyRight
                ? main.anchoredPosition.x + mainWidth + gap
                : main.anchoredPosition.x - mainWidth - gap;
            tooltipRect.anchoredPosition = new Vector2(x, main.anchoredPosition.y);
            tooltipRect.SetAsLastSibling();
        }

        private static void SetActiveSafe(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value) go.SetActive(value);
        }

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
        private static void ResetStatics()
        {
            Instance = null;
            DragSuppressed = false;
        }
    }
}
