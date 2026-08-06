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

        // GetWorldCorners 결과를 담는 버퍼(매 호출 할당 방지).
        private readonly Vector3[] cornerBuffer = new Vector3[4];

        // 창이 부모(캔버스/뷰포트) 밖으로 나가지 않게 되돌린다. 창의 실제 경계를 부모 로컬 좌표로 직접 재고
        // 넘친 만큼만 anchoredPosition을 밀어넣으므로, 창의 앵커·피벗이 무엇이든(중앙이 아니어도) 정확히 동작한다.
        // 다른 해상도에서 저장된 좌표가 화면 밖을 가리켜 창을 놓치는 것도 함께 막는다.
        private void ClampToParent()
        {
            if (parentRect == null) return;

            // 창의 네 모서리(월드) → 부모 로컬 좌표. 축 정렬 UI라 좌하단[0]·우상단[2]이면 경계가 잡힌다.
            target.GetWorldCorners(cornerBuffer);
            Vector2 bottomLeft = parentRect.InverseTransformPoint(cornerBuffer[0]);
            Vector2 topRight = parentRect.InverseTransformPoint(cornerBuffer[2]);

            Rect parent = parentRect.rect;   // 부모 로컬 경계(xMin/xMax/yMin/yMax, 부모 피벗 반영)

            Vector2 offset = Vector2.zero;
            if (bottomLeft.x < parent.xMin) offset.x += parent.xMin - bottomLeft.x;   // 왼쪽으로 넘침
            else if (topRight.x > parent.xMax) offset.x -= topRight.x - parent.xMax;  // 오른쪽으로 넘침
            if (bottomLeft.y < parent.yMin) offset.y += parent.yMin - bottomLeft.y;   // 아래로 넘침
            else if (topRight.y > parent.yMax) offset.y -= topRight.y - parent.yMax;  // 위로 넘침

            if (offset != Vector2.zero) target.anchoredPosition += offset;
        }
    }
}
