using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Items;
using ProjectS.Players;

namespace ProjectS.Managers
{
    /// <summary>
    /// 재화(골드)·강화 재료·보유 아이템을 소유하는 매니저. 강화의 자원 공급자(<see cref="IEnhanceResources"/>)로 동작하고,
    /// 인벤토리 UI·아이템 획득·포션 퀵슬롯의 데이터 소유자다.
    ///
    /// 보유 아이템은 <b>위치 고정 격자</b>로 들고 있다 — 각 셀이 특정 슬롯이라 사용자가 드래그로 아이템을 원하는
    /// 슬롯(빈칸 포함)으로 옮기면 그 배치가 유지·저장된다(중간 빈칸 허용). 장비·소모품 탭은 각자 격자를 쓴다.
    /// (2026-07-23 TH / 2026-08-04 격자화)
    /// </summary>
    public class InventoryManager : MonoBehaviour, IEnhanceResources
    {
        public static InventoryManager Instance { get; private set; }

        /// <summary>인벤토리 한 탭의 슬롯 수(장비/소모품 각각). UI 그리드 슬롯 수와 일치시킨다.</summary>
        public const int Capacity = 30;

        /// <summary>HUD 포션 퀵슬롯 개수.</summary>
        public const int QuickSlotCount = 2;

        [Header("보유 재화 (스캐폴드: 실제 인벤토리 연동 전 임시 초기값)")]
        [SerializeField] private int gold = 50000;
        [SerializeField] private int lowMaterial = 30;
        [SerializeField] private int highMaterial = 10;

        // 보유 장비 격자(장비 탭). 셀 = 장비 or null. 강화창도 같은 인스턴스를 참조한다(OwnedEquipment).
        private readonly EquipmentInstance[] equipGrid = new EquipmentInstance[Capacity];

        // 보유 스택형 아이템 격자(소모품 탭 = 소비품 + 재료). 셀 = 스택 or null.
        private readonly ItemStack[] consumeGrid = new ItemStack[Capacity];

        // 소비품별 다음 사용 가능 시각(Time.time 기준). CooldownSec가 있는 소비품의 연타를 막는다.
        private readonly Dictionary<int, float> consumableCooldowns = new();

        // 포션 퀵슬롯에 등록된 소비품 itemId(0=빈칸). 인덱스 = HUD 슬롯 번호(0=Q, 1=E).
        private readonly int[] quickSlots = new int[QuickSlotCount];

        // 착용 중인 장비(부위별 1개). 가방 격자와 분리 — 착용분은 격자를 차지하지 않는다.
        private readonly Dictionary<EquipSlot, EquipmentInstance> equipped = new();

        /// <summary>현재 보유 골드.</summary>
        public int Gold => gold;

        /// <summary>현재 보유 하급 재료.</summary>
        public int LowMaterial => lowMaterial;

        /// <summary>현재 보유 상급 재료.</summary>
        public int HighMaterial => highMaterial;

        /// <summary>보유 장비 목록(강화 선택 팝업 등이 순회한다). 격자에서 빈칸을 뺀 것.</summary>
        public IReadOnlyList<EquipmentInstance> OwnedEquipment => NonNull(equipGrid);

        /// <summary>보유 스택형 아이템 목록(소비품 + 재료). 격자에서 빈칸을 뺀 것.</summary>
        public IReadOnlyList<ItemStack> StackItems => NonNull(consumeGrid);

        /// <summary>격자 인덱스의 장비(빈칸/범위밖이면 null). UI가 슬롯 위치대로 그릴 때 쓴다.</summary>
        /// <param name="index">격자 인덱스</param>
        public EquipmentInstance GetEquipmentAt(int index)
            => (index >= 0 && index < equipGrid.Length) ? equipGrid[index] : null;

        /// <summary>격자 인덱스의 스택(빈칸/범위밖이면 null). UI가 슬롯 위치대로 그릴 때 쓴다.</summary>
        /// <param name="index">격자 인덱스</param>
        public ItemStack GetStackAt(int index)
            => (index >= 0 && index < consumeGrid.Length) ? consumeGrid[index] : null;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // 스탯 스냅샷 재요청(씬 진입 등) 시 골드와 착용 장비 스탯을 함께 다시 반영한다.
        // 부트스트랩 복원 때는 플레이어가 아직 없을 수 있어(RecomputeEquipmentStats no-op), 씬 진입에서 이 훅이 확정 반영한다.
        private void OnEnable()
        {
            PlayerEvents.OnStatsRefreshRequested += PublishGold;
            PlayerEvents.OnStatsRefreshRequested += RecomputeEquipmentStatsSilent;
        }

