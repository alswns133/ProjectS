using UnityEngine;

/// <summary>
/// 피격 상태. EnemyStats가 데미지를 받은 뒤 Enemy.OnDamaged를 통해 진입한다.
/// 일반 몬스터의 짧은 경직용이며, 보스/슈퍼아머 몬스터는 Enemy 인스펙터에서 끌 수 있다.
/// 데미지 적용 자체는 Stats가 이미 끝낸 뒤이므로, 이 상태는 연출/행동 중단만 담당한다.
/// </summary>
public class EnemyHitState : EnemyBaseState
{
    private float elapsed;

    public EnemyHitState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        elapsed = 0f;

        // 피격 중에는 이동을 멈추고 피격 모션만 재생한다.
        // 공격 도중 맞으면 현재 공격 상태를 끊는 인터럽트 역할도 한다.
        enemy.Movement.Stop();
        enemy.Animation.SetSpeed(0f);
        enemy.Animation.PlayHit();
        enemy.Effects?.Play(EnemyEffects.EffectCue.Hit);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // 경직 시간이 끝나면 다시 Chase로 돌아가 전투 흐름을 이어간다.
        if (elapsed >= enemy.HitStunDuration)
            enemy.StateMachine.ChangeState(enemy.ChaseState);
    }
}
