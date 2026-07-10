using UnityEngine;

/// <summary>
/// CombatEvents의 데미지 발생 이벤트를 받아 월드 데미지 텍스트를 띄운다.
/// 전투 로직은 숫자와 위치만 발행하고, 연출 생성/풀링은 여기서만 처리한다.
/// </summary>
public class DamageTextSpawner : PooledSpawner<DamageText>
{
    // 같은 위치에 여러 타격이 들어와도 숫자가 완전히 겹치지 않게 살짝 흩뿌린다.
    [SerializeField] private float spawnJitter = 0.3f;

    private void OnEnable() => CombatEvents.OnDamageDealt += OnDamageDealt;
    private void OnDisable() => CombatEvents.OnDamageDealt -= OnDamageDealt;

    private void OnDamageDealt(Vector3 worldPos, int amount)
    {
        Vector2 jitter = Random.insideUnitCircle * spawnJitter;
        Vector3 position = worldPos + new Vector3(jitter.x, 0f, jitter.y);

        GetFromPool().Show(amount, position, ReturnToPool);
    }
}