        private void OnDisable()
        {
            PlayerEvents.OnStatsRefreshRequested -= PublishGold;
            PlayerEvents.OnStatsRefreshRequested -= RecomputeEquipmentStatsSilent;
        }

        // 재화는 테이블 없이 즉시 반영해 HUD가 바로 맞고, 아이템 복원은 정의 테이블(ItemData 등)이 필요해
        // JsonManager 로딩을 기다린 뒤 수행한다. async void는 Unity 진입점(Start)에서만 예외적으로 허용.
        private async void Start()
        {
            ApplySelectedCharacterSave();
            PublishGold();

            JsonManager json = JsonManager.Instance;
            if (json != null && !json.IsReady) await json.ReadyTask;
            if (this == null) return;

            RestoreFrom(GameSession.SelectedCharacter);
        }

        // 선택된 캐릭터 세이브(GameSession)의 재화를 반영한다. 세션이 없으면(직접 씬 테스트) 인스펙터 초기값을 둔다.
        private void ApplySelectedCharacterSave()
        {
            CharacterSaveData save = GameSession.SelectedCharacter;
            if (save == null) return;

            gold = save.gold;
            lowMaterial = save.lowMaterial;
            highMaterial = save.highMaterial;
        }

        /// <summary>현재 보유 재화와 아이템(장비·스택·퀵슬롯)을 세이브 데이터에 기록한다. 저장 시점에 호출한다.</summary>
        /// <param name="save">기록 대상 세이브(선택된 캐릭터). null이면 무시.</param>
        public void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            save.gold = gold;
            save.lowMaterial = lowMaterial;
            save.highMaterial = highMaterial;

            // 장비/스택은 격자 위치(slot)를 함께 저장해 배치가 유지되게 한다. 정의는 tableId로 복원.
            save.equipment = new List<EquipmentSave>();
            for (int i = 0; i < equipGrid.Length; i++)
            {
                EquipmentInstance eq = equipGrid[i];
                if (eq?.Item == null) continue;
                save.equipment.Add(new EquipmentSave
                {
                    tableId = eq.Item.Index,
                    enhanceStep = eq.EnhanceStep,
                    slot = i,
                    mainStat = eq.RolledMainStat,
                    options = SaveOptions(eq.Options),
                });
            }

            save.equipped = new List<EquippedSave>();
            foreach (KeyValuePair<EquipSlot, EquipmentInstance> kv in equipped)
            {
                EquipmentInstance eq = kv.Value;
                if (eq?.Item == null) continue;
                save.equipped.Add(new EquippedSave
                {
                    equipSlot = (int)kv.Key,
                    tableId = eq.Item.Index,
                    enhanceStep = eq.EnhanceStep,
                    mainStat = eq.RolledMainStat,
                    options = SaveOptions(eq.Options),
                });
            }

            save.stackItems = new List<ItemStackSave>();
            for (int i = 0; i < consumeGrid.Length; i++)
            {
                ItemStack stack = consumeGrid[i];
                if (stack?.Item == null || stack.Count <= 0) continue;
                save.stackItems.Add(new ItemStackSave { tableId = stack.Item.Index, count = stack.Count, slot = i });
            }

            save.potionQuickSlots = (int[])quickSlots.Clone();
        }

