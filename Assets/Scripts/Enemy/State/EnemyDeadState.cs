using UnityEngine;

/// <summary>
/// 사망 상태. 사망 연출을 재생하고 AI/충돌을 끈다. 다른 상태로 전환되지 않는 막다른 상태.
/// 연출 시간이 지나면 오브젝트를 비활성화한다(풀링/드롭 도입 전까지의 제거 방식).
/// </summary>
public class EnemyDeadState : EnemyBaseState
{
    private float elapsed;

    public EnemyDeadState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        elapsed = 0f;

        enemy.Animation.PlayDie();
        enemy.Effects?.Play(EnemyEffects.EffectCue.Death);

        // 시체가 길을 막거나 추가 타격을 받지 않도록 이동과 충돌을 모두 끈다.
        enemy.Movement.DisableAgent();
        if (enemy.BodyCollider != null) enemy.BodyCollider.enabled = false;
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= enemy.DespawnDelay)
            enemy.gameObject.SetActive(false);
    }
}
