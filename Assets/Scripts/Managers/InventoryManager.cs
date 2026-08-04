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
    /// 재화(골드)·강화 재료·보유 장비를 소유하는 매니저. 강화의 자원 공급자(<see cref="IEnhanceResources"/>)로 동작한다.
    /// 지금은 실제 저장/로드 없이 인스펙터 초기값만 들고 있는 스캐폴드다.
    /// 인벤토리 UI·아이템 획득 로직이 붙으면 이 클래스가 그 값의 소유자가 된다.
    /// (2026-07-23 TH)
    /// </summary>
    public class InventoryManager : MonoBehaviour, IEnhanceResources
    {
        public static InventoryManager Instance { get; private set; }

        [Header("보유 재화 (스캐폴드: 실제 인벤토리 연동 전 임시 초기값)")]
        [SerializeField] private int gold = 50000;
        [SerializeField] private int lowMaterial = 30;
        [SerializeField] private int highMaterial = 10;

        // 보유 장비(장비 탭). 강화창도 이 목록을 강화 대상으로 나열하므로 같은 인스턴스를 공유한다.
        private readonly List<EquipmentInstance> ownedEquipment = new();

        // 보유 스택형 아이템(소비품·재료 = 소모품 탭). 같은 아이템은 MaxStack까지 한 스택에 묶인다.
        private readonly List<ItemStack> stackItems = new();

        // 소비품별 다음 사용 가능 시각(Time.time 기준). CooldownSec가 있는 소비품의 연타를 막는다.
        private readonly Dictionary<int, float> consumableCooldowns = new();

        /// <summary>HUD 포션 퀵슬롯 개수.</summary>
        public const int QuickSlotCount = 2;

        // 포션 퀵슬롯에 등록된 소비품 itemId(0=빈칸). 인덱스 = HUD 슬롯 번호(0=Q, 1=E).
        private readonly int[] quickSlots = new int[QuickSlotCount];

        /// <summary>현재 보유 골드.</summary>
        public int Gold => gold;

        /// <summary>현재 보유 하급 재료.</summary>
        public int LowMaterial => lowMaterial;

        /// <summary>현재 보유 상급 재료.</summary>
        public int HighMaterial => highMaterial;

        /// <summary>보유 장비 목록(장비 탭·강화 선택 팝업이 나열한다).</summary>
        public IReadOnlyList<EquipmentInstance> OwnedEquipment => ownedEquipment;

        /// <summary>보유 스택형 아이템 목록(소모품 탭이 나열한다. 소비품 + 재료).</summary>
        public IReadOnlyList<ItemStack> StackItems => stackItems;

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

        // HUD가 (재)표시되며 스탯 스냅샷 재발행을 요청할 때(씬 진입 등) 골드도 함께 다시 쏜다.
        // 골드는 PlayerStats가 아니라 이 매니저가 소유하므로, 리프레시 요청을 여기서도 받아야
        // 씬 진입 시 실제 보유 골드가 HUD에 반영된다(예전 씬들이 하드코딩 값으로 덮던 것을 대체).
        private void OnEnable() => PlayerEvents.OnStatsRefreshRequested += PublishGold;
        private void OnDisable() => PlayerEvents.OnStatsRefreshRequested -= PublishGold;

        // 재화는 테이블 없이 즉시 반영해 HUD가 바로 맞고, 아이템 복원은 정의 테이블(ItemData 등)이 필요해
        // JsonManager 로딩을 기다린 뒤 수행한다. async void는 Unity 진입점(Start)에서만 예외적으로 허용
        // (JsonManager·QuestManager와 같은 방침).
        private async void Start()
        {
            // 선택된 캐릭터 세이브가 있으면 그 재화를 반영한 뒤, 구독자(HUD)에게 발행한다.
            ApplySelectedCharacterSave();
            PublishGold();

            JsonManager json = JsonManager.Instance;
            if (json != null && !json.IsReady) await json.ReadyTask;
            if (this == null) return;

            RestoreFrom(GameSession.SelectedCharacter);
        }

        // 선택된 캐릭터 세이브(GameSession)의 재화를 반영한다. 재화는 캐릭터별로 분리 저장된다.
        // 세션이 없으면(직접 씬 테스트) 인스펙터 초기값을 그대로 둔다.
        private void ApplySelectedCharacterSave()
        {
            CharacterSaveData save = GameSession.SelectedCharacter;
            if (save == null) return;

            gold = save.gold;
            lowMaterial = save.lowMaterial;
            highMaterial = save.highMaterial;
        }

        /// <summary>현재 보유 재화와 아이템(장비·스택)을 세이브 데이터에 기록한다. 저장 시점에 호출한다.</summary>
        /// <param name="save">기록 대상 세이브(선택된 캐릭터). null이면 무시.</param>
        public void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            save.gold = gold;
            save.lowMaterial = lowMaterial;
            save.highMaterial = highMaterial;

            // 장비는 인스턴스마다 다른 강화 단계만, 스택은 수량만 저장한다(정의는 tableId로 복원).
            save.equipment = new List<EquipmentSave>(ownedEquipment.Count);
            foreach (EquipmentInstance eq in ownedEquipment)
            {
                if (eq?.Item == null) continue;
                save.equipment.Add(new EquipmentSave { tableId = eq.Item.Index, enhanceStep = eq.EnhanceStep });
            }

            save.stackItems = new List<ItemStackSave>(stackItems.Count);
            foreach (ItemStack stack in stackItems)
            {
                if (stack?.Item == null || stack.Count <= 0) continue;
                save.stackItems.Add(new ItemStackSave { tableId = stack.Item.Index, count = stack.Count });
            }

            save.potionQuickSlots = (int[])quickSlots.Clone();
        }

        /// <summary>
        /// 세이브의 보유 아이템을 현재 상태로 복원한다(부트스트랩에서 JsonManager 로딩 후 1회).
        /// 정의가 사라진 아이템(테이블 변경 등)은 건너뛴다. 재화는 <see cref="ApplySelectedCharacterSave"/>가
        /// 먼저 처리하므로 여기서는 아이템만 재구성한다.
        /// </summary>
        /// <param name="save">복원할 세이브. null이면 아무것도 하지 않는다.</param>
        public void RestoreFrom(CharacterSaveData save)
        {
            if (save == null) return;

            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;   // 호출측(Start)이 Ready를 보장한다

            ownedEquipment.Clear();
            if (save.equipment != null)
            {
                foreach (EquipmentSave es in save.equipment)
                {
                    if (es == null) continue;

                    ItemData item = json.Get<ItemData>(es.tableId);
                    EquipmentData equip = json.Get<EquipmentData>(es.tableId);
                    if (item == null || equip == null) continue;   // 정의 없음 → 건너뜀

                    ownedEquipment.Add(new EquipmentInstance(item, equip, es.enhanceStep));
                }
            }

            stackItems.Clear();
            if (save.stackItems != null)
            {
                foreach (ItemStackSave ss in save.stackItems)
                {
                    if (ss == null || ss.count <= 0) continue;

                    ItemData item = json.Get<ItemData>(ss.tableId);
                    if (item == null) continue;   // 정의 없음 → 건너뜀

                    // 소비품이면 효과 행이 함께 붙는다. 재료는 ConsumableData가 없어 null(정상).
                    ConsumableData consumable = json.Get<ConsumableData>(ss.tableId);
                    stackItems.Add(new ItemStack(item, consumable, ss.count));
                }
            }

            // 포션 퀵슬롯 복원: 여전히 소비품인 등록만 살린다(테이블 변경·카테고리 바뀜 방어).
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

            // 이미 살아있는 HUD 슬롯이 반영하도록 발행(늦게 켜지는 슬롯은 자체적으로 GetQuickSlot을 읽는다).
            for (int i = 0; i < quickSlots.Length; i++)
                InventoryEvents.FireQuickSlotChanged(i, quickSlots[i]);
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

            // 커밋(①): 구매·강화는 의도된 재화 소모 → 즉시 저장.
            PlayerSaveService.SaveNow();
        }

        /// <summary>골드를 지급하고 변경을 브로드캐스트한다(퀘스트 보상 등). 0 이하면 무시한다.</summary>
        /// <param name="amount">추가할 골드량</param>
        public void AddGold(int amount)
        {
            if (amount <= 0) return;

            gold += amount;
            PlayerEvents.FireGoldChanged(gold);

            // 획득은 잦을 수 있어(드랍 등) dirty로 묶는다(②). 퀘스트 반납 보상은 반납 시 SaveNow가 함께 담는다.
            PlayerSaveService.MarkDirty();
        }

        /// <summary>
        /// 강화 대상 후보를 코드에서 직접 채워넣는다(디버그·테스트용). 일반 획득 경로는 <see cref="AddItem"/>다.
        /// </summary>
        /// <param name="instance">등록할 장비 인스턴스</param>
        public void RegisterEquipment(EquipmentInstance instance)
        {
            if (instance != null) ownedEquipment.Add(instance);
        }

        /// <summary>
        /// 아이템을 인벤토리에 추가한다(드랍·보상·구매 공통 진입점). 카테고리로 분기해
        /// 장비는 개별 인스턴스로, 소비품·재료는 스택으로 담고 <see cref="InventoryEvents.OnItemAdded"/>를 발행한다.
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

                // 장비는 스택 불가 — 개수만큼 별개 인스턴스(+0)로 만든다.
                for (int i = 0; i < count; i++)
                    ownedEquipment.Add(new EquipmentInstance(item, equip));
            }
            else
            {
                // 소비품이면 효과 행이 붙고, 재료는 null(정상).
                ConsumableData consumable = json.Get<ConsumableData>(itemId);
                AddToStacks(item, consumable, count);
            }

            InventoryEvents.FireItemAdded(item);

            // 획득은 잦을 수 있어(드랍 등) dirty로 묶는다(오토세이브/경계 flush가 저장).
            PlayerSaveService.MarkDirty();
        }

        // 스택형 아이템을 담는다. 같은 아이템의 여유 스택부터 채우고, 남으면 MaxStack 단위로 새 스택을 만든다.
        private void AddToStacks(ItemData item, ConsumableData consumable, int count)
        {
            int remaining = count;

            foreach (ItemStack stack in stackItems)
            {
                if (stack.Item.Index != item.Index || stack.IsFull) continue;

                remaining = stack.Add(remaining);
                if (remaining <= 0) return;
            }

            while (remaining > 0)
            {
                int take = Mathf.Min(remaining, item.MaxStack);
                stackItems.Add(new ItemStack(item, consumable, take));
                remaining -= take;
            }
        }

        /// <summary>
        /// 소비품 1개를 사용한다. 회복 효과를 플레이어에게 적용하고 수량을 1 차감한다.
        /// 즉시형(DurationSec==0)은 HealAmount를 한 번, 지속형은 초당 HealAmount씩 DurationSec 동안 회복한다.
        /// 쿨다운 중이거나 소비품이 아니거나 플레이어가 없으면 아무것도 하지 않고 false를 돌려준다.
        /// </summary>
        /// <param name="stack">사용할 스택(소모품 탭 슬롯에서 넘긴다)</param>
        /// <returns>실제로 사용했으면 true</returns>
        public bool UseConsumable(ItemStack stack)
        {
            if (stack == null || !stack.IsConsumable || stack.Count <= 0) return false;

            int itemId = stack.Item.Index;
            if (consumableCooldowns.TryGetValue(itemId, out float readyAt) && Time.time < readyAt)
                return false;   // 아직 쿨다운

            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player == null) return false;

            ConsumableData data = stack.Consumable;
            if (data.DurationSec > 0f)
                StartCoroutine(HealOverTime(data.HealAmount, data.DurationSec));
            else
                player.Stats.Heal(data.HealAmount);

            if (data.CooldownSec > 0f)
                consumableCooldowns[itemId] = Time.time + data.CooldownSec;

            // HUD 퀵슬롯이 쿨다운 연출을 시작하도록 알린다(등록 슬롯 여부는 슬롯이 판단).
            InventoryEvents.FireConsumableUsed(itemId, data.CooldownSec);

            // 수량 차감(0이면 스택 제거). 스택은 UI가 이 목록을 다시 훑어 갱신한다.
            stack.Remove(1);
            if (stack.Count <= 0) stackItems.Remove(stack);

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
        /// 퀵슬롯에 소비품을 등록한다(우클릭 메뉴·드래그 드롭 공통 진입점). 소비품만 허용하며(재료·장비 제외),
        /// itemId 0은 등록 해제다. 변경을 <see cref="InventoryEvents.OnQuickSlotChanged"/>로 알리고 저장 dirty로 잡는다.
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
        /// 실제 효과·쿨다운·수량차감은 <see cref="UseConsumable"/>가 처리한다.
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
            foreach (ItemStack stack in stackItems)
                if (stack.Item != null && stack.Item.Index == itemId) total += stack.Count;
            return total;
        }

        // 지정 itemId의 사용 가능한(수량>0) 스택 하나를 찾는다. 없으면 null.
        private ItemStack FindStack(int itemId)
        {
            foreach (ItemStack stack in stackItems)
                if (stack.Item != null && stack.Item.Index == itemId && stack.Count > 0) return stack;
            return null;
        }
    }
}
