/// <summary>
/// 추적 상태. 공격 사거리에 들어오면 공격으로 전환한다.
/// 기획: 한번 인식한 플레이어는 놓치지 않는다 → 대기로 복귀하는 전환이 없다.
/// 사거리 안이지만 쿨다운 중이면 제자리에서 대상을 바라보며 기다린다.
/// </summary>
public class EnemyChaseState : EnemyBaseState
{
    public EnemyChaseState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        enemy.Movement.Resume();
    }

    public override void Update()
    {
        if (enemy.Target == null) return;

        float distance = enemy.DistanceToTarget();

        if (distance <= enemy.Combat.AttackRange)
        {
            if (enemy.Combat.CanAttack)
            {
                enemy.StateMachine.ChangeState(enemy.AttackState);
                return;
            }

            // 쿨다운 대기: 이동은 멈추되 대상은 계속 바라본다.
            // 플레이어가 등 뒤로 돌아가는 동안 몬스터가 엉뚱한 곳을 보는 것을 막는다.
            enemy.Movement.Stop();
            enemy.Movement.Face(enemy.Target.position);
            enemy.Animation.SetSpeed(0f);
            return;
        }

        enemy.Movement.Resume();
        enemy.Movement.SetDestination(enemy.Target.position);

        // 닿을 수 없는 위치(점프로 올라간 바위 위 등)면: 갈 수 있는 데까지 간 뒤 노려보며 대기.
        // Idle로 돌아가지 않는다(기획: 어그로 유지). 플레이어가 닿는 위치로 내려오면
        // 다음 프레임의 경로 재계산이 PathComplete가 되면서 추적이 자동 재개된다.
        if (!enemy.Movement.HasReachablePath && enemy.Movement.ReachedPathEnd)
        {
            enemy.Movement.Stop();
            enemy.Movement.Face(enemy.Target.position);
            enemy.Animation.SetSpeed(0f);
            return;
        }

        enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
    }
}