        /// <summary>
        /// 세이브의 보유 아이템을 격자로 복원한다(부트스트랩에서 JsonManager 로딩 후 1회).
        /// 저장된 slot 위치에 배치하고, 범위 밖·중복·구버전 세이브(slot 누락→0)면 다음 빈 셀로 폴백한다(자동 마이그레이션).
        /// 정의가 사라진 아이템은 건너뛴다. 재화는 <see cref="ApplySelectedCharacterSave"/>가 먼저 처리한다.
        /// </summary>
        /// <param name="save">복원할 세이브. null이면 아무것도 하지 않는다.</param>
        public void RestoreFrom(CharacterSaveData save)
        {
            if (save == null) return;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;   // 호출측(Start)이 Ready를 보장한다

            System.Array.Clear(equipGrid, 0, equipGrid.Length);
            if (save.equipment != null)
            {
                foreach (EquipmentSave es in save.equipment)
                {
                    if (es == null) continue;

                    EquipmentInstance inst = BuildEquipment(json, es.tableId, es.enhanceStep, es.mainStat, es.options);
                    if (inst != null) PlaceAt(equipGrid, es.slot, inst);   // 정의 없으면 skip
                }
            }

            equipped.Clear();
            if (save.equipped != null)
            {
                foreach (EquippedSave es in save.equipped)
                {
                    if (es == null) continue;

                    EquipmentInstance inst = BuildEquipment(json, es.tableId, es.enhanceStep, es.mainStat, es.options);
                    if (inst != null) equipped[(EquipSlot)es.equipSlot] = inst;
                }
            }

            System.Array.Clear(consumeGrid, 0, consumeGrid.Length);
            if (save.stackItems != null)
            {
                foreach (ItemStackSave ss in save.stackItems)
                {
                    if (ss == null || ss.count <= 0) continue;

                    ItemData item = json.Get<ItemData>(ss.tableId);
                    if (item == null) continue;   // 정의 없음 → 건너뜀

                    ConsumableData consumable = json.Get<ConsumableData>(ss.tableId);
                    PlaceAt(consumeGrid, ss.slot, new ItemStack(item, consumable, ss.count));
                }
            }

            // 포션 퀵슬롯 복원: 여전히 소비품인 등록만 살린다.
            for (int i = 0; i < quickSlots.Length; i++) quickSlots[i] = 0;
            if (save.potionQuickSlots != null)
            {
                int n = Mathf.Min(save.potionQuickSlots.Length, quickSlots.Length);
                for (int i = 0; i < n; i++)
                {
                    int id = save.potionQuickSlots[i];
                    if (id == 0) continue;

                    ItemData item = json.Get<ItemData>(id);
                    quickSlots[i] = (item != null && item.Category == ItemCategory.Consumable) ? id : 0;
                }
            }

            // 살아있는 UI가 반영하도록 발행(늦게 켜지는 슬롯은 자체적으로 Get*을 읽는다).
            InventoryEvents.FireInventoryChanged();
            for (int i = 0; i < quickSlots.Length; i++)
                InventoryEvents.FireQuickSlotChanged(i, quickSlots[i]);

            // 착용 장비 스탯 반영(조용히 — HUD 준비 전이라 발행하면 미초기화 게이지에서 NRE).
            // 플레이어가 아직 없으면 no-op → 씬 진입 시 OnStatsRefreshRequested가 다시 조용히 건다.
            RecomputeEquipmentStats(false);

            // 보유 아이콘을 미리 로드해 캐시에 데워둔다 → 인벤/장비창 첫 오픈 때 "빈칸→채워짐" 팝인 제거.
            // 아틀라스로 묶여 있으면 첫 주소가 아틀라스를 통째로 올려 나머지도 그 즉시 준비된다.
            PreloadOwnedIcons();
        }

        // 보유 장비·스택·착용 장비의 아이콘 주소를 모아 미리 로드한다(부트스트랩 복원 직후).
        private void PreloadOwnedIcons()
        {
            var addresses = new List<string>();

            foreach (EquipmentInstance eq in equipGrid) AddIconAddress(addresses, eq?.Item);
            foreach (ItemStack stack in consumeGrid) AddIconAddress(addresses, stack?.Item);
            foreach (EquipmentInstance eq in equipped.Values) AddIconAddress(addresses, eq?.Item);

            ItemIconLoader.Preload(addresses);
        }

        private static void AddIconAddress(List<string> list, ItemData item)
        {
            if (item != null && !string.IsNullOrEmpty(item.IconAddress)) list.Add(item.IconAddress);
        }

        // 옵션 목록을 세이브 DTO로 직렬화한다(표시용 퍼센트·라벨은 복원 때 재조립하므로 타입·값만).
        private static List<ItemOptionSave> SaveOptions(IReadOnlyList<ItemOption> options)
        {
            var list = new List<ItemOptionSave>(options?.Count ?? 0);
            if (options != null)
                foreach (ItemOption o in options)
                    list.Add(new ItemOptionSave { type = (int)o.Type, value = o.Value });
            return list;
        }

        // 세이브된 옵션을 런타임 ItemOption으로 복원한다. 퍼센트·라벨은 ItemOptionData(타입×등급)에서 재조립.
        private static List<ItemOption> RebuildOptions(ItemGrade grade, List<ItemOptionSave> saved)
        {
            var list = new List<ItemOption>(saved?.Count ?? 0);
            if (saved == null) return list;

            JsonManager json = JsonManager.Instance;
            foreach (ItemOptionSave os in saved)
            {
                if (os == null) continue;

                var type = (ItemOptionType)os.type;
                bool isPercent = false;
                string label = type.ToString();

                if (json != null && json.IsReady)
                    foreach (ItemOptionData od in json.ItemOptionDict.Values)
                        if (od.OptionType == type && od.Grade == grade)
                        {
                            isPercent = od.IsPercent;
                            label = od.Label;
                            break;
                        }

                list.Add(new ItemOption(type, os.value, isPercent, label));
            }
            return list;
        }

