using System;
using UnityEngine;
using ProjectS.Enemies;

namespace ProjectS.Events
{
    /// <summary>
    /// 보스(레이드 포함) 전용 사실 허브. "보스가 등장/퇴장했다", "그로기 게이지가 변했다" 같은
    /// 보스 UI가 필요로 하는 사실만 발행한다. 일반 전투 사실(데미지·HP 비율)은 <see cref="CombatEvents"/>가
    /// 이미 담당하므로 여기 중복 발행하지 않는다 — 보스 HP 비율은 CombatEvents.OnEnemyHealthChanged를
    /// IsBoss로 걸러 재사용하고, 절대 수치·이름·줄당HP는 등장 시 받은 보스의 EnemyStats에서 직접 읽는다.
    ///
    /// 관심사를 CombatEvents에 섞지 않고 분리한 이유: 보스 HP 바 UI는 "지금 어느 보스가 화면에 떠 있나"라는
    /// 등장/퇴장 수명과 그로기라는 보스 고유 개념을 다루는데, 이는 잡몹 월드 바가 쓰는 전투 사실과 결이 다르다.
    ///
    /// <para>현재는 화면에 보스 바 하나(현재 교전 보스)를 가정한다. 멀티 보스가 필요해지면 이벤트에
    /// 식별자를 더한다. 그로기 이벤트가 EnemyStats를 함께 싣는 것은 그 확장과 구독자 매칭을 대비한 것이다.</para>
    /// </summary>
    public static class BossEvents
    {
        /// <summary>
        /// 보스 등장(보스방 진입 / 레이드 입장). 구독자(보스 HP 바 프레젠터)가 바를 켜고
        /// 이름·최대 HP·줄당 HP를 이 Boss의 EnemyStats에서 읽어 초기화한다.
        /// </summary>
        public static event Action<Boss> OnBossAppeared;

        /// <summary>보스 등장 발행. 보스방 트리거/레이드 입장 시점에서 호출한다.</summary>
        /// <param name="boss">등장한 보스. 이름·HP·줄당 HP·그로기의 소유자.</param>
        public static void FireBossAppeared(Boss boss) => OnBossAppeared?.Invoke(boss);

        /// <summary>
        /// 보스 퇴장(사망 / 교전 종료 / 씬 이탈). 구독자는 바를 숨긴다.
        /// 등장과 짝을 맞춰 어느 보스가 사라졌는지 실어 보낸다(현재는 단일 보스라 참조 비교용).
        /// </summary>
        public static event Action<Boss> OnBossDisappeared;

        /// <summary>보스 퇴장 발행.</summary>
        /// <param name="boss">사라진 보스.</param>
        public static void FireBossDisappeared(Boss boss) => OnBossDisappeared?.Invoke(boss);

        /// <summary>
        /// 보스 그로기 게이지 변화(변화한 보스, 남은 비율 0~1, 잠금 여부).
        /// EnemyGroggy가 게이지·잠금 상태가 바뀔 때마다 발행한다. 프레젠터가 그로기 바와
        /// 잠금(자물쇠) 표시를 갱신한다. locked=true면 특수 패턴(레이드 기믹) 중이라 게이지가 깎이지 않는 상태다.
        /// </summary>
        public static event Action<EnemyStats, float, bool> OnBossGroggyChanged;

        /// <summary>보스 그로기 변화 발행. EnemyGroggy가 값/잠금 갱신 직후 호출한다.</summary>
        /// <param name="source">그로기가 바뀐 보스의 스탯(구독자 매칭용).</param>
        /// <param name="ratio">남은 그로기 비율(0~1).</param>
        /// <param name="locked">특수 패턴으로 그로기가 잠겨 있는지 여부.</param>
        public static void FireBossGroggyChanged(EnemyStats source, float ratio, bool locked)
            => OnBossGroggyChanged?.Invoke(source, ratio, locked);

        /// <summary>
        /// 모든 구독을 초기화. 도메인 리로드를 꺼도 플레이 시작 시 깨끗한 상태를 보장한다
        /// (static 이벤트가 이전 플레이 세션의 죽은 구독자를 들고 있는 것을 방지).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnBossAppeared = null;   // ★ 새 이벤트는 여기에도 반드시 추가
            OnBossDisappeared = null;
            OnBossGroggyChanged = null;
        }
    }
}
