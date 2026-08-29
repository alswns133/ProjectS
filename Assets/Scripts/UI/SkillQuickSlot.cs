using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Skills;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 스킬 단축키 슬롯 한 칸(1~4). 등록된 액티브 스킬의 <b>아이콘만</b> 표시하고, 스킬창(K)에서 드래그한 스킬을
    /// 드롭받아 등록한다. HUD 슬롯끼리 드래그하면 자리를 바꾸고, 슬롯 밖으로 끌어 놓거나 우클릭하면 해제한다.
    /// </summary>
    /// <remarks>
    /// <b>쿨다운은 이 컴포넌트가 다루지 않는다</b> — HUD에 이미 있는 <see cref="Framework.SkillCooldownSlot"/>
    /// (HUDPanel.skillSlots)이 담당한다. 그래서 인스펙터엔 <see cref="slotNumber"/>만 지정하면 되고, 아이콘 Image는
    /// 비워 두면 이 오브젝트의 <see cref="Image"/>를 자동으로 쓴다(슬롯 본체 이미지가 곧 스킬 아이콘인 경우).
    /// 본체가 프레임이고 아이콘이 따로면 그 Image를 <see cref="icon"/>에 지정한다. 실제 발동은 1~4 키가 하고
    /// (PlayerCombat이 <see cref="SkillState.GetSlot"/>로 조회), 이 슬롯은 표시·등록만 한다.
    /// 드래그/드롭/우클릭을 받으려면 raycastTarget인 Graphic이 있어야 한다.
    /// </remarks>
    public class SkillQuickSlot : MonoBehaviour,
        IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("슬롯 번호(1~4). SkillState 로드아웃 슬롯 번호와 일치.")]
        [SerializeField] private int slotNumber = 1;
        [Tooltip("등록 스킬 아이콘을 그릴 Image(비우면 이 오브젝트의 Image를 자동 사용).")]
        [SerializeField] private Image icon;
        [Tooltip("SG(소울게이지)가 부족할 때 켤 표시(번개 아이콘 등). 비워두면 표시 안 함.")]
        [SerializeField] private GameObject insufficientGaugeIcon;
        [Tooltip("SG 부족 시 스킬 아이콘을 흐리게 할 알파값(0~1). 번개 표시와 함께 시각적으로 강조.")]
        [SerializeField, Range(0f, 1f)] private float insufficientAlpha = 0.4f;
        [Tooltip("마을 등 스킬을 쓸 수 없는 구역에서 아이콘을 흐리게 할 알파값(0~1). SG 부족보다 더 눌러 사용 불가를 알림.")]
        [SerializeField, Range(0f, 1f)] private float unusableAlpha = 0.35f;

        private int registeredId;
        private float registeredSgCost;   // 등록 스킬의 SG 소모량(부족 표시 비교용)
        private float currentSg = float.MaxValue;   // 현재 SG(첫 이벤트 전엔 충분한 것으로 본다)
        private bool combatUsable = true;   // 현재 구역에서 스킬을 쓸 수 있는지(마을=false). 마을에선 아이콘을 흐리게.
        private GameObject dragGhost;

        // 드래그가 어떤 슬롯에 드롭됐는지 추적한다. 드롭되지 않은 채 끝나면(슬롯 밖) 해제로 본다.
        private static bool dropConsumed;

        private int SlotNumber => slotNumber;

        private void Awake()
        {
            if (icon == null) icon = GetComponent<Image>();
        }

        private void OnEnable()
        {
            SkillEvents.OnLoadoutChanged += HandleLoadoutChanged;
            PlayerEvents.OnSGChanged += HandleSgChanged;
            PlayerEvents.OnCombatZoneChanged += HandleCombatZoneChanged;

            // 켤 때 현재 등록·SG·구역 상태를 끌어와 맞춘다.
            registeredId = SkillState.GetSlot(slotNumber);
            var player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player != null)
            {
                if (player.Stats != null) currentSg = player.Stats.CurrentSkillGauge;
                // 스폰 순서상 EnterVillage/EnterDungeon이 이 구독보다 먼저 불렸을 수 있어, 초기값을 직접 끌어온다.
                combatUsable = player.IsCombatEnabled;
            }

            Refresh();
        }

        private void OnDisable()
        {
            SkillEvents.OnLoadoutChanged -= HandleLoadoutChanged;
            PlayerEvents.OnSGChanged -= HandleSgChanged;
            PlayerEvents.OnCombatZoneChanged -= HandleCombatZoneChanged;
            SkillTooltip.Instance?.Hide(this);
        }

        /// <summary>스킬창 슬롯을 드롭하면 등록, 다른 HUD 슬롯을 드롭하면 자리 교환.</summary>
        public void OnDrop(PointerEventData eventData)
        {
            dropConsumed = true;

            GameObject dragged = eventData.pointerDrag;
            if (dragged == null) return;

            // ① 스킬창(K)의 액티브 슬롯 → 등록
            SkillSlotView source = dragged.GetComponent<SkillSlotView>();
            if (source != null && source.SkillId != 0)
            {
                SkillState.SetSlot(slotNumber, source.SkillId);
                return;
            }

            // ② 다른 HUD 슬롯 → 자리 교환
            SkillQuickSlot other = dragged.GetComponent<SkillQuickSlot>();
            if (other != null && other != this)
                SkillState.SwapSlots(other.SlotNumber, slotNumber);
        }

        // ---- 좌클릭 드래그(고스트): 등록된 스킬을 집어 다른 슬롯으로 옮기거나(스왑) 밖에 놓아 해제 ----
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (registeredId == 0 || icon == null || icon.sprite == null) return;

            dropConsumed = false;
            SkillTooltip.Instance?.Hide();
            SkillTooltip.DragSuppressed = true;

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            dragGhost = new GameObject("SkillDragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(canvas.rootCanvas != null ? canvas.rootCanvas.transform : canvas.transform, false);
            dragGhost.transform.SetAsLastSibling();

            Image ghostImg = dragGhost.GetComponent<Image>();
            ghostImg.sprite = icon.sprite;
            ghostImg.raycastTarget = false;
            ghostImg.color = new Color(1f, 1f, 1f, 0.7f);

            // 아이콘이 앵커 stretch면 sizeDelta가 (0,0)이라 고스트가 0×0으로 안 보인다 → 실제 렌더 크기로 폴백.
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

            // 어느 슬롯에도 드롭되지 않았으면(슬롯 밖에 놓음) 등록 해제.
            if (!dropConsumed) SkillState.ClearSlot(slotNumber);
        }

        /// <summary>우클릭으로 등록을 해제한다.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && registeredId != 0)
                SkillState.ClearSlot(slotNumber);
        }

        /// <summary>마우스를 올리면 등록된 스킬의 툴팁을 띄운다(빈 슬롯이면 무시).</summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (registeredId != 0) SkillTooltip.Instance?.Show(registeredId, eventData.position, this);
        }

        /// <summary>마우스가 벗어나면 툴팁을 숨긴다.</summary>
        public void OnPointerExit(PointerEventData eventData) => SkillTooltip.Instance?.Hide();

        private void HandleLoadoutChanged(int changedSlot, int skillId)
        {
            if (changedSlot != slotNumber) return;
            registeredId = skillId;
            Refresh();
        }

        // SG가 바뀌면 부족 표시만 다시 판단한다(아이콘은 그대로).
        private void HandleSgChanged(float cur, float max)
        {
            currentSg = cur;
            UpdateGaugeIndicator();
        }

        // 마을↔던전 전환 시 흐림 표시를 다시 판단한다.
        private void HandleCombatZoneChanged(bool combatEnabled)
        {
            combatUsable = combatEnabled;
            UpdateGaugeIndicator();
        }

        // 등록 스킬 아이콘 + SG 부족 표시 갱신(빈 슬롯이면 아이콘을 끄고 표시도 끈다).
        private void Refresh()
        {
            // 부족 표시 비교용 SG 소모량 캐시(빈 슬롯이면 0). SgCost는 SkillTable(액티브 skillId 공유)에서 온다.
            registeredSgCost = (registeredId != 0 && JsonManager.Instance != null)
                ? (JsonManager.Instance.Get<SkillTable>(registeredId)?.SgCost ?? 0f)
                : 0f;

            if (registeredId == 0)
            {
                if (icon != null) { icon.sprite = null; icon.enabled = false; }
                UpdateGaugeIndicator();
                return;
            }

            SkillGrowthTable row = JsonManager.Instance != null ? JsonManager.Instance.Get<SkillGrowthTable>(registeredId) : null;
            LoadIcon(row != null ? row.IconAddress : null);
            UpdateGaugeIndicator();
        }

        // 아이콘 흐림·SG 부족 표시를 한곳에서 갱신한다. 알파의 주인은 이 함수뿐이라
        // 마을 흐림과 SG 부족 흐림이 서로 덮어쓰며 싸우지 않는다.
        private void UpdateGaugeIndicator()
        {
            // 마을 등 사용 불가 구역이면 SG와 무관하게 흐리게 하고, 번개(부족) 표시는 켜지 않는다.
            bool insufficient = combatUsable && registeredId != 0 && registeredSgCost > 0f && currentSg < registeredSgCost;

            if (insufficientGaugeIcon != null && insufficientGaugeIcon.activeSelf != insufficient)
                insufficientGaugeIcon.SetActive(insufficient);

            // 아이콘 알파: 사용 불가 구역 > SG 부족 > 정상 순으로 눌러 표시. RGB는 유지하고 알파만 바꾼다.
            if (icon != null)
            {
                float alpha = !combatUsable ? unusableAlpha : (insufficient ? insufficientAlpha : 1f);
                Color c = icon.color;
                c.a = alpha;
                icon.color = c;
            }
        }

        private async void LoadIcon(string address)
        {
            if (icon == null) return;
            if (string.IsNullOrEmpty(address)) { icon.sprite = null; icon.enabled = false; return; }

            int requested = registeredId;
            Sprite sprite = await ItemIconLoader.LoadAsync(address);
            if (this == null || icon == null) return;
            if (registeredId != requested) return;   // 대기 중 등록이 바뀜

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }
    }
}
