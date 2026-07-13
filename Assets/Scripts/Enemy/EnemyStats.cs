using UnityEngine;

/// <summary>
/// 몬스터 HP와 사망 판정. 피격 진입점(IDamageable)을 구현한다.
/// </summary>
public class EnemyStats : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    [SerializeField] private float damageTextHeight = 0.5f;
    private int currentHp;
    private Enemy enemy;

    public bool IsDead => currentHp <= 0;

    private void Awake()
    {
        currentHp = maxHp;
        // 상태 머신 없이 단독 배치된 대상(테스트용)도 있을 수 있어 null을 허용한다.
        enemy = GetComponent<Enemy>();
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;                       // 이미 죽었으면 무시(1회 사망 보장)

        currentHp = Mathf.Max(0, currentHp - amount);
        //Debug.Log($"{name} 피격! {amount} 데미지 → 남은 HP {currentHp}", this);

        // 연출은 이벤트로만 알린다(데미지 텍스트·이펙트가 각자 구독).
        // 받은 쪽이 발행하는 이유: 나중에 방어력 보정이 생겨도 '실제 적용된' 수치를 아는 곳은 여기다
        CombatEvents.FireDamageDealt(transform.position + Vector3.up * damageTextHeight, amount);

        if (IsDead)
        {
            // 비활성화 '전에' 발행해야 구독자(처치 이펙트 등)가 위치를 신뢰할 수 있다.
            CombatEvents.FireEnemyDied(transform.position);

            // 사망 연출(애니메이션·AI/충돌 해제·제거 타이밍)은 DeadState가 담당한다.
            // 상태 머신이 없는 단독 배치 대상만 예전처럼 즉시 비활성화한다.
            if (enemy != null) enemy.OnDied();
            else gameObject.SetActive(false);
        }
    }
}
