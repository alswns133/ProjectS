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
        /// 경험치 변경 (현재EXP, 최대EXP)
        /// </summary>
        public static event Action<int, int> OnExpChanged;

        /// 골드 변경 (현재골드)
        public static event Action<int> OnGoldChanged;

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
            OnExpChanged = null;
            OnGoldChanged = null;
            OnSkillUsed = null;
            OnPlayerDied = null;   // ★ 새 이벤트는 여기에도 반드시 추가
        }
    }
}
