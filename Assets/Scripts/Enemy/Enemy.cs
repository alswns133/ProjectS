﻿using UnityEngine;

/// <summary>
/// 몬스터의 중앙 컨텍스트. 기능 컴포넌트를 한곳에서 보유하고 상태 머신을 구동한다.
/// 순찰/발견/추격/공격/피격/사망의 큰 흐름만 중재하고,
/// 실제 이동·애니메이션·판정은 EnemyMovement/EnemyAnimation/EnemyCombat과 각 State가 담당한다.
/// 상태들은 이 Enemy를 통해 필요한 컴포넌트와 튜닝 값에 접근한다(Player와 같은 컨텍스트 패턴).
/// </summary>
// RequireComponent: Enemy를 붙이면 필수 부품들이 자동으로 함께 추가된다.
// → 프리팹 제작 중 컴포넌트 누락으로 런타임 null이 나는 실수를 구조적으로 줄인다.
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyAnimation))]
[RequireComponent(typeof(EnemyCombat))]
public class Enemy : MonoBehaviour
{
    // ── 순찰 ─────────────────────────────────────────────────────────
    // 순찰 지점. 비워두면 제자리 대기(Idle)로 시작하고,
    // 하나 이상 넣으면 PatrolState에서 지점들을 순서대로 돈다.
    // 순찰 경로 판단은 상태가 담당하고, "어디를 돌지"라는 데이터만 컨텍스트가 소유한다.
    [Header("순찰")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolWaitTime = 1f;

    // ── 발견 ─────────────────────────────────────────────────────────
    // 감지 설정. Idle/Patrol에서 플레이어가 detectionRange 안에 들어오면 DetectState로 진입한다.
    // 바로 Chase로 가지 않는 이유: "플레이어 발견" 연출과 짧은 대시를 하나의 커밋 동작으로 보장하기 위함.
    // detectDuration 동안 발견 연출/대시를 처리한 뒤 ChaseState로 넘어간다.
    [Header("감지")]
    [SerializeField] private float detectionRange = 8f;
    [SerializeField] private float detectDuration = 0.7f;
    [SerializeField] private float detectDashSpeedMultiplier = 1.8f;
    [SerializeField, Min(0f)] private float targetNavMeshSampleRadius = 0.5f;
    [SerializeField, Min(0.02f)] private float targetPathCheckInterval = 0.25f;

    // ── 피격 경직 ────────────────────────────────────────────────────
    // 피격 경직 설정. 일반 몬스터는 켜고, 보스/슈퍼아머 몬스터는 끄는 식으로 쓴다.
    // hitStunCooldown은 연타를 맞을 때 매 프레임 HitState로 재진입하는 것을 막는 안전장치다.
    // "피격마다 무조건 경직"으로 두면 다단히트/장판에 갇혀 공격과 추격을 영영 못 할 수 있다.
    [Header("피격 반응")]
    [SerializeField] private bool useHitStun = true;
    [SerializeField] private float hitStunDuration = 0.35f;
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("사망")]
    [SerializeField] private float despawnDelay = 3f;

    // ── 군중 제어 ────────────────────────────────────────────────────
    // 여러 근접 몬스터가 플레이어 한 점으로 몰려 겹치고 떠는 것을 막는 설정.
    // 슬롯 같은 중앙 관리 시스템 없이 "개체별 선호 교전 거리 + 앞이 막히면 옆으로 돌기"만으로
    // 분산시킨다 → 슬롯 배정/반납 상태가 없어 누수·경합으로 망가질 여지가 없다.
    // 교전 거리는 AttackRange의 배수라 몬스터 크기(사거리)에 자동으로 비례한다.
    [Header("군중 제어")]
    // 선호 교전 거리 = AttackRange × 이 범위에서 뽑은 개체별 랜덤 배수.
    // Min~Max 폭이 넓을수록 서는 거리가 흩어져 자연스럽고, 좁을수록 정렬된 느낌이 난다.
    // 상한을 0.95로 막은 이유: 1.0 이상이면 목적지가 공격 사거리 밖이 되어 영영 공격을 못 한다.
    [SerializeField, Range(0.5f, 0.95f)] private float combatDistanceFactorMin = 0.7f;
    [SerializeField, Range(0.5f, 0.95f)] private float combatDistanceFactorMax = 0.9f;

