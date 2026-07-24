using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Enhance;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 강화 대상 장비를 고르는 팝업. 강화창(Panel) 위에 떠서 Esc 한 번에 이 팝업만 닫힌다
    /// (BasePopup은 스택이 아닌 리스트라 뒤의 강화창은 살아있다).
    /// 선택 결과는 static 이벤트로 알린다 — 팝업 인스턴스를 UIManager가 관리해 밖에서 참조를 들고 있지
    /// 않기 때문이며, 프로젝트의 이벤트 허브 패턴과도 일관된다.
    /// (2026-07-23 TH)
    /// </summary>
    public class ItemSelectPopup : BasePopup
    {
        /// <summary>장비를 골랐을 때 발행. 강화 Presenter가 구독한다.</summary>
        public static event Action<EquipmentInstance> OnEquipmentChosen;

        [SerializeField] private Transform listRoot;
        [SerializeField] private ItemSelectEntryView entryPrefab;

        private readonly List<ItemSelectEntryView> entries = new();

        protected override void OnShow()
        {
            Rebuild();
        }

        protected override void OnHide()
        {
            Clear();
        }

        // 열릴 때마다 보유 장비 목록으로 다시 채운다(강화 후 단계가 바뀌므로 재생성).
        private void Rebuild()
        {
            Clear();

            var inv = InventoryManager.Instance;
            if (inv == null || entryPrefab == null || listRoot == null) return;

            foreach (var equip in inv.OwnedEquipment)
            {
                var view = Instantiate(entryPrefab, listRoot);
                view.Set(equip, Choose);
                entries.Add(view);
            }
        }

        private void Clear()
        {
            foreach (var e in entries)
            {
                if (e != null) Destroy(e.gameObject);
            }
            entries.Clear();
        }

        private void Choose(EquipmentInstance chosen)
        {
            OnEquipmentChosen?.Invoke(chosen);
            RequestClose();
        }

        /// <summary>
        /// static 이벤트 구독 초기화. 도메인 리로드를 꺼도 이전 세션 구독자가 남지 않게 한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnEquipmentChosen = null;
        }
    }
}
