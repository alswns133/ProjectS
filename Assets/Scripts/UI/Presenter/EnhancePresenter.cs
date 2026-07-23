using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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
        [SerializeField] private EnhancePanel view;

        private EnhanceService service;
        private EquipmentInstance target;
        private bool isBusy;

        private void Awake()
        {
            if (view == null) view = GetComponent<EnhancePanel>();
        }

        protected override void Subscribe()
        {
            view.OnEnhanceRequested += HandleEnhanceRequested;
            view.OnSlotClicked += HandleSlotClicked;
            ItemSelectPopup.OnEquipmentChosen += HandleEquipmentChosen;
            PlayerEvents.OnGoldChanged += HandleGoldChanged;
        }

        protected override void Unsubscribe()
        {
            view.OnEnhanceRequested -= HandleEnhanceRequested;
            view.OnSlotClicked -= HandleSlotClicked;
            ItemSelectPopup.OnEquipmentChosen -= HandleEquipmentChosen;
            PlayerEvents.OnGoldChanged -= HandleGoldChanged;
        }

        private void HandleSlotClicked()
        {
            // 장비 선택은 강화창 위에 팝업으로. Esc 한 번이면 이 팝업만 닫히고 강화창은 살아있다.
            UIManager.Instance.ShowPopup<ItemSelectPopup>();
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

            // 1) 검증 + 판정 + 차감 (연출 전에 데이터 확정)
            if (!service.TryEnhance(target, out var result))
            {
                // TODO: 재료/골드 부족 토스트 표시
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

            // TODO: 재료 아이콘/이름은 재료 아이템 테이블에서 조회. 지금은 개수만 채운다.
            list.Add(new MaterialSlotInfo(null, "하급 재료", inv != null ? inv.LowMaterial : 0, info.LowMaterial));
            list.Add(new MaterialSlotInfo(null, "상급 재료", inv != null ? inv.HighMaterial : 0, info.HighMaterial));
            return list;
        }
    }
}
