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
    /// 스킬창 슬롯 한 칸: 아이콘 + "현재/최대" 레벨 텍스트 + ▲/▼ 스테퍼. 값 계산은 하지 않는 순수 뷰다.
    /// <b>액티브 스킬</b>은 추가로 단축키 등록을 지원한다 — <b>좌클릭 드래그</b>로 HUD 스킬 슬롯에 얹고(고스트 아이콘),
    /// <b>우클릭</b>으로 등록 슬롯 선택 메뉴 콜백을 부른다. 마우스를 올리면 스킬 툴팁을 띄운다.
    /// </summary>
    /// <remarks>
    /// 의존 방향(화면→Framework)을 지키려 슬롯은 컨텍스트 메뉴를 직접 열지 않고 우클릭 콜백(<see cref="RightClicked"/>)만
    /// 올린다 — 호스트 패널(SkillPopup)이 메뉴를 연다(인벤 <c>InventoryItemSlot</c>과 같은 규칙). 툴팁은 Framework
    /// 위젯이라 슬롯이 직접 부른다. 포인터/드래그 이벤트를 받으려면 raycastTarget인 Graphic이 있어야 한다.
    /// </remarks>
    public class SkillSlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image icon;
        [Tooltip("\"현재/최대\" 레벨 표기(예: 1/5).")]
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private Button upButton;
        [SerializeField] private Button downButton;
        [Tooltip("잠긴 스킬(미해금)에 켤 자물쇠 표시(선택).")]
        [SerializeField] private GameObject lockedOverlay;
        [Tooltip("잠긴 스킬의 아이콘 알파(흐리게).")]
        [SerializeField, Range(0f, 1f)] private float lockedAlpha = 0.4f;

        // 비동기 아이콘 로드 중 슬롯이 다른 스킬로 갱신되면 늦게 온 스프라이트를 버리는 기준.
        private string currentAddress;
        private bool wired;
        private bool isActive;          // 액티브만 단축키 등록(드래그·우클릭 메뉴) 대상
        private bool isUnlocked = true; // 잠긴 스킬은 투자·등록 불가, 흐리게 표시
        private GameObject dragGhost;

        /// <summary>이 슬롯이 표시 중인 스킬 식별자. Presenter가 조회·편집 키로, HUD 드롭 대상이 등록 키로 쓴다.</summary>
        public int SkillId { get; private set; }

        /// <summary>▲(레벨 올리기)를 눌렀을 때.</summary>
        public event Action<SkillSlotView> Increased;

        /// <summary>▼(레벨 내리기)를 눌렀을 때.</summary>
        public event Action<SkillSlotView> Decreased;

        /// <summary>마우스를 올렸을 때(우측 프리뷰 갱신용).</summary>
        public event Action<SkillSlotView> Focused;

        /// <summary>우클릭했을 때(호스트가 등록 슬롯 선택 메뉴를 연다). 커서 위치를 위해 이벤트를 함께 넘긴다.</summary>
        public event Action<SkillSlotView, PointerEventData> RightClicked;

        /// <summary>슬롯에 스킬을 바인딩한다. 레벨 수치는 <see cref="SetLevel"/>로 따로 넣는다.</summary>
        /// <param name="info">표시할 스킬의 정적 정보</param>
        public void Bind(SkillSlotInfo info)
        {
            EnsureWired();
            gameObject.SetActive(true);
            SkillId = info.SkillId;
            isActive = info.IsActive;
            isUnlocked = info.IsUnlocked;
            currentAddress = info.IconAddress;
            LoadIcon(info.IconAddress);

            // 잠긴 스킬: 자물쇠 표시 + 아이콘 흐리게.
            if (lockedOverlay != null) lockedOverlay.SetActive(!isUnlocked);
            if (icon != null)
            {
                Color c = icon.color;
                c.a = isUnlocked ? 1f : lockedAlpha;
                icon.color = c;
            }
        }

        /// <summary>레벨 표기와 ▲/▼ 활성 상태를 갱신한다.</summary>
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
            isActive = false;
            currentAddress = null;
            gameObject.SetActive(false);
        }

        /// <summary>마우스를 올리면 프리뷰 갱신을 요청하고 스킬 툴팁을 띄운다.</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            Focused?.Invoke(this);
            if (SkillId != 0) SkillTooltip.Instance?.Show(SkillId, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => SkillTooltip.Instance?.Hide();

        /// <summary>우클릭이면 등록 메뉴 콜백을 부른다(액티브만). 좌클릭은 스테퍼 버튼이 처리하므로 여기선 무시.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && isActive && isUnlocked && SkillId != 0)
                RightClicked?.Invoke(this, eventData);
        }

        // ---- 좌클릭 드래그(고스트): InventoryItemSlot과 동일 패턴. 액티브만 집을 수 있다 ----
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!isActive || !isUnlocked || SkillId == 0 || icon == null) return;

            SkillTooltip.Instance?.Hide();
            SkillTooltip.DragSuppressed = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            dragGhost = new GameObject("SkillDragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(TopmostCanvas(canvas).transform, false);
            dragGhost.transform.SetAsLastSibling();

            Image ghostImg = dragGhost.GetComponent<Image>();
            ghostImg.sprite = icon.sprite;
            ghostImg.raycastTarget = false;
            ghostImg.color = new Color(1f, 1f, 1f, 0.7f);

            // 아이콘이 앵커 stretch면 sizeDelta가 (0,0)이라 고스트가 0×0으로 안 보인다 → 실제 렌더 크기(rect.size)로,
            // 그마저 0이면 기본값으로 폴백한다.
            Vector2 size = icon.rectTransform.rect.size;
            if (size.x < 1f || size.y < 1f) size = new Vector2(64f, 64f);
            ((RectTransform)dragGhost.transform).sizeDelta = size;
            dragGhost.transform.position = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragGhost != null) dragGhost.transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragGhost != null) Destroy(dragGhost);
            dragGhost = null;
            SkillTooltip.DragSuppressed = false;
        }

        /// <summary>창이 닫혀 슬롯이 비활성되면 이 슬롯이 띄운 툴팁을 닫는다.</summary>
        private void OnDisable() => SkillTooltip.Instance?.Hide(this);

        // 버튼 리스너는 한 번만 건다(Bind가 Awake보다 먼저 불릴 수 있어 지연 배선).
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
            if (!string.Equals(currentAddress, address)) return;

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        // 활성 캔버스 중 sortingOrder가 가장 높은 루트 캔버스(드래그 고스트를 어떤 창보다 위에 그리기 위함).
        private static Canvas TopmostCanvas(Canvas fallback)
        {
            Canvas top = fallback;
            int bestOrder = fallback != null ? fallback.sortingOrder : int.MinValue;

            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isActiveAndEnabled) continue;

                Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
                if (root.sortingOrder > bestOrder)
                {
                    bestOrder = root.sortingOrder;
                    top = root;
                }
            }

            return top;
        }
    }
}
