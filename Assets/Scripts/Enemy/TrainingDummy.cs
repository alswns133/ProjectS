using UnityEngine;

/// <summary>
/// 트레이닝 더미. 공격 판정·데미지 검증용 최소 대상.
/// 이동·AI 없이 IDamageable만 구현해 "맞고 죽는" 것만 한다.
/// </summary>
public class TrainingDummy : MonoBehaviour, IDamageable
{
    [SerializeField] private int maxHp = 100;
    private int currentHp;

    public bool IsDead => currentHp <= 0;

    private void Awake() => currentHp = maxHp;

    public void TakeDamage(int amount)
    {
        if (IsDead) return;                       // 이미 죽었으면 무시(1회 사망 보장)

        currentHp = Mathf.Max(0, currentHp - amount);
        Debug.Log($"{name} 피격! {amount} 데미지 → 남은 HP {currentHp}", this);

        if (IsDead)
        {
            Debug.Log($"{name} 사망", this);
            // 지금은 그냥 비활성. 나중에 사망 연출·드롭으로 확장
            gameObject.SetActive(false);
        }
    }
}