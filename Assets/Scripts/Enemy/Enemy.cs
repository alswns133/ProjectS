using UnityEngine;

/// <summary>
/// 몬스터의 중앙 컨텍스트. 기능 컴포넌트를 연결하고 상태 머신을 구동한다.
/// 세부 로직은 각 컴포넌트와 상태 클래스에 둔다.
/// </summary>
[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyAnimation))]
[RequireComponent(typeof(EnemyCombat))]
public class Enemy : MonoBehaviour
{
    // 일반 몬스터 기획: 한번 인식하면 복귀 없이 계속 추격한다.
    // 감지 반경은 Idle→Chase 진입 판정에만 쓰이므로 컨텍스트가 소유한다.
    [SerializeField] private float detectionRange = 8f;

    // 사망 연출이 끝난 뒤 시체가 사라지기까지의 시간.
    [SerializeField] private float despawnDelay = 3f;

    public EnemyStats Stats { get; private set; }
    public EnemyMovement Movement { get; private set; }
    public EnemyAnimation Animation { get; private set; }
    public EnemyCombat Combat { get; private set; }
    public EnemyStateMachine StateMachine { get; private set; }

    // 상태 인스턴스는 미리 생성해 보관 → 전환할 때마다 new 하지 않아 GC 부담이 없다(Player와 동일 패턴).
    public EnemyIdleState IdleState { get; private set; }
    public EnemyChaseState ChaseState { get; private set; }
    public EnemyAttackState AttackState { get; private set; }
    public EnemyDeadState DeadState { get; private set; }

    /// <summary>추적 대상(플레이어). 씬에 없으면 null이며, 상태들은 대상이 없으면 아무것도 하지 않는다.</summary>
    public Transform Target { get; private set; }

    /// <summary>몸통 콜라이더. 사망 시 추가 타격·길막힘을 막기 위해 DeadState가 끈다.</summary>
    public Collider BodyCollider { get; private set; }

    /// <summary>플레이어 감지 반경. Idle 상태가 추적 진입 판정에 쓴다.</summary>
    public float DetectionRange => detectionRange;

    /// <summary>사망 연출 후 오브젝트가 사라지기까지의 시간(초).</summary>
    public float DespawnDelay => despawnDelay;

    private void Awake()
    {
        Movement = GetComponent<EnemyMovement>();
        Animation = GetComponent<EnemyAnimation>();
        Stats = GetComponent<EnemyStats>();
        Combat = GetComponent<EnemyCombat>();
        BodyCollider = GetComponent<Collider>();
        StateMachine = new EnemyStateMachine();

        IdleState = new EnemyIdleState(this);
        ChaseState = new EnemyChaseState(this);
        AttackState = new EnemyAttackState(this);
        DeadState = new EnemyDeadState(this);
    }

    private void Start()
    {
        // 플레이어는 씬에 1명뿐이므로 시작 시 1회만 찾는다. 매 프레임 Find는 금지.
        Player player = FindAnyObjectByType<Player>();
        if (player != null) Target = player.transform;

        StateMachine.ChangeState(IdleState);
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
    /// 사망 진입점. EnemyStats가 HP 0을 확정한 직후 호출한다.
    /// DeadState는 다른 상태로 전환되지 않는 막다른 상태다.
    /// </summary>
    public void OnDied() => StateMachine.ChangeState(DeadState);

    private void OnDrawGizmosSelected()
    {
        // 감지 반경 미리보기. 히트박스(빨강)와 구분되게 노란색을 쓴다.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
