using System;
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

    // 피격 경직 튜닝. 이 데미지 이상이면 '강한 피격'으로 분류돼
    // 별도 모션(doHitLarge)과 더 긴 경직이 적용된다.
    [Header("Hit Stagger")]
    [SerializeField] private int strongHitThreshold = 20;
    [SerializeField] private float hitStaggerDuration = 0.4f;
    [SerializeField] private float strongHitStaggerDuration = 0.8f;

    // 스킬의 자원(SG). 스태미나와 달리 자동 재생이 없고,
    // 공격/스킬을 대상에 적중시켜야만 회복된다(기획) → 전투를 유도하는 자원.
    // 적중당 회복량은 공격 종류마다 달라 PlayerCombat의 히트박스 슬롯이 소유한다.
    [Header("Skill Gauge")]
    [SerializeField] private float maxSkillGauge = 100f;

    private float currentSkillGauge;

    public bool IsDead => currentHp <= 0;

    /// <summary>
    /// 마지막으로 실제 적용된 타격이 강한 피격이었는지 여부.
    /// HitState는 피격 모션 분기에, DeadState는 사망 모션 분기(doDie/doDieLarge)에 읽는다.
    /// </summary>
    public bool LastHitWasStrong { get; private set; }

    /// <summary>이번 피격의 경직 시간(초). 강한 피격이면 더 길다. HitState가 종료 판정에 쓴다.</summary>
    public float CurrentStaggerDuration => LastHitWasStrong ? strongHitStaggerDuration : hitStaggerDuration;

    /// <summary>
    /// 데미지가 실제로 적용됐을 때 발행된다(무적으로 씹은 공격, 사망 타격은 제외 — 사망은 OnPlayerDied가 담당).
    /// Player가 받아 피격 경직 상태(HitState) 진입으로 연결한다.
    /// </summary>
    public event Action Damaged;

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

        // 기획: 시작 시 게이지는 가득 찬 상태다.
        currentSkillGauge = maxSkillGauge;
        PlayerEvents.FireSgChanged(currentSkillGauge, maxSkillGauge);
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
    /// 스킬 게이지 소모를 시도한다. 잔량이 부족하면 아무것도 소모하지 않고 false를 반환하므로,
    /// 호출측(Player.OnSkill)은 반환값으로 스킬 발동 여부를 함께 결정한다(TryUseStamina와 동일 계약).
    /// </summary>
    /// <param name="amount">소모할 양</param>
    /// <returns>소모에 성공했으면 true</returns>
    public bool TryUseSkillGauge(float amount)
    {
        if (currentSkillGauge < amount) return false;

        currentSkillGauge -= amount;
        PlayerEvents.FireSgChanged(currentSkillGauge, maxSkillGauge);
        return true;
    }

    /// <summary>
    /// 공격/스킬이 대상에 적중할 때마다 게이지를 회복한다.
    /// 회복량은 공격 종류(히트박스 슬롯)마다 달라 호출측이 넘긴다 — 강공격 > 일반 공격(기획).
    /// 가득 차 있으면 이벤트 발행 없이 조용히 지나간다(무의미한 UI 갱신 방지).
    /// </summary>
    /// <param name="amount">회복할 양</param>
    public void GainSkillGauge(float amount)
    {
        if (currentSkillGauge >= maxSkillGauge) return;

        currentSkillGauge = Mathf.Min(maxSkillGauge, currentSkillGauge + amount);
        PlayerEvents.FireSgChanged(currentSkillGauge, maxSkillGauge);
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

        // 강/약 분류를 HP 반영보다 먼저 확정한다 → 사망 시 DeadState가 바로 읽을 수 있다.
        LastHitWasStrong = amount >= strongHitThreshold;

        currentHp = Mathf.Max(0, currentHp - amount);
        PlayerEvents.FireHpChanged(currentHp, maxHp);

        if (IsDead)                          // 이번 데미지로 0이 됐으면
            PlayerEvents.FirePlayerDied();   // 죽음 발행 (경직 대신 사망이 우선)
        else
            Damaged?.Invoke();               // 살아 있을 때만 피격 경직으로 연결
    }
}
