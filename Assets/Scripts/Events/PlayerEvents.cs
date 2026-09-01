using System;
using UnityEngine;

namespace ProjectS.Events
{
    public static class PlayerEvents
    {
        /// <summary>
        /// HP 변경 (현재HP, 최대HP)
        /// </summary>
        public static event Action<float, float> OnHpChanged;

        /// <summary>
        /// SG 변경 (현재SG, 최대SG)
        /// </summary>
        public static event Action<float, float> OnSGChanged;

        /// <summary>
        /// 스태미나 변경 (현재 스태미나, 최대 스태미나). 구르기/공중 대시 소모와 자동 회복 시 발행된다.
        /// </summary>
        public static event Action<float, float> OnStaminaChanged;

        /// <summary>
        /// 레벨업 (레벨)
        /// </summary>
        public static event Action<int> OnLevelUp;

        /// <summary>
        /// 현재 레벨 변경 (레벨). 표시용이라 스폰 시점의 최초 1회에도 발행된다.
        /// OnLevelUp과 나눠 둔 이유: 레벨업 연출(이펙트·사운드)을 OnLevelUp에 붙였을 때
        /// 스폰 때마다 그 연출이 터지지 않게 하기 위함이다.
        /// </summary>
        public static event Action<int> OnLevelChanged;

        /// <summary>
        /// 경험치 변경 (현재EXP, 최대EXP)
        /// </summary>
        public static event Action<int, int> OnExpChanged;

        /// 골드 변경 (현재골드)
        public static event Action<int> OnGoldChanged;

        /// <summary>
        /// UI가 (재)구독 직후 현재 스탯 스냅샷을 다시 받으려고 요청한다. PlayerStats가 받아 전체 스탯을 다시 발행한다.
        /// HUD가 숨겨져(비활성) 구독이 끊긴 사이 발생한 변경(예: 상호작용 중 받은 퀘스트 보상)을 복구하기 위함이다.
        /// </summary>
        public static event Action OnStatsRefreshRequested;

        /// <summary>
        /// 전투 스탯(공격력·방어·치명·보스뎀·방관·뎀증 등)이 바뀌었을 때 발행(장비 착용/해제 등).
        /// 장비창이 구독해 스탯 표시를 다시 읽는다. HP/스태미나/SG는 기존 OnHp/Stamina/SgChanged로 별도 갱신된다.
        /// </summary>
        public static event Action OnCombatStatsChanged;

        /// <summary>
        /// 스킬 사용 (스킬 번호, 쿨타임 길이(초)). 발동에 성공한 순간 1회 발행된다.
        /// UI는 이 신호로 카운트다운을 시작하고 이후는 자체 타이머로 진행한다
        /// → 남은 시간을 매 프레임 폴링하지 않기 위한 설계.
        /// </summary>
        public static event Action<int, float> OnSkillUsed;

        /// <summary>
        /// 플레이어 사망. 구독자(상태머신·UI·사운드·게임매니저 등)가 각자 반응한다.
        /// </summary>
        public static event Action OnPlayerDied;

        /// <summary>
        /// 마우스 모드 전환(true=커서 해제·마우스 조작 가능, false=커서 잠금·TPS 조작).
        /// HUD 위젯이 이 신호로 클릭 가능 여부를 가른다 — 커서가 잠긴 상태에서 화면 중앙 레이캐스트가
        /// UI에 걸려 의도치 않게 눌리는 것을 막기 위함이다.
        /// </summary>
        public static event Action<bool> OnCursorModeChanged;

        /// <summary>
        /// 전투 구역 전환(true=던전 등 전투 허용, false=마을 등 전투 비활성). 마을 진입/이탈 시 발행된다.
        /// HUD 스킬 슬롯이 이 신호로 "지금 스킬을 쓸 수 있는 구역인가"를 갈라 아이콘을 흐리게/원래대로 표시한다.
        /// combatEnabled와 같은 의미이며, 슬롯이 스폰 타이밍상 이 이벤트를 놓쳤을 때를 대비해
        /// 구독자는 <see cref="Players.Player.IsCombatEnabled"/>로 초기값을 직접 끌어온다.
        /// </summary>
        public static event Action<bool> OnCombatZoneChanged;

        /// <summary>
        /// 히트 콤보(연속 유효타) 수 변경 (현재 히트 수). 적중으로 올라갈 때와 리셋으로 0이 될 때 모두 발행된다.
        /// HUD가 구독해 "N HITS" 표시를 갱신하며, 0이면 숨긴다.
        /// </summary>
        public static event Action<int> OnHitComboChanged;

        // Fire 메서드 (Player쪽에서 호출)

        /// <summary>
        /// HP 변경 이벤트 발행. 구독자(HP UI 등)에게 현재/최대 HP를 알림.
        /// </summary>
        /// <param name="cur">현재 HP</param>
        /// <param name="max">최대 HP</param>
        public static void FireHpChanged(float cur, float max)
            => OnHpChanged?.Invoke(cur, max);