    // 아군 차단 SphereCast의 두께(반경). 몬스터 몸통 폭에 맞춘다.
    // 너무 크면 옆을 스쳐 가는 아군에도 막힌 것으로 판정해 불필요하게 옆으로 돈다.
    [SerializeField, Min(0f)] private float allyBlockRadius = 0.4f;

    // 아군 차단을 검사하는 전방 거리. 이 안에 아군이 있으면 "막혔다"로 판정한다.
    // 짧으면 이미 몸이 닿은 뒤에야 돌기 시작하고, 길면 멀리 있는 아군에도 미리 돌아 대열이 벌어진다.
    [SerializeField, Min(0f)] private float allyBlockDistance = 1.2f;

    // SphereCast 시작점의 높이(발밑 기준). 지면 콜라이더나 낮은 장식물에 걸리지 않게
    // 몸통 높이쯤에서 쏘기 위한 오프셋이다. 0이면 바닥을 긁어 오탐이 날 수 있다.
    [SerializeField, Min(0f)] private float allyBlockProbeHeight = 0.5f;

    // 앞이 막혔을 때 목적지를 트는 각도(타겟 중심 접선 방향). 방향은 개체별 좌/우 랜덤 고정.
    // 작으면 옆으로 도는 티가 안 나서 계속 뒤에서 밀고, 크면 크게 우회해 포위가 빨라지는 대신 동선이 과장돼 보인다.
    [SerializeField, Range(0f, 90f)] private float allySidestepAngle = 45f;

    /// <summary>HP와 사망 판정을 소유하는 스탯 컴포넌트.</summary>
    public EnemyStats Stats { get; private set; }
    /// <summary>NavMeshAgent 기반 이동과 정지/재개를 담당하는 이동 컴포넌트.</summary>
    public EnemyMovement Movement { get; private set; }
    /// <summary>Animator 파라미터와 트리거를 제어하는 애니메이션 브릿지.</summary>
    public EnemyAnimation Animation { get; private set; }
    /// <summary>공격 패턴 선택, 쿨다운, 히트 판정을 담당하는 전투 컴포넌트.</summary>
    public EnemyCombat Combat { get; private set; }
    /// <summary>몬스터 로컬 파티클 이펙트를 의미 단위로 재생하는 선택 컴포넌트.</summary>
    public EnemyEffects Effects { get; private set; }
    /// <summary>현재 Enemy 상태 하나를 보유하고 전환 순서를 보장하는 상태 머신.</summary>
    public EnemyStateMachine StateMachine { get; private set; }

    // 상태 인스턴스도 상태끼리 전환할 때 참조하므로 읽기 전용 공개.
    // Awake에서 한 번만 생성해 재사용한다 → 전환 때마다 new 하지 않아 GC 부담이 없다.
    /// <summary>제자리 대기 상태. 순찰 지점이 없을 때 시작 상태로 사용된다.</summary>
    public EnemyIdleState IdleState { get; private set; }
    /// <summary>웨이포인트 순찰 상태. 순찰 지점이 있을 때 시작 상태로 사용된다.</summary>
    public EnemyPatrolState PatrolState { get; private set; }
    /// <summary>플레이어 최초 발견 연출과 대시를 처리하는 상태.</summary>
    public EnemyDetectState DetectState { get; private set; }
    /// <summary>플레이어를 추적하고 공격 진입을 판단하는 상태.</summary>
    public EnemyChaseState ChaseState { get; private set; }
    /// <summary>선택된 공격 패턴을 재생하는 상태.</summary>
    public EnemyAttackState AttackState { get; private set; }
    /// <summary>피격 경직과 피격 연출을 처리하는 상태.</summary>
    public EnemyHitState HitState { get; private set; }
    /// <summary>사망 연출과 AI/충돌 비활성화를 처리하는 최종 상태.</summary>
    public EnemyDeadState DeadState { get; private set; }

