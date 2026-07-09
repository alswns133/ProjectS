using UnityEngine;

/// <summary>
/// 몬스터 HP와 사망 판정. 피격 진입점(IDamageable)을 구현한다.
/// </summary>
public class EnemyStats : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    [SerializeField] private float damageTextHeight = 0.5f;
    private int currentHp;

    public bool IsDead => currentHp <= 0;

    private void Awake()
    {
        currentHp = maxHp;
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
            //Debug.Log($"{name} 사망", this);
            // 지금은 그냥 비활성. 나중에 사망 연출·드롭으로 확장
            gameObject.SetActive(false);
        }
    }
}
