using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 상점의 판매 화면. 고정 개수 판매 슬롯 격자 + 상단 예상 판매 골드 + 최종 [판매하기] 버튼.
    ///
    /// <b>예약 모델</b>: 슬롯에 올려도 인벤 데이터는 그대로 두고 "팔 예정 목록"(<see cref="ShopSellEntry"/>)만 쌓는다.
    /// 실제 제거·골드 지급은 [판매하기]에서 한 번에 <see cref="ShopManager.SellAll"/>로 한다.
    /// 이렇게 하면 판매 화면을 나가거나 창을 닫거나 접속이 끊겨도 아이템이 사라지지 않는다(되돌리기 처리가 필요 없음).
    ///
    /// 인벤토리 쪽 입력(우클릭으로 바로 올리기)은 <see cref="Active"/>로 찾아 들어온다 — 판매 화면이 떠 있을 때만 non-null.
    /// 예약이 바뀔 때마다 <see cref="UIEvents.FireSellReservationChanged"/>로 인벤 창에 다시 그리라고 알린다.
    /// </summary>
    public class ShopSellView : MonoBehaviour
    {
        [Header("슬롯")]
        [SerializeField] private Transform slotRoot;
        [SerializeField] private ShopSellSlot slotPrefab;
        [Tooltip("판매 슬롯 개수(스샷 6×7=42). 다 차면 더 못 올린다.")]
        [SerializeField] private int slotCount = 42;

        [Header("표시")]
        [Tooltip("상단 골드 아이콘 옆 — 올린 항목들의 판매가 합계.")]
        [SerializeField] private TMP_Text totalGoldText;

        [Header("버튼 / 팝업")]
        [Tooltip("최종 판매 확정 버튼. 올린 게 없으면 비활성.")]
        [SerializeField] private Button sellConfirmButton;
        [SerializeField] private SellCountDialog countDialog;

        [Header("판매 확인")]
        [Tooltip("이 등급 이상의 장비가 섞여 있으면 [판매하기] 때 한 번 더 확인창을 띄운다(되팔기 불가라 실수 방지).")]
        [SerializeField] private ItemGrade confirmGrade = ItemGrade.Rare;

        [Header("문구")]
        [SerializeField] private string fullMessage = "더 이상 아이템을 판매할 수 없습니다.";
        [SerializeField] private string confirmMessage = "높은 등급의 장비가 포함되어 있습니다.\n정말 판매하시겠습니까?";

        private readonly List<ShopSellSlot> slots = new();

        /// <summary>지금 열려 있는 판매 화면. 인벤토리 우클릭이 여기로 라우팅된다. 판매 화면이 아니면 null.</summary>
        public static ShopSellView Active { get; private set; }

        private void Awake()
        {
            if (sellConfirmButton != null) sellConfirmButton.onClick.AddListener(OnSellConfirm);
            EnsureSlots();
        }

        private void OnEnable()
        {
            Active = this;

            // 다른 경로(포션 사용·장착·퀘스트 반납·파괴 등)로 인벤이 바뀌면 예약이 실제 보유량과 어긋날 수 있다.
            // 포션 사용은 OnInventoryChanged 없이 OnItemRemoved만 발행하므로 둘 다 듣는다.
            InventoryEvents.OnInventoryChanged += ValidateEntries;
            InventoryEvents.OnItemRemoved += OnItemRemoved;
            Refresh();
        }

        private void OnDisable()
        {
            // Active를 먼저 내려야, 아래 ClearAll이 알린 예약 변경에서 인벤 창이 "판매 화면 없음"으로 다시 그린다.
            if (Active == this) Active = null;
            InventoryEvents.OnInventoryChanged -= ValidateEntries;
            InventoryEvents.OnItemRemoved -= OnItemRemoved;

            // 판매 화면을 나가면(모드 전환·창 닫기) 예약을 비운다. 예약 모델이라 인벤엔 영향 없음.
            if (countDialog != null) countDialog.Close();
            ClearAll();
        }

        // ---------- 추가 ----------

        /// <summary>
        /// 인벤 슬롯의 아이템을 판매 목록에 올린다. 장비는 바로, 스택은 수량 팝업을 거친다.
        /// 인벤 우클릭(InventoryPopup)과 판매 슬롯 드롭(ShopSellSlot.OnDrop)이 공통으로 부른다.
        /// </summary>
        /// <param name="source">출발 인벤 슬롯.</param>
        /// <returns>입력을 판매 화면이 처리했으면 true(실패 토스트 포함). 인벤 쪽은 true면 기존 우클릭 메뉴를 띄우지 않는다.</returns>
        public bool TryAddFromInventory(InventoryItemSlot source)
        {
            if (source == null || source.IsEmpty) return false;

            // 수량 팝업이 떠 있는 동안 들어온 입력은 먹기만 한다(팝업 뒤에서 다른 아이템이 끼어들지 않게).
            if (countDialog != null && countDialog.IsOpen) return true;

            if (source.Equipment != null)
            {
                if (!IsReserved(source.Equipment)) AddEquipment(source.Equipment);
                return true;   // 이미 올라간 장비면 조용히 무시
            }

            ItemStack stack = source.Stack;
            if (stack == null) return false;

            int remaining = stack.Count - GetReservedCount(stack);
            if (remaining <= 0) return true;   // 이미 전부 올라감

            // 꽉 찼는지는 팝업을 띄우기 "전에" 거른다 — 개수까지 고르게 한 뒤 "못 올립니다"라고 하면 헛수고가 된다.
            // 같은 스택이 이미 올라가 있으면 그 칸에 합산하므로 빈칸이 없어도 된다.
            if (FindSlotOf(stack) == null && FindEmptySlot() == null)
            {
                ShowFull();
                return true;
            }

            if (countDialog != null) countDialog.Show(remaining, n => AddStack(stack, n));
            else AddStack(stack, remaining);   // 팝업 미배선이면 전부 올린다(기능이 통째로 막히지 않게)

            return true;
        }

        // 장비 한 개를 빈 슬롯에 올린다.
        private void AddEquipment(EquipmentInstance equipment)
        {
            ShopSellSlot slot = FindEmptySlot();
            if (slot == null)
            {
                ShowFull();
                return;
            }

            slot.Set(new ShopSellEntry(equipment));
            Refresh();
        }

        // 스택 n개를 올린다. 같은 스택이 이미 올라가 있으면 그 칸 개수에 합산(칸을 하나 더 쓰지 않음).
        private void AddStack(ItemStack stack, int count)
        {
            // 수량 팝업이 떠 있던 사이 포션을 쓰는 등으로 보유가 줄었을 수 있으니, 확정 시점 기준으로 다시 자른다.
            int remaining = stack.Count - GetReservedCount(stack);
            count = Mathf.Min(count, remaining);
            if (count <= 0) return;

            ShopSellSlot existing = FindSlotOf(stack);
            if (existing != null)
            {
                existing.Entry.Count += count;
                existing.Set(existing.Entry);   // 개수 표시 갱신
                Refresh();
                return;
            }

            ShopSellSlot slot = FindEmptySlot();
            if (slot == null)
            {
                ShowFull();
                return;
            }

            slot.Set(new ShopSellEntry(stack, count));
            Refresh();
        }

        // ---------- 제거 ----------

        /// <summary>판매 슬롯 하나를 비운다(슬롯 우클릭, 인벤으로 드래그 되돌리기).</summary>
        /// <param name="slot">비울 슬롯.</param>
        public void Remove(ShopSellSlot slot)
        {
            if (slot == null || slot.IsEmpty) return;

            slot.Set(null);
            Compact();
            Refresh();
        }

        // 전부 비운다(판매 완료, 판매 화면 이탈).
        private void ClearAll()
        {
            foreach (ShopSellSlot slot in slots) slot.Set(null);
            Refresh();
        }

        // 중간 빈칸을 없애 항목을 앞으로 당긴다. 빼고 나면 구멍이 남아 "몇 개 올렸는지"가 한눈에 안 보이므로
        // 올린 순서를 유지한 채 앞에서부터 다시 채운다.
        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < slots.Count; read++)
            {
                ShopSellEntry entry = slots[read].Entry;
                if (entry == null) continue;

                if (read != write)
                {
                    slots[write].Set(entry);
                    slots[read].Set(null);
                }
                write++;
            }
        }

        // ---------- 최종 판매 ----------

        // [판매하기] 버튼. 예약 목록 전체를 ShopManager에 넘겨 한 번에 판다.
        private void OnSellConfirm()
        {
            List<ShopSellEntry> entries = CollectEntries();
            if (entries.Count == 0) return;

            // 판매는 되돌릴 수 없으므로, 고등급 장비가 섞여 있으면 한 번 더 묻는다.
            // 확인창이 없으면(씬 미배치) 묻지 않고 판다 — 파괴와 달리 판매는 골드가 들어오는 정상 거래라 막지 않는다.
            if (ContainsHighGrade(entries) && ConfirmDialog.Instance != null)
            {
                ConfirmDialog.Instance.Show(confirmMessage, () => ExecuteSell(entries));
                return;
            }

            ExecuteSell(entries);
        }

        private void ExecuteSell(List<ShopSellEntry> entries)
        {
            if (ShopManager.Instance == null) return;

            // 성공 시 인벤은 OnInventoryChanged로, 골드는 OnGoldChanged로 각 창이 알아서 갱신된다.
            if (ShopManager.Instance.SellAll(entries)) ClearAll();
        }

        private List<ShopSellEntry> CollectEntries()
        {
            var list = new List<ShopSellEntry>();
            foreach (ShopSellSlot slot in slots)
                if (slot.Entry != null) list.Add(slot.Entry);
            return list;
        }

        private bool ContainsHighGrade(List<ShopSellEntry> entries)
        {
            foreach (ShopSellEntry entry in entries)
                if (entry.Equipment?.Item != null && entry.Equipment.Item.Grade >= confirmGrade) return true;
            return false;
        }

        // ---------- 조회 (인벤 표시·수량 상한에 사용) ----------

        /// <summary>이 장비가 이미 판매 슬롯에 올라가 있는지. 인벤 슬롯 회색 처리에도 쓴다.</summary>
        public bool IsReserved(EquipmentInstance equipment)
        {
            if (equipment == null) return false;

            foreach (ShopSellSlot slot in slots)
                if (slot.Entry != null && ReferenceEquals(slot.Entry.Equipment, equipment)) return true;
            return false;
        }

        /// <summary>이 스택에서 판매 슬롯에 올라간 개수. 수량 팝업 상한(보유 − 예약)과 인벤 남은 수량 표시에 쓴다.</summary>
        public int GetReservedCount(ItemStack stack)
        {
            ShopSellSlot slot = FindSlotOf(stack);
            return slot != null ? slot.Entry.Count : 0;
        }

        // ---------- 내부 ----------

        private void OnItemRemoved(ItemData _) => ValidateEntries();

        // 인벤이 다른 경로로 바뀌었을 때 예약을 실제 보유와 맞춘다.
        private void ValidateEntries()
        {
            InventoryManager inv = InventoryManager.Instance;
            if (inv == null) return;

            bool changed = false;

            foreach (ShopSellSlot slot in slots)
            {
                ShopSellEntry entry = slot.Entry;
                if (entry == null) continue;

                if (entry.Equipment != null)
                {
                    // 장착·파괴 등으로 가방에서 빠졌으면 예약도 뺀다.
                    if (!Contains(inv.OwnedEquipment, entry.Equipment))
                    {
                        slot.Set(null);
                        changed = true;
                    }
                    continue;
                }

                // 스택이 가방에서 사라졌거나(다 씀) 비었으면 빼고, 줄었으면 예약 개수를 보유량까지 깎는다.
                if (entry.Stack == null || entry.Stack.Count <= 0 || !Contains(inv.StackItems, entry.Stack))
                {
                    slot.Set(null);
                    changed = true;
                }
                else if (entry.Count > entry.Stack.Count)
                {
                    entry.Count = entry.Stack.Count;
                    slot.Set(entry);
                    changed = true;
                }
            }

            if (!changed) return;

            Compact();
            Refresh();
        }

        private static bool Contains<T>(IReadOnlyList<T> list, T target) where T : class
        {
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], target)) return true;
            return false;
        }

        // 합계 골드·확정 버튼 상태를 다시 계산하고, 인벤 창에 예약 표시를 갱신하라고 알린다(추가/제거/검증 후 항상 호출).
        private void Refresh()
        {
            int total = 0;
            bool any = false;

            foreach (ShopSellSlot slot in slots)
            {
                if (slot.Entry == null) continue;
                total += slot.Entry.TotalPrice;
                any = true;
            }

            if (totalGoldText != null) totalGoldText.text = total.ToString();
            if (sellConfirmButton != null) sellConfirmButton.interactable = any;

            UIEvents.FireSellReservationChanged();
        }

        private void ShowFull() => UIEvents.FireToast(fullMessage);

        private ShopSellSlot FindEmptySlot()
        {
            foreach (ShopSellSlot slot in slots)
                if (slot.IsEmpty) return slot;
            return null;
        }

        private ShopSellSlot FindSlotOf(ItemStack stack)
        {
            if (stack == null) return null;

            foreach (ShopSellSlot slot in slots)
                if (slot.Entry?.Stack != null && ReferenceEquals(slot.Entry.Stack, stack)) return slot;
            return null;
        }

        // 슬롯을 slotCount만큼 1회 생성한다(빈칸 포함 고정 격자 — InventoryPopup.EnsureSlots와 같은 방식).
        private void EnsureSlots()
        {
            if (slotRoot == null || slotPrefab == null) return;

            while (slots.Count < slotCount)
            {
                ShopSellSlot slot = Instantiate(slotPrefab, slotRoot);
                slot.name = $"SellSlot{slots.Count:00}";
                slot.Init(this);
                slot.Set(null);
                slots.Add(slot);
            }
        }
    }
}
