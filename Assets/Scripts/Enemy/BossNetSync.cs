using Mirror;
using ProjectS.Events;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스의 <b>UI/표시 상태 네트워크 동기화</b>. 서버 권위 보스는 HP·그로기가 서버에서만 바뀌므로
    /// (<see cref="BossServerAuthority"/>가 관찰자 AI를 끈다), 관찰자·호스트의 보스 HP 바가 서버를 따라오게 만든다.
    /// <see cref="EnemyStats.TakeDamage"/>가 서버에서 발행한 값을 SyncVar로 실어 관찰자 UI에 재현한다.
    ///
    /// <list type="bullet">
    ///   <item><b>바 등장/퇴장</b>: 모든 클라(호스트+관찰자)에서 <see cref="BossEvents.FireBossAppeared"/>를 발행한다.
    ///     관찰자에선 <c>Boss</c>(Enemy) 컴포넌트가 꺼져 <c>Boss.Start</c>의 발행이 안 되므로 여기가 유일한 경로다
    ///     — 이게 "멀티에서 보스 바가 안 뜨던" 원인의 수정이다.</item>
    ///   <item><b>HP 동기화</b>: 서버가 데미지 적용 시 발행하는 HP를 SyncVar로 받아 관찰자의 표시 HP를 갱신한다.</item>
    ///   <item><b>그로기 동기화</b>: 그로기 게이지·잠금도 같은 방식으로 재현한다.</item>
    /// </list>
    ///
    /// <para><b>호스트/서버 처리.</b> 서버(=호스트 포함)는 이미 실제 HP를 갖고 이벤트를 로컬로 발행하므로, SyncVar
    /// 훅은 <c>!isServer</c>(순수 관찰자)에서만 재현한다(이중 반영 방지). 등장/퇴장은 UI가 있는 모든 클라에서 발행한다.</para>
    /// </summary>
    public class BossNetSync : NetworkBehaviour
    {
        private Boss boss;
        private EnemyStats stats;

        // 서버 HP. -1 = 아직 미설정(피격 전) → 관찰자는 자기 로컬 풀피를 그대로 쓰고 이 값을 무시한다
        // (스폰 직후 0으로 동기화돼 바가 '사망'으로 뜨는 것을 막는 센티넬).
        [SyncVar(hook = nameof(OnHpSynced))] private int netHp = -1;

        // 서버 그로기(남은 비율 0~1, 잠금). 초기값은 '가득·해제'라 피격 전에도 정상 표시된다(센티넬 불필요).
        [SyncVar(hook = nameof(OnGroggyRatioSynced))] private float netGroggyRatio = 1f;
        [SyncVar(hook = nameof(OnGroggyLockedSynced))] private bool netGroggyLocked;

        private void Awake()
        {
            boss = GetComponent<Boss>();
            stats = GetComponent<EnemyStats>();
        }

        // ── 서버: 로컬 전투 이벤트를 SyncVar로 옮겨 담는다 ──

        public override void OnStartServer()
        {
            CombatEvents.OnEnemyHealthChanged += OnServerHealthChanged;
            BossEvents.OnBossGroggyChanged += OnServerGroggyChanged;
        }

        public override void OnStopServer()
        {
            CombatEvents.OnEnemyHealthChanged -= OnServerHealthChanged;
            BossEvents.OnBossGroggyChanged -= OnServerGroggyChanged;
        }

        /// <summary>
        /// 서버가 현재 HP를 즉시 복제한다(피격을 기다리지 않고). <b>페이즈 전환</b>처럼 스폰 시점부터 풀피가 아닌
        /// 보스에 쓴다 — 안 부르면 센티넬(-1) 때문에 관찰자가 자기 로컬 풀피로 바를 그려 "2페이즈가 풀피로 보이는" 사고가 난다.
        /// </summary>
        /// <param name="hp">복제할 현재 HP.</param>
        [Server]
        public void ServerSetHp(int hp) => netHp = Mathf.Max(0, hp);

        // 서버가 데미지 적용 시 발행. 내 보스 것만 걸러 현재 HP를 SyncVar에 담는다(→ 관찰자로 복제).
        private void OnServerHealthChanged(EnemyStats source, float ratio)
        {
            if (source != stats || stats == null) return;
            netHp = stats.CurrentHp;
        }

        private void OnServerGroggyChanged(EnemyStats source, float ratio, bool locked)
        {
            if (source != stats) return;
            netGroggyRatio = ratio;
            netGroggyLocked = locked;
        }

        // ── 클라: 등장/퇴장 발행 + SyncVar 재현 ──

        public override void OnStartClient()
        {
            if (boss != null) BossEvents.FireBossAppeared(boss);
        }

        public override void OnStopClient()
        {
            if (boss != null) BossEvents.FireBossDisappeared(boss);
        }

        // 서버 HP가 복제돼 오면 관찰자의 표시 HP를 갱신하고 바 갱신 이벤트를 재발행한다.
        private void OnHpSynced(int _, int newHp)
        {
            // 서버(호스트 포함)는 이미 실제 HP·이벤트를 가짐. 미설정(-1)이면 관찰자는 로컬 풀피 유지.
            if (isServer || newHp < 0 || stats == null) return;

            stats.SetNetworkHp(newHp);
            CombatEvents.FireEnemyHealthChanged(stats, stats.MaxHp > 0 ? (float)newHp / stats.MaxHp : 0f);
        }

        private void OnGroggyRatioSynced(float _, float __) => ApplyGroggy();
        private void OnGroggyLockedSynced(bool _, bool __) => ApplyGroggy();

        // 두 그로기 SyncVar 중 어느 쪽이 바뀌든 현재 값으로 바를 재현한다(관찰자만 — 서버는 로컬 발행).
        private void ApplyGroggy()
        {
            if (isServer || stats == null) return;
            BossEvents.FireBossGroggyChanged(stats, netGroggyRatio, netGroggyLocked);
        }
    }
}
