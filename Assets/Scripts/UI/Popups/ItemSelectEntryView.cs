using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Enhance;

namespace ProjectS.UI
{
    /// <summary>
    /// 장비 선택 팝업의 한 줄. 아이콘 + 이름 + 현재 강화 단계를 보여주고,
    /// 클릭 시 콜백으로 자기 장비 인스턴스를 넘긴다. 목록 갱신은 팝업이 재생성으로 처리한다.
    /// (2026-07-23 TH)
    /// </summary>
    public class ItemSelectEntryView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Button button;

        private EquipmentInstance instance;
        private Action<EquipmentInstance> onClick;

        /// <summary>
        /// 한 줄을 대상 장비로 채운다.
        /// </summary>
        /// <param name="equip">표시할 장비 인스턴스</param>
        /// <param name="callback">클릭 시 이 인스턴스를 넘길 콜백</param>
        public void Set(EquipmentInstance equip, Action<EquipmentInstance> callback)
        {
            instance = equip;
            onClick = callback;

            if (nameText != null && equip != null && equip.Item != null)
                nameText.text = $"{equip.Item.Name} +{equip.EnhanceStep}";

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClick?.Invoke(instance));
            }
        }
    }
}
