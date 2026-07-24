using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ProjectS.Enhance;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 인벤토리 한 칸(장비). 강화 대상 선택 입력을 담당한다.
    /// - 더블클릭: OnDoubleClicked(Instance) 발행
    /// - 드래그: 드래그 중 커서에 고스트 아이콘을 붙인다. 실제 드롭 판정은 드롭 대상(CoreSlotDropTarget)이
    ///   eventData.pointerDrag로 이 슬롯을 읽어 처리한다.
    /// 포인터/드래그 이벤트를 받으려면 이 오브젝트에 raycastTarget인 Graphic(아이콘/배경)이 있어야 한다.
    /// (2026-07-23 TH)
    /// </summary>
    public class InventorySlotView : MonoBehaviour,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;

        /// <summary>이 슬롯이 표시 중인 장비 인스턴스.</summary>
        public EquipmentInstance Instance { get; private set; }

        /// <summary>슬롯을 더블클릭했을 때 그 장비를 알린다(강화 대상 선택 등).</summary>
        public static event Action<EquipmentInstance> OnDoubleClicked;

        private GameObject dragGhost;

        /// <summary>슬롯을 장비로 채운다.</summary>
        /// <param name="equip">표시할 장비 인스턴스</param>
        public void Set(EquipmentInstance equip)
        {
            Instance = equip;

            if (nameText != null && equip != null && equip.Item != null)
                nameText.text = $"{equip.Item.Name} +{equip.EnhanceStep}";

            // TODO: 아이콘 스프라이트는 equip.Item.IconAddress로 어드레서블 로드해 넣는다. 지금은 자리만.
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.clickCount == 2) OnDoubleClicked?.Invoke(Instance);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (icon == null) return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 고스트: 드래그 동안 커서를 따라다니는 반투명 아이콘. 드롭 레이캐스트를 막지 않도록 raycastTarget off.
            dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
            dragGhost.transform.SetParent(canvas.transform, false);
            dragGhost.transform.SetAsLastSibling();

            var ghostImg = dragGhost.GetComponent<Image>();
            ghostImg.sprite = icon.sprite;
            ghostImg.raycastTarget = false;
            ghostImg.color = new Color(1f, 1f, 1f, 0.7f);

            ((RectTransform)dragGhost.transform).sizeDelta = icon.rectTransform.sizeDelta;
            dragGhost.transform.position = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragGhost != null) dragGhost.transform.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragGhost != null) Destroy(dragGhost);
            dragGhost = null;
        }

        /// <summary>static 이벤트 구독 초기화(도메인 리로드를 꺼도 이전 세션 구독자가 남지 않게).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OnDoubleClicked = null;
    }
}