    /// <summary>추적 대상(플레이어). 씬에 없으면 null이며, 상태들은 대상이 없으면 자연스럽게 대기한다.</summary>
    public Transform Target { get; private set; }

    /// <summary>피격용 루트 콜라이더. 사망 시 추가 피격과 물리 충돌을 막기 위해 DeadState가 끈다.</summary>
    public Collider BodyCollider { get; private set; }

    /// <summary>플레이어 감지 반경. Idle/Patrol 상태가 발견 진입 판정에 쓴다.</summary>
    public float DetectionRange => detectionRange;
    /// <summary>순찰 지점이 하나 이상 있으면 PatrolState로 시작한다.</summary>
    public bool HasPatrol => patrolPoints != null && patrolPoints.Length > 0;
    /// <summary>등록된 순찰 지점 수. PatrolState가 다음 지점 인덱스를 순환시킬 때 쓴다.</summary>
    public int PatrolPointCount => patrolPoints != null ? patrolPoints.Length : 0;
    /// <summary>순찰 지점에 도착한 뒤 다음 지점으로 가기 전 대기 시간.</summary>
    public float PatrolWaitTime => patrolWaitTime;
    /// <summary>발견 연출/대시 상태가 유지되는 시간.</summary>
    public float DetectDuration => detectDuration;
    /// <summary>발견 대시 중 NavMeshAgent 기본 속도에 곱할 배수.</summary>
    public float DetectDashSpeedMultiplier => detectDashSpeedMultiplier;
    /// <summary>피격 상태가 유지되는 시간.</summary>
    public float HitStunDuration => hitStunDuration;
    /// <summary>사망 연출 후 오브젝트가 사라지기까지의 시간.</summary>
    public float DespawnDelay => despawnDelay;

    private float nextHitStunTime;
    private float nextPathCheckTime;
    private bool cachedCanReachTarget;

    // 군중 제어 개체값. Awake에서 한 번 뽑아 고정한다.
    private float combatDistanceFactor;
    private float sidestepSign;
    private readonly RaycastHit[] allyBlockHits = new RaycastHit[8];

    private void Awake()
    {
        // 컴포넌트 캐싱은 Awake에서 1회만. 상태들이 매 프레임 GetComponent를 하지 않게 한다.
        Movement = GetComponent<EnemyMovement>();
        Animation = GetComponent<EnemyAnimation>();
        Stats = GetComponent<EnemyStats>();
        Combat = GetComponent<EnemyCombat>();
        Effects = GetComponent<EnemyEffects>();
        BodyCollider = GetComponent<Collider>();
        StateMachine = new EnemyStateMachine();

        // 상태를 미리 만들어 보관한다. 각 상태는 MonoBehaviour가 아니므로 Enemy 컨텍스트를 생성자 주입으로 받는다.
        IdleState = new EnemyIdleState(this);
        PatrolState = new EnemyPatrolState(this);
        DetectState = new EnemyDetectState(this);
        ChaseState = new EnemyChaseState(this);
        AttackState = new EnemyAttackState(this);
        HitState = new EnemyHitState(this);
        DeadState = new EnemyDeadState(this);

        // 군중 제어 개체값: 같은 프리팹이라도 교전 거리와 회피 방향이 미묘하게 달라진다.
        // 여러 마리가 몰려와도 한 원 위에 정렬된 "레일 위를 움직이는 AI" 느낌이 나지 않게 하기 위함.
        combatDistanceFactor = Random.Range(
            combatDistanceFactorMin, Mathf.Max(combatDistanceFactorMin, combatDistanceFactorMax));
        sidestepSign = Random.value < 0.5f ? -1f : 1f;
    }

    private void Start()
    {
        // 플레이어는 씬에 1명뿐이라는 전제라 시작 시 1회만 찾는다. 매 프레임 Find는 피한다.
        Player player = FindAnyObjectByType<Player>();
        if (player != null) Target = player.transform;

        // 순찰 지점이 있으면 순찰 몬스터, 없으면 제자리 대기 몬스터로 시작한다.
        StateMachine.ChangeState(HasPatrol ? PatrolState : IdleState);
    }

