namespace ProjectS.Enemies
{
    ﻿using UnityEngine;
    using UnityEngine.AI;

    /// <summary>
    /// NavMeshAgent 기반 이동. 추적 목적지 설정과 정지/재개, 사망 시 에이전트 비활성화를 담당한다.
    /// 플레이어(CharacterController)와 달리 길 찾기가 필요해 NavMesh를 사용한다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyMovement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float reachSampleHeightTolerance = 0.5f;

        // 밀집 시 회피 계산으로 순간 속도가 프레임마다 진동해 걷기↔대기 애니메이션이 깜빡이는 것을 막는 설정.
        // 속도를 평활화한 뒤 시작/멈춤 문턱을 분리(히스테리시스)해 문턱 근처 진동이 판정을 못 뒤집게 한다.
        [Header("애니메이션 속도 판정")]
        [SerializeField, Min(0f)] private float animationSpeedDeadZone = 0.3f;

        // 순간 속도를 이 시간에 걸친 지수 평균으로 뭉갠다. 0이면 즉시 반영.
        [SerializeField, Min(0f)] private float speedSmoothingTime = 0.2f;

        // 움직임 시작으로 인정하는 문턱. 멈춤 문턱(deadZone)보다 높게 잡아야
        // 두 문턱 사이에서 진동하는 속도가 걷기/대기 판정을 뒤집지 못한다.
        [SerializeField, Min(0f)] private float animationMoveStartThreshold = 0.6f;

        // 회피 우선순위 범위. 전원이 같은 값(유니티 기본 50)이면 밀집 시 서로 대칭으로
        // 밀어내는 회피 계산이 반복되어 제자리 떨림이 생긴다. 개체마다 다른 값을 주면
        // 낮은 숫자(높은 우선순위) 쪽이 길을 얻고 나머지가 비켜서며 떨림이 크게 줄어든다.
        [Header("군중 회피")]
        [SerializeField, Range(0, 99)] private int avoidancePriorityMin = 30;
        [SerializeField, Range(0, 99)] private int avoidancePriorityMax = 70;

        private NavMeshAgent agent;
        private NavMeshPath path;

        // 공중 런처 동안 클립 루트모션을 위치해 직접 적용하기 위한 Animator 참조
        // EnemyAnimation과 별개로 캐싱 (루트모션 출력을 읽는 용도)
        private Animator animator;

        // true인 동안에만 OnAnimatormove가 루트모션을 위치에 더한다 (공중에 떠 있는 구간)
        // 평소 이동은 NavMeshAgent가 위치를 소유하므로 꺼져 있으면 루트모션을 무시
        private bool useRootMotion;

        // 발견 대시처럼 일시적으로 속도를 바꾼 뒤 원래 값으로 되돌리기 위한 기준 속도.
        // NavMeshAgent의 기본 speed는 에디터 튜닝 값이므로 Awake에서 보관한다.
        private float baseSpeed;

        // 애니메이션 속도 판정용 내부 상태. Update에서 갱신한다.
        private float smoothedSpeed;
        private bool isMovingForAnimation;

        /// <summary>
        /// 애니메이션 블렌드용 이동 속력. 순간 속도가 아니라 평활화 + 이중 문턱을 거친 값이라
        /// 밀집 시 회피 진동에도 걷기/대기가 깜빡이지 않는다. 대기 판정이면 0을 돌려준다.
        /// </summary>
        public float CurrentSpeed => isMovingForAnimation ? smoothedSpeed : 0f;

        /// <summary>현재 경로의 끝에 도달했는지 여부. 순찰 상태가 지점 도착 판정에 쓴다.</summary>
        public bool ReachedPathEnd => agent.enabled && !agent.pathPending
            && agent.remainingDistance <= agent.stoppingDistance;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            path = new NavMeshPath();
            baseSpeed = agent.speed;
            animator = GetComponent<Animator>();

