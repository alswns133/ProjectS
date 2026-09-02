using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 레이드 아레나 경계 배리어의 발광을 제어하는 컴포넌트.
    /// 평소에는 거의 보이지 않다가, 대상이 경계에 접근하거나 부딪힌 지점만 밝아진다.
    /// ProjectS/Arena Barrier 셰이더와 짝으로 동작한다.
    ///
    /// 발광 지점은 배리어 콜라이더의 Collider.ClosestPoint로 구한다.
    /// 덕분에 아레나가 원이든 다각형이든 벽이 몇 개든 이 코드는 알 필요가 없다.
    /// 물리 충돌 콜백을 쓰지 않는 이유는 두 가지다.
    ///   1) CharacterController의 충돌은 움직이는 쪽(OnControllerColliderHit)으로만 오므로
    ///      벽이 아니라 플레이어 코드를 건드려야 한다.
    ///   2) 콜백은 "부딪힌 순간"에만 오기 때문에, 벽에 붙어 있는 "동안" 이어지는
    ///      근접 발광을 만들 수 없다.
    ///
    /// 막는 것은 이 컴포넌트가 아니라 그 콜라이더 자체가 한다. 여기서는 읽기만 한다.
    ///
    /// 머티리얼을 복제하지 않고 MaterialPropertyBlock으로만 값을 덮어쓰므로,
    /// 런타임 머티리얼 누수가 없다.
    /// </summary>
    public class ArenaBarrier : MonoBehaviour
    {
        /// <summary>동시에 셰이더로 넘길 수 있는 접촉점 수. ArenaBarrier.shader의 MAX_POINTS와 반드시 같아야 한다.</summary>
        public const int MaxPoints = 8;

        [Header("배리어 면")]
        [Tooltip("발광을 그릴 렌더러들. 비워두면 자식에서 모두 찾아 쓴다.")]
        [SerializeField] private Renderer[] barrierRenderers;

        [Tooltip("경계를 막는 콜라이더들. 비워두면 자식에서 모두 찾아 쓴다. " +
                 "ClosestPoint를 쓰므로 Box/Sphere/Capsule 또는 Convex Mesh여야 한다. " +
                 "Convex가 아닌 MeshCollider는 ClosestPoint가 입력을 그대로 돌려주기 때문에 발광이 엉뚱한 곳에 뜬다.")]
        [SerializeField] private Collider[] barrierColliders;

        [Header("근접 발광")]
        [Tooltip("배리어 표면에서 이 거리 안으로 들어오면 발광이 켜지기 시작한다. 가까울수록 밝아진다.")]
        [SerializeField] private float proximityDistance = 3f;

        [Header("파문")]
        [Tooltip("표면에 이보다 가까워지면 부딪힌 것으로 보고 파문을 한 번 낸다. " +
                 "캐릭터 반지름 때문에 실제로는 0이 되지 않으므로 넉넉히 잡는다.")]
        [SerializeField] private float contactDistance = 0.6f;

        [Tooltip("한 대상이 파문을 연속으로 쏟아내지 않게 하는 최소 간격(초). " +
                 "벽에 밀착해 비비면 매 프레임 터지므로 반드시 필요하다.")]
        [SerializeField] private float rippleCooldown = 0.5f;

        [Header("추적 대상")]
        [Tooltip("경계 접촉을 검사할 대상. 플레이어를 넣는다. 런타임에는 RegisterTarget으로도 추가할 수 있다.")]
        [SerializeField] private List<Transform> targets = new List<Transform>();

        [Tooltip("판정에 쓸 높이 보정. 대상 피벗이 발밑이므로 가슴 높이만큼 올려서 재야 " +
                 "발광이 바닥이 아니라 몸 높이에 뜬다.")]
        [SerializeField] private float contactHeightOffset = 1f;

        private static readonly int ProximityPointsId = Shader.PropertyToID("_ProximityPoints");
        private static readonly int RipplePointsId = Shader.PropertyToID("_RipplePoints");
        private static readonly int ProximityCountId = Shader.PropertyToID("_ProximityCount");
        private static readonly int RippleCountId = Shader.PropertyToID("_RippleCount");
        private static readonly int RippleLifeId = Shader.PropertyToID("_RippleLife");

        private MaterialPropertyBlock block;

        // SetVectorArray는 첫 호출 때 길이가 고정되므로, 항상 같은 크기의 배열을 넘긴다.
        // 실제로 유효한 개수는 _ProximityCount / _RippleCount로 따로 알려준다.
        private readonly Vector4[] proximityPoints = new Vector4[MaxPoints];
        private readonly Vector4[] ripplePoints = new Vector4[MaxPoints];

        private readonly List<Vector4> activeRipples = new List<Vector4>(MaxPoints);
        private readonly Dictionary<Transform, float> lastRippleTime = new Dictionary<Transform, float>();

        private float rippleLife = 0.8f;

        private void Awake()
        {
            block = new MaterialPropertyBlock();

            // 인스펙터에서 지정하지 않았으면 자식 전부를 대상으로 본다.
            // 기둥 사이마다 면과 콜라이더를 나눠 놓는 구성이 흔하므로 하나로 한정하지 않는다.
            if (barrierRenderers == null || barrierRenderers.Length == 0)
            {
                barrierRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (barrierColliders == null || barrierColliders.Length == 0)
            {
                barrierColliders = GetComponentsInChildren<Collider>(true);
            }

            if (barrierRenderers.Length == 0 || barrierColliders.Length == 0)
            {
                Debug.LogWarning($"{name}: 배리어 면 또는 콜라이더가 없다. 발광이 그려지지 않는다.", this);
                enabled = false;
                return;
            }

            // 파문 수명의 기준점은 머티리얼이다. 여기서 읽어야 인스펙터에서 연출을 튜닝했을 때
            // 코드 쪽 수명과 어긋나 파문이 중간에 끊기거나, 이미 사라진 파문이 슬롯을 계속 차지하지 않는다.
            // sharedMaterial을 읽어야 머티리얼 인스턴스가 생기지 않는다.
            Material source = barrierRenderers[0].sharedMaterial;
            if (source != null && source.HasProperty(RippleLifeId))
            {
                rippleLife = Mathf.Max(0.05f, source.GetFloat(RippleLifeId));
            }
        }

        /// <summary>
        /// 경계 접촉을 검사할 대상을 추가한다. 파티원이 늦게 합류하거나 부활할 때 호출한다.
        /// </summary>
        /// <param name="target">추적할 대상. null이거나 이미 등록된 대상이면 무시한다.</param>
        public void RegisterTarget(Transform target)
        {
            if (target == null || targets.Contains(target)) return;
            targets.Add(target);
        }

        /// <summary>
        /// 추적 대상에서 제외한다. 대상이 파괴되기 전에 호출해야 쿨다운 기록이 남지 않는다.
        /// </summary>
        /// <param name="target">제외할 대상.</param>
        public void UnregisterTarget(Transform target)
        {
            if (target == null) return;
            targets.Remove(target);
            lastRippleTime.Remove(target);
        }

        /// <summary>
        /// 지정한 월드 좌표에서 파문을 한 번 일으킨다.
        /// 넉백으로 벽에 처박히거나 보스 기믹이 배리어를 때리는 연출에 쓴다.
        /// </summary>
        /// <param name="worldPosition">파문이 퍼져나갈 중심. 배리어 표면 근처여야 보인다.</param>
        public void TriggerRipple(Vector3 worldPosition)
        {
            AddRipple(worldPosition, Time.timeSinceLevelLoad);
        }

        // 대상 이동이 끝난 뒤에 판정해야 발광이 한 프레임 밀리지 않는다.
        private void LateUpdate()
        {
            // 셰이더의 _Time.y와 같은 기준이어야 파문 나이가 어긋나지 않는다.
            float now = Time.timeSinceLevelLoad;

            int proximityCount = UpdateProximity(now);
            int rippleCount = CollectRipples(now);

            for (int i = 0; i < barrierRenderers.Length; i++)
            {
                Renderer target = barrierRenderers[i];
                if (target == null) continue;

                target.GetPropertyBlock(block);
                block.SetVectorArray(ProximityPointsId, proximityPoints);
                block.SetVectorArray(RipplePointsId, ripplePoints);
                block.SetInteger(ProximityCountId, proximityCount);
                block.SetInteger(RippleCountId, rippleCount);
                target.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// 대상마다 가장 가까운 배리어 표면 지점을 찾아 근접 발광 점을 채우고,
        /// 충분히 붙었으면 파문을 낸다.
        /// </summary>
        /// <returns>이번 프레임에 유효한 근접 점의 개수.</returns>
        private int UpdateProximity(float now)
        {
            if (proximityDistance <= 0.01f) return 0;

            float rangeSqr = proximityDistance * proximityDistance;
            int count = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                if (count >= MaxPoints) break;

                Transform target = targets[i];
                if (target == null) continue;

                Vector3 samplePoint = target.position + Vector3.up * contactHeightOffset;

                if (!TryFindClosestSurface(samplePoint, rangeSqr, out Vector3 surface, out float distance))
                {
                    continue;
                }

                float strength = Mathf.Clamp01(1f - distance / proximityDistance);
                proximityPoints[count] = new Vector4(surface.x, surface.y, surface.z, strength);
                count++;

                if (distance <= contactDistance && HasRippleCooldownElapsed(target, now))
                {
                    lastRippleTime[target] = now;
                    AddRipple(surface, now);
                }
            }

            return count;
        }

        /// <summary>
        /// 사거리 안에서 가장 가까운 배리어 표면 지점을 찾는다.
        /// </summary>
        /// <param name="samplePoint">기준이 되는 월드 좌표.</param>
        /// <param name="rangeSqr">근접 판정 거리의 제곱.</param>
        /// <param name="surface">찾은 표면 지점.</param>
        /// <param name="distance">그 지점까지의 거리.</param>
        /// <returns>사거리 안에 표면이 있으면 true.</returns>
        private bool TryFindClosestSurface(Vector3 samplePoint, float rangeSqr, out Vector3 surface, out float distance)
        {
            surface = default;
            float bestSqr = rangeSqr;
            bool found = false;

            for (int c = 0; c < barrierColliders.Length; c++)
            {
                Collider collider = barrierColliders[c];
                if (collider == null || !collider.enabled) continue;

                // ClosestPoint는 싼 연산이 아니다. 바운즈로 먼저 걸러서 대부분의 벽을 건너뛴다.
                if (collider.bounds.SqrDistance(samplePoint) > bestSqr) continue;

                Vector3 point = collider.ClosestPoint(samplePoint);
                float sqr = (point - samplePoint).sqrMagnitude;

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    surface = point;
                    found = true;
                }
            }

            distance = found ? Mathf.Sqrt(bestSqr) : 0f;
            return found;
        }

        private bool HasRippleCooldownElapsed(Transform target, float now)
        {
            return !lastRippleTime.TryGetValue(target, out float last) || now - last >= rippleCooldown;
        }

        /// <summary>
        /// 수명이 다한 파문을 걷어내고 남은 것을 셰이더로 넘길 배열에 채운다.
        /// </summary>
        /// <returns>이번 프레임에 살아있는 파문의 개수.</returns>
        private int CollectRipples(float now)
        {
            for (int i = activeRipples.Count - 1; i >= 0; i--)
            {
                if (now - activeRipples[i].w > rippleLife)
                {
                    activeRipples.RemoveAt(i);
                }
            }

            int count = Mathf.Min(activeRipples.Count, MaxPoints);
            for (int i = 0; i < count; i++)
            {
                ripplePoints[i] = activeRipples[i];
            }

            return count;
        }

        private void AddRipple(Vector3 position, float startTime)
        {
            // 슬롯이 꽉 차면 가장 오래된 것을 밀어낸다. 방금 부딪힌 쪽이 더 중요하다.
            if (activeRipples.Count >= MaxPoints)
            {
                activeRipples.RemoveAt(0);
            }

            activeRipples.Add(new Vector4(position.x, position.y, position.z, startTime));
        }

        // 플레이 중에 어느 지점이 발광 대상으로 잡혔는지 눈으로 확인하기 위한 표시.
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || barrierColliders == null) return;

            Gizmos.color = new Color(0.1f, 0.85f, 1f, 0.9f);

            for (int i = 0; i < targets.Count; i++)
            {
                Transform target = targets[i];
                if (target == null) continue;

                Vector3 samplePoint = target.position + Vector3.up * contactHeightOffset;
                float rangeSqr = proximityDistance * proximityDistance;

                if (TryFindClosestSurface(samplePoint, rangeSqr, out Vector3 surface, out _))
                {
                    Gizmos.DrawLine(samplePoint, surface);
                    Gizmos.DrawWireSphere(surface, 0.3f);
                }
            }
        }
    }
}
