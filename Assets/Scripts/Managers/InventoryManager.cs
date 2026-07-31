using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
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

        private void Start()
        {
            // HUD 등 구독자가 초기 골드를 받도록 1회 발행. (실제 로드 로직으로 교체 예정)
            PlayerEvents.FireGoldChanged(gold);
        }

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
        }

        /// <summary>골드를 지급하고 변경을 브로드캐스트한다(퀘스트 보상 등). 0 이하면 무시한다.</summary>
        /// <param name="amount">추가할 골드량</param>
        public void AddGold(int amount)
        {
            if (amount <= 0) return;

            gold += amount;
            PlayerEvents.FireGoldChanged(gold);
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
