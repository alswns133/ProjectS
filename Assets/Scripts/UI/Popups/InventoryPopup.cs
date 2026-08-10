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

        [Header("탭 선택 표시 (선택된 탭 이미지 컬러를 바꾼다)")]
        [SerializeField] private Image equipmentTabImage;
        [SerializeField] private Image consumableTabImage;
        [SerializeField] private Color tabNormalColor = new Color(0.4f, 0.42f, 0.5f, 1f);
        [SerializeField] private Color tabSelectedColor = Color.white;

        // 마지막으로 본 탭을 기기 로컬(PlayerPrefs)에 저장 — 껐다 켜도 보던 탭이 유지된다(창 위치와 같은 방침).
        private const string TabPrefKey = "inventory.tab";

        [Header("슬롯 그리드")]
        [SerializeField] private Transform slotRoot;
        [SerializeField] private InventoryItemSlot slotPrefab;
        // 슬롯 수는 InventoryManager.Capacity(격자 크기)와 일치시킨다 — 아이템이 슬롯 위치를 갖기 때문.

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

            // 이동식 창 위치 저장 키를 코드로 주입(인벤은 유일하므로 인스펙터 매직 스트링 대신 상수).
            // OnInit은 SetActive 이전 1회라, 뒤이은 DraggableWindow.OnEnable이 이 키로 위치를 복원한다.
            if (TryGetComponent(out DraggableWindow window))
                window.SetWindowId(WindowIds.Inventory);

            EnsureSlots();
        }

        protected override void OnShow()
        {
            // 열려 있는 동안 아이템 변화(획득·사용·이동)를 즉시 반영하고, 골드 표시를 실제 보유량으로 맞춘다.
            InventoryEvents.OnItemAdded += HandleItemsChanged;
            InventoryEvents.OnItemRemoved += HandleItemsChanged;
            InventoryEvents.OnInventoryChanged += HandleInventoryChanged;
            PlayerEvents.OnGoldChanged += SetGold;

            // 골드 소유자(InventoryManager)에게 현재 값 재발행을 요청한다(HUD와 같은 스냅샷 경로).
            PlayerEvents.FireStatsRefreshRequested();

            // 저장된 탭을 복원하고 선택 표시를 맞춘 뒤 그린다(껐다 켜도 보던 탭 유지).
            currentTab = (Tab)PlayerPrefs.GetInt(TabPrefKey, (int)Tab.Equipment);
            UpdateTabVisual();
            Rebuild();
        }

        protected override void OnHide()
        {
            InventoryEvents.OnItemAdded -= HandleItemsChanged;
            InventoryEvents.OnItemRemoved -= HandleItemsChanged;
            InventoryEvents.OnInventoryChanged -= HandleInventoryChanged;
            PlayerEvents.OnGoldChanged -= SetGold;

            // 슬롯 위에 마우스를 둔 채 I키로 닫으면 PointerExit가 안 와 툴팁이 남을 수 있어 강제로 숨긴다.
            ItemTooltip.Instance?.Hide();
        }

        // 탭을 바꾸고, 선택 표시를 갱신하고, 선택 탭을 저장한 뒤 그리드를 다시 채운다.
        private void SetTab(Tab tab)
        {
            currentTab = tab;

            PlayerPrefs.SetInt(TabPrefKey, (int)tab);
            PlayerPrefs.Save();

            UpdateTabVisual();
            Rebuild();
        }

        // 선택된 탭 이미지를 selected 컬러로, 나머지는 normal 컬러로 바꾼다. 코드가 직접 제어하므로 창 밖을
        // 클릭해 버튼 선택이 풀려도(EventSystem 디셀렉트) 표시가 사라지지 않는다.
        // (탭 Button의 Transition은 None으로 두어 버튼 자체 하이라이트와 충돌하지 않게 한다.)
        private void UpdateTabVisual()
        {
            if (equipmentTabImage != null)
                equipmentTabImage.color = currentTab == Tab.Equipment ? tabSelectedColor : tabNormalColor;
            if (consumableTabImage != null)
                consumableTabImage.color = currentTab == Tab.Consumable ? tabSelectedColor : tabNormalColor;
        }

        // 슬롯 프리팹을 격자 크기(Capacity)만큼 1회 생성해 재사용한다(탭 전환/갱신마다 파괴·재생성하지 않음).
        private void EnsureSlots()
        {
            if (slotRoot == null || slotPrefab == null) return;

            while (slots.Count < InventoryManager.Capacity)
            {
                InventoryItemSlot slot = Instantiate(slotPrefab, slotRoot);
                slot.SetGridIndex(slots.Count);   // 리스트 위치 = 격자 셀 인덱스(고정). 장비 드래그 해제의 목적지로 쓰인다.
                slot.SetRightClickHandler(OnSlotRightClicked);
                slot.SetDropHandler(OnSlotDropped);
                slots.Add(slot);
            }
        }

        // 격자 위치대로 그린다 — 슬롯 i는 격자 셀 i를 표시한다(빈 셀은 빈칸). 아이템이 슬롯 위치를 가지므로 압축하지 않는다.
        private void Rebuild()
        {
            EnsureSlots();

            InventoryManager inv = InventoryManager.Instance;

            for (int i = 0; i < slots.Count; i++)
            {
                if (inv == null)
                {
                    slots[i].SetEmpty();
                    continue;
                }

                if (currentTab == Tab.Equipment)
                {
                    var equip = inv.GetEquipmentAt(i);
                    if (equip != null) slots[i].SetEquipment(equip);
                    else slots[i].SetEmpty();
                }
                else
                {
                    var stack = inv.GetStackAt(i);
                    if (stack != null) slots[i].SetStack(stack);
                    else slots[i].SetEmpty();
                }
            }
        }

        // 인벤 슬롯끼리 드롭 → 현재 탭 격자에서 위치 이동/교환. 슬롯의 격자 인덱스는 slots 리스트상 위치와 같다.
        private void OnSlotDropped(InventoryItemSlot source, InventoryItemSlot target)
        {
            int from = slots.IndexOf(source);
            int to = slots.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return;

            InventoryManager.Instance?.Move(currentTab == Tab.Equipment, from, to);
            // Move가 OnInventoryChanged를 발행해 HandleInventoryChanged가 그리드를 다시 그린다.
        }

        // 위치 이동 등 배치 변경 시 현재 탭을 다시 그린다.
        private void HandleInventoryChanged()
        {
            if (IsVisible) Rebuild();
        }

        // 슬롯을 우클릭하면 커서 위치에 컨텍스트 메뉴를 연다 — 장비는 [장착][파괴], 소비품은 [등록1][등록2][사용][파괴].
        // 재료(비소비 스택)는 아직 메뉴가 없다. (좌클릭은 슬롯이 드래그로 처리)
        private void OnSlotRightClicked(InventoryItemSlot slot, PointerEventData eventData)
        {
            if (slot.Equipment != null)
                ItemContextMenu.Instance?.Show(slot.Equipment, eventData.position);
            else if (slot.Stack != null && slot.Stack.IsConsumable)
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
