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
    [Header("Patrol")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolWaitTime = 1f;

    // ── 발견 ─────────────────────────────────────────────────────────
    // 감지 설정. Idle/Patrol에서 플레이어가 detectionRange 안에 들어오면 DetectState로 진입한다.
    // 바로 Chase로 가지 않는 이유: "플레이어 발견" 연출과 짧은 대시를 하나의 커밋 동작으로 보장하기 위함.
    // detectDuration 동안 발견 연출/대시를 처리한 뒤 ChaseState로 넘어간다.
    [Header("Detection")]
    [SerializeField] private float detectionRange = 8f;
    [SerializeField] private float detectDuration = 0.7f;
    [SerializeField] private float detectDashSpeedMultiplier = 1.8f;

    // ── 피격 경직 ────────────────────────────────────────────────────
    // 피격 경직 설정. 일반 몬스터는 켜고, 보스/슈퍼아머 몬스터는 끄는 식으로 쓴다.
    // hitStunCooldown은 연타를 맞을 때 매 프레임 HitState로 재진입하는 것을 막는 안전장치다.
    // "피격마다 무조건 경직"으로 두면 다단히트/장판에 갇혀 공격과 추격을 영영 못 할 수 있다.
    [Header("Hit Reaction")]
    [SerializeField] private bool useHitStun = true;
    [SerializeField] private float hitStunDuration = 0.35f;
    [SerializeField] private float hitStunCooldown = 0.2f;

    [Header("Death")]
    [SerializeField] private float despawnDelay = 3f;

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
    /// <summary>사망 연출과 AI/충돌 비활성화를 처리하는 막다른 상태.</summary>
    public EnemyDeadState DeadState { get; private set; }

    /// <summary>추적 대상(플레이어). 씬에 없으면 null이며, 상태들은 대상이 없으면 자연스럽게 대기한다.</summary>
    public Transform Target { get; private set; }

    /// <summary>피격용 루트 콜라이더. 사망 시 추가 타격·길막힘을 막기 위해 DeadState가 끈다.</summary>
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
    /// DeadState는 다른 상태로 전환되지 않는 막다른 상태다.
    /// </summary>
    public void OnDied() => StateMachine.ChangeState(DeadState);

    private void OnDrawGizmosSelected()
    {
        // 감지 반경 미리보기. 공격 히트박스(빨강)와 구분되게 노란색을 쓴다.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
