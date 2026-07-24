using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화창 본체(순수 View). 판정·검증을 절대 알지 않고, 참조 보유와 표시/연출만 담당한다.
    /// 실제 강화 로직은 EnhancePresenter → EnhanceService로 흐른다.
    /// 상점과 같은 급의 전체 화면 UI라 BasePanel이며, 장비 선택은 위에 뜨는 ItemSelectPopup으로 처리한다.
    /// (2026-07-23 TH)
    /// </summary>
    public class EnhancePanel : BasePanel
    {
        [Header("코어")]
        [SerializeField] private Image coreIcon;
        [SerializeField] private Button coreSlotButton;   // 클릭 시 장비 선택 팝업
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text curLevelText;
        [SerializeField] private TMP_Text nextLevelText;

        [Header("확률")]
        [SerializeField] private TMP_Text rateText;
        [SerializeField] private SegmentGaugeView rateGauge;

        [Header("비용")]
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text ownedGoldText;

        [Header("버튼")]
        [SerializeField] private Button enhanceButton;
        [SerializeField] private Button closeButton;

        [Header("재료 리스트")]
        [SerializeField] private Transform materialListRoot;
        [SerializeField] private MaterialSlotView materialSlotPrefab;

        [Header("스탯 프리뷰")]
        [SerializeField] private Transform statListRoot;
        [SerializeField] private StatRowView statRowPrefab;

        private readonly List<MaterialSlotView> materialSlots = new();
        private readonly List<StatRowView> statRows = new();

        /// <summary>강화 버튼을 눌렀을 때. Presenter가 검증·판정을 시작한다.</summary>
        public event Action OnEnhanceRequested;

        /// <summary>코어 슬롯을 눌렀을 때. Presenter가 장비 선택 팝업을 연다.</summary>
        public event Action OnSlotClicked;

        protected override void OnInit()
        {
            // 자식 컴포넌트의 Awake 순서에 기대지 않도록 버튼 배선을 여기서 일괄 처리한다.
            // (OnInit은 SetActive(true) 이전에 1회 호출된다 — BasePanel.Show 참고. 그래서 여기서
            //  자식 Awake가 아직 안 돈 상태여도, 버튼 리스너 등록처럼 참조만 쓰는 작업은 안전하다.)
            if (enhanceButton != null) enhanceButton.onClick.AddListener(() => OnEnhanceRequested?.Invoke());
            if (coreSlotButton != null) coreSlotButton.onClick.AddListener(() => OnSlotClicked?.Invoke());
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance.Back());
        }

        /// <summary>
        /// 강화 대상과 정보를 표시한다. 아이콘/이름/레벨/확률/비용/스탯 프리뷰를 갱신한다.
        /// </summary>
        /// <param name="item">대상 아이템 공통 정보</param>
        /// <param name="info">현재 상태 기준 강화 정보 스냅샷</param>
        public void SetTarget(ItemData item, EnhanceInfo info)
        {
            if (item != null)
            {
                if (nameText != null) nameText.text = item.Name;
                if (typeText != null) typeText.text = item.Category.ToString();
            }

            if (curLevelText != null) curLevelText.text = $"+{info.CurrentStep}";
            if (nextLevelText != null) nextLevelText.text = info.IsMax ? "MAX" : $"+{info.CurrentStep + 1}";

            if (rateText != null) rateText.text = info.IsMax ? "-" : $"{info.SuccessRate * 100f:0.#}%";
            if (rateGauge != null) rateGauge.SetRatio(info.SuccessRate);

            if (costText != null) costText.text = info.IsMax ? "-" : info.ZenyCost.ToString();

            BuildStatRows(info);
        }

        /// <summary>
        /// 재료 목록을 표시한다. 슬롯 개수가 가변이라 프리팹을 재생성한다.
        /// </summary>
        /// <param name="mats">재료 표시 DTO 목록</param>
        public void SetMaterials(IReadOnlyList<MaterialSlotInfo> mats)
        {
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
                view.Set(m.Icon, m.Name, m.Owned, m.Required);
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
        /// 강화 버튼/슬롯 조작 가능 여부. 연출 중 연타를 막기 위해 false로 잠근다.
        /// </summary>
        /// <param name="value">true면 조작 가능</param>
        public void SetInteractable(bool value)
        {
            if (enhanceButton != null) enhanceButton.interactable = value;
            if (coreSlotButton != null) coreSlotButton.interactable = value;
        }

        /// <summary>
        /// 강화 결과 연출만 재생한다(판정 없음). 마을에서 timeScale=0일 수 있어 unscaled로 대기한다.
        /// 실제 파티클/플래시는 FX_Overlay에 연결해 이 코루틴에서 성공/실패로 분기해 트리거한다.
        /// </summary>
        /// <param name="result">표시할 판정 결과</param>
        /// <returns>연출 코루틴</returns>
        public IEnumerator PlayResult(EnhanceResult result)
        {
            // TODO: FX_Overlay 연출 트리거(result.Success로 성공/실패 분기). 지금은 시간만 대기.
            yield return new WaitForSecondsRealtime(1.2f);
        }

        // 주 스탯 한 줄 프리뷰. 옵션 프리뷰가 늘어나면 여러 줄로 확장한다.
        private void BuildStatRows(EnhanceInfo info)
        {
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
    }
}
