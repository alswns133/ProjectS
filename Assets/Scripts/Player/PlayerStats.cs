using UnityEngine;

public class PlayerStats : MonoBehaviour, IDamageable
{
    [SerializeField] private int characterId = 1;   // 이 ID로 스탯 테이블 조회

    [SerializeField] private int maxHp;
    [SerializeField] private int currentHp;
    [SerializeField] private int defense;

    // 회피(구르기)의 자원. 소모는 TryUseStamina, 회복은 Update의 자동 재생이 담당한다.
    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaRegenPerSecond = 15f;

    private float currentStamina;

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

        currentStamina = maxStamina;
        PlayerEvents.FireStaminaChanged(currentStamina, maxStamina);
    }

    private void Update()
    {
        // 스태미나 자동 회복. 가득 차 있으면 이벤트 발행도 없이 조용히 지나간다
        // (매 프레임 무의미한 UI 갱신을 피하기 위함).
        if (IsDead) return;
        if (currentStamina >= maxStamina) return;

        currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
        PlayerEvents.FireStaminaChanged(currentStamina, maxStamina);
    }

    /// <summary>
    /// 스태미나 소모를 시도한다. 잔량이 부족하면 아무것도 소모하지 않고 false를 반환하므로,
    /// 호출측(Player.TryRoll)은 반환값으로 동작 발동 여부를 함께 결정하면 된다.
    /// </summary>
    /// <param name="amount">소모할 양</param>
    /// <returns>소모에 성공했으면 true</returns>
    public bool TryUseStamina(float amount)
    {
        if (currentStamina < amount) return false;

        currentStamina -= amount;
        PlayerEvents.FireStaminaChanged(currentStamina, maxStamina);
        return true;
    }

    // [2026.07.13 태하] 피격 비네트의 회복 복귀 검증용으로 추가. 포션 등 정식 회복에도 그대로 쓰면 된다.
    /// <summary>
    /// HP 회복. 최대 HP를 넘지 않게 잘린다. 사망 상태에서는 무시된다
    /// (부활은 회복과 다른 흐름이 필요하므로 여기서 처리하지 않는다).
    /// </summary>
    /// <param name="amount">회복량(양수만 유효)</param>
    public void Heal(int amount)
    {
        if (IsDead) return;
        if (amount <= 0) return;

        currentHp = Mathf.Min(maxHp, currentHp + amount);
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
