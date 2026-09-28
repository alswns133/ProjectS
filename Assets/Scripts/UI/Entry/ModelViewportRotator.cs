using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectS.UI
{
    /// <summary>
    /// 프리뷰 뷰포트 위에서 드래그해 3D 모델을 좌우로 돌린다. 화살표 버튼도 같은 값을 건드리도록
    /// <see cref="StepLeft"/>/<see cref="StepRight"/>를 함께 제공한다.
    ///
    /// 드래그와 화살표가 각자 회전을 관리하면, 드래그로 돌린 뒤 화살표를 누르는 순간
    /// 원래 각도로 튄다. 그래서 둘 다 <see cref="target"/>의 현재 Y 회전에 더하는 방식으로 통일한다.
    ///
    /// 이 컴포넌트는 뷰포트(RawImage)에 붙이며, 그 그래픽의 <c>raycastTarget</c>이 켜져 있어야
    /// 드래그 이벤트가 들어온다.
    /// </summary>
    public class ModelViewportRotator : MonoBehaviour, IDragHandler
    {
        [Tooltip("돌릴 대상. 캐릭터 프리팹이 붙는 CharacterStage/ModelRoot.")]
        [SerializeField] private Transform target;

        [Tooltip("드래그 1픽셀당 회전 각도.")]
        [SerializeField] private float dragSpeed = 0.35f;

        [Tooltip("화살표 버튼 한 번에 도는 각도.")]
        [SerializeField] private float stepAngle = 30f;

        // target은 씬 오브젝트(CharacterStage/ModelRoot)라 프리팹 에셋에는 저장되지 않는다.
        // 프리팹에서 꽂으려다 비워둔 채 넘어가면 Rotate()의 null 가드에 걸려 버튼도 드래그도
        // 에러 하나 없이 조용히 죽는다(실제로 원인 추적에 시간이 걸렸다). 그래서 여기서 먼저 알린다.
        private void Awake()
        {
            if (target == null)
            {
                Debug.LogWarning($"[ModelViewportRotator] '{name}'의 Target이 비어 있어 회전이 동작하지 않습니다. " +
                                 "씬에서 CharacterStage/ModelRoot를 꽂아 주세요(프리팹에서는 꽂히지 않습니다).", this);
            }
        }

        /// <summary>드래그로 모델을 돌린다. EventSystem이 호출한다.</summary>
        /// <param name="eventData">드래그 정보</param>
        public void OnDrag(PointerEventData eventData)
        {
            // 오른쪽으로 끌면 모델이 오른쪽으로 도는 느낌이 되도록 부호를 뒤집는다.
            // 감각이 반대로 느껴지면 이 부호만 바꾸면 된다.
            Rotate(-eventData.delta.x * dragSpeed);
        }

        /// <summary>왼쪽 화살표. 한 단계 돌린다.</summary>
        public void StepLeft() => Rotate(stepAngle);

        /// <summary>오른쪽 화살표. 한 단계 돌린다.</summary>
        public void StepRight() => Rotate(-stepAngle);

        /// <summary>
        /// 회전을 정면으로 되돌린다. 생성 페이지를 나갈 때 부르지 않으면
        /// 스테이지를 공유하는 캐릭터 선택 화면에서 캐릭터가 뒤통수를 보인다.
        /// </summary>
        public void ResetRotation()
        {
            if (target != null) target.localRotation = Quaternion.identity;
        }

        private void Rotate(float degrees)
        {
            if (target == null) return;
            target.Rotate(0f, degrees, 0f, Space.World);
        }
    }
}