            // Random.Range(int)는 max가 배타적이므로 +1로 최댓값도 포함시킨다.
            agent.avoidancePriority = Random.Range(
                avoidancePriorityMin, Mathf.Max(avoidancePriorityMin, avoidancePriorityMax) + 1);
        }

        private void Update()
        {
            float raw = (!agent.enabled || agent.isStopped) ? 0f : agent.velocity.magnitude;

            // 지수 이동 평균. Lerp 계수를 deltaTime 기반 지수식으로 계산해 프레임레이트와 무관하게 감쇠된다.
            smoothedSpeed = speedSmoothingTime <= 0f
                ? raw
                : Mathf.Lerp(smoothedSpeed, raw, 1f - Mathf.Exp(-Time.deltaTime / speedSmoothingTime));

            // 이중 문턱: 시작은 높은 문턱, 멈춤은 낮은 문턱. 사이 구간에서는 현재 판정을 유지한다.
            if (isMovingForAnimation)
            {
                if (smoothedSpeed < animationSpeedDeadZone) isMovingForAnimation = false;
            }
            else
            {
                if (smoothedSpeed >= animationMoveStartThreshold) isMovingForAnimation = true;
            }
        }

        /// <summary>목적지를 갱신한다. 추적 상태가 매 프레임 호출한다.</summary>
        public void SetDestination(Vector3 worldPos)
        {
            // 사망(에이전트 꺼짐)이나 NavMesh 밖 스폰 직후 호출돼도 예외가 나지 않게 방어한다.
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.SetDestination(worldPos);
        }

        /// <summary>지정 위치까지 완전한 NavMesh 경로가 있는지 검사한다.</summary>
        public bool CanReach(Vector3 worldPos, float sampleRadius)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return false;

            if (!NavMesh.SamplePosition(
                worldPos, // 검사할 목표 위치: 보통 플레이어 위치
                out NavMeshHit hit, // 목표 주변에서 찾은 가장 가까운 NavMesh 지점
                sampleRadius, // 검색 반경: 플레이어가 NavMesh에서 살짝 떠 있거나 가장자리에 있을 때 보정할 거리
                agent.areaMask)) // 이 에이전트가 이동 가능한 NavMesh Area 마스크
                return false;

            // 점프로 올라간 높은 지형 아래의 NavMesh가 잡히면 갈 수 있다고 오판할 수 있다.
            if (Mathf.Abs(hit.position.y - worldPos.y) > reachSampleHeightTolerance) return false;
            if (!agent.CalculatePath(hit.position, path)) return false;

            return path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>이동을 멈춘다(경로는 유지). 대기·공격 상태 진입 시 호출한다.</summary>
        public void Stop()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
        }

        /// <summary>
        /// 이동을 멈추고 경로까지 지운다. 지금 향하던 목적지가 더 이상 의미 없을 때 쓴다.
        /// Stop()은 경로를 남기므로, 나중에 Resume()이 불리는 순간 에이전트가 옛 목적지로
        /// 한 프레임 끌려간다(공격이 끝나고 추격으로 돌아올 때 몸이 툭 밀리는 원인).
        /// 순찰은 ReachedPathEnd로 도착을 판정하므로 이 메서드를 쓰지 않는다.
        /// </summary>
        public void StopAndClearPath()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>멈췄던 이동을 재개한다. 추적 상태 진입 시 호출한다.</summary>
        public void Resume()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = false;
        }

        /// <summary>지정 위치를 즉시 바라본다(수평 성분만). 공격 시작 시 대상 정렬에 쓴다.</summary>
        public void Face(Vector3 worldPos)
        {
            Vector3 dir = worldPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(dir);
        }

        /// <summary>
        /// 기본 NavMeshAgent 속도에 배수를 적용한다.
        /// DetectState의 발견 대시처럼 짧게 속도를 바꾸는 상태에서만 사용한다.
        /// </summary>
        public void SetSpeedMultiplier(float multiplier)
        {
            // DetectState 전용 튜닝. multiplier가 음수로 들어와도 역주행하지 않도록 0 이상으로 제한한다.
            if (agent == null) return;
            agent.speed = baseSpeed * Mathf.Max(0f, multiplier);
        }

        /// <summary>
        /// NavMeshAgent 속도를 Awake에서 저장한 기본값으로 되돌린다.
        /// 발견 대시가 다른 상태로 끊겨도 속도 변경이 남지 않게 Exit에서 호출한다.
        /// </summary>
        public void ResetSpeed()
        {
            // DetectState가 끝날 때 반드시 호출해 추격/공격 상태가 원래 이동 속도를 쓰게 한다.
            if (agent == null) return;
            agent.speed = baseSpeed;
        }

        /// <summary>
        /// 공중 런처 진입 - NavMeshAgent를 비활성화 후 위치 제어를 클립 루트모션에 넘김
        /// 에이전트가 켜져 있으면 프레임 몸을 NavMesh 높이로 끌어내려 상승이 무효화 됨
        /// </summary>
        public void BeginRootMotion()
        {
            if (agent.enabled) agent.enabled = false;
            useRootMotion = true;
        }

        /// <summary>
        /// 착지 처리) 루트모션 적용을 끄고 현재 위치에서 가장 가까운 NavMesh 지점으로 에이전트를 복귀
        /// Warp로 재배치하지 않으면 공중에서 옮겨간 위치가 NavMesh 밖이라 에이전트를 켜는 순간 경고 및 순간이동
        /// sampleRadius는 착지점 주변에서 유효한 바닥을 찾는 검색 반경
        /// </summary>
        /// <param name="sampleRadius"></param>
        public void EndRootMotionAndLand(float sampleRadius = 1.5f)
        {
            useRootMotion = false;

            if (!agent.enabled) agent.enabled = true;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        /// <summary>
        /// 애니메이터가 매 프레임 호출 (루트모션 처리를 스크립트가 담당)
        /// 공중 런처 구간에서만 클립의 이동량(deltaPosition)을 위치에 더해 몸을 띄운다.
        /// 평소엔 useRootMotion이 false여서 무시, Idle/Walk/Attack 클립의 미세 루트모션이 위치에 새지 않는다.
        /// </summary>
        private void OnAnimatorMove()
        {
            if (!useRootMotion) return;
            transform.position += animator.deltaPosition;
        }

        /// <summary>에이전트를 완전히 끈다. 사망 시 1회 호출하며 되돌리지 않는다.</summary>
        public void DisableAgent() => agent.enabled = false;
    }
}
