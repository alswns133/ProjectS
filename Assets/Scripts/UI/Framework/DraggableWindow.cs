using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 창을 이동식으로 만든다. 창 루트(RectTransform)에 붙이고, 실제 드래그 감지는 타이틀바에 붙인
    /// <see cref="WindowDragHandle"/>이 이 컴포넌트로 넘긴다(본문 전체가 아니라 타이틀바로만 끌게 하기 위함).
    /// - 드래그로 이동하고, 끝나면 <see cref="WindowLayoutStore"/>에 위치를 저장한다(다음에 그 자리로 열림).
    /// - 창을 누르면 형제 창들 위로 올라온다(z-order).
    /// - 열릴 때 저장 위치를 복원하고, 해상도가 달라 창이 화면 밖이면 부모 안으로 되돌린다(clamp).
    /// 여러 창이 공존하는 UX(<see cref="BasePopup"/> 리스트 모델)에서 각 창의 배치를 사용자 설정처럼 유지한다.
    /// </summary>
    public class DraggableWindow : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] private RectTransform target;   // 이동·저장 대상(비우면 자기 루트)
        [Tooltip("PlayerPrefs 저장 키. 창마다 유일해야 한다. 전용 스크립트가 있는 창은 코드로 SetWindowId(WindowIds.*) 주입, " +
                 "범용 창(전용 스크립트 없이 이 컴포넌트만 붙인 창)은 여기서 직접 지정")]
        [SerializeField] private string windowId;

        private Canvas canvas;
        private RectTransform parentRect;
        private Vector2 pointerStart;
        private Vector2 windowStart;

        private void Awake()
        {
            if (target == null) target = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            parentRect = target.parent as RectTransform;
        }

        /// <summary>
        /// 위치 저장 키를 코드로 지정한다(전용 스크립트가 있는 창은 인스펙터 대신 상수로 주입 — 오타 방지).
        /// 팝업의 OnInit(SetActive 이전)에서 부르면, 뒤이은 OnEnable이 이 키로 저장 위치를 복원한다.
        /// 이미 활성 중이면 즉시 다시 로드한다.
        /// </summary>
        /// <param name="id">고유 창 키(<see cref="WindowIds"/>)</param>
        public void SetWindowId(string id)
        {
            windowId = id;
            if (isActiveAndEnabled)
            {
                WindowLayoutStore.Load(windowId, target);
                ClampToParent();
            }
        }

        // 열릴 때마다 저장 위치를 복원한다(팝업 Show → SetActive(true) → 여기). 없으면 배치 그대로 둔다.
        private void OnEnable()
        {
            WindowLayoutStore.Load(windowId, target);
            ClampToParent();
        }

        /// <summary>창 본문 어디든 누르면 형제 창들 위로 올린다.</summary>
        public void OnPointerDown(PointerEventData eventData) => BringToFront();

        /// <summary>타이틀바 드래그 시작. 기준 좌표를 기록하고 창을 앞으로 올린다.</summary>
        /// <param name="eventData">드래그 이벤트</param>
        public void BeginDrag(PointerEventData eventData)
        {
            BringToFront();
            pointerStart = eventData.position;
            windowStart = target.anchoredPosition;
        }

        /// <summary>타이틀바 드래그 중. 포인터 이동량만큼 창을 옮긴다(캔버스 스케일 보정).</summary>
        /// <param name="eventData">드래그 이벤트</param>
        public void Drag(PointerEventData eventData)
        {
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            target.anchoredPosition = windowStart + (eventData.position - pointerStart) / scale;
        }

        /// <summary>타이틀바 드래그 종료. 화면 안으로 보정한 뒤 위치를 저장한다.</summary>
        /// <param name="eventData">드래그 이벤트</param>
        public void EndDrag(PointerEventData eventData)
        {
            ClampToParent();
            WindowLayoutStore.Save(windowId, target);
        }

        private void BringToFront() => target.SetAsLastSibling();

        // 창이 부모(캔버스/뷰포트) 밖으로 나가지 않게 anchoredPosition을 눌러 담는다.
        // 다른 해상도에서 저장된 좌표가 화면 밖을 가리켜 창을 놓치는 것을 막는다.
        private void ClampToParent()
        {
            if (parentRect == null) return;

            Rect parent = parentRect.rect;
            Rect self = target.rect;
            Vector2 pivot = target.pivot;

            // 앵커가 부모 중앙(대부분의 창 기본값)이라는 가정 하의 이동 한계.
            float halfW = parent.width * 0.5f;
            float halfH = parent.height * 0.5f;

            float minX = -halfW + self.width * pivot.x;
            float maxX = halfW - self.width * (1f - pivot.x);
            float minY = -halfH + self.height * pivot.y;
            float maxY = halfH - self.height * (1f - pivot.y);

            // 창이 부모보다 크면 min>max가 되어 뒤집히므로, 그럴 땐 중앙(0)에 둔다.
            Vector2 pos = target.anchoredPosition;
            pos.x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : 0f;
            pos.y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : 0f;
            target.anchoredPosition = pos;
        }
    }
}
