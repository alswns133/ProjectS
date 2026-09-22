using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 총구에서 앞으로 선을 긋고, 처음 닿은 표면에서 끊어 그 자리에 도트를 붙이는 조준용 레이저 사이트.
    /// 연출 전용이라 데미지·판정과 무관하다 — "어디를 겨누고 있는가"만 보여준다(핵 착탄 지점 표시 등).
    /// 색·두께·글로우는 LineRenderer 머티리얼에서, 도트 모양은 dot 오브젝트 쪽에서 만든다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class LaserSight : MonoBehaviour
    {
        [Header("조준")]
        [Tooltip("아무것도 맞히지 못했을 때 선을 그을 최대 거리.")]
        [SerializeField] private float maxDistance = 100f;

        [Tooltip("레이저가 멈출 대상. Player/BodyPart는 반드시 빼둔다 — 넣으면 자기 몸을 맞혀 선 길이가 0이 된다.")]
        [SerializeField] private LayerMask hitMask;

        [Header("도트")]
        [Tooltip("닿은 지점에 붙일 도트. 비워두면 선만 그린다.")]
        [SerializeField] private Transform dot;

        [Tooltip("도트를 표면에서 띄우는 높이(z-fighting 방지).")]
        [SerializeField] private float dotSurfaceOffset = 0.02f;

        private LineRenderer line;

        /// <summary>이번 프레임에 무언가에 닿았는지. 닿지 않았으면 최대 거리까지 그린 상태다.</summary>
        public bool HasHit { get; private set; }

        /// <summary>선이 끝나는 지점. 핵 착탄 위치처럼 겨냥점을 읽어가는 쪽이 쓴다.</summary>
        public Vector3 HitPoint { get; private set; }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
        }

        // 초기화를 Start가 아니라 OnEnable에 두는 이유: 풀에서 꺼내 재사용할 때 Start는 다시 돌지 않는다.
        private void OnEnable()
        {
            if (dot != null) dot.gameObject.SetActive(false);
        }

        // 총구가 애니메이션에 매달려 있으므로 애니메이션 갱신 뒤인 LateUpdate에서 읽는다.
        // Update에서 읽으면 한 프레임 밀려 조준할 때 선이 떤다.
        private void LateUpdate()
        {
            Vector3 origin = transform.position;
            Vector3 direction = transform.forward;

            // Collide: 이 프로젝트의 적 피격 콜라이더는 Is Trigger다. 무시하면 몬스터에서 선이 안 멈춘다.
            HasHit = Physics.Raycast(origin, direction, out RaycastHit hit,
                                     maxDistance, hitMask, QueryTriggerInteraction.Collide);

            HitPoint = HasHit ? hit.point : origin + direction * maxDistance;

            line.SetPosition(0, origin);
            line.SetPosition(1, HitPoint);

            if (dot == null) return;

            dot.gameObject.SetActive(HasHit);
            if (!HasHit) return;

            // 표면 법선으로 눕혀야 벽이든 몬스터든 그 면에 눌린 것처럼 보인다.
            dot.SetPositionAndRotation(hit.point + hit.normal * dotSurfaceOffset,
                                       Quaternion.LookRotation(hit.normal));
        }
    }
}
