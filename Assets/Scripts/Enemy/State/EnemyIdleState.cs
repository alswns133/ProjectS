using UnityEngine;

/// <summary>
/// 대기 상태. 순찰 지점이 없는 몬스터의 기본 상태다.
/// 대기 애니메이션 2종 중 하나를 고르고, 플레이어가 감지 반경에 들어오면 DetectState로 전환한다.
/// </summary>
public class EnemyIdleState : EnemyBaseState
{
    public EnemyIdleState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        enemy.Movement.Stop();
        // 대기 애니메이션이 단조롭지 않도록 진입할 때마다 0/1 중 하나를 고른다.
        enemy.Animation.SetIdleVariant(Random.Range(0, 2));
    }

    public override void Update()
    {
        enemy.Animation.SetSpeed(0f);

        // 첫 발견은 바로 Chase로 가지 않고 DetectState를 거쳐 발견 연출/대시를 재생한다.
        if (enemy.DistanceToTarget() <= enemy.DetectionRange)
            enemy.StateMachine.ChangeState(enemy.DetectState);
    }
}
