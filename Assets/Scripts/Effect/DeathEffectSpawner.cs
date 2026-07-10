using UnityEngine;

/// <summary>
/// CombatEvents의 적 사망 이벤트(OnEnemyDied)를 받아 죽은 자리에 처치 이펙트를 재생한다.
/// 죽는 적은 즉시 비활성화되므로 연출을 적 오브젝트에 붙일 수 없다 → 외부 스포너가 담당.
/// 높이·크기 보정은 이펙트 프리팹 안에서 조정한다(이벤트 좌표는 발밑 기준).
/// </summary>
public class DeathEffectSpawner : PooledSpawner<HitEffect>
{
    private void OnEnable() => CombatEvents.OnEnemyDied += OnEnemyDied;
    private void OnDisable() => CombatEvents.OnEnemyDied -= OnEnemyDied;

    private void OnEnemyDied(Vector3 worldPos) => GetFromPool().Play(worldPos, ReturnToPool);
}
