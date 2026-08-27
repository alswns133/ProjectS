using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Items;
using ProjectS.Skills;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 스킬창 본체(순수 View). K키로 열리며(<see cref="SkillHotkey"/>), 액티브·패시브 스킬 레벨을
    /// ▲/▼로 미리 배치하고 [확인]으로 일괄 커밋한다. 판정·SP 계산은 절대 알지 않고,
    /// 실제 로직은 <see cref="SkillPresenter"/> → <see cref="ISkillWindowSource"/>로 흐른다.
    /// </summary>
    /// <remarks>
    /// 인벤·강화·장비창과 같은 급의 공존형 이동식 창이라 <see cref="BasePopup"/>이다.
    /// (2026-08-26 신규 — 데이터/SP 시스템 확정 전 UI 껍데기 + 배선까지)
    /// </remarks>
    public class SkillPopup : BasePopup
    {
        [Header("슬롯")]
        [Tooltip("액티브 스킬 슬롯(스크린샷 기준 4칸). 데이터가 적으면 뒤 칸은 자동으로 숨는다.")]
        [SerializeField] private SkillSlotView[] activeSlots = new SkillSlotView[4];
        [Tooltip("패시브 스킬 슬롯(스크린샷 기준 7칸).")]
        [SerializeField] private SkillSlotView[] passiveSlots = new SkillSlotView[7];

        [Header("프리뷰 (우측)")]
        [Tooltip("스킬 소개 영상/이미지 자리. 영상 재생은 후속 작업 — 지금은 아이콘 스프라이트만 띄운다.")]
        [SerializeField] private Image previewImage;
        [SerializeField] private TMP_Text previewNameText;
        [SerializeField] private TMP_Text previewDescriptionText;

        [Header("SP / 버튼")]
        [Tooltip("보유·사용 SP 표기(예: 0 / 45).")]
        [SerializeField] private TMP_Text spText;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [Tooltip("우상단 X. 없으면 취소 버튼만으로도 닫힌다.")]
        [SerializeField] private Button closeButton;

        // skillId → 슬롯 뷰. Presenter가 특정 슬롯 하나만 갱신할 때 쓴다.
        private readonly Dictionary<int, SkillSlotView> slotById = new();
        private string previewAddress;

        /// <summary>팝업이 열려 초기 상태를 준비했을 때. Presenter가 편집 세션을 새로 연다.</summary>
        public event Action OnOpened;

        /// <summary>▲를 눌러 레벨 올리기를 요청. 인자는 스킬 식별자.</summary>
        public event Action<int> OnIncreaseRequested;

        /// <summary>▼를 눌러 레벨 내리기를 요청.</summary>
        public event Action<int> OnDecreaseRequested;

        /// <summary>슬롯에 마우스를 올려 프리뷰 갱신을 요청.</summary>
        public event Action<int> OnSlotFocused;

        /// <summary>[RESET]을 눌렀을 때(이번 배치 되돌리기).</summary>
        public event Action OnResetRequested;

        /// <summary>[확인]을 눌렀을 때(일괄 커밋).</summary>
        public event Action OnConfirmRequested;

        /// <summary>[취소]/X를 눌렀을 때(적용 없이 닫기).</summary>
        public event Action OnCancelRequested;

        protected override void OnInit()
        {
            // 자식 Awake 순서에 기대지 않도록 버튼 배선을 여기서 일괄 처리한다(EnhancePopup과 동일).
            if (resetButton != null) resetButton.onClick.AddListener(() => OnResetRequested?.Invoke());
            if (confirmButton != null) confirmButton.onClick.AddListener(() => OnConfirmRequested?.Invoke());
            if (cancelButton != null) cancelButton.onClick.AddListener(() => OnCancelRequested?.Invoke());
            if (closeButton != null) closeButton.onClick.AddListener(() => OnCancelRequested?.Invoke());

            WireSlots(activeSlots);
            WireSlots(passiveSlots);

            // 이동식 창 위치 저장 키 주입(EnhancePopup과 동일). DraggableWindow가 없으면 무시.
            if (TryGetComponent(out DraggableWindow window))
                window.SetWindowId(WindowIds.Skill);
        }

        protected override void OnShow()
        {
            // 팝업은 재사용되므로 직전 프리뷰가 남지 않게 비우고, Presenter에 새 세션을 알린다.
            ClearPreview();
            OnOpened?.Invoke();
        }

        // 슬롯의 ▲/▼/hover 이벤트를 팝업 이벤트(스킬 식별자 인자)로 중계한다.
        private void WireSlots(SkillSlotView[] slots)
        {
            if (slots == null) return;

            foreach (SkillSlotView slot in slots)
            {
                if (slot == null) continue;
                slot.Increased += s => OnIncreaseRequested?.Invoke(s.SkillId);
                slot.Decreased += s => OnDecreaseRequested?.Invoke(s.SkillId);
                slot.Focused += s => OnSlotFocused?.Invoke(s.SkillId);
            }
        }

        /// <summary>
        /// 두 그룹의 슬롯을 바인딩한다. 데이터가 슬롯 수보다 적으면 남는 칸은 숨기고, 많으면 초과분은 버린다.
        /// </summary>
        /// <param name="active">액티브 스킬 목록</param>
        /// <param name="passive">패시브 스킬 목록</param>
        public void SetSlots(IReadOnlyList<SkillSlotInfo> active, IReadOnlyList<SkillSlotInfo> passive)
        {
            slotById.Clear();
            BindGroup(activeSlots, active);
            BindGroup(passiveSlots, passive);
        }

        private void BindGroup(SkillSlotView[] views, IReadOnlyList<SkillSlotInfo> data)
        {
            if (views == null) return;

            for (int i = 0; i < views.Length; i++)
            {
                SkillSlotView view = views[i];
                if (view == null) continue;

                if (data != null && i < data.Count)
                {
                    view.Bind(data[i]);
                    slotById[data[i].SkillId] = view;
                }
                else view.SetEmpty();
            }
        }

        /// <summary>슬롯 하나의 레벨/스테퍼 상태를 갱신한다.</summary>
        /// <param name="skillId">대상 스킬</param>
        /// <param name="current">현재(편집 중) 레벨</param>
        /// <param name="max">최대 레벨</param>
        /// <param name="canUp">▲ 가능 여부</param>
        /// <param name="canDown">▼ 가능 여부</param>
        public void SetSlotLevel(int skillId, int current, int max, bool canUp, bool canDown)
        {
            if (slotById.TryGetValue(skillId, out SkillSlotView view))
                view.SetLevel(current, max, canUp, canDown);
        }

        /// <summary>SP 표기를 갱신한다(사용 / 총).</summary>
        /// <param name="used">사용한 SP</param>
        /// <param name="total">총 SP</param>
        public void SetSp(int used, int total)
        {
            if (spText != null) spText.text = $"{used} / {total}";
        }

        /// <summary>우측 프리뷰(이름·설명·이미지)를 갱신한다.</summary>
        /// <param name="info">표시할 스킬 정보</param>
        public void SetPreview(SkillSlotInfo info)
        {
            if (previewNameText != null) previewNameText.text = info.Name;
            if (previewDescriptionText != null) previewDescriptionText.text = info.Description;

            // 소개 영상 자리 — 지금은 아이콘 스프라이트를 이미지로 띄운다(주소가 있으면).
            string address = string.IsNullOrEmpty(info.PreviewMediaAddress) ? info.IconAddress : info.PreviewMediaAddress;
            LoadPreviewImage(address);
        }

        /// <summary>확인/취소를 눌러 창을 닫는다(Presenter가 로직을 마친 뒤 호출).</summary>
        public void Close() => RequestClose();

        // 프리뷰를 비운다(재오픈 시 직전 스킬이 남지 않게).
        private void ClearPreview()
        {
            previewAddress = null;
            if (previewNameText != null) previewNameText.text = "스킬 이름";
            if (previewDescriptionText != null) previewDescriptionText.text = "스킬 설명";
            if (previewImage != null)
            {
                previewImage.sprite = null;
                previewImage.enabled = false;
            }
        }

        private async void LoadPreviewImage(string address)
        {
            previewAddress = address;
            if (previewImage != null)
            {
                previewImage.sprite = null;
                previewImage.enabled = false;
            }

            if (string.IsNullOrEmpty(address)) return;

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (this == null || previewImage == null) return;
            if (!string.Equals(previewAddress, address)) return;   // 다른 슬롯으로 hover 이동됨

            previewImage.sprite = sprite;
            previewImage.enabled = sprite != null;
        }
    }
}