    private void Update()
    {
        StateMachine.Update();
    }

    /// <summary>
    /// 대상까지의 거리. 대상이 없으면 무한대를 돌려주어
    /// 감지/공격 판정이 조건문 없이 자연스럽게 실패하게 한다.
    /// </summary>
    public float DistanceToTarget()
        => Target != null ? Vector3.Distance(transform.position, Target.position) : float.PositiveInfinity;

    /// <summary>감지 거리 안에 있으면 발견을 허용한다.</summary>
    public bool CanDetectTarget()
        => DistanceToTarget() <= detectionRange;

    /// <summary>
    /// 타겟 위치까지 NavMesh 완전 경로가 있는지 주기적으로 검사한다.
    /// 플레이어가 점프로만 갈 수 있는 곳에 있으면 false가 되어 추격 대신 바라보기로 처리된다.
    /// </summary>
    public bool CanReachTarget()
    {
        if (Target == null) return false;
        if (Time.time < nextPathCheckTime) return cachedCanReachTarget;

        nextPathCheckTime = Time.time + targetPathCheckInterval;
        cachedCanReachTarget = Movement.CanReach(Target.position, targetNavMeshSampleRadius);
        return cachedCanReachTarget;
    }

    /// <summary>
    /// 추적 목적지. 플레이어 위치가 아니라 "선호 교전 거리만큼 떨어진 지점"을 돌려준다.
    /// 모든 몬스터가 같은 점을 목표로 하는 한 어떤 회피 알고리즘도 겹침을 못 막기 때문에,
    /// 목적지 자체를 개체별로 분산시킨다. 타겟 방향 앞이 아군에 막혀 있으면
    /// 목적지를 접선 방향으로 틀어 뒤에서 밀지 않고 옆자리로 돌아 들어간다.
    /// ChaseState가 매 프레임 호출한다.
    /// </summary>
    public Vector3 GetChaseDestination()
    {
        if (Target == null) return transform.position;

        Vector3 direction = transform.position - Target.position;
        direction.y = 0f;

        // 타겟과 정확히 겹쳐 방향을 구할 수 없으면 타겟 등 뒤를 기본 방향으로 쓴다.
        if (direction.sqrMagnitude < 0.0001f) direction = -Target.forward;
        direction.Normalize();

        // 회전 방향(sidestepSign)은 개체별로 고정한다. 매번 다시 뽑으면
        // 좌우 목적지 사이를 오가며 떨고, 전원이 같은 쪽이면 한 줄로 따라돌기만 한다.
        if (IsBlockedByAlly())
            direction = Quaternion.AngleAxis(allySidestepAngle * sidestepSign, Vector3.up) * direction;

        return Target.position + direction * (Combat.AttackRange * combatDistanceFactor);
    }

