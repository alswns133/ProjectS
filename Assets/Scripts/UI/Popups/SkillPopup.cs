using ProjectS.Debugging;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Skills;
using ProjectS.UI.Framework;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 스킬창 본체(순수 View). K키로 열리며(<see cref="PopupHotkey"/>), 액티브·패시브 스킬 레벨을
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
        [Tooltip("소개 영상/이미지 영역 루트. 패시브 스킬엔 소개 영상을 띄우지 않으므로 이 영역만 끈다(비우면 previewImage 오브젝트로 폴백). 이름·설명은 패시브도 표시.")]
        [SerializeField] private GameObject previewMediaRoot;
        [Tooltip("영상이 없거나 로드에 실패했을 때 대신 띄우는 스킬 아이콘 이미지.")]
        [SerializeField] private Image previewImage;
        [Tooltip("스킬 소개 영상(RawImage + VideoPlayer + AddressableVideoView). 비우면 아이콘만 띄운다.")]
        [SerializeField] private AddressableVideoView previewVideo;
        [SerializeField] private GameObject[] previewIcon;
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
            // 창을 열자마자 슬롯 ▲/▼를 클릭할 수 있어야 하므로 마우스 모드로 전환한다.
            PlayerManager.Instance?.Player?.SetCursorMode(true);

            // 팝업은 재사용되므로 직전 프리뷰가 남지 않게 비우고, Presenter에 새 세션을 알린다.
            ClearPreview();
            OnOpened?.Invoke();

            SetPreviewIcon(PlayerManager.Instance != null ? PlayerManager.Instance.CurrentCharacterId : 0);
        }

        protected override void OnHide()
        {
            // 영상 클립은 스킬창을 열어 둔 동안만 필요하다 — 닫으면 메모리에서 내린다.
            if (previewVideo != null) previewVideo.Stop();

            // 인벤·장비창이 아직 열려 있으면 마우스 모드를 유지한다(공존 팝업이라 하나만 닫혀도 잠그면 안 됨).
            UIManager ui = UIManager.Instance;
            if (ui != null && !ui.IsPopupOpen<InventoryPopup>() && !ui.IsPopupOpen<EquipmentPopup>())
                PlayerManager.Instance?.Player?.SetCursorMode(false);
        }

        // 캐릭터 타입(1=검사·2=거너)에 맞는 프리뷰 아이콘 하나만 켠다.
        // 아이콘 등록이 모자라면 조용히 전부 꺼지는 대신 경고를 남긴다 — 원인을 못 찾고 헤매기 쉬운 증상이라서다.
        private void SetPreviewIcon(int characterType)
        {
            if (previewIcon == null || previewIcon.Length == 0) return;
            if (characterType < 1) return; // 캐릭터 타입의 시작은 1부터 시작하므로 1보다 이하라면 리턴함

            int index = characterType - 1;   // characterType은 1부터 시작하므로 인덱스와 맞춘다
            if (index >= previewIcon.Length)
            {
                DevLog.Warning($"[SkillPopup] 프리뷰 아이콘이 {previewIcon.Length}개뿐인데 캐릭터 타입은 {characterType}입니다. 인스펙터에 아이콘을 추가하세요.", this);
                return;
            }

            for (int i = 0; i < previewIcon.Length; i++)
            {
                if (previewIcon[i] != null)
                    previewIcon[i].SetActive(i == index);
            }
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
                // 우클릭 → 단축키 등록 메뉴(화면 계층이 연다. Framework 슬롯은 콜백만 올림).
                slot.RightClicked += (s, e) => SkillContextMenu.Instance?.Show(s.SkillId, e.position);
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

        /// <summary>SP 표기를 갱신한다. "사용/총"이 아니라 <b>남은 포인트 하나</b>를 보여준다(1레벨당 1 SP).</summary>
        /// <param name="remaining">남은 SP</param>
        public void SetSp(int remaining)
        {
            if (spText != null) spText.text = remaining.ToString();
        }

        /// <summary>
        /// 우측 프리뷰를 갱신한다. 이름·설명은 액티브·패시브 모두 표시하고, <b>소개 영상 이미지는 액티브만</b> 띄운다
        /// (패시브는 소개 영상 영역을 끈다 — 기획).
        /// </summary>
        /// <param name="info">표시할 스킬 정보</param>
        public void SetPreview(SkillSlotInfo info)
        {
            if (previewNameText != null) previewNameText.text = info.Name;
            if (previewDescriptionText != null) previewDescriptionText.text = info.Description;

            // 이전 스킬 영상은 어느 경우든 먼저 내린다(패시브로 옮겨 가도 뒤에서 계속 돌지 않게).
            if (previewVideo != null) previewVideo.Stop();

            // 소개 영상은 액티브 전용. 패시브면 영역을 끄고 로드하지 않는다.
            SetMediaActive(info.IsActive);
            if (!info.IsActive) return;

            // 영상 주소가 있으면 영상, 없거나 로드에 실패하면 아이콘으로 대신한다.
            if (!string.IsNullOrEmpty(info.PreviewMediaAddress) && previewVideo != null)
                LoadPreviewVideo(info.PreviewMediaAddress, info.IconAddress);
            else
                LoadPreviewImage(info.IconAddress);
        }

        // 소개 영상 이미지 영역만 켜고 끈다(패시브=off). 루트 미지정 시 previewImage 오브젝트로 폴백.
        private void SetMediaActive(bool on)
        {
            if (previewMediaRoot != null) previewMediaRoot.SetActive(on);
            else if (previewImage != null) previewImage.gameObject.SetActive(on);
        }

        /// <summary>확인/취소를 눌러 창을 닫는다(Presenter가 로직을 마친 뒤 호출).</summary>
        public void Close() => RequestClose();

        // 프리뷰를 비운다(재오픈 시 직전 스킬이 남지 않게).
        private void ClearPreview()
        {
            if (previewVideo != null) previewVideo.Stop();
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

        // 영상 로드·재생은 AddressableVideoView가 맡는다. 영상 주소가 미등록이거나 로드에 실패하면
        // (외부 에셋 미동기화 PC 포함) 아이콘으로 대신한다.
        private void LoadPreviewVideo(string address, string fallbackIconAddress)
        {
            previewAddress = address;
            if (previewImage != null)
            {
                previewImage.sprite = null;
                previewImage.enabled = false;
            }

            previewVideo.Play(address, () => LoadPreviewImage(fallbackIconAddress));
        }
    }
}
