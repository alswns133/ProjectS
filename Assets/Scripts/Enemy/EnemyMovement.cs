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

        [Header("회전")]
        // 목적지 방향으로 도는 각속도(도/초). 크게 잡을수록 "홱 돌고 나서 이동"이 또렷해진다.
        [SerializeField, Min(0f)] private float turnSpeed = 720f;

        // 이 각도(도)보다 많이 틀어져 있으면 제자리에서 먼저 돌고, 그 안쪽이면 이동하며 돈다.
        // 회전-먼저-이동을 만드는 문턱. 작으면 자주 멈춰 돌고, 크면 거의 이동하며 돈다.
        [SerializeField, Range(0f, 180f)] private float moveTurnGateAngle = 45f;

        [Header("루트모션 복귀")]
        // 대쉬 루트모션(발견/공격)이 끝나 에이전트를 다시 켤 때, 이 반경 안에서 가장 가까운 NavMesh 지점을 찾아 복귀한다.
        // 루트모션이 mesh 밖으로 조금 벗어나도 다시 붙게 하기 위함. 너무 크게 잡으면 엉뚱한 곳으로 순간이동할 수 있다.
        [SerializeField, Min(0.1f)] private float agentReturnSampleRadius = 2f;

        private NavMeshAgent agent;
        private NavMeshPath path;

        // 공중 런처 동안 클립 루트모션을 위치해 직접 적용하기 위한 Animator 참조
        // EnemyAnimation과 별개로 캐싱 (루트모션 출력을 읽는 용도)
        private Animator animator;

        // true인 동안에만 OnAnimatormove가 루트모션을 위치에 더한다 (공중에 떠 있는 구간)
        // 평소 이동은 NavMeshAgent가 위치를 소유하므로 꺼져 있으면 루트모션을 무시
        private bool useRootMotion;

        // 코드 구동 대시(돌진) 상태. 루트모션이 없는(제자리) 돌진 클립을 위해, 시작 순간 방향으로 커밋해
        // 커브가 지정한 속도로 일직선 전진시킨다(멧돼지 돌진 — 진행 중 대상을 추적하지 않음).
        // 루트모션 돌진(useRootMotion)과 달리 에이전트를 끄지 않는다: agent.Move가 NavMesh에 클램프하므로
        // 종료 시 재클램프(EndAttackRootMotion의 SamplePosition)가 필요 없다.
        private bool codedCharging;
        private Vector3 codedChargeDir;
        private float codedChargeDuration;
        private float codedChargeElapsed;
        private AnimationCurve codedChargeCurve;

        // 돌진 루트모션이 이 대상보다 chargeKeepDistance 안쪽으로는 전진하지 않게 막는다(모션은 유지, 위치 전진만 정지).
        // 0이면 비활성 → 일반 잡몹은 영향 없음. 대상 null이면(공중 런처 등) 클램프 안 함.
        [SerializeField, Min(0f)] private float chargeKeepDistance = 0f;
        private Transform chargeKeepTarget;

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

        /// <summary>
        /// 레이드 보스 전용.
        /// 현재 이동 방향을 자기 로컬 기준(x=오른쪽, z=앞)으로 정규화해 돌려준다.
        /// 8방향 로코모션 블렌드값(MoveX/MoveY) 계산용. 거의 정지 상태면 Vector3.zero.
        /// </summary>
        public Vector3 LocalMoveDirection()
        {
            if (!agent.enabled) return Vector3.zero;
            Vector3 v = agent.velocity;
            float mag = v.magnitude;
            if (mag < 0.01f) return Vector3.zero;
            return transform.InverseTransformDirection(v / mag);
        }

        /// <summary>현재 경로의 끝에 도달했는지 여부. 순찰 상태가 지점 도착 판정에 쓴다.</summary>
        public bool ReachedPathEnd => agent.enabled && agent.isOnNavMesh && !agent.pathPending
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

        // 스폰/재활성 시 에이전트를 NavMesh에 확실히 올린다. 스포너가 mesh에서 살짝 벗어난 곳(스폰 y=0 등)에
        // 소환해도 가까운 지점으로 warp해 붙인다. off-mesh면 SetDestination이 무시돼 적이 아예 안 움직이기 때문이다.
        // 풀링으로 재활성되는 적도 매번 붙도록 Start가 아니라 OnEnable에서 한다.
        private void OnEnable()
        {
            if (agent == null || !agent.enabled || agent.isOnNavMesh) return;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, agentReturnSampleRadius, NavMesh.AllAreas))
                agent.Warp(hit.position);
            else
                Debug.LogWarning($"{name}: 스폰 지점 {transform.position} 근처({agentReturnSampleRadius}m)에 NavMesh가 없어 " +
                                 "에이전트가 붙지 못했다 → 움직이지 못한다. NavMesh 베이크/스폰 위치를 확인하라.", this);
        }

        private void Update()
        {
            // isStopped는 NavMesh 위 활성 에이전트에서만 읽을 수 있다 → off-mesh면 정지로 간주하고 먼저 걸러낸다.
            float raw = (!agent.enabled || !agent.isOnNavMesh || agent.isStopped) ? 0f : agent.velocity.magnitude;

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
            agent.velocity = Vector3.zero;   // 관성 제거: isStopped만으론 autoBraking으로 미끄러져 공격 진입 지점을 지나침
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
        /// NavMeshAgent의 자동 회전을 켜고 끈다. 회전을 코드가 직접 소유하는 구간(추격의 회전-먼저-이동,
        /// 나중의 공격 조준)에서 끈다. 끄지 않으면 에이전트가 이동 방향으로 몸을 계속 돌려 코드 회전과 다툰다.
        /// </summary>
        public void SetAutoRotation(bool enabled)
        {
            if (agent != null) agent.updateRotation = enabled;
        }

        /// <summary>
        /// 경로 방향으로 몸을 돌리며 이동한다. 많이 틀어져 있으면(moveTurnGateAngle 초과) 이동을 멈추고
        /// 제자리에서 먼저 돌고, 정렬되면 이동을 재개한다("빠르게 회전 → 이동" 순서).
        /// 자동 회전(updateRotation)을 끈 상태에서 호출해야 한다 — 안 그러면 에이전트 회전과 다툰다.
        /// </summary>
        /// <returns>제자리 회전 중이면 true(호출부가 이동 애니 대신 정지 모션을 쓰게 한다).</returns>
        public bool MoveWithTurnGate()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return false;

            // 직선 타겟 방향이 아니라 '경로 상 다음 코너'로 돈다 → 벽을 우회하는 중에도 실제 이동 방향과 맞는다.
            // steeringTarget은 isStopped여도 유효해, 멈춰 도는 동안에도 돌 방향을 잃지 않는다.
            Vector3 dir = agent.steeringTarget - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return false;

            dir.Normalize();

            // 게이트 판정은 '돌기 전' 각도로 한다. 그래야 이번 프레임 회전량과 무관하게 일관된다.
            float angle = Vector3.Angle(transform.forward, dir);
            bool turningInPlace = angle > moveTurnGateAngle;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);

            agent.isStopped = turningInPlace;   // 크게 틀어져 있으면 이동 정지(회전만), 정렬되면 이동
            return turningInPlace;
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
        /// 이동 속도를 지정 값으로 고정한다. 상태별로 "순찰=Walk / 추격=Run"을 명확히 가르기 위해
        /// PatrolState/ChaseState가 Enter에서 각자의 속도를 넣는다. baseSpeed(발견 대시 배수의 기준)는
        /// 건드리지 않아 DetectState의 SetSpeedMultiplier와 독립적으로 동작한다.
        /// </summary>
        public void SetMoveSpeed(float speed)
        {
            if (agent == null) return;
            agent.speed = Mathf.Max(0f, speed);
        }

        /// <summary>
        /// 스폰 중심 반경 안에서 NavMesh 위의 무작위 지점을 하나 뽑는다. 순찰 배회 목적지에 쓴다.
        /// 반경 안에 유효한 NavMesh가 없으면 false를 돌려 호출부가 안전하게 제자리 유지하게 한다.
        /// 첫 표본이 NavMesh 밖일 수 있어 몇 번 재시도한다(밀집·좁은 통로에서도 목적지를 얻게).
        /// </summary>
        public bool TryGetRandomPatrolPoint(Vector3 center, float radius, out Vector3 result)
        {
            if (agent != null && radius > 0f)
            {
                for (int i = 0; i < 5; i++)
                {
                    Vector2 offset = Random.insideUnitCircle * radius;
                    Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);

                    if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, agent.areaMask))
                    {
                        result = hit.position;
                        return true;
                    }
                }
            }

            result = center;
            return false;
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
        /// 공격 대쉬 시작. 이 구간 동안 <see cref="OnAnimatorMove"/>가 공격 클립의 수평 루트모션을 transform에 직접 더해 전진시킨다.
        /// 에이전트는 끈다(켜 두면 NavMesh 높이로 몸을 끌어내려 루트모션이 뭉개진다). transform 직접 이동이라
        /// NavMesh 클램프가 없어 mesh 밖으로 벗어날 수 있으므로, 종료 시 <see cref="EndAttackRootMotion"/>이 가까운 지점으로 복귀시킨다.
        /// EnemyAttackState.Enter에서 켜고 Exit에서 반드시 끈다(피격/사망으로 끊겨도 전진이 남지 않게).
        /// </summary>
        public void BeginAttackRootMotion(Transform keepAwayFrom = null)
        {
            chargeKeepTarget = keepAwayFrom;
            if (agent.enabled) agent.enabled = false;
            useRootMotion = true;
        }

        /// <summary>
        /// 공격 대쉬 종료. 루트모션 전진을 끈다. 끄지 않으면 이후 Idle/Walk 클립의 미세 루트모션이 위치에 샌다.
        /// </summary>
        /// <remarks>
        /// 대쉬 루트모션은 <see cref="OnAnimatorMove"/>에서 transform을 직접 옮기므로 NavMesh 클램프가 없다 →
        /// mesh 밖으로 벗어난 채 에이전트를 켜면 "not close enough to the NavMesh"로 생성이 실패한다.
        /// 그래서 착지(<see cref="EndRootMotionAndLand"/>)와 같이, 켜기 전에 가장 가까운 NavMesh 지점으로 먼저 끌어온다.
        /// </remarks>
        public void EndAttackRootMotion()
        {
            chargeKeepTarget = null;
            useRootMotion = false;
            if (agent.enabled) return;

            // NavMesh 밖으로 벗어났을 수 있으므로, 켜기 전에 가까운 지점을 찾아 transform을 먼저 그 위로 올린다
            // (transform을 먼저 옮겨두면 enabled = true가 그 자리에서 성공한다).
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, agentReturnSampleRadius, NavMesh.AllAreas))
                transform.position = hit.position;

            agent.enabled = true;
        }

        /// <summary>
        /// 코드 구동 대시(돌진) 시작. 루트모션이 없는 돌진 클립을 위해, 시작 순간의 <paramref name="direction"/>으로
        /// 방향을 잠그고(평면화·정규화) <paramref name="speedCurve"/>가 지정한 속도로 <paramref name="duration"/>초 동안
        /// 일직선 전진시킨다. 진행 중 방향을 다시 잡지 않아 대상을 추적하지 않는다(멧돼지 돌진).
        /// </summary>
        /// <remarks>
        /// 루트모션 돌진(<see cref="BeginAttackRootMotion"/>)과 달리 에이전트를 끄지 않는다:
        /// <see cref="TickCodedCharge"/>가 <c>agent.Move</c>로 밀어 NavMesh에 클램프되므로, 종료 시 가까운
        /// NavMesh 지점으로 되돌리는 재클램프가 필요 없다. EnemyAttackState.Enter에서 켜고 Exit에서 반드시
        /// <see cref="EndCodedCharge"/>로 끈다(피격/사망으로 끊겨도 전진이 다음 상태로 새지 않게).
        /// </remarks>
        public void BeginCodedCharge(Vector3 direction, float duration, AnimationCurve speedCurve)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            // 방향이 사실상 0(대상과 같은 자리)일 땐 바라보는 방향으로 돌진한다.
            codedChargeDir = flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward;
            codedChargeDuration = Mathf.Max(0f, duration);
            codedChargeCurve = speedCurve;
            codedChargeElapsed = 0f;
            codedCharging = true;
        }

        /// <summary>
        /// 코드 구동 대시를 한 프레임 진행한다. EnemyAttackState.Update가 돌진 동안 매 프레임 호출한다.
        /// 커브를 정규화 시간(경과/지속)으로 샘플해 속도(m/s)를 얻고, 잠긴 방향으로 <c>agent.Move</c>한다.
        /// 지속 시간이 다 차면 스스로 정지한다 — 커브가 시작 속도 0이어도 시간은 흐르므로 데드락이 없다.
        /// </summary>
        public void TickCodedCharge(float deltaTime)
        {
            if (!codedCharging) return;
            if (!agent.enabled || !agent.isOnNavMesh) return;

            codedChargeElapsed += deltaTime;
            float t = codedChargeDuration > 0f ? Mathf.Clamp01(codedChargeElapsed / codedChargeDuration) : 1f;
            float speed = codedChargeCurve != null ? codedChargeCurve.Evaluate(t) : 0f;

            if (speed > 0f) agent.Move(codedChargeDir * (speed * deltaTime));

            if (codedChargeElapsed >= codedChargeDuration) codedCharging = false;
        }

        /// <summary>
        /// 코드 구동 대시를 끝낸다(전진 정지). 공격이 정상 종료되든 피격/사망으로 끊기든
        /// EnemyAttackState.Exit에서 반드시 호출한다.
        /// </summary>
        public void EndCodedCharge() => codedCharging = false;

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
            if (useRootMotion)
            {
                Vector3 delta = animator.deltaPosition;

                if (chargeKeepTarget != null && chargeKeepDistance > 0f)
                {
                    Vector3 toPlayer = chargeKeepTarget.position - transform.position;
                    toPlayer.y = 0f;

                    // 유지 거리 안 + 델타가 플레이어 쪽으로 파고드는 성분일 때만 수평 전진을 버린다(수직은 유지).
                    Vector3 flat = new Vector3(delta.x, 0f, delta.z);
                    if (toPlayer.magnitude <= chargeKeepDistance && Vector3.Dot(flat, toPlayer) > 0f)
                    {
                        delta.x = 0f;
                        delta.z = 0f;
                    }
                }

                transform.position += delta;
                return;
            }
        }

        /// <summary>에이전트를 완전히 끈다. 사망 시 1회 호출하며 되돌리지 않는다.</summary>
        public void DisableAgent() => agent.enabled = false;
    }
}
