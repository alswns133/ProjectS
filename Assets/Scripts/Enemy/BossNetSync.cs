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

        // 서버가 테이블로 확정한 최대 HP·줄 수. -1 = 서버 스탯 미확정.
        // 관찰자는 이 값을 바의 분모로 쓴다 — 자기 로컬 테이블로 계산하면 서버와 다른 행을 읽을 수 있다.
        [SyncVar(hook = nameof(OnMaxHpSynced))] private int netMaxHp = -1;
        [SyncVar(hook = nameof(OnMaxHpSynced))] private int netSegmentCount = -1;

        // 서버 그로기(남은 비율 0~1, 잠금). 초기값은 '가득·해제'라 피격 전에도 정상 표시된다(센티넬 불필요).
        [SyncVar(hook = nameof(OnGroggyRatioSynced))] private float netGroggyRatio = 1f;
        [SyncVar(hook = nameof(OnGroggyLockedSynced))] private bool netGroggyLocked;

        private EnemyAnimation enemyAnimation;
        private EnemyCombat enemyCombat;

        private void Awake()
        {
            boss = GetComponent<Boss>();
            stats = GetComponent<EnemyStats>();
            enemyAnimation = GetComponent<EnemyAnimation>();
            enemyCombat = GetComponent<EnemyCombat>();
        }

        // ── 애니메이터 트리거 전달 ──

        /// <summary>
        /// 서버 AI가 켠 애니메이터 트리거를 관찰자 클라에도 켠다. <see cref="EnemyAnimation"/>이 트리거를 켤 때마다 부른다.
        /// 서버가 아니거나 아직 네트워크 스폰 전(싱글 로컬 스폰)이면 아무 일도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// NetworkAnimator는 트리거를 동기화하지 않아, 이게 없으면 클라에서 공격·헛잡기 모션이 안 나온다.
        /// </remarks>
        /// <param name="triggerHash">켤 트리거의 해시.</param>
        /// <param name="attackIndex">함께 보낼 공격 번호. 음수면 없음.</param>
        public void RelayTrigger(int triggerHash, int attackIndex)
        {
            if (!isServer) return;
            RpcTrigger(triggerHash, attackIndex);
        }

        /// <summary>
        /// 서버가 쏜 투사체를 구경하는 클라에서도 보이게 한다. 서버가 아니거나 네트워크 스폰 전(싱글)이면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="slot">EnemyCombat attacks 배열 번호. 음수면 조우 공격.</param>
        /// <param name="position">발사 위치.</param>
        /// <param name="rotation">발사 방향.</param>
        public void RelayProjectile(int slot, Vector3 position, Quaternion rotation)
        {
            if (!isServer) return;
            RpcProjectile(slot, position, rotation);
        }

        /// <summary>
        /// 페이즈 전환 연출을 파티원 화면에서도 같은 시각에 재생하게 한다(서버 → 이 보스를 보는 모든 클라).
        /// </summary>
        /// <param name="next">새로 등장하는 다음 페이즈 보스(방금 서버가 스폰한 것).</param>
        /// <param name="startTime">서버 시계 기준 연출 시작 시각.</param>
        public void RelayPhaseTransition(Boss next, double startTime)
        {
            if (!isServer || next == null || !next.TryGetComponent(out NetworkIdentity nextIdentity)) return;
            RpcPhaseTransition(nextIdentity.netId, startTime);
        }

        [ClientRpc]
        private void RpcPhaseTransition(uint nextNetId, double startTime)
        {
            // ★ 호스트도 받는다(isServer여도 건너뛰지 않는다). 서버 쪽 재생에는 화면 연출(UI 끄기·입력 잠금)이 없어,
            //   호스트가 이 파티원이면 여기서 붙인다. 파티원이 아닌 호스트에게는 관심 영역 관리가 이 보스를 안 보여 줘 오지 않는다.
            if (boss == null) return;

            Boss next = NetworkClient.spawned.TryGetValue(nextNetId, out NetworkIdentity nextIdentity) && nextIdentity != null
                ? nextIdentity.GetComponent<Boss>()
                : null;

            // 서버 프로세스(호스트)는 여러 인스턴스를 들고 있을 수 있어 이 보스의 씬에서, 순수 클라는 하나뿐이라 전체에서 찾는다.
            UnityEngine.SceneManagement.Scene scene = isServer ? gameObject.scene : default;
            ProjectS.Scenes.BossIntroDirector cutscene =
                ProjectS.Scenes.BossIntroDirector.Find(scene, ProjectS.Scenes.BossIntroDirector.DirectorRole.PhaseTransition);

            if (cutscene != null) cutscene.PlayPhaseTransition(boss, next, startTime, true);
            else Debug.LogWarning("[BossNetSync] 페이즈 전환 연출 디렉터를 찾지 못해 이 화면에서는 연출 없이 전환됩니다.", this);
        }

        [ClientRpc]
        private void RpcProjectile(int slot, Vector3 position, Quaternion rotation)
        {
            // 호스트는 서버의 원본 투사체를 이미 보고 있다.
            if (isServer || enemyCombat == null) return;
            enemyCombat.FireVisualProjectile(slot, position, rotation);
        }

        [ClientRpc]
        private void RpcTrigger(int triggerHash, int attackIndex)
        {
            // 호스트는 서버 쪽에서 이미 같은 애니메이터에 켰다(두 번 켜면 트리거가 한 번 더 소비될 수 있다).
            if (isServer || enemyAnimation == null) return;

            enemyAnimation.ApplyNetworkTrigger(triggerHash, attackIndex);
        }

        // ── 서버: 로컬 전투 이벤트를 SyncVar로 옮겨 담는다 ──

        public override void OnStartServer()
        {
            CombatEvents.OnEnemyHealthChanged += OnServerHealthChanged;
            BossEvents.OnBossGroggyChanged += OnServerGroggyChanged;

            // 스탯 테이블은 EnemyStats.Start에서 비동기로 확정된다. 스폰 순간엔 아직 인스펙터 폴백이라
            // 확정을 기다렸다 복제한다(이미 확정됐으면 바로).
            if (stats == null) return;
            if (stats.IsStatsReady) ServerPushStats();
            else stats.StatsReady += ServerPushStats;
        }

        public override void OnStopServer()
        {
            CombatEvents.OnEnemyHealthChanged -= OnServerHealthChanged;
            BossEvents.OnBossGroggyChanged -= OnServerGroggyChanged;
            if (stats != null) stats.StatsReady -= ServerPushStats;
        }

        // 서버가 확정한 스탯을 관찰자에게 넘긴다. 현재 HP도 함께 실어 센티넬(-1) 구간을 없앤다.
        [Server]
        private void ServerPushStats()
        {
            if (stats == null) return;
            stats.StatsReady -= ServerPushStats;

            netMaxHp = stats.MaxHp;
            netSegmentCount = stats.SegmentCount;
            netHp = stats.CurrentHp;
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
            // ★ 등장 발행 "전에" 서버 값을 입힌다. 바(BossHpPresenter)는 등장 순간의 MaxHp로 초기화되는데,
            //   관찰자에선 이 시점에 로컬 테이블 로딩도 안 끝나 인스펙터 폴백(maxHp=100 등)으로 그려지던 문제를 막는다.
            //   스폰 페이로드에 이미 확정값이 실려 왔으면 여기서 바로 맞고, 아직이면 훅(OnMaxHpSynced)이 나중에 맞춘다.
            if (!isServer) ApplyNetworkStats();

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

        // 서버 스탯이 늦게 확정돼 복제돼 온 경우. 값을 입히고 바가 새 분모로 다시 그리도록 갱신 이벤트를 낸다.
        private void OnMaxHpSynced(int _, int __)
        {
            if (isServer || stats == null) return;
            if (!ApplyNetworkStats()) return;

            CombatEvents.FireEnemyHealthChanged(stats, stats.MaxHp > 0 ? (float)stats.CurrentHp / stats.MaxHp : 0f);
        }

        // 관찰자 로컬 스탯에 서버 값(최대 HP·줄 수·현재 HP)을 입힌다. 입혔으면 true.
        private bool ApplyNetworkStats()
        {
            if (stats == null || netMaxHp <= 0) return false;

            stats.SetNetworkStats(netMaxHp, netSegmentCount);
            if (netHp >= 0) stats.SetNetworkHp(netHp);
            return true;
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
