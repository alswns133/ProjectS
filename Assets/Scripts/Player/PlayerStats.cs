using System;
using System.Threading.Tasks;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.Players
{
    public class PlayerStats : MonoBehaviour, IDamageable
    {
        [SerializeField] private int characterId = 1;   // 이 ID로 스탯 테이블 조회

        // 시작 레벨. 저장·성장 시스템이 아직 없어 인스펙터로 정한다.
        // 이 값으로 PlayerLevelTable을 조회해 HP/AD/방어도가 정해진다.
        [SerializeField] private int level = 1;

        // 현재 레벨에서 쌓은 경험치(0 ~ RequiredExp). AddExp로 쌓이고 임계치를 넘으면 자동 레벨업한다.
        private int currentExp;

        // 아래 전투 스탯은 테이블이 덮어쓴다(HP·AD·방어도는 PlayerLevelTable, 치명타는 PlayerStatTable).
        // 인스펙터 값은 테이블 로딩이 끝나기 전과 행 조회 실패 시에만 쓰이는 폴백이다.
        // maxHp를 0으로 두면 로딩 대기 중 IsDead가 true가 되어 스폰 즉시 사망 판정이 나므로 0으로 두지 않는다.
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int currentHp;
        [SerializeField] private float defense;
        [SerializeField] private float attackPower = 100f;

        // 치명타는 레벨로 자라지 않고 캐릭터·아이템·패시브로만 변동한다(뒤 둘은 미구현).
        [Header("치명타")]
        [SerializeField, Range(0f, 1f)] private float critChance = 0.05f;
        [SerializeField, Min(1f)] private float critDamage = 1.5f;

        // 회피(구르기)의 자원. 소모는 TryUseStamina, 회복은 Update의 자동 재생이 담당한다.
        // 설계 수치 시트 기준: 최대 100(패시브로만 변동), 회피 1회 20 소모(= 최대 5회 연속), 초당 5 회복.
        // 회피 소모량은 입력 중재자가 소유하므로 Player.rollStaminaCost에 있다.
        [Header("스태미나")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float staminaRegenPerSecond = 5f;

        private float currentStamina;

        // 피격 경직 튜닝. 이 데미지 이상이면 '강한 피격'으로 분류돼
        // 별도 모션(doHitLarge)과 더 긴 경직이 적용된다.
        // 데미지 텍스트가 뜨는 높이(머리 위). 캐릭터 키에 맞춰 조정한다.
        [SerializeField] private float damageTextHeight = 1.8f;

        [Header("피격 경직")]
        [SerializeField] private int strongHitThreshold = 20;
        [SerializeField] private float hitStaggerDuration = 0.4f;
        [SerializeField] private float strongHitStaggerDuration = 0.8f;

        // 구르기 모션이 끝난 뒤에도 유지되는 잔여 무적 시간(초).
        // 구르기 판정 종료와 동시에 무적이 끊기면 일어나는 프레임에 바로 얻어맞아 회피가 빡빡하게 느껴진다.
        [Header("회피 무적")]
        [SerializeField, Min(0f)] private float postRollInvincibleDuration = 1f;

        // 스킬의 자원(SG). 수급 경로가 둘이고 서로 더해진다:
        //   1) 자연 회복 - 설계 수치 시트 확정으로 전투 중에도 초당 5씩 찬다.
        //      (예전에는 "때려야만 찬다"였으나 시트 확정으로 바뀌었다. 옛 규칙으로 되돌리려면 이 값을 0으로.)
        //   2) 적중 수급 - 평타 +5/타격, 우클릭 +20. 공격 종류마다 달라 SkillTable 행(SgGain)이 소유한다.
        [Header("스킬 게이지")]
        [SerializeField] private float maxSkillGauge = 100f;
        [SerializeField] private float skillGaugeRegenPerSecond = 5f;

        private float currentSkillGauge;

        public bool IsDead => currentHp <= 0;

        /// <summary>
        /// 이 캐릭터의 PlayerStatTable 행 ID. 스킬 ID를 유도하는 데도 쓰인다
        /// (캐릭터 스킬 ID = characterId * 100 + 스킬번호).
        /// </summary>
        public int CharacterId => characterId;

        /// <summary>현재 레벨. HP·AD·방어도가 이 값으로 PlayerLevelTable에서 결정된다.</summary>
        public int Level => level;

        /// <summary>
        /// 이 캐릭터의 스킬 묶음 접두사(검사 "SW", 거너 "GN").
        /// PlayerCombat이 SkillTable에서 자기 캐릭터의 스킬 행을 골라낼 때 쓴다.
        /// 테이블 로딩 전에는 비어 있다.
        /// </summary>
        public string SkillSetPrefix { get; private set; }

        /// <summary>현재 레벨에서 다음 레벨까지 필요한 경험치(PlayerLevelTable에서 읽는다).</summary>
        public int RequiredExp { get; private set; }

        /// <summary>현재 레벨에서 쌓은 경험치(0 ~ RequiredExp).</summary>
        public int CurrentExp => currentExp;

        /// <summary>
        /// 총 AD. 무기·공퍼·패시브·깡공이 모두 미구현이라 현재는 테이블의 기본 AD가 그대로 들어간다.
        /// 장비를 붙일 때 총AD 합성은 이 프로퍼티 안에서 하면 계산기와 호출부는 손대지 않아도 된다.
        /// </summary>
        public float AttackPower => attackPower;

        /// <summary>치명타 확률(0~1).</summary>
        public float CritChance => critChance;

        /// <summary>치명타 배율.</summary>
        public float CritDamage => critDamage;

        /// <summary>IDamageable. 방어 경감은 때린 쪽이 이 값을 읽어 계산한다.</summary>
        public float Defense => defense;

        /// <summary>IDamageable. 플레이어는 보스 추가뎀% 대상이 아니다.</summary>
        public bool IsBoss => false;

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
        /// 회피 무적 여부. 두 경로의 합이다:
        /// 구르기 중 수동 무적(PlayerRollState가 Enter/Exit에서 켜고 끔) + 구르기 종료 후 잔여 무적(타이머).
        /// true인 동안 일반 공격 데미지는 무시되고, 즉사기(ignoreInvincibility)만 통과한다.
        /// </summary>
        public bool IsInvincible => manualInvincible || Time.time < invincibleUntilTime;

        // 구르기 상태가 Enter/Exit 짝으로 제어하는 수동 무적.
        // 잔여 무적(invincibleUntilTime)과 분리해 두어, Exit의 해제 보장이 타이머와 얽히지 않는다.
        private bool manualInvincible;
        private float invincibleUntilTime;

        /// <summary>
        /// 무적 상태를 켜고 끈다. 호출측(구르기 상태)이 Enter/Exit 짝으로 호출해야
        /// 무적이 영구히 남는 사고가 없다(상태 머신의 Exit 보장에 기댄다).
        /// </summary>
        public void SetInvincible(bool value) => manualInvincible = value;

        /// <summary>
        /// 구르기 종료 직후의 잔여 무적을 부여한다. PlayerRollState.Exit가 호출한다.
        /// SetInvincible(false)와 별도의 타이머로 동작하므로 "Exit에서 반드시 해제" 규칙과 충돌하지 않고,
        /// 시간이 지나면 저절로 풀려 무적이 영구히 남는 사고도 없다.
        /// </summary>
        public void GrantPostRollInvincibility()
            => invincibleUntilTime = Time.time + postRollInvincibleDuration;

        private void Awake()
        {
            // 테이블 로딩은 비동기라 첫 프레임에는 아직 값이 없다.
            // 폴백 값으로 자원을 먼저 채워, currentHp가 0인 채 IsDead가 true가 되는 창(=스폰 즉시 사망)을 없앤다.
            FillResourcesToMax();
        }

        // HUD 등 UI가 (재)구독 시 현재 스탯을 다시 달라고 요청하면 전체를 재발행한다.
        // 상호작용 중 HUD가 비활성되어 놓친 변경(퀘스트 보상 등)을 다시 켜질 때 복구하기 위함이다.
        private void OnEnable() => PlayerEvents.OnStatsRefreshRequested += PublishAllStats;
        private void OnDisable() => PlayerEvents.OnStatsRefreshRequested -= PublishAllStats;

        // async void는 Awake/Start 같은 진입점에서만 예외적으로 허용한다(JsonManager와 같은 방침).
        private async void Start()
        {
            // 캐릭터 선택으로 접속했다면, 저장된 성장 값(타입/레벨/경험치)을 테이블 로딩 전에 반영한다.
            // 세션이 없으면(직접 씬 테스트) 인스펙터 폴백을 그대로 둔다.
            ApplySelectedCharacterSave();

            // 테이블 로딩이 늦어져도 HUD가 빈 채로 남지 않도록 폴백 값으로 먼저 발행한다.
            PublishAllStats();

            await ApplyStatTableAsync();

            // 로딩을 기다리는 동안 씬 전환 등으로 파괴됐을 수 있다.
            if (this == null) return;

            // 테이블로 최대치가 바뀌었으므로 자원을 다시 채우고 UI를 갱신한다.
            FillResourcesToMax();
            PublishAllStats();
        }

        // 선택된 캐릭터 세이브(GameSession)의 성장 값을 반영한다. 스탯 테이블 로딩 '전에' 호출해야
        // 저장된 레벨로 PlayerLevelTable을 조회한다. 세션이 없으면 인스펙터 값을 유지한다.
        private void ApplySelectedCharacterSave()
        {
            CharacterSaveData save = GameSession.SelectedCharacter;
            if (save == null) return;

            characterId = save.characterType;
            level = Mathf.Max(1, save.level);
            currentExp = Mathf.Max(0, save.currentExp);
        }

        /// <summary>
        /// 현재 성장 상태(타입/레벨/경험치)를 세이브 데이터에 기록한다. 저장 시점에 호출한다.
        /// HP·스태미나·게이지는 씬 진입 시 풀충전 설계라 저장하지 않는다.
        /// </summary>
        /// <param name="save">기록 대상 세이브(선택된 캐릭터). null이면 무시.</param>
        public void WriteTo(CharacterSaveData save)
        {
            if (save == null) return;

            save.characterType = characterId;
            save.level = level;
            save.currentExp = currentExp;
        }

        /// <summary>
        /// 캐릭터 행(치명타·스킬 접두사)과 레벨 행(HP·AD·방어도)을 읽어 스탯에 반영한다.
        /// 테이블이나 행이 없으면 인스펙터 폴백을 유지해 플레이가 멈추지 않게 한다.
        /// </summary>
        private async Task ApplyStatTableAsync()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null) return;

            if (!json.IsReady) await json.ReadyTask;
            if (this == null) return;

            ApplyCharacterRow(json);
            ApplyLevelRow(json);
        }

        // 캐릭터 고유값. 레벨과 무관하므로 스폰 시 한 번만 필요하다.
        private void ApplyCharacterRow(JsonManager json)
        {
            PlayerStatTable row = json.Get<PlayerStatTable>(characterId);
            if (row == null)
            {
                Debug.LogWarning($"[PlayerStats] PlayerStatTable에 CharacterId {characterId} 행이 없습니다. 인스펙터 폴백을 사용합니다.", this);
                return;
            }

            critChance = row.CritChance;
            critDamage = row.CritDamage;
            SkillSetPrefix = row.SkillSetPrefix;
        }

        // 레벨별 성장 수치. 레벨이 바뀔 때마다 다시 호출된다.
        private void ApplyLevelRow(JsonManager json)
        {
            PlayerLevelTable row = json.Get<PlayerLevelTable>(level);
            if (row == null)
            {
                Debug.LogWarning($"[PlayerStats] PlayerLevelTable에 Level {level} 행이 없습니다. 인스펙터 폴백을 사용합니다.", this);
                return;
            }

            maxHp = row.BaseHP;
            attackPower = row.BaseAD;
            defense = row.BaseDefense;
            RequiredExp = row.RequiredExp;
        }

        /// <summary>
        /// 레벨을 바꾸고 그 레벨의 성장 수치를 다시 반영한다. 레벨업 시스템이 생기면 이 메서드를 호출하면 된다.
        /// 늘어난 최대 HP만큼 현재 HP도 올려준다 — 레벨업 직후 체력 비율이 떨어져 더 약해진 것처럼
        /// 느껴지는 것을 막기 위함이다. 전체 회복 여부는 기획 확정 전이라 여기서 임의로 채우지 않는다.
        /// </summary>
        /// <param name="newLevel">새 레벨. PlayerLevelTable에 없는 레벨이면 아무것도 하지 않는다.</param>
        public void SetLevel(int newLevel)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return;

            if (json.Get<PlayerLevelTable>(newLevel) == null)
            {
                Debug.LogWarning($"[PlayerStats] PlayerLevelTable에 Level {newLevel} 행이 없어 레벨 변경을 건너뜁니다.", this);
                return;
            }

            int previousMaxHp = maxHp;
            level = newLevel;
            ApplyLevelRow(json);

            currentHp = Mathf.Min(maxHp, currentHp + Mathf.Max(0, maxHp - previousMaxHp));
            PublishAllStats();

            // 레벨 변화는 저장 대상. AddExp 경로는 그 직후 SaveNow로 즉시 올리고, 외부 직접 호출은 오토세이브가 받는다.
            PlayerSaveService.MarkDirty();
        }

        /// <summary>
        /// 경험치를 지급한다(퀘스트 보상 등). 임계치(RequiredExp)를 넘으면 자동으로 레벨업하고,
        /// 남은 경험치는 다음 레벨로 이월한다(한 번에 여러 레벨도 가능). 최대 레벨이면 경험치 바를 가득 채운다.
        /// 레벨업 시 SetLevel이 HP·레벨 변경을 발행하고, 여기서 경험치 변경(FireExpChanged)을 발행하므로
        /// 구독 중인 HUD는 자동으로 갱신된다.
        /// </summary>
        /// <param name="amount">추가할 경험치(0 이하는 무시)</param>
        public void AddExp(int amount)
        {
            if (amount <= 0) return;

            int startLevel = level;
            currentExp += amount;

            // 임계치를 넘는 동안 레벨업. RequiredExp는 SetLevel→ApplyLevelRow에서 새 레벨 값으로 갱신된다.
            while (RequiredExp > 0 && currentExp >= RequiredExp)
            {
                int consumed = RequiredExp;
                int before = level;
                SetLevel(level + 1);
                if (level == before) break;   // 더 오를 레벨이 없음(최대 레벨) → 중단
                currentExp -= consumed;
            }

            // 최대 레벨에서 넘친 경험치는 바를 가득 찬 상태로 고정한다(정상 진행 중엔 이미 RequiredExp 미만).
            if (RequiredExp > 0) currentExp = Mathf.Min(currentExp, RequiredExp);

            PlayerEvents.FireExpChanged(currentExp, RequiredExp);

            // 레벨업은 커밋(①) → 즉시 저장, 경험치만 오른 경우는 dirty(②)로 묶는다.
            if (level > startLevel) PlayerSaveService.SaveNow();
            else PlayerSaveService.MarkDirty();
        }

        // 기획: 시작 시 HP·스태미나·게이지는 모두 가득 찬 상태다.
        private void FillResourcesToMax()
        {
            currentHp = maxHp;
            currentStamina = maxStamina;
            currentSkillGauge = maxSkillGauge;
        }

        /// <summary>
        /// 씬 진입 시 HP·스태미나·스킬 게이지를 최대치로 회복한다(기획: 씬 전환 시 자원 풀 충전).
        /// 값만 세팅하고 발행은 하지 않는다 — 호출측이 곧바로 <see cref="PlayerEvents.FireStatsRefreshRequested"/>로
        /// 전체 스탯을 다시 쏘므로, 여기서 또 발행하면 같은 프레임에 중복 갱신이 된다.
        /// 사망 상태에서는 회복하지 않는다(부활은 별도 흐름).
        /// </summary>
        public void RefillOnSceneEnter()
        {
            if (IsDead) return;
            FillResourcesToMax();
        }

        private void PublishAllStats()
        {
            PlayerEvents.FireLevelChanged(level);
            PlayerEvents.FireHpChanged(currentHp, maxHp);
            PlayerEvents.FireStaminaChanged(currentStamina, maxStamina);
            PlayerEvents.FireSgChanged(currentSkillGauge, maxSkillGauge);
            if (RequiredExp > 0) PlayerEvents.FireExpChanged(currentExp, RequiredExp);
        }

        private void Update()
        {
            if (IsDead) return;

            // 두 자원은 서로 독립이라 한쪽이 가득 찼다고 다른 쪽 회복을 건너뛰면 안 된다.
            RegenerateStamina();
            RegenerateSkillGauge();
        }

        // 가득 차 있으면 이벤트 발행도 없이 조용히 지나간다
        // (매 프레임 무의미한 UI 갱신을 피하기 위함). 아래 SG 회복도 같은 방침.
        private void RegenerateStamina()
        {
            if (currentStamina >= maxStamina) return;

            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
            PlayerEvents.FireStaminaChanged(currentStamina, maxStamina);
        }

        // 설계 수치 시트: 전투 중 여부와 무관하게 초당 5씩 회복한다.
        // 적중 수급(GainSkillGauge)과는 별개 경로라 둘이 함께 쌓인다.
        private void RegenerateSkillGauge()
        {
            if (currentSkillGauge >= maxSkillGauge) return;

            currentSkillGauge = Mathf.Min(maxSkillGauge, currentSkillGauge + skillGaugeRegenPerSecond * Time.deltaTime);
            PlayerEvents.FireSgChanged(currentSkillGauge, maxSkillGauge);
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
        /// 공격/스킬이 대상에 적중할 때마다 게이지를 회복한다. 자연 회복과 더해지는 별개 경로다.
        /// 회복량은 공격 종류마다 달라 호출측이 넘긴다(SkillTable의 SgGain — 평타 +5, 우클릭 +20).
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
        /// <returns>데미지가 실제로 적용됐으면 true. 무적/사망으로 씹혔으면 false(IDamageable 계약).</returns>
        public bool TakeDamage(in DamageResult result) => TakeDamage(in result, false);

        /// <summary>
        /// 데미지 적용. 즉사기처럼 회피 불가 판정이 필요한 공격만 ignoreInvincibility를 true로 호출한다.
        /// </summary>
        /// <param name="result">계산이 끝난 피해</param>
        /// <param name="ignoreInvincibility">true면 구르기 무적을 관통(즉사기 전용)</param>
        /// <returns>데미지가 실제로 적용됐으면 true. 때린 쪽이 히트 이펙트 발행 여부를 이 값으로 분기한다.</returns>
        public bool TakeDamage(in DamageResult result, bool ignoreInvincibility)
        {
            if (IsDead) return false;   // ★ 이 가드가 죽음 1회 발행을 보장하는 핵심

            // 구르기 무적: 즉사기가 아니면 데미지·이벤트 모두 없던 일로 한다
            if (IsInvincible && !ignoreInvincibility) return false;

            int amount = result.Amount;

            // 강/약 분류를 HP 반영보다 먼저 확정한다 → 사망 시 DeadState가 바로 읽을 수 있다.
            LastHitWasStrong = amount >= strongHitThreshold;

            currentHp = Mathf.Max(0, currentHp - amount);
            PlayerEvents.FireHpChanged(currentHp, maxHp);

            // 플레이어가 맞은 것이므로 때린 쪽의 치명타 여부와 무관하게 항상 '플레이어 피격'으로 띄운다.
            CombatEvents.FireDamageDealt(
                transform.position + Vector3.up * damageTextHeight,
                amount,
                DamageTextKind.PlayerDamaged);

            if (IsDead)                          // 이번 데미지로 0이 됐으면
                PlayerEvents.FirePlayerDied();   // 죽음 발행 (경직 대신 사망이 우선)
            else
                Damaged?.Invoke();               // 살아 있을 때만 피격 경직으로 연결

            return true;
        }
    }
}