        /// <summary>
        /// SG 변경 이벤트 발행. 구독자에게 현재/최대 SG를 알림.
        /// </summary>
        /// <param name="cur">현재 SG</param>
        /// <param name="max">최대 SG</param>
        public static void FireSgChanged(float cur, float max)
            => OnSGChanged?.Invoke(cur, max);

        /// <summary>
        /// 스태미나 변경 이벤트 발행. 구독자(스태미나 UI 등)에게 현재/최대 스태미나를 알림.
        /// </summary>
        /// <param name="cur">현재 스태미나</param>
        /// <param name="max">최대 스태미나</param>
        public static void FireStaminaChanged(float cur, float max)
            => OnStaminaChanged?.Invoke(cur, max);

        /// <summary>
        /// 레벨업 이벤트 발행. 구독자에게 도달한 레벨을 알림.
        /// </summary>
        /// <param name="level">새로 도달한 레벨</param>
        public static void FireLevelUp(int level)
            => OnLevelUp?.Invoke(level);

        /// <summary>
        /// 현재 레벨 발행. 표시용이므로 스폰 직후(스탯 테이블 반영 후)에도 1회 호출한다.
        /// </summary>
        /// <param name="level">현재 레벨</param>
        public static void FireLevelChanged(int level)
            => OnLevelChanged?.Invoke(level);

        /// <summary>
        /// Exp 변경 이벤트 발행. 구독자(HP UI 등)에게 현재/최대 Exp를 알림.
        /// </summary>
        /// <param name="cur">현재 Exp</param>
        /// <param name="max">다음 레벨까지 필요한 Exp</param>
        public static void FireExpChanged(int cur, int max)
            => OnExpChanged?.Invoke(cur, max);

        /// <summary>
        /// 골드 변경 이벤트 발행. 구독자에게 현재 보유 골드를 알림.
        /// </summary>
        /// <param name="gold">현재 보유 골드</param>
        public static void FireGoldChanged(int gold)
            => OnGoldChanged?.Invoke(gold);

        /// <summary>스탯 스냅샷 재발행을 요청한다(UI가 재구독 시 호출). PlayerStats가 받아 전체 스탯을 다시 쏜다.</summary>
        public static void FireStatsRefreshRequested()
            => OnStatsRefreshRequested?.Invoke();

        public static void FireCombatStatsChanged()
            => OnCombatStatsChanged?.Invoke();

        /// <summary>
        /// 스킬 사용 이벤트 발행. 쿨타임·게이지 판정을 모두 통과해 실제 발동했을 때만 호출한다.
        /// </summary>
        /// <param name="skillNumber">사용한 스킬 번호(1~)</param>
        /// <param name="cooldown">쿨타임 길이(초)</param>
        public static void FireSkillUsed(int skillNumber, float cooldown)
            => OnSkillUsed?.Invoke(skillNumber, cooldown);

        /// <summary>
        /// 플레이어 사망 이벤트 발행. HP가 0에 도달한 순간 1회 호출된다.
        /// </summary>
        public static void FirePlayerDied()
            => OnPlayerDied?.Invoke();

        /// <summary>
        /// 마우스 모드 전환 이벤트 발행. 커서 잠금 상태가 실제로 바뀔 때 호출한다.
        /// </summary>
        /// <param name="isMouseMode">true=마우스 조작 가능, false=커서 잠금(TPS 조작)</param>
        public static void FireCursorModeChanged(bool isMouseMode)
            => OnCursorModeChanged?.Invoke(isMouseMode);

        /// <summary>
        /// 전투 구역 전환 이벤트 발행. 마을 진입(false)/던전 진입(true) 시 호출한다.
        /// </summary>
        /// <param name="combatEnabled">true=전투 허용 구역, false=마을 등 전투 비활성 구역</param>
        public static void FireCombatZoneChanged(bool combatEnabled)
            => OnCombatZoneChanged?.Invoke(combatEnabled);

        /// <summary>
        /// 히트 콤보 수 변경 이벤트 발행. 카운트 증감(리셋 포함)은 PlayerHitCombo가 판단하고,
        /// 이 메서드는 바뀐 결과값만 전달한다. 리셋 시엔 0으로 호출된다.
        /// </summary>
        /// <param name="hitCount">현재 누적 히트 수(0이면 콤보 없음)</param>
        public static void FireHitComboChanged(int hitCount)
            => OnHitComboChanged?.Invoke(hitCount);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다.
        /// (static 이벤트가 이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnHpChanged = null;
            OnSGChanged = null;
            OnStaminaChanged = null;
            OnLevelUp = null;
            OnLevelChanged = null;
            OnExpChanged = null;
            OnGoldChanged = null;
            OnStatsRefreshRequested = null;
            OnCombatStatsChanged = null;
            OnSkillUsed = null;
            OnPlayerDied = null;
            OnCursorModeChanged = null;
            OnCombatZoneChanged = null;
            OnHitComboChanged = null;  // ★ 새 이벤트는 여기에도 반드시 추가
        }
    }
}