    /// <summary>
    /// 타겟 방향 앞쪽에 살아 있는 다른 몬스터가 있는지 검사한다.
    /// NavMeshAgent 회피만으로는 한 점에 몰린 근접 대열에서 뒷줄이 앞줄을 미는 것을 막지 못한다.
    /// </summary>
    public bool IsBlockedByAlly()
    {
        if (Target == null) return false;

        Vector3 direction = Target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return false;

        direction.Normalize();
        Vector3 origin = transform.position + Vector3.up * allyBlockProbeHeight;
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            allyBlockRadius,
            direction,
            allyBlockHits,
            allyBlockDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = allyBlockHits[i].collider;
            if (hit == null || hit == BodyCollider) continue;

            Enemy other = hit.GetComponentInParent<Enemy>();
            if (other != null && other != this && !other.Stats.IsDead)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 순찰 지점 위치 조회. 비어 있거나 null 슬롯이면 현재 위치를 돌려 안전하게 실패한다.
    /// 에디터에서 순찰 포인트 배열을 비우거나 일부 슬롯을 놓쳐도 상태 머신이 예외로 멈추지 않게 한다.
    /// </summary>
    public Vector3 GetPatrolPoint(int index)
    {
        if (!HasPatrol) return transform.position;

        Transform point = patrolPoints[Mathf.Abs(index) % patrolPoints.Length];
        return point != null ? point.position : transform.position;
    }

    /// <summary>
    /// 피격 반응 진입점. EnemyStats가 데미지를 적용하고 사망하지 않았을 때 호출한다.
    /// 경직 사용 여부와 쿨다운을 확인한 뒤 HitState로 전환한다.
    /// </summary>
    public void OnDamaged()
    {
        // 피격 경직은 선택 기능이다. 사망/쿨다운 중에는 상태를 흔들지 않는다.
        // 데미지 적용과 사망 판정은 EnemyStats가 단일 소유하고, 여기는 "살아남은 뒤 반응"만 맡는다.
        if (!useHitStun) return;
        if (Stats.IsDead) return;
        if (StateMachine.Current == DeadState) return;
        if (Time.time < nextHitStunTime) return;

        nextHitStunTime = Time.time + hitStunCooldown;
        StateMachine.ChangeState(HitState);
    }

    /// <summary>
    /// 사망 진입점. EnemyStats가 HP 0을 확정한 직후 호출한다.
    /// DeadState는 다른 상태로 전환되지 않는 최종 상태다.
    /// </summary>
    public void OnDied() => StateMachine.ChangeState(DeadState);

    private void OnDrawGizmosSelected()
    {
        // 감지 반경 미리보기. 공격 히트박스(빨강)와 구분되게 노란색을 쓴다.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        DrawCombatDistanceGizmos();
        DrawAllyBlockGizmos();
    }

    // 선호 교전 거리 미리보기.
    // 에디터 모드: 개체값이 아직 없으므로 min/max 두 원을 자기 위치에 그려 "설 거리의 범위"를 보여준다.
    // 플레이 모드: 타겟 기준으로 이 개체가 실제 뽑은 교전 원과, 지금 향하고 있는 목적지를 그린다.
    private void DrawCombatDistanceGizmos()
    {
        // Combat은 Awake에서 캐싱되므로 에디터 모드에서는 직접 찾는다(선택된 오브젝트만 그려 부담 없음).
        EnemyCombat combat = Combat != null ? Combat : GetComponent<EnemyCombat>();
        if (combat == null) return;

        float attackRange = combat.AttackRange;
        if (attackRange <= 0f) return;

        if (Application.isPlaying && Target != null)
        {
            // 실제 교전 원(주황)과 현재 목적지(점 + 연결선). 옆으로 도는 중이면 선이 접선 방향으로 꺾인다.
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(Target.position, attackRange * combatDistanceFactor);

            Vector3 destination = GetChaseDestination();
            Gizmos.DrawSphere(destination, 0.15f);
            Gizmos.DrawLine(transform.position, destination);
            return;
        }

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, attackRange * combatDistanceFactorMin);
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, attackRange * combatDistanceFactorMax);
    }

    // 아군 차단 SphereCast 프로브 미리보기. 초록=뚫림, 빨강=막힘(막힘 판정은 플레이 중에만).
    // 캡슐 모양(시작 원 + 끝 원 + 연결선)이 실제 검사 부피와 일치하므로 반경/거리를 눈으로 튜닝할 수 있다.
    private void DrawAllyBlockGizmos()
    {
        Vector3 direction = transform.forward;
        if (Target != null)
        {
            Vector3 toTarget = Target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
        }

        Gizmos.color = Application.isPlaying && IsBlockedByAlly() ? Color.red : Color.green;

        Vector3 origin = transform.position + Vector3.up * allyBlockProbeHeight;
        Vector3 end = origin + direction * allyBlockDistance;
        Gizmos.DrawWireSphere(origin, allyBlockRadius);
        Gizmos.DrawWireSphere(end, allyBlockRadius);
        Gizmos.DrawLine(origin, end);
    }
}
