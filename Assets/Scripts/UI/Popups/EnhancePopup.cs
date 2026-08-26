using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화창 본체(순수 View). 판정·검증을 절대 알지 않고, 참조 보유와 표시/연출만 담당한다.
    /// 실제 강화 로직은 EnhancePresenter → EnhanceService로 흐른다.
    /// 상점·인벤·장비창과 같은 급의 창이라 BasePopup이다(패널 스택이 아니라 공존 창 — 인벤토리 팝업에서
    /// 장비를 드래그하거나 좌더블클릭해 강화하는 흐름상 인벤과 동시에 떠 있어야 한다.
    /// (2026-07-23 TH / 2026-08-19 Panel→Popup 전환)
    /// </summary>
    public class EnhancePopup : BasePopup
    {
        [Header("코어")]
        [SerializeField] private Image coreIcon;
        [Tooltip("코어 슬롯의 드롭/hover 컴포넌트(CoreSlotDropTarget). 선택한 장비를 알려 hover 시 아이템 툴팁이 뜨게 한다.")]
        [SerializeField] private CoreSlotDropTarget coreSlot;
        [Tooltip("코어 슬롯 위 강화 단계 배지(+N). 인벤/장비 슬롯과 같은 표기. 0강이면 숨긴다. 비워두면 표시 안 함.")]
        [SerializeField] private TMP_Text coreEnhanceText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text curLevelText;
        [SerializeField] private TMP_Text nextLevelText;

        [Header("확률")]
        [SerializeField] private TMP_Text rateText;
        // 성공률 게이지(세그먼트 링)는 팝업이 직접 값을 넣지 않는다. 게이지 채움은 EnhanceGaugeSweep이
        // OnTargetChanged로 성공률을 받아 소유하고, SegmentGaugeView가 그 fillAmount를 미러링한다.
        // (씬에서 SegmentGaugeView.sourceFill = GaugeF의 Image로 배선) — rateText만 숫자로 표기한다.

        [Header("비용")]
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text ownedGoldText;

        [Header("버튼")]
        [SerializeField] private Button enhanceButton;
        [SerializeField] private Button closeButton;

        [Header("재료 (리뉴얼: 고정 3×2 슬롯)")]
        [Tooltip("리뉴얼 UI의 재료 슬롯 6개를 왼쪽 위부터 순서대로 연결한다. 현재 강화는 2종만 쓰며 나머지는 빈칸으로 유지된다.")]
        [SerializeField] private MaterialSlotView[] fixedMaterialSlots = new MaterialSlotView[6];

        [Header("재료 (구 UI 호환용)")]
        [SerializeField] private Transform materialListRoot;
        [SerializeField] private MaterialSlotView materialSlotPrefab;

        [Header("스탯 프리뷰 (리뉴얼: 현재 / 다음 고정 표기)")]
        [SerializeField] private TMP_Text currentStatLabelText;
        [SerializeField] private TMP_Text currentStatValueText;
        [SerializeField] private TMP_Text nextStatLabelText;
        [SerializeField] private TMP_Text nextStatValueText;

        [Header("스탯 프리뷰 (구 UI 호환용)")]
        [SerializeField] private Transform statListRoot;
        [SerializeField] private StatRowView statRowPrefab;

        private readonly List<MaterialSlotView> materialSlots = new();
        private readonly List<StatRowView> statRows = new();
        private string coreIconAddress;

        /// <summary>강화 버튼을 눌렀을 때. Presenter가 검증·판정을 시작한다.</summary>
        public event Action OnEnhanceRequested;

        /// <summary>팝업이 열려 초기 선택 상태를 준비했을 때. Presenter는 이전 대상을 비운다.</summary>
        public event Action OnOpened;

        /// <summary>
        /// 강화 결과 연출이 시작될 때. 창 안의 데코 연출(코어 링 회전 등)이 여기에 붙는다.
        /// ★ OnEnhanceRequested가 아니라 이쪽을 쓴다 — 버튼 클릭은 골드·재료 부족으로 그냥 튕길 수 있어서,
        ///   그걸 신호로 삼으면 강화가 시작되지도 않았는데 연출만 도는 경우가 생긴다.
        /// </summary>
        public event Action OnResultPlayStarted;

        /// <summary>
        /// 강화 결과 연출이 끝났을 때. 데코 연출이 평상시 상태로 돌아갈 신호다.
        /// (연출 도중 창이 닫히면 이 이벤트는 오지 않는다 — 구독자는 자기 OnEnable에서 상태를 되돌려야 한다.)
        /// </summary>
        public event Action OnResultPlayFinished;

        /// <summary>
        /// 표시 대상이 갱신될 때(<see cref="SetTarget"/>). 게이지 스윕처럼 "평상시 위치"가 필요한
        /// 연출이 현 단계 성공률을 여기서 받는다. 인스펙터 배선 없이 붙이기 위해 이벤트로 낸다.
        /// </summary>
        public event Action<EnhanceInfo> OnTargetChanged;

        /// <summary>
        /// 결과 연출 시작 시, 성공/실패까지 함께 알린다.
        /// <see cref="OnResultPlayStarted"/>와 동시에 발행하며, 결과에 따라 분기해야 하는 연출
        /// (게이지가 끝에 도달 vs 닿을 듯하다 되돌아감)이 이쪽을 쓴다.
        /// 기존 구독자를 깨지 않으려고 인자 없는 이벤트를 바꾸지 않고 따로 뒀다.
        /// </summary>
        public event Action<EnhanceResult> OnResultPlay;

        protected override void OnInit()
        {
            // 자식 컴포넌트의 Awake 순서에 기대지 않도록 버튼 배선을 여기서 일괄 처리한다.
            // (OnInit은 SetActive(true) 이전에 1회 호출된다 — BasePopup.Show 참고. 그래서 여기서
            //  자식 Awake가 아직 안 돈 상태여도, 버튼 리스너 등록처럼 참조만 쓰는 작업은 안전하다.)
            // 대상 선택은 인벤토리에서 장비를 코어 슬롯에 드래그드롭으로 처리한다(코어 슬롯의 CoreSlotDropTarget이
            // 담당). 그래서 코어 슬롯 버튼 자체에는 클릭 배선을 하지 않는다.
            if (enhanceButton != null) enhanceButton.onClick.AddListener(() => OnEnhanceRequested?.Invoke());
            if (closeButton != null) closeButton.onClick.AddListener(() => RequestClose());   // 팝업 자기 닫기(ShopPopup과 동일)

            // 이동식 창 위치 저장 키 주입(InventoryPopup과 동일 방식). OnInit은 SetActive 이전 1회라,
            // 뒤이은 DraggableWindow.OnEnable이 이 키로 저장 위치를 복원한다. DraggableWindow가 없으면 무시.
            if (TryGetComponent(out DraggableWindow window))
                window.SetWindowId(WindowIds.Enhance);
        }

        protected override void OnShow()
        {
            // 팝업은 재사용되므로 직전에 선택했던 장비/비용/성공률이 다음 오픈에 남으면 안 된다.
            SetEmptyState();
            OnOpened?.Invoke();
        }

        protected override void OnHide()
        {
            // NPC 허브에서 열렸다면 닫힐 때 허브로 돌아가 상호작용 잠금을 푼다(ShopPopup.OnHide와 동일 흐름).
            // NPC 없이 열린 경우(테스트 등)엔 EnhanceManager가 no-op이라 안전하다.
            EnhanceManager.Instance?.OnEnhanceClosed();
        }

        /// <summary>
        /// 강화 대상과 정보를 표시한다. 아이콘/이름/레벨/확률/비용/스탯 프리뷰를 갱신한다.
        /// </summary>
        /// <param name="item">대상 아이템 공통 정보</param>
        /// <param name="equipment">대상 장비 고유 정보(무기 종류·주스탯)</param>
        /// <param name="info">현재 상태 기준 강화 정보 스냅샷</param>
        public void SetTarget(ItemData item, EquipmentData equipment, EnhanceInfo info)
        {
            if (item != null)
            {
                if (nameText != null) nameText.text = $"{item.Name} +{info.CurrentStep}";
                if (typeText != null) typeText.text = FormatTypeGrade(item, equipment);
                SetCoreIcon(item.IconAddress);
            }

            if (curLevelText != null) curLevelText.text = $"+{info.CurrentStep}";
            if (nextLevelText != null) nextLevelText.text = info.IsMax ? "MAX" : $"+{info.CurrentStep + 1}";
            // 코어 슬롯 위 강화 배지(+N). 인벤/장비 슬롯과 같은 표기 — 0강은 숨긴다.
            if (coreEnhanceText != null) coreEnhanceText.text = info.CurrentStep > 0 ? $"+{info.CurrentStep}" : string.Empty;

            if (rateText != null) rateText.text = info.IsMax ? "MAX" : $"{info.SuccessRate * 100f:0}%";
            // 게이지는 OnTargetChanged → EnhanceGaugeSweep 경로로만 움직인다(여기서 직접 세팅하면 궤적이 두 갈래).

            if (costText != null) costText.text = info.IsMax ? "-" : info.ZenyCost.ToString("N0");
            if (enhanceButton != null) enhanceButton.interactable = !info.IsMax;

            BuildStatRows(info);

            OnTargetChanged?.Invoke(info);
        }

        /// <summary>
        /// 재료 목록을 표시한다. 슬롯 개수가 가변이라 프리팹을 재생성한다.
        /// </summary>
        /// <param name="mats">재료 표시 DTO 목록</param>
        public void SetMaterials(IReadOnlyList<MaterialSlotInfo> mats)
        {
            if (HasFixedMaterialSlots())
            {
                for (int i = 0; i < fixedMaterialSlots.Length; i++)
                {
                    MaterialSlotView slot = fixedMaterialSlots[i];
                    if (slot == null) continue;

                    if (mats != null && i < mats.Count)
                    {
                        MaterialSlotInfo material = mats[i];
                        slot.Set(material.IconAddress, material.Name, material.Owned, material.Required, material.ItemId);
                    }
                    else slot.SetEmpty();
                }
                return;
            }

            // 빌더가 넣어둔 디자인 샘플 슬롯까지 포함해 기존 자식을 모두 정리한다
            // (자기가 만든 것만 지우면 샘플 슬롯이 남아 중복된다).
            if (materialListRoot != null)
            {
                foreach (Transform child in materialListRoot) Destroy(child.gameObject);
            }
            materialSlots.Clear();

            if (mats == null || materialSlotPrefab == null || materialListRoot == null) return;

            foreach (var m in mats)
            {
                var view = Instantiate(materialSlotPrefab, materialListRoot);
                view.Set(m.IconAddress, m.Name, m.Owned, m.Required, m.ItemId);
                materialSlots.Add(view);
            }
        }

        /// <summary>
        /// 보유 골드 표시를 갱신한다. (골드 변경 이벤트를 Presenter가 받아 전달)
        /// </summary>
        /// <param name="gold">현재 보유 골드</param>
        public void SetOwnedGold(int gold)
        {
            if (ownedGoldText != null) ownedGoldText.text = gold.ToString();
        }

        /// <summary>
        /// 장비를 아직 올리지 않은 초기 화면. 사진의 안내문·0%·빈 코어·고정 6칸·+0→+0을 만든다.
        /// </summary>
        public void SetEmptyState()
        {
            if (nameText != null) nameText.text = "강화할 장비를 선택해주세요.";
            if (typeText != null) typeText.text = string.Empty;
            ClearCoreIcon();
            SetCoreEquipment(null);   // 빈 슬롯이므로 hover 툴팁 대상도 비운다.

            if (curLevelText != null) curLevelText.text = "+0";
            if (nextLevelText != null) nextLevelText.text = "+0";
            if (coreEnhanceText != null) coreEnhanceText.text = string.Empty;   // 빈 슬롯 — 강화 배지 숨김
            if (rateText != null) rateText.text = "0%";
            if (costText != null) costText.text = "0";
            if (enhanceButton != null) enhanceButton.interactable = false;

            SetMaterials(null);
            SetStatPreview(MainStatType.None, 0, 0, true);

            foreach (StatRowView row in statRows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            statRows.Clear();
        }

        /// <summary>
        /// 코어에 올라간 장비를 코어 슬롯(<see cref="CoreSlotDropTarget"/>)에 알린다. hover 아이템 툴팁이 이 대상을 쓴다.
        /// 아이콘/이름/스탯 프리뷰는 <see cref="SetTarget"/>가 이미 갱신하므로, 여기선 툴팁 대상만 넘긴다.
        /// Presenter가 대상 갱신 시 호출하고, 빈 상태에서는 null로 지운다.
        /// </summary>
        /// <param name="equip">현재 강화 대상(없으면 null)</param>
        public void SetCoreEquipment(EquipmentInstance equip)
        {
            if (coreSlot != null) coreSlot.SetEquipment(equip);
        }

        /// <summary>
        /// 강화 버튼/슬롯 조작 가능 여부. 연출 중 연타를 막기 위해 false로 잠근다.
        /// </summary>
        /// <param name="value">true면 조작 가능</param>
        public void SetInteractable(bool value)
        {
            if (enhanceButton != null) enhanceButton.interactable = value;
        }

        /// <summary>
        /// 강화 결과 연출만 재생한다(판정 없음). 마을에서 timeScale=0일 수 있어 unscaled로 대기한다.
        /// 실제 파티클/플래시는 FX_Overlay에 연결해 이 코루틴에서 성공/실패로 분기해 트리거한다.
        /// </summary>
        /// <param name="result">표시할 판정 결과</param>
        /// <returns>연출 코루틴</returns>
        public IEnumerator PlayResult(EnhanceResult result)
        {
            // 데코 연출(EnhanceGaugeCycleSpin 등)에 "지금부터 연출 구간"을 알린다.
            // 판정이 이미 끝난 구간이라 화면상 변하는 수치가 없어서, 이 신호를 받는 연출이
            // 대기 시간을 채워주지 않으면 1.2초가 통째로 정지 화면이 된다.
            OnResultPlayStarted?.Invoke();
            OnResultPlay?.Invoke(result);

            // TODO: FX_Overlay 연출 트리거(result.Success로 성공/실패 분기). 지금은 시간만 대기.
            yield return new WaitForSecondsRealtime(1.2f);

            OnResultPlayFinished?.Invoke();
        }

        // 주 스탯 한 줄 프리뷰. 옵션 프리뷰가 늘어나면 여러 줄로 확장한다.
        private void BuildStatRows(EnhanceInfo info)
        {
            SetStatPreview(info.MainStatType, info.CurrentMainStat, info.NextMainStat, info.IsMax);

            // 리뉴얼 UI의 고정 현재/다음 텍스트가 연결됐다면 구 프리팹 목록은 만들지 않는다.
            if (HasFixedStatPreview()) return;

            foreach (var row in statRows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            statRows.Clear();

            if (statRowPrefab == null || statListRoot == null || info.MainStatType == MainStatType.None) return;

            var view = Instantiate(statRowPrefab, statListRoot);
            string next = info.IsMax ? null : info.NextMainStat.ToString();

            int deltaValue = info.IsMax ? 0 : info.NextMainStat - info.CurrentMainStat;
            string delta = info.IsMax ? null : (deltaValue >= 0 ? $"+{deltaValue}" : deltaValue.ToString());

            view.Set(info.MainStatType.ToString(), info.CurrentMainStat.ToString(), next, delta, deltaValue);
            statRows.Add(view);
        }

        private bool HasFixedMaterialSlots()
        {
            if (fixedMaterialSlots == null || fixedMaterialSlots.Length == 0) return false;
            foreach (MaterialSlotView slot in fixedMaterialSlots)
                if (slot != null) return true;
            return false;
        }

        private bool HasFixedStatPreview()
            => currentStatLabelText != null || currentStatValueText != null ||
               nextStatLabelText != null || nextStatValueText != null;

        private void SetStatPreview(MainStatType type, int current, int next, bool isEmptyOrMax)
        {
            string label = type == MainStatType.None ? "메인 스탯" : LocalizeStat(type);
            string currentText = type == MainStatType.None ? "-" : current.ToString("N0");
            string nextText = (type == MainStatType.None || isEmptyOrMax) ? "-" : next.ToString("N0");

            if (currentStatLabelText != null) currentStatLabelText.text = label;
            if (currentStatValueText != null) currentStatValueText.text = currentText;
            if (nextStatLabelText != null) nextStatLabelText.text = label;
            if (nextStatValueText != null) nextStatValueText.text = nextText;
        }

        private static string LocalizeStat(MainStatType type)
            => type == MainStatType.AttackDamage ? "공격력" :
               type == MainStatType.Defense ? "방어력" : "메인 스탯";

        private static string FormatTypeGrade(ItemData item, EquipmentData equipment)
        {
            string type = equipment != null && equipment.WeaponType != WeaponType.None
                ? equipment.WeaponType.ToString().ToUpperInvariant()
                : item.Category.ToString().ToUpperInvariant();
            return $"{type} / {item.Grade.ToString().ToUpperInvariant()}";
        }

        private async void SetCoreIcon(string address)
        {
            if (coreIcon == null) return;

            coreIconAddress = address;
            ShowCoreIcon(null);   // 로드 전엔 투명(흰 사각형 팝인 방지) — 단 enabled는 유지해 드롭 판정면을 살려둔다.
            Sprite sprite = await ItemIconLoader.LoadAsync(address);
            if (this == null || coreIcon == null || !isActiveAndEnabled) return;
            if (!string.Equals(coreIconAddress, address)) return;

            ShowCoreIcon(sprite);
        }

        private void ClearCoreIcon()
        {
            coreIconAddress = null;
            ShowCoreIcon(null);
        }

        // 코어 아이콘은 항상 enabled로 두고 스프라이트 유무로 알파만 토글해 시각만 숨긴다.
        // ★ enabled=false로 끄면 이 이미지가 코어 슬롯의 유일한 raycastTarget이라(배경 raycast는 off)
        //   빈 슬롯에서 드롭·hover 판정면이 통째로 사라져, 첫 오픈 때 드래그가 코어에 안 붙는다.
        private void ShowCoreIcon(Sprite sprite)
        {
            if (coreIcon == null) return;

            coreIcon.sprite = sprite;
            coreIcon.enabled = true;   // raycast 유지(빈 슬롯에서도 드롭을 받는다)

            Color c = coreIcon.color;
            c.a = sprite != null ? 1f : 0f;   // 스프라이트 없으면 투명(빈 슬롯 배경이 그대로 보인다)
            coreIcon.color = c;
        }
    }
}
