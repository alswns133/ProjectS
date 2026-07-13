using UnityEngine;

/// <summary>
/// 공격 상태. 진입 시 1회 공격을 시작하고, 모션 시간이 지나면 추적으로 돌아간다.
/// 실제 히트 판정은 공격 클립의 Animation Event(EnemyCombat.OnAttackHit)가 담당한다.
/// </summary>
public class EnemyAttackState : EnemyBaseState
{
    private float elapsed;

    public EnemyAttackState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        elapsed = 0f;

        // 공격 커밋: 시작 순간 대상을 바라본 방향으로 고정한다(모션 중 회전 없음).
        // 플레이어의 회피가 유효해지도록 공격 방향을 추적하지 않는 기획.
        enemy.Movement.Stop();
        if (enemy.Target != null) enemy.Movement.Face(enemy.Target.position);

        enemy.Combat.BeginAttack();
        enemy.Animation.PlayAttack();
        enemy.Animation.SetSpeed(0f);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // 종료 후 거리 재판정은 Chase가 맡는다.
        // 사거리 안이면 쿨다운을 기다렸다 재공격, 벗어났으면 다시 따라간다.
        if (elapsed >= enemy.Combat.AttackDuration)
            enemy.StateMachine.ChangeState(enemy.ChaseState);
    }
}
