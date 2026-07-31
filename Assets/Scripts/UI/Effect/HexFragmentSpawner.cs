using UnityEngine;
using ProjectS.Effects;

namespace ProjectS.UI
{
    /// <summary>
    /// 육각 파편 풀 겸 연출 레이어. 파편은 이 오브젝트의 자식으로 생성되므로,
    /// 이 컴포넌트가 붙은 RectTransform이 곧 파편이 노는 좌표계다.
    ///
    /// <b>반드시 스크롤 뷰의 RectMask2D 바깥에 두어야 한다.</b> 카드는 Viewport 안에 있어서,
    /// 파편을 카드 밑에 만들면 카드를 벗어나는 순간 마스크에 잘려 사라진다.
    /// HUD 최상위(또는 FX 오버레이)에 두고, 좌표는 <see cref="WorldToOverlay"/>로 변환해 넘긴다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HexFragmentSpawner : PooledSpawner<HexFragment>
    {
        private RectTransform overlay;
        private Canvas canvas;

        /// <summary>파편이 놓이는 좌표계. 스윕 라이트처럼 함께 띄울 연출도 여기에 붙인다.</summary>
        public RectTransform Overlay
        {
            get
            {
                if (overlay == null) overlay = (RectTransform)transform;
                return overlay;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            overlay = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        /// <summary>
        /// 월드 좌표를 이 레이어의 로컬 좌표로 바꾼다. 카드는 스크롤 뷰 안, 파편은 오버레이 위라
        /// 부모가 달라 좌표계를 직접 옮겨야 한다.
        /// </summary>
        /// <param name="world">변환할 월드 좌표(보통 카드 RectTransform의 TransformPoint 결과)</param>
        /// <returns>오버레이 기준 anchoredPosition으로 쓸 수 있는 로컬 좌표</returns>
        public Vector2 WorldToOverlay(Vector3 world)
        {
            // Screen Space - Overlay 캔버스는 카메라를 null로 넘겨야 변환이 맞는다.
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera
                : null;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Overlay, screen, cam, out Vector2 local);
            return local;
        }

        /// <summary>파편 하나를 풀에서 꺼내 날려 보낸다.</summary>
        /// <param name="overlayPosition">오버레이 기준 시작 위치(<see cref="WorldToOverlay"/> 결과)</param>
        /// <param name="size">파편 크기(px)</param>
        /// <param name="color">파편 색</param>
        /// <param name="velocity">초기 속도(px/초)</param>
        /// <param name="angularSpeed">초당 회전 각도</param>
        /// <param name="lifetime">수명(초)</param>
        public void Spawn(Vector2 overlayPosition, float size, Color color,
                          Vector2 velocity, float angularSpeed, float lifetime)
        {
            HexFragment fragment = GetFromPool();
            if (fragment == null) return;

            fragment.transform.SetParent(Overlay, false);
            fragment.transform.SetAsLastSibling();
            fragment.gameObject.SetActive(true);
            fragment.Play(overlayPosition, size, color, velocity, angularSpeed, lifetime, ReturnToPool);
        }
    }
}
