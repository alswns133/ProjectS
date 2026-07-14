using UnityEngine;

/// <summary>
/// 공격 상태. 진입 시 현재 거리에서 가능한 공격 패턴 하나를 고르고 공격 애니메이션을 재생한다.
/// 실제 데미지 판정은 공격 클립의 Animation Event(EnemyCombat.OnAttackHit)가 담당한다.
/// 공격이 시작되면 방향과 쿨다운을 확정하는 '커밋' 상태라, 모션 중에는 플레이어를 계속 따라 돌지 않는다.
/// </summary>
public class EnemyAttackState : EnemyBaseState
{
    private float elapsed;

    public EnemyAttackState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        elapsed = 0f;

        // 공격 커밋: 시작 순간 대상을 바라본 방향으로 고정한다.
        // 모션 중 계속 추적하지 않아 플레이어 회피가 의미 있게 작동한다.
        enemy.Movement.Stop();
        if (enemy.Target != null) enemy.Movement.Face(enemy.Target.position);

        // 공격 선택과 쿨다운 소모는 Combat가 소유한다.
        // 상태는 "공격 시작"을 요청하고, 선택된 공격 번호만 Animation에 넘긴다.
        enemy.Combat.BeginAttack(enemy.DistanceToTarget());
        enemy.Animation.PlayAttack(enemy.Combat.CurrentAttackIndex);
        enemy.Effects?.Play(EnemyEffects.EffectCue.Attack);
        enemy.Animation.SetSpeed(0f);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // 공격 모션이 끝났다고 보는 시간이 지나면 Chase로 돌아가 거리/쿨타임을 다시 판단한다.
        if (elapsed >= enemy.Combat.AttackDuration)
            enemy.StateMachine.ChangeState(enemy.ChaseState);
    }
}
