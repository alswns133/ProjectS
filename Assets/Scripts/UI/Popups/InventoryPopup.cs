using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 인벤토리 창(순수 View). 장비/소모품 탭을 전환하며 보유 아이템을 고정 크기 슬롯 그리드에 표시한다
    /// (보유분만 앞에서부터 채우고 나머지는 빈칸). 하단에 보유 골드를 표시하고, 소비품 슬롯 클릭 시 사용한다.
    /// 소유·획득·사용·세이브는 <see cref="InventoryManager"/>가 담당하고, 이 창은 표시와 입력 전달만 한다.
    ///
    /// 장비·스킬 등 다른 창과 <b>동시에 띄워두는</b> 창이라 스택형 BasePanel이 아니라 리스트형 BasePopup이다
    /// (서로 밀어내지 않고 공존). 이동식(<see cref="DraggableWindow"/>)이라 배치가 사용자 설정처럼 유지된다.
    /// I키(<see cref="InventoryHotkey"/>)로 이 창만 토글한다.
    /// </summary>
    public class InventoryPopup : BasePopup
    {
        // 어느 카테고리 묶음을 보여줄지. 장비는 EquipmentInstance, 소모품은 스택(소비품+재료)을 나열한다.
        private enum Tab
        {
            Equipment,
            Consumable
        }

        [Header("탭")]
        [SerializeField] private Button equipmentTabButton;
        [SerializeField] private Button consumableTabButton;

        [Header("슬롯 그리드")]
        [SerializeField] private Transform slotRoot;
        [SerializeField] private InventoryItemSlot slotPrefab;
        [Tooltip("표시할 총 슬롯(빈칸 포함) 개수")]
        [SerializeField] private int slotCount = 30;

        [Header("기타")]
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private Button closeButton;

        private readonly List<InventoryItemSlot> slots = new();
        private Tab currentTab = Tab.Equipment;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만. (OnInit은 SetActive(true) 이전 1회 호출 — BasePopup.Show 참고)
            if (equipmentTabButton != null) equipmentTabButton.onClick.AddListener(() => SetTab(Tab.Equipment));
            if (consumableTabButton != null) consumableTabButton.onClick.AddListener(() => SetTab(Tab.Consumable));
            if (closeButton != null) closeButton.onClick.AddListener(() => RequestClose());

            EnsureSlots();
        }

        protected override void OnShow()
        {
            // 열려 있는 동안 아이템 변화(획득·사용)를 즉시 반영하고, 골드 표시를 실제 보유량으로 맞춘다.
            InventoryEvents.OnItemAdded += HandleItemsChanged;
            InventoryEvents.OnItemRemoved += HandleItemsChanged;
            PlayerEvents.OnGoldChanged += SetGold;

            // 골드 소유자(InventoryManager)에게 현재 값 재발행을 요청한다(HUD와 같은 스냅샷 경로).
            PlayerEvents.FireStatsRefreshRequested();

            Rebuild();
        }

        protected override void OnHide()
        {
            InventoryEvents.OnItemAdded -= HandleItemsChanged;
            InventoryEvents.OnItemRemoved -= HandleItemsChanged;
            PlayerEvents.OnGoldChanged -= SetGold;

            // 슬롯 위에 마우스를 둔 채 I키로 닫으면 PointerExit가 안 와 툴팁이 남을 수 있어 강제로 숨긴다.
            ItemTooltip.Instance?.Hide();
        }

        // 탭을 바꾸고 그리드를 다시 채운다. 같은 탭을 다시 눌러도 재빌드만 하므로 안전하다.
        private void SetTab(Tab tab)
        {
            currentTab = tab;
            Rebuild();
        }

        // 슬롯 프리팹을 slotCount만큼 1회 생성해 재사용한다(탭 전환/갱신마다 파괴·재생성하지 않음).
        private void EnsureSlots()
        {
            if (slotRoot == null || slotPrefab == null) return;

            while (slots.Count < slotCount)
            {
                InventoryItemSlot slot = Instantiate(slotPrefab, slotRoot);
                slot.SetRightClickHandler(OnSlotRightClicked);
                slots.Add(slot);
            }
        }

        // 현재 탭의 보유 아이템으로 앞에서부터 채우고 남는 칸은 빈칸으로 둔다.
        private void Rebuild()
        {
            EnsureSlots();

            InventoryManager inv = InventoryManager.Instance;
            int filled = 0;

            if (inv != null)
            {
                if (currentTab == Tab.Equipment)
                {
                    foreach (var equip in inv.OwnedEquipment)
                    {
                        if (filled >= slots.Count) break;
                        slots[filled].SetEquipment(equip);
                        filled++;
                    }
                }
                else
                {
                    foreach (var stack in inv.StackItems)
                    {
                        if (filled >= slots.Count) break;
                        slots[filled].SetStack(stack);
                        filled++;
                    }
                }
            }

            for (int i = filled; i < slots.Count; i++)
                slots[i].SetEmpty();
        }

        // 소비품 슬롯을 우클릭하면 커서 위치에 컨텍스트 메뉴(등록1/등록2/사용)를 연다.
        // 장비·재료 우클릭은 후속(장착/버리기)이라 지금은 무시한다. (좌클릭은 슬롯이 드래그로 처리)
        private void OnSlotRightClicked(InventoryItemSlot slot, PointerEventData eventData)
        {
            if (slot.Stack != null && slot.Stack.IsConsumable)
                ItemContextMenu.Instance?.Show(slot.Stack, eventData.position);
        }

        // 아이템 추가/제거 이벤트는 아이템 인자를 쓰지 않고 현재 탭 전체를 다시 그린다(수량 변화까지 반영).
        private void HandleItemsChanged(ItemData _)
        {
            if (IsVisible) Rebuild();
        }

        private void SetGold(int gold)
        {
            if (goldText != null) goldText.text = gold.ToString();
        }
    }
}
