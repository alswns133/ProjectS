using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// HUD 포션 퀵슬롯 한 칸. 등록된 소비품의 아이콘·보유 수량·쿨다운을 표시하고, 인벤에서 드래그한 소비품을
    /// 드롭받아 등록한다(<see cref="IDropHandler"/>). 실제 사용은 Q/E 입력(<see cref="PotionQuickSlotHotkey"/>)이나
    /// 우클릭 메뉴가 <see cref="InventoryManager.UseQuickSlot"/>로 하고, 이 슬롯은 표시·드롭 등록만 담당한다.
    /// HUDPanel과 독립된 자체 구독 컴포넌트라 HUDPanel 코드를 건드리지 않는다.
    /// </summary>
    public class PotionQuickSlot : MonoBehaviour, IDropHandler
    {
        [Tooltip("슬롯 번호(0=Q, 1=E). InventoryManager 퀵슬롯 인덱스와 일치")]
        [SerializeField] private int slotIndex;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text countText;
        [Tooltip("쿨다운 오버레이. Image Type=Filled, Radial 360이면 시계방향으로 걷힌다")]
        [SerializeField] private Image cooldownOverlay;
        [Tooltip("Q/E 키 표기(선택)")]
        [SerializeField] private TMP_Text keyHintText;

        [SerializeField] private Color inStockColor = Color.white;
        [SerializeField] private Color emptyStockColor = new Color(1f, 1f, 1f, 0.4f);

        private int registeredId;
        private Coroutine cooldownRoutine;

        private void OnEnable()
        {
            InventoryEvents.OnQuickSlotChanged += HandleQuickSlotChanged;
            InventoryEvents.OnItemAdded += HandleItemsChanged;
            InventoryEvents.OnItemRemoved += HandleItemsChanged;
            InventoryEvents.OnConsumableUsed += HandleConsumableUsed;

            if (keyHintText != null) keyHintText.text = slotIndex == 0 ? "Q" : "E";

            // 이벤트를 놓쳤어도 켜질 때 현재 등록 상태를 끌어와 맞춘다(세이브 복원이 먼저 끝난 경우 대비).
            registeredId = InventoryManager.Instance != null ? InventoryManager.Instance.GetQuickSlot(slotIndex) : 0;
            Refresh();
            SetCooldownIdle();
        }

        private void OnDisable()
        {
            InventoryEvents.OnQuickSlotChanged -= HandleQuickSlotChanged;
            InventoryEvents.OnItemAdded -= HandleItemsChanged;
            InventoryEvents.OnItemRemoved -= HandleItemsChanged;
            InventoryEvents.OnConsumableUsed -= HandleConsumableUsed;
        }

        /// <summary>인벤에서 드래그한 소비품을 드롭하면 이 슬롯에 등록한다(재료·장비는 무시).</summary>
        public void OnDrop(PointerEventData eventData)
        {
            InventoryItemSlot slot = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryItemSlot>()
                : null;

            if (slot?.Stack == null || !slot.Stack.IsConsumable) return;

            InventoryManager.Instance?.RegisterQuickSlot(slotIndex, slot.Stack.Item.Index);
        }

        private void HandleQuickSlotChanged(int index, int itemId)
        {
            if (index != slotIndex) return;
            registeredId = itemId;
            Refresh();
        }

        // 보유 수량이 바뀌면(획득·사용) 등록 슬롯의 수량 표기를 갱신한다.
        private void HandleItemsChanged(ItemData _)
        {
            if (registeredId != 0) UpdateCount();
        }

        private void HandleConsumableUsed(int itemId, float cooldownSec)
        {
            if (itemId == registeredId && cooldownSec > 0f) StartCooldown(cooldownSec);
        }

        // 등록 아이템 아이콘 + 수량 전체 갱신.
        private void Refresh()
        {
            if (registeredId == 0)
            {
                if (icon != null) { icon.sprite = null; icon.enabled = false; }
                if (countText != null) countText.text = string.Empty;
                return;
            }

            ItemData item = JsonManager.Instance != null ? JsonManager.Instance.Get<ItemData>(registeredId) : null;
            LoadIcon(item);
            UpdateCount();
        }

        private void UpdateCount()
        {
            int count = InventoryManager.Instance != null ? InventoryManager.Instance.GetConsumableCount(registeredId) : 0;

            if (countText != null) countText.text = count.ToString();
            if (icon != null) icon.color = count > 0 ? inStockColor : emptyStockColor;   // 재고 0이면 흐리게
        }

        private async void LoadIcon(ItemData item)
        {
            if (icon == null) return;
            if (item == null) { icon.enabled = false; return; }

            Sprite sprite = await ItemIconLoader.LoadAsync(item.IconAddress);
            if (this == null || icon == null) return;
            if (registeredId != item.Index) return;   // 대기 중 등록이 바뀜

            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        // ---- 쿨다운 표시(SkillCooldownSlot 패턴: 시작 신호 한 번 → 자체 코루틴) ----
        private void StartCooldown(float duration)
        {
            if (cooldownOverlay == null || duration <= 0f) return;
            if (cooldownRoutine != null) StopCoroutine(cooldownRoutine);
            cooldownRoutine = StartCoroutine(CooldownRoutine(duration));
        }

        private IEnumerator CooldownRoutine(float duration)
        {
            float remaining = duration;
            cooldownOverlay.enabled = true;

            while (remaining > 0f)
            {
                cooldownOverlay.fillAmount = remaining / duration;
                yield return null;
                remaining -= Time.deltaTime;
            }

            SetCooldownIdle();
            cooldownRoutine = null;
        }

        private void SetCooldownIdle()
        {
            if (cooldownOverlay == null) return;
            cooldownOverlay.fillAmount = 0f;
            cooldownOverlay.enabled = false;
        }
    }
}
