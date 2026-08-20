using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Data;
using ProjectS.Enhance;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화창의 흐름 제어자. View 이벤트를 받아 검증 → 판정(Service) → 연출 → 표시 갱신 → 이벤트 발행
    /// 순서를 지킨다. 판정·차감은 연출 시작 전에 끝내, 연출 도중 종료돼도 데이터가 어긋나지 않게 한다
    /// (설계 문서 3절). isBusy로 연타를 막는다(리액터형 큰 버튼이라 특히 필요).
    /// (2026-07-23 TH)
    /// </summary>
    public class EnhancePresenter : BasePresenter
    {
        [SerializeField] private EnhancePopup view;

        private EnhanceService service;
        private EquipmentInstance target;
        private bool isBusy;

        private void Awake()
        {
            if (view == null) view = GetComponent<EnhancePopup>();
        }

        protected override void Subscribe()
        {
            view.OnEnhanceRequested += HandleEnhanceRequested;
            // 대상 선택 두 가지(구 InventorySlotView 설계 의도 유지): 인벤 장비를 코어 슬롯에 드래그드롭 + 인벤 장비 좌더블클릭.
            // 둘 다 실제 인벤 슬롯(InventoryItemSlot)에서 나오며, 강화창이 열려 있는 동안(=이 구독이 사는 동안)만 반응한다.
            CoreSlotDropTarget.OnDropped += HandleEquipmentChosen;
            InventoryItemSlot.OnEquipmentDoubleClicked += HandleEquipmentChosen;
            PlayerEvents.OnGoldChanged += HandleGoldChanged;
        }

        protected override void Unsubscribe()
        {
            view.OnEnhanceRequested -= HandleEnhanceRequested;
            CoreSlotDropTarget.OnDropped -= HandleEquipmentChosen;
            InventoryItemSlot.OnEquipmentDoubleClicked -= HandleEquipmentChosen;
            PlayerEvents.OnGoldChanged -= HandleGoldChanged;
        }

        private void HandleEquipmentChosen(EquipmentInstance chosen)
        {
            target = chosen;
            RefreshView();
        }

        private void HandleGoldChanged(int gold)
        {
            view.SetOwnedGold(gold);
        }

        private void HandleEnhanceRequested()
        {
            if (isBusy || target == null) return;

            EnsureService();

            // 1) 진행 불가 사유를 눌러본 유저에게 토스트로 알린다(조용히 무시하면 왜 안 되는지 알 수 없다).
            //    판정·차감 전에 검사하므로 재화는 건드리지 않는다.
            var info = service.BuildInfo(target);
            var inv = InventoryManager.Instance;

            if (info.IsMax)
            {
                UIEvents.FireToast("이미 최대 강화 단계입니다.");
                return;
            }

            if (inv == null || inv.Gold < info.ZenyCost)
            {
                UIEvents.FireToast("골드가 부족합니다.");
                return;
            }

            if (inv.LowMaterial < info.LowMaterial || inv.HighMaterial < info.HighMaterial)
            {
                UIEvents.FireToast("강화 재료가 부족합니다.");
                return;
            }

            // 2) 검증 + 판정 + 차감 (연출 전에 데이터 확정). 위 검사를 통과했어도 Service가 최종 게이트다.
            if (!service.TryEnhance(target, out var result))
            {
                UIEvents.FireToast("강화를 진행할 수 없습니다.");
                return;
            }

            StartCoroutine(RunResult(result));
        }

        private IEnumerator RunResult(EnhanceResult result)
        {
            isBusy = true;
            view.SetInteractable(false);

            // 2) 연출 (판정은 이미 끝났으므로 표시만 미룬다)
            yield return view.PlayResult(result);

            // 3) 표시 갱신 + 이벤트 발행
            RefreshView();
            EnhanceEvents.FireEnhanced(result);

            view.SetInteractable(true);
            isBusy = false;
        }

        private void RefreshView()
        {
            if (target == null) return;

            EnsureService();

            var info = service.BuildInfo(target);
            view.SetTarget(target.Item, info);
            view.SetMaterials(BuildMaterials(info));
            view.SetOwnedGold(InventoryManager.Instance != null ? InventoryManager.Instance.Gold : 0);
        }

        // Service는 InventoryManager(자원 공급자)에 의존한다. 매니저 Awake 순서를 보장할 수 없어
        // 첫 사용 시점에 지연 생성한다.
        private void EnsureService()
        {
            service ??= new EnhanceService(InventoryManager.Instance);
        }

        private IReadOnlyList<MaterialSlotInfo> BuildMaterials(EnhanceInfo info)
        {
            var list = new List<MaterialSlotInfo>();
            if (info.IsMax) return list;

            var inv = InventoryManager.Instance;

            AddMaterialSlot(list, InventoryManager.LowMaterialItemId, inv != null ? inv.LowMaterial : 0, info.LowMaterial);
            AddMaterialSlot(list, InventoryManager.HighMaterialItemId, inv != null ? inv.HighMaterial : 0, info.HighMaterial);
            return list;
        }

        // 재료 아이템 ID로 이름·아이콘 주소를 조회해 슬롯 DTO를 만든다. 필요량 0인 재료는 감춘다
        // (하급만 드는 초반 강화에 상급 재료 칸이 "N/0"으로 뜨는 것을 막는다).
        private void AddMaterialSlot(List<MaterialSlotInfo> list, int itemId, int owned, int required)
        {
            if (required <= 0) return;

            JsonManager json = JsonManager.Instance;
            ItemData item = json != null ? json.Get<ItemData>(itemId) : null;
            string name = item != null ? item.Name : "재료";
            string iconAddress = item != null ? item.IconAddress : null;

            list.Add(new MaterialSlotInfo(iconAddress, name, owned, required));
        }
    }
}
