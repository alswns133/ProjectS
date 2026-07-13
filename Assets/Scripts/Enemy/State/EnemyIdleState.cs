/// <summary>
/// 대기 상태. 감지 반경 안에 플레이어가 들어오면 추적으로 전환한다.
/// 일반 몬스터는 순찰 없이 제자리에서 대기한다(기획).
/// </summary>
public class EnemyIdleState : EnemyBaseState
{
    public EnemyIdleState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        enemy.Movement.Stop();
    }

    public override void Update()
    {
        // 잔여 이동 관성이 애니메이션에 남지 않게 매 프레임 0으로 눌러준다.
        enemy.Animation.SetSpeed(0f);

        if (enemy.DistanceToTarget() <= enemy.DetectionRange)
            enemy.StateMachine.ChangeState(enemy.ChaseState);
    }
}
