using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 창의 타이틀바에 붙여 드래그 입력을 감지하고 <see cref="DraggableWindow"/>로 넘긴다.
    /// 본문(슬롯 등)이 아니라 타이틀바에서만 창을 끌게 하려고 드래그 감지를 여기로 분리한다.
    /// 드래그를 받으려면 이 오브젝트에 raycastTarget인 Graphic(타이틀바 배경 Image)이 있어야 한다.
    /// </summary>
    public class WindowDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private DraggableWindow window;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (window != null) window.BeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (window != null) window.Drag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (window != null) window.EndDrag(eventData);
        }
    }
}
