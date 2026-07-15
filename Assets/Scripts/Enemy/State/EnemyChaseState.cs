using UnityEngine;

/// <summary>
/// Chase state. Switches to attack when the target enters attack range.
/// Once aggro is acquired, this state does not return to idle by itself.
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

            // Wait in place during attack cooldown, but keep facing the target.
            enemy.Movement.Stop();
            enemy.Movement.Face(enemy.Target.position);
            enemy.Animation.SetSpeed(0f);
            return;
        }

        if (!enemy.CanReachTarget())
        {
            enemy.Movement.Stop();
            enemy.Movement.Face(enemy.Target.position);
            enemy.Animation.SetSpeedImmediate(0f);
            return;
        }

        enemy.Movement.Resume();

        // 플레이어 위치가 아니라 개체별로 분산된 교전 지점으로 이동한다(군중 제어).
        // 교전 거리가 AttackRange보다 짧게 잡히므로 도착 전에 위의 공격 전환 판정에 걸린다.
        enemy.Movement.SetDestination(enemy.GetChaseDestination());
        enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
    }
}
