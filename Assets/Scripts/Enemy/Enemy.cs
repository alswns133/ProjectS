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
    public EnemyStats Stats { get; private set; }
    public EnemyMovement Movement { get; private set; }
    public EnemyAnimation Animation { get; private set; }
    public EnemyCombat Combat { get; private set; }
    public EnemyStateMachine StateMachine { get; private set; }

    private void Awake()
    {
        Movement = GetComponent<EnemyMovement>();
        Animation = GetComponent<EnemyAnimation>();
        Stats = GetComponent<EnemyStats>();
        Combat = GetComponent<EnemyCombat>();
        StateMachine = new EnemyStateMachine();
    }

    private void Start()
    {
        StateMachine.ChangeState(new EnemyIdleState(this));
    }

    private void Update()
    {
        StateMachine.Update();
    }
}
