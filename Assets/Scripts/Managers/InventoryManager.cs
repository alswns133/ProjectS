using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;

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

        // 강화 대상 후보. 실제 인벤토리가 붙기 전까지는 RegisterEquipment로 코드에서 채운다.
        private readonly List<EquipmentInstance> ownedEquipment = new();

        /// <summary>현재 보유 골드.</summary>
        public int Gold => gold;

        /// <summary>현재 보유 하급 재료.</summary>
        public int LowMaterial => lowMaterial;

        /// <summary>현재 보유 상급 재료.</summary>
        public int HighMaterial => highMaterial;

        /// <summary>보유 장비 목록(장비 선택 팝업이 나열한다).</summary>
        public IReadOnlyList<EquipmentInstance> OwnedEquipment => ownedEquipment;

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

        private void Start()
        {
            // 선택된 캐릭터 세이브가 있으면 그 재화를 반영한 뒤, 구독자(HUD)에게 발행한다.
            ApplySelectedCharacterSave();
            PublishGold();
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

        /// <summary>현재 보유 재화를 세이브 데이터에 기록한다. 저장 시점에 호출한다.</summary>
        /// <param name="save">기록 대상 세이브(선택된 캐릭터). null이면 무시.</param>
        public void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            save.gold = gold;
            save.lowMaterial = lowMaterial;
            save.highMaterial = highMaterial;
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
        /// 스캐폴드용 진입점: 강화 대상 후보를 코드에서 채워넣는다.
        /// 실제 인벤토리(아이템 획득/장착)가 붙으면 이 메서드는 그쪽으로 대체된다.
        /// </summary>
        /// <param name="instance">등록할 장비 인스턴스</param>
        public void RegisterEquipment(EquipmentInstance instance)
        {
            if (instance != null) ownedEquipment.Add(instance);
        }
    }
}