        // 세이브 값으로 장비 인스턴스를 재구성한다. 정의가 사라졌으면 null(호출측이 skip).
        private static EquipmentInstance BuildEquipment(JsonManager json, int tableId, int enhanceStep, int mainStat, List<ItemOptionSave> optionSaves)
        {
            ItemData item = json.Get<ItemData>(tableId);
            EquipmentData equip = json.Get<EquipmentData>(tableId);
            if (item == null || equip == null) return null;

            List<ItemOption> options = RebuildOptions(item.Grade, optionSaves);
            int rolled = mainStat > 0 ? mainStat : -1;   // 구세이브(0)는 생성자에서 MainStatBase로 폴백
            return new EquipmentInstance(item, equip, enhanceStep, rolled, options);
        }

        // 현재 보유 골드를 발행한다. 초기 1회(Start)와 스냅샷 재요청(씬 진입) 양쪽에서 쓴다.
        private void PublishGold() => PlayerEvents.FireGoldChanged(gold);

        /// <summary>지정 비용을 감당할 수 있는지.</summary>
        public bool CanAfford(int zeny, int low, int high)
            => gold >= zeny && lowMaterial >= low && highMaterial >= high;

        /// <summary>비용을 차감하고 골드 변경을 브로드캐스트한다. 호출 전 CanAfford로 확인한다.</summary>
        public void Spend(int zeny, int low, int high)
        {
            gold -= zeny;
            lowMaterial -= low;
            highMaterial -= high;
            PlayerEvents.FireGoldChanged(gold);

            PlayerSaveService.SaveNow();   // 커밋: 구매·강화는 의도된 재화 소모 → 즉시 저장
        }

        /// <summary>골드를 지급하고 변경을 브로드캐스트한다(퀘스트 보상 등). 0 이하면 무시한다.</summary>
        /// <param name="amount">추가할 골드량</param>
        public void AddGold(int amount)
        {
            if (amount <= 0) return;

            gold += amount;
            PlayerEvents.FireGoldChanged(gold);
            PlayerSaveService.MarkDirty();
        }

        /// <summary>
        /// 강화 대상 후보를 코드에서 직접 채워넣는다(디버그·테스트용). 일반 획득 경로는 <see cref="AddItem"/>다.
        /// </summary>
        /// <param name="instance">등록할 장비 인스턴스</param>
        public void RegisterEquipment(EquipmentInstance instance)
        {
            if (instance == null) return;
            PlaceAt(equipGrid, -1, instance);   // 첫 빈 셀
        }

        /// <summary>
        /// 아이템을 인벤토리에 추가한다(드랍·보상·구매 공통 진입점). 카테고리로 분기해 장비는 첫 빈 셀에,
        /// 소비품·재료는 스택으로 담고 <see cref="InventoryEvents.OnItemAdded"/>를 발행한다.
        /// 정의 테이블이 필요하므로 JsonManager 로딩(IsReady) 후 호출한다.
        /// </summary>
        /// <param name="itemId">아이템 테이블 ID(ItemData.Index)</param>
        /// <param name="count">추가 수량(장비는 개수만큼 인스턴스 생성). 1 미만이면 무시</param>
        public void AddItem(int itemId, int count = 1)
        {
            if (count <= 0) return;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady)
            {
                Debug.LogWarning("[Inventory] JsonManager 준비 전 AddItem 호출 — 무시됨");
                return;
            }

            ItemData item = json.Get<ItemData>(itemId);
            if (item == null)
            {
                Debug.LogWarning($"[Inventory] 정의 없는 아이템 추가 시도: {itemId}");
                return;
            }

            if (item.Category == ItemCategory.Weapon || item.Category == ItemCategory.Armor)
            {
                EquipmentData equip = json.Get<EquipmentData>(itemId);
                if (equip == null)
                {
                    Debug.LogWarning($"[Inventory] 장비 수치(EquipmentData) 없음: {itemId}");
                    return;
                }

                // 장비는 스택 불가 — 개수만큼 별개 인스턴스(옵션·주스탯 롤)를 빈 셀에 담는다.
                for (int i = 0; i < count; i++)
                    PlaceAt(equipGrid, -1, ItemOptionRoller.Create(item, equip));
            }
            else
            {
                // 소비품이면 효과 행이 붙고, 재료는 null(정상).
                ConsumableData consumable = json.Get<ConsumableData>(itemId);
                AddToStacks(item, consumable, count);
            }

