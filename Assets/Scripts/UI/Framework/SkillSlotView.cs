using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Items;
using ProjectS.Skills;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 스킬창 슬롯 한 칸: 아이콘 + "현재/최대" 레벨 텍스트 + ▲/▼ 스테퍼.
    /// 값 계산은 하지 않는 순수 뷰다 — 버튼 클릭·마우스 진입을 이벤트로 올리고,
    /// 표시는 Presenter가 넘겨준 값(<see cref="SetLevel"/>)만 반영한다.
    /// </summary>
    /// <remarks>
    /// 액티브·패시브 슬롯이 같은 위젯이라 프레임워크 뷰로 둔다. 아이콘은 아이템 아이콘과 같은
    /// 어드레서블 로더(<see cref="ItemIconLoader"/>)를 태운다(스킬 아이콘 주소도 같은 경로).
    /// hover 프리뷰가 뜨려면 이 오브젝트에 raycastTarget인 Graphic(배경/아이콘)이 있어야 한다.
    /// </remarks>
    public class SkillSlotView : MonoBehaviour, IPointerEnterHandler
    {
        [SerializeField] private Image icon;
        [Tooltip("\"현재/최대\" 레벨 표기(예: 1/5).")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Button upButton;
        [SerializeField] private Button downButton;

        // 비동기 아이콘 로드 중 슬롯이 다른 스킬로 갱신되면 늦게 온 스프라이트를 버리는 기준.
        private string currentAddress;
        private bool wired;

        /// <summary>이 슬롯이 표시 중인 스킬 식별자. Presenter가 조회·편집 키로 쓴다.</summary>
        public int SkillId { get; private set; }

        /// <summary>▲(레벨 올리기)를 눌렀을 때. 인자는 이 슬롯이다.</summary>
        public event Action<SkillSlotView> Increased;

        /// <summary>▼(레벨 내리기)를 눌렀을 때.</summary>
        public event Action<SkillSlotView> Decreased;

        /// <summary>마우스를 올렸을 때(우측 프리뷰 갱신용).</summary>
        public event Action<SkillSlotView> Focused;

        /// <summary>
        /// 슬롯에 스킬을 바인딩한다. 레벨 수치는 <see cref="SetLevel"/>로 따로 넣는다
        /// (배치 편집 중 레벨만 자주 갱신되므로 아이콘 바인딩과 분리한다).
        /// </summary>
        /// <param name="info">표시할 스킬의 정적 정보</param>
        public void Bind(SkillSlotInfo info)
        {
            EnsureWired();
            gameObject.SetActive(true);
            SkillId = info.SkillId;
            currentAddress = info.IconAddress;
            LoadIcon(info.IconAddress);
        }

        /// <summary>
        /// 레벨 표기와 ▲/▼ 활성 상태를 갱신한다.
        /// </summary>
        /// <param name="current">현재(편집 중) 레벨</param>
        /// <param name="max">최대 레벨</param>
        /// <param name="canUp">▲를 누를 수 있는가(상한·SP 여유 반영)</param>
        /// <param name="canDown">▼를 누를 수 있는가(이번 세션 배치분 존재)</param>
        public void SetLevel(int current, int max, bool canUp, bool canDown)
        {
            if (levelText != null) levelText.text = $"{current}/{max}";
            if (upButton != null) upButton.interactable = canUp;
            if (downButton != null) downButton.interactable = canDown;
        }

        /// <summary>슬롯을 통째로 숨긴다(그룹 슬롯 수보다 데이터가 적을 때).</summary>
        public void SetEmpty()
        {
            SkillId = 0;
            currentAddress = null;
            gameObject.SetActive(false);
        }

        /// <summary>마우스를 올리면 프리뷰 갱신을 요청한다.</summary>
        public void OnPointerEnter(PointerEventData eventData) => Focused?.Invoke(this);

        // 버튼 리스너는 한 번만 건다. Bind가 Awake보다 먼저 불릴 수 있어(비활성 상태로 초기화하는 경우)
        // 여기서 지연 배선한다.
        private void EnsureWired()
        {
            if (wired) return;
            wired = true;

            if (upButton != null) upButton.onClick.AddListener(() => Increased?.Invoke(this));
            if (downButton != null) downButton.onClick.AddListener(() => Decreased?.Invoke(this));
        }

        private async void LoadIcon(string address)
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            Sprite sprite = await ItemIconLoader.LoadAsync(address);

            if (this == null || icon == null) return;
            if (!string.Equals(currentAddress, address)) return;   // 슬롯이 다른 스킬로 교체됨

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
