using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [SerializeField] private int characterId = 1;   // 이 ID로 스탯 테이블 조회

    [SerializeField] private int maxHp;
    [SerializeField] private int currentHp;
    [SerializeField] private int defense;

    public bool IsDead => currentHp <= 0;

    /// <summary>
    /// 회피 무적 여부. 구르기 상태(PlayerRollState)가 Enter/Exit에서 켜고 끈다.
    /// true인 동안 일반 공격 데미지는 무시되고, 즉사기(ignoreInvincibility)만 통과한다.
    /// </summary>
    public bool IsInvincible { get; private set; }

    /// <summary>
    /// 무적 상태를 켜고 끈다. 호출측(구르기 상태)이 Enter/Exit 짝으로 호출해야
    /// 무적이 영구히 남는 사고가 없다(상태 머신의 Exit 보장에 기댄다).
    /// </summary>
    public void SetInvincible(bool value) => IsInvincible = value;

    private void Start()
    {
        // JsonManager에서 characterId로 스탯 로드 (네 기존 구조 활용)
        // var row = JsonManager.Get<CharacterStatRow>(characterId);
        // maxHp = row.MaxHp; defense = row.Defense;
        currentHp = maxHp;
        PlayerEvents.FireHpChanged(currentHp, maxHp);
    }

    /// <summary>
    /// IDamageable 기본 경로. 일반 공격은 전부 여기로 들어오며 회피 무적의 영향을 받는다.
    /// </summary>
    public void TakeDamage(int amount) => TakeDamage(amount, false);

    /// <summary>
    /// 데미지 적용. 즉사기처럼 회피 불가 판정이 필요한 공격만 ignoreInvincibility를 true로 호출한다.
    /// </summary>
    /// <param name="amount">데미지 양</param>
    /// <param name="ignoreInvincibility">true면 구르기 무적을 관통(즉사기 전용)</param>
    public void TakeDamage(int amount, bool ignoreInvincibility)
    {
        if (IsDead) return;   // ★ 이 가드가 죽음 1회 발행을 보장하는 핵심

        // 구르기 무적: 즉사기가 아니면 데미지·이벤트 모두 없던 일로 한다
        if (IsInvincible && !ignoreInvincibility) return;

        currentHp = Mathf.Max(0, currentHp - amount);
        PlayerEvents.FireHpChanged(currentHp, maxHp);

        if (IsDead)                          // 이번 데미지로 0이 됐으면
            PlayerEvents.FirePlayerDied();   // 죽음 발행
    }
}
