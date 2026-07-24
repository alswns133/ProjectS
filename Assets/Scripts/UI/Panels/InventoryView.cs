using System.Collections.Generic;
using UnityEngine;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 보유 장비를 슬롯으로 나열하는 인벤토리 뷰. 강화창과 나란히 두고, 슬롯 더블클릭/드래그로 강화 대상을 고른다.
    /// BasePanel/BasePopup을 상속하지 않는 순수 컴포넌트라 패널·팝업·영역 어디에든 얹을 수 있어,
    /// "강화창과 어떻게 나란히 둘지"(호스팅 방식)를 씬에서 자유롭게 정할 수 있다.
    /// (2026-07-23 TH)
    /// </summary>
    public class InventoryView : MonoBehaviour
    {
        [SerializeField] private Transform slotRoot;
        [SerializeField] private InventorySlotView slotPrefab;

        private readonly List<InventorySlotView> slots = new();

        private void OnEnable() => Rebuild();

        /// <summary>
        /// 보유 장비 목록으로 슬롯을 다시 만든다. 강화로 +N이 바뀌면(EnhanceEvents.OnEnhanced 등)
        /// 외부에서 다시 호출해 표시를 맞춘다.
        /// </summary>
        public void Rebuild()
        {
            if (slotRoot == null) return;

            foreach (Transform child in slotRoot) Destroy(child.gameObject);
            slots.Clear();

            var inv = InventoryManager.Instance;
            if (inv == null || slotPrefab == null) return;

            foreach (var equip in inv.OwnedEquipment)
            {
                var view = Instantiate(slotPrefab, slotRoot);
                view.Set(equip);
                slots.Add(view);
            }
        }
    }
}