            InventoryEvents.FireItemAdded(item);
            PlayerSaveService.MarkDirty();
        }

        // 스택형 아이템을 담는다. 같은 아이템의 여유 스택부터 채우고, 남으면 빈 셀에 새 스택(MaxStack 단위)을 만든다.
        private void AddToStacks(ItemData item, ConsumableData consumable, int count)
        {
            int remaining = count;

            foreach (ItemStack stack in consumeGrid)
            {
                if (stack == null || stack.Item.Index != item.Index || stack.IsFull) continue;

                remaining = stack.Add(remaining);
                if (remaining <= 0) return;
            }

            while (remaining > 0)
            {
                int cell = FirstEmpty(consumeGrid);
                if (cell < 0)
                {
                    Debug.LogWarning("[Inventory] 소모품 격자가 가득 차 획득분 일부가 유실됨");
                    return;
                }

                int take = Mathf.Min(remaining, item.MaxStack);
                consumeGrid[cell] = new ItemStack(item, consumable, take);
                remaining -= take;
            }
        }

        /// <summary>
        /// 인벤토리 안에서 아이템을 옮긴다(드래그-드롭). 빈 셀로 옮기면 이동, 다른 아이템 위로 옮기면 자리 교환(swap).
        /// 같은 탭 격자 안에서만 동작하며, 변경을 <see cref="InventoryEvents.OnInventoryChanged"/>로 알리고 저장 dirty로 잡는다.
        /// </summary>
        /// <param name="equipmentTab">true=장비 탭 격자, false=소모품 탭 격자</param>
        /// <param name="from">출발 격자 인덱스</param>
        /// <param name="to">도착 격자 인덱스</param>
        public void Move(bool equipmentTab, int from, int to)
        {
            if (from == to) return;

            if (equipmentTab) SwapCells(equipGrid, from, to);
            else SwapCells(consumeGrid, from, to);

            InventoryEvents.FireInventoryChanged();
            PlayerSaveService.MarkDirty();
        }

        // ---------- 파괴(버리기) ----------

        /// <summary>
        /// 가방의 장비 한 점을 파괴한다(우클릭 컨텍스트 메뉴 → 확인 팝업을 거친 뒤 호출한다).
        /// 격자에서 제거하고 <see cref="InventoryEvents.OnItemRemoved"/>·<see cref="InventoryEvents.OnInventoryChanged"/>를 발행한다.
        /// 강화·옵션이 붙어 있어도 복구할 수 없는 의도적 행동이라, 오토세이브를 기다리지 않고 즉시 저장(SaveNow)으로 커밋한다
        /// (크래시로 파괴가 되살아나지 않게 함).
        /// </summary>
        /// <param name="instance">파괴할 장비 인스턴스(가방에 있던 것). 착용 중이거나 이미 사라진 인스턴스면 아무 일도 하지 않는다.</param>
        /// <returns>실제로 파괴했으면 true</returns>
        public bool DiscardEquipment(EquipmentInstance instance)
        {
            if (instance?.Item == null) return false;
            if (!RemoveFromGrid(equipGrid, instance)) return false;   // 가방에 없음(착용 중/이미 파괴됨)

            InventoryEvents.FireItemRemoved(instance.Item);
            InventoryEvents.FireInventoryChanged();
            PlayerSaveService.SaveNow();
            return true;
        }

        /// <summary>
        /// 소모품·재료 스택을 통째로 파괴한다(우클릭 컨텍스트 메뉴 → 확인 팝업을 거친 뒤 호출한다).
        /// 셀을 비우고 변경을 발행한다. 파괴는 의도적·비가역 행동이라 즉시 저장(SaveNow)으로 커밋한다.
        /// </summary>
        /// <param name="stack">파괴할 스택(가방에 있던 것). 이미 사라진 스택이면 아무 일도 하지 않는다.</param>
        /// <returns>실제로 파괴했으면 true</returns>
        public bool DiscardStack(ItemStack stack)
        {
            if (stack?.Item == null) return false;
            if (!RemoveFromGrid(consumeGrid, stack)) return false;

            InventoryEvents.FireItemRemoved(stack.Item);
            InventoryEvents.FireInventoryChanged();
            PlayerSaveService.SaveNow();
            return true;
        }

        // ---------- 장착 ----------

        /// <summary>지정 부위에 착용 중인 장비(없으면 null).</summary>
        /// <param name="slot">장착 부위</param>
        public EquipmentInstance GetEquipped(EquipSlot slot)
            => equipped.TryGetValue(slot, out EquipmentInstance eq) ? eq : null;

        /// <summary>
        /// 장비를 착용한다. 부위는 <see cref="EquipmentData.EquipSlot"/>으로 정하고, 무기는 캐릭터 직업과 무기 종류가
        /// 맞아야 한다. 기존 착용분은 가방으로 되돌린다. 성공 시 스탯 재계산·<see cref="InventoryEvents.OnItemEquipped"/> 발행.
        /// </summary>
        /// <param name="instance">착용할 장비(가방에 있던 것)</param>
        /// <returns>착용에 성공했으면 true</returns>
        public bool Equip(EquipmentInstance instance)
        {
            if (instance?.Equipment == null) return false;

            EquipSlot slot = instance.Equipment.EquipSlot;
            if (slot == EquipSlot.None) return false;

            // 무기는 직업 제한: 검사=검, 거너=총.
            if (slot == EquipSlot.Weapon && !CanUseWeapon(instance.Equipment.WeaponType)) return false;

            // 가방에서 빼고(착용은 격자를 차지하지 않음), 기존 착용분이 있으면 가방으로 되돌린다.
            RemoveFromGrid(equipGrid, instance);
            if (equipped.TryGetValue(slot, out EquipmentInstance old) && old != null)
                PlaceAt(equipGrid, -1, old);

            equipped[slot] = instance;

            InventoryEvents.FireItemEquipped(instance.Item);
            InventoryEvents.FireInventoryChanged();   // 가방 격자 변경(착용분 제거/기존분 복귀)
            RecomputeEquipmentStats();
            PlayerSaveService.SaveNow();   // 장착은 의도적 행동 → 즉시 커밋(오토세이브 대기 없이 Firebase 저장)
            return true;
        }

        /// <summary>
        /// 지정 부위의 장비를 해제해 가방으로 되돌린다(장비창→인벤 드래그·클릭 해제 공통).
        /// <paramref name="targetIndex"/> 셀 상태에 따라 가른다:
        /// <list type="bullet">
        /// <item>비어 있으면 → 그 자리에 놓는다.</item>
        /// <item>이 부위로 착용 가능한 장비가 들어 있으면 → <b>맞바꾼다</b>(그 장비를 착용하고 착용 중이던 것을 그 셀에 둠 —
        ///       인벤끼리 드롭 swap과 같은 결. net 0이라 가방이 가득 차 있어도 가능).</item>
        /// <item>맞바꿀 수 없는 다른 부위 장비가 들어 있거나 -1이면 → 첫 빈 셀에 놓는다(둘 곳이 없으면 실패).</item>
        /// </list>
        /// 성공 시 스탯 재계산·<see cref="InventoryEvents.OnItemUnequipped"/>(스왑이면 <see cref="InventoryEvents.OnItemEquipped"/>도) 발행.
        /// </summary>
        /// <param name="slot">해제할 부위</param>
        /// <param name="targetIndex">되돌릴 가방 격자 셀(드래그로 놓은 슬롯). 기본 -1이면 첫 빈 셀(클릭 해제 경로).</param>
        /// <returns>해제(또는 스왑)에 성공했으면 true</returns>
        public bool Unequip(EquipSlot slot, int targetIndex = -1)
        {
            if (!equipped.TryGetValue(slot, out EquipmentInstance instance) || instance == null) return false;

            // 대상 셀에 이 부위로 착용 가능한 장비가 있으면 맞바꾼다(무기는 직업 제한도 만족해야 함).
            EquipmentInstance target = GetEquipmentAt(targetIndex);
            bool canSwap = target?.Equipment != null
                && target.Equipment.EquipSlot == slot
                && (slot != EquipSlot.Weapon || CanUseWeapon(target.Equipment.WeaponType));

            if (canSwap)
            {
                equipGrid[targetIndex] = instance;   // 착용 중이던 것 → 놓은 셀
                equipped[slot] = target;             // 놓은 셀의 장비 → 착용

                InventoryEvents.FireItemUnequipped(instance.Item);
                InventoryEvents.FireItemEquipped(target.Item);
            }
            else
            {
                if (FirstEmpty(equipGrid) < 0) return false;   // 가방 가득 → 해제 불가

                equipped[slot] = null;
                PlaceAt(equipGrid, targetIndex, instance);   // 빈 셀이면 그 자리, 아니면 첫 빈 셀

                InventoryEvents.FireItemUnequipped(instance.Item);
            }

            InventoryEvents.FireInventoryChanged();
            RecomputeEquipmentStats();
            PlayerSaveService.SaveNow();   // 해제·스왑은 의도적 행동 → 즉시 커밋
            return true;
        }

        // 무기 착용 가능 판정: 캐릭터 타입(1=검사·2=거너)과 무기 종류가 맞아야 한다. 알 수 없으면 허용.
        private static bool CanUseWeapon(WeaponType weapon)
        {
            if (weapon == WeaponType.None) return true;

            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            int charType = player != null ? player.Stats.CharacterId : 0;

            if (charType == 1) return weapon == WeaponType.Sword;
            if (charType == 2) return weapon == WeaponType.Gun;
            return true;
        }

        // 착용 장비 전체를 합산해 플레이어 스탯에 반영한다. 착용/해제(publish:true)와 복원·리프레시(publish:false)에서 호출.
        // publish=false면 값만 조용히 반영 — 부트스트랩/HUD 준비 전 발행이 미초기화 FillGauge를 건드리는 NRE를 막는다.
        private void RecomputeEquipmentStats(bool publish = true)
        {
            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player == null) return;

            EquipmentStats stats = EquipmentStatCalculator.Compute(equipped.Values);
            player.Stats.ApplyEquipmentStats(stats, publish);
        }

        // OnStatsRefreshRequested(씬 진입·HUD 재표시)에서 장비 스탯을 다시 반영하되 조용히(발행 없이) 한다.
        // 발행은 같은 신호로 도는 PlayerStats.PublishAllStats가 담당한다(장비값은 이미 반영돼 있어 정확).
        private void RecomputeEquipmentStatsSilent() => RecomputeEquipmentStats(false);

        /// <summary>
        /// 소비품 1개를 사용한다. 회복 효과를 적용하고 수량을 1 차감한다(즉시형은 총량 한 번, 지속형은 초당 회복).
        /// 쿨다운 중이거나 소비품이 아니거나 플레이어가 없으면 아무것도 하지 않고 false를 돌려준다.
        /// </summary>
        /// <param name="stack">사용할 스택</param>
        /// <returns>실제로 사용했으면 true</returns>
        public bool UseConsumable(ItemStack stack)
        {
            if (stack == null || !stack.IsConsumable || stack.Count <= 0) return false;

            int itemId = stack.Item.Index;
            if (consumableCooldowns.TryGetValue(itemId, out float readyAt) && Time.time < readyAt)
                return false;   // 아직 쿨다운

            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player == null) return false;

            if (player.Stats.IsDead) return false; // 죽은 상태 → 사용 안 함

            if (player.Stats.CurrentHp == player.Stats.MaxHp) return false;   // 체력 만땅 → 사용 안 함

            ConsumableData data = stack.Consumable;
            if (data.DurationSec > 0f)
                StartCoroutine(HealOverTime(data.HealAmount, data.DurationSec));
            else
                player.Stats.Heal(data.HealAmount);

            if (data.CooldownSec > 0f)
                consumableCooldowns[itemId] = Time.time + data.CooldownSec;

            InventoryEvents.FireConsumableUsed(itemId, data.CooldownSec);

            // 수량 차감(0이면 격자 셀 비움).
            stack.Remove(1);
            if (stack.Count <= 0) RemoveFromGrid(consumeGrid, stack);

            InventoryEvents.FireItemRemoved(stack.Item);
            PlayerSaveService.MarkDirty();
            return true;
        }

        // 지속형 소비품의 초당 회복. 사용 중 플레이어가 사라지면(씬 전환 등) 조용히 멈춘다.
        private IEnumerator HealOverTime(int perSecond, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                yield return new WaitForSeconds(1f);
                elapsed += 1f;

                Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
                if (player == null) yield break;
                player.Stats.Heal(perSecond);
            }
        }

        // ---------- 포션 퀵슬롯 ----------

        /// <summary>지정 퀵슬롯에 등록된 소비품 itemId(빈칸이면 0). 범위를 벗어나면 0.</summary>
        /// <param name="index">슬롯 번호(0=Q, 1=E)</param>
        public int GetQuickSlot(int index)
            => (index >= 0 && index < quickSlots.Length) ? quickSlots[index] : 0;

        /// <summary>
        /// 퀵슬롯에 소비품을 등록한다(우클릭 메뉴·드래그 드롭 공통 진입점). 소비품만 허용하며 itemId 0은 해제.
        /// 변경을 <see cref="InventoryEvents.OnQuickSlotChanged"/>로 알리고 저장 dirty로 잡는다.
        /// </summary>
        /// <param name="index">슬롯 번호(0=Q, 1=E)</param>
        /// <param name="itemId">등록할 소비품 itemId(0=해제)</param>
        public void RegisterQuickSlot(int index, int itemId)
        {
            if (index < 0 || index >= quickSlots.Length) return;

            if (itemId != 0)
            {
                JsonManager json = JsonManager.Instance;
                ItemData item = json != null ? json.Get<ItemData>(itemId) : null;
                if (item == null || item.Category != ItemCategory.Consumable) return;   // 소비품만
            }

            quickSlots[index] = itemId;
            InventoryEvents.FireQuickSlotChanged(index, itemId);
            PlayerSaveService.MarkDirty();
        }

        /// <summary>
        /// 퀵슬롯에 등록된 소비품을 사용한다(Q/E 입력). 등록이 없거나 재고가 없으면 아무것도 하지 않는다.
        /// </summary>
        /// <param name="index">슬롯 번호(0=Q, 1=E)</param>
        /// <returns>실제로 사용했으면 true</returns>
        public bool UseQuickSlot(int index)
        {
            if (index < 0 || index >= quickSlots.Length) return false;

            int itemId = quickSlots[index];
            if (itemId == 0) return false;

            ItemStack stack = FindStack(itemId);
            return stack != null && UseConsumable(stack);
        }

        /// <summary>지정 아이템의 총 보유 수량(스택 합). HUD 퀵슬롯 수량 표시에 쓴다.</summary>
        /// <param name="itemId">아이템 테이블 ID</param>
        public int GetConsumableCount(int itemId)
        {
            int total = 0;
            foreach (ItemStack stack in consumeGrid)
                if (stack != null && stack.Item != null && stack.Item.Index == itemId) total += stack.Count;
            return total;
        }

        // 지정 itemId의 사용 가능한(수량>0) 스택 하나를 찾는다. 없으면 null.
        private ItemStack FindStack(int itemId)
        {
            foreach (ItemStack stack in consumeGrid)
                if (stack != null && stack.Item != null && stack.Item.Index == itemId && stack.Count > 0) return stack;
            return null;
        }

        // ---------- 격자 헬퍼 ----------

        // 격자에서 빈칸을 뺀 목록을 만든다(강화·기존 순회 호환용). 자주 안 불리므로 매번 생성해도 무방.
        private static List<T> NonNull<T>(T[] grid) where T : class
        {
            List<T> list = new List<T>();
            foreach (T cell in grid)
                if (cell != null) list.Add(cell);
            return list;
        }

        private static int FirstEmpty<T>(T[] grid) where T : class
        {
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == null) return i;
            return -1;
        }

        // 원하는 index에 배치하되, 범위 밖·이미 참·index<0이면 첫 빈 셀로 폴백한다(세이브 복원·획득 공통).
        private static void PlaceAt<T>(T[] grid, int index, T item) where T : class
        {
            if (index >= 0 && index < grid.Length && grid[index] == null)
            {
                grid[index] = item;
                return;
            }

            int cell = FirstEmpty(grid);
            if (cell >= 0) grid[cell] = item;
            else Debug.LogWarning("[Inventory] 격자가 가득 차 아이템을 배치하지 못함");
        }

        // 격자에서 해당 인스턴스를 찾아 비운다. 실제로 지웠으면 true(파괴 경로가 헛이벤트를 막는 데 쓴다).
        private static bool RemoveFromGrid<T>(T[] grid, T item) where T : class
        {
            for (int i = 0; i < grid.Length; i++)
                if (ReferenceEquals(grid[i], item)) { grid[i] = null; return true; }
            return false;
        }

        private static void SwapCells<T>(T[] grid, int a, int b) where T : class
        {
            if (a < 0 || a >= grid.Length || b < 0 || b >= grid.Length) return;
            (grid[a], grid[b]) = (grid[b], grid[a]);
        }

        // ---------- 아이템 판매 ----------

        /// <summary>스택에서 count개를 팔아 골드를 지급한다. 판매가 = ItemData.SellPrice × count.</summary>
        public bool SellStack(ItemStack stack, int count)
        {
            if (stack?.Item == null || count <= 0 || stack.Count < count) return false;

            int payout = stack.Item.SellPrice * count;

            stack.Remove(count);

            if(stack.Count <= 0)
                RemoveFromGrid(consumeGrid, stack); // 기존 private 헬퍼 재사용

            AddGold(payout);
            InventoryEvents.FireItemRemoved(stack.Item);
            InventoryEvents.FireInventoryChanged();
            PlayerSaveService.SaveNow();   // 판매는 의도적 행동 → 즉시 커밋

            return true;

        }

        /// <summary>가방의 장비 한 점을 팔아 골드를 지급한다.</summary>
        public bool SellEquipment(EquipmentInstance instance)
        {
            if (instance?.Item == null) return false;
            if(!RemoveFromGrid(equipGrid, instance)) return false;   // 가방에 없음(착용 중/이미 사라짐)

            AddGold(instance.Item.SellPrice);
            InventoryEvents.FireItemRemoved(instance.Item);
            InventoryEvents.FireInventoryChanged();
            PlayerSaveService.SaveNow();   // 판매는 의도적 행동 → 즉시 커밋

            return true;
        }
    }
}
