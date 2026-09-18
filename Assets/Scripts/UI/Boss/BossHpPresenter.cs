using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 HP 바 프레젠터. 보스 등장/퇴장·HP·그로기 이벤트를 받아 <see cref="BossHpView"/>를 갱신한다.
    /// HUDPresenter와 같은 결(이벤트→View 변환)이며, 뷰와 같은 오브젝트에 붙는다.
    ///
    /// HP 비율은 <see cref="CombatEvents.OnEnemyHealthChanged"/>를 재사용하되(잡몹 월드 바와 같은 이벤트),
    /// 지금 화면에 뜬 보스(<see cref="currentBoss"/>)의 것만 받아 원본 수치로 줄 수를 계산한다.
    /// 현재는 화면에 보스 바 하나를 가정한다(레이드도 대표 보스 하나). 멀티 보스가 필요해지면 식별로 확장한다.
    /// </summary>
    public class BossHpPresenter : BasePresenter
    {
        [SerializeField] private BossHpView view;

        // 지금 바가 붙어 있는 보스의 스탯. HP/그로기 이벤트를 이 보스 것만 받도록 거르는 기준.
        private EnemyStats currentBoss;

        // 이름 폴백(오브젝트 이름)용 보스 참조. currentBoss와 짝으로 세팅·해제한다.
        private Boss currentBossObject;

        // ★ 꺼져 있는 동안에도 구독을 유지한다(Awake에서 구독, OnDestroy에서 해제).
        //   보스 페이즈 전환 연출은 UIManager 루트를 통째로 꺼 두는데, 호스트·싱글에서는 그 사이에 다음 페이즈 등장
        //   (FireBossAppeared)이 발행된다. OnDisable에서 구독을 끊으면 이 등장을 놓쳐, 연출 뒤에도 바가 1페이즈에 붙은 채
        //   2페이즈 HP를 "다른 보스"로 걸러 안 깎였다(2026-09-18). 뷰는 코루틴 없이 Update로만 그려, 꺼진 동안 값만 받아 둬도 안전하다.
        protected override bool KeepSubscribedWhileDisabled => true;

        private void Awake()
        {
            if (view == null) view = GetComponent<BossHpView>();
            Subscribe();
        }

        private void OnDestroy() => Unsubscribe();

        protected override void Subscribe()
        {
            BossEvents.OnBossAppeared += OnBossAppeared;
            BossEvents.OnBossDisappeared += OnBossDisappeared;
            BossEvents.OnBossGroggyChanged += OnBossGroggyChanged;
            CombatEvents.OnEnemyHealthChanged += OnEnemyHealthChanged;
            PlayerEvents.OnCombatZoneChanged += OnCombatZoneChanged;
        }

        protected override void Unsubscribe()
        {
            BossEvents.OnBossAppeared -= OnBossAppeared;
            BossEvents.OnBossDisappeared -= OnBossDisappeared;
            BossEvents.OnBossGroggyChanged -= OnBossGroggyChanged;
            CombatEvents.OnEnemyHealthChanged -= OnEnemyHealthChanged;
            PlayerEvents.OnCombatZoneChanged -= OnCombatZoneChanged;
        }

        // 마을 진입 등 전투 구역이 아닌 곳으로 바뀌면 보스 바를 내린다. 보스를 잡지 않고 던전을 떠난 경우
        // (사망 후 마을 복귀 등)에는 OnBossDisappeared가 오지 않아 바가 남으므로, 마을 진입 신호로 확실히 숨긴다.
        private void OnCombatZoneChanged(bool combatEnabled)
        {
            if (combatEnabled) return;   // 던전 진입(true)에서는 등장 이벤트가 바를 관리한다.

            view.Hide();
            currentBoss = null;
        }

        // 보스 등장: 바를 켜고 이름·초기 HP·초기 그로기를 세팅한다.
        private void OnBossAppeared(Boss boss)
        {
            if (boss == null || boss.Stats == null) return;

            currentBoss = boss.Stats;
            currentBossObject = boss;

            view.Show(ResolveName());
            view.SetHp(currentBoss.CurrentHp, currentBoss.MaxHp, currentBoss.SegmentCount);

            // 그로기 컴포넌트가 있으면 현재 값으로, 없으면 가득·해제 상태로 초기화한다.
            if (boss.Groggy != null) view.SetGroggy(boss.Groggy.Ratio, boss.Groggy.IsGroggyLocked);
            else view.SetGroggy(1f, false);
        }

        // 보스 퇴장: 지금 뜬 보스가 사라졌을 때만 바를 숨긴다.
        private void OnBossDisappeared(Boss boss)
        {
            if (boss != null && boss.Stats != currentBoss) return;

            view.Hide();
            currentBoss = null;
            currentBossObject = null;
        }

        // HP 변화: 지금 뜬 보스의 것만 받아 원본 수치로 줄 수·바를 갱신한다.
        // 이름도 함께 맞춘다 — 등장 순간엔 테이블 로딩 전이거나(인스펙터 폴백) 관찰자가 서버 이름을 아직 못 받아
        // 키·오브젝트 이름이 떴을 수 있어서, 이후 갱신 때 확정된 이름으로 바꿔 끼운다.
        private void OnEnemyHealthChanged(EnemyStats stats, float ratio)
        {
            if (stats == null || stats != currentBoss) return;

            view.SetName(ResolveName());
            view.SetHp(stats.CurrentHp, stats.MaxHp, stats.SegmentCount);
        }

        // 표시 이름 폴백: 테이블 Name → NameKey(내부 키) → 오브젝트 이름. 이름 칸을 아직 안 채운 행도 무언가는 뜨게 한다.
        private string ResolveName()
        {
            if (currentBoss == null) return string.Empty;
            if (!string.IsNullOrEmpty(currentBoss.DisplayName)) return currentBoss.DisplayName;
            if (!string.IsNullOrEmpty(currentBoss.DisplayNameKey)) return currentBoss.DisplayNameKey;
            return currentBossObject != null ? currentBossObject.name : string.Empty;
        }

        // 그로기 변화: 지금 뜬 보스의 것만 받아 그로기 바·자물쇠를 갱신한다.
        private void OnBossGroggyChanged(EnemyStats source, float ratio, bool locked)
        {
            if (source == null || source != currentBoss) return;

            view.SetGroggy(ratio, locked);
        }
    }
}
