using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [SerializeField] private int characterId = 1;   // 이 ID로 스탯 테이블 조회

    [SerializeField] private int maxHp;
    [SerializeField] private int currentHp;
    [SerializeField] private int defense;

    public bool IsDead => currentHp <= 0;

    private void Start()
    {
        // JsonManager에서 characterId로 스탯 로드 (네 기존 구조 활용)
        // var row = JsonManager.Get<CharacterStatRow>(characterId);
        // maxHp = row.MaxHp; defense = row.Defense;
        currentHp = maxHp;
        PlayerEvents.FireHpChanged(currentHp, maxHp);
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;   // ★ 이 가드가 죽음 1회 발행을 보장하는 핵심

        currentHp = Mathf.Max(0, currentHp - amount);
        PlayerEvents.FireHpChanged(currentHp, maxHp);

        if (IsDead)                          // 이번 데미지로 0이 됐으면
            PlayerEvents.FirePlayerDied();   // 죽음 발행
    }
}
