using System.Threading.Tasks;
using Mirror;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using ProjectS.Events;
using ProjectS.Scenes;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 다단 페이즈 보스의 페이즈 전환을 담당하는 옵트인 컴포넌트(<see cref="RaidBossLocomotion"/>과 같은 결 —
    /// 이게 붙은 보스만 페이즈 전환 동작을 한다). <b>1페이즈 프리팹에 붙이고, 다음 페이즈 프리팹을 참조로 연결한다.</b>
    ///
    /// <para>
    /// <b>모델: 하나의 HP 풀을 두 프리팹이 나눠 갖는 변신.</b> 1페이즈 HP가 <see cref="thresholdRatio"/>(기본 50%)에
    /// 닿으면 <b>죽지 않고</b> 다음 페이즈 프리팹으로 갈아끼운다. 남은 HP는 그대로 이어받는다("50%에서 변신, HP 이어짐").
    /// 스킬·애니메이션·이펙트가 페이즈마다 다르므로 컴포넌트 스왑이 아니라 프리팹 교체로 처리한다.
    /// </para>
    /// <para>
    /// <b>두 가지 코어 훅에 기댄다</b>(둘 다 <see cref="EnemyStats"/>의 옵트인 기능):
    /// ① <see cref="EnemyStats.SetDamageFloorRatio"/> — 1페이즈가 임계 밑으로 <b>죽지 않게</b> 막는다(즉사 레이스 방지).
    /// ② <see cref="EnemyStats.SetSpawnHp"/> — 2페이즈가 테이블 로딩으로 풀피가 되지 않고 이어받은 HP로 시작하게 한다.
    /// </para>
    /// <para>
    /// <b>결과창(clearBoss) 인계</b>: 1페이즈는 <c>IsEndBoss=false</c>로 두고(죽어도 결과창 X — 게다가 전환 때 죽이지 않고
    /// Destroy로 걷어내 <c>OnDespawn</c>/<c>FireBossDisappeared</c>를 아예 안 탄다), 이 컴포넌트가 다음 페이즈를 스폰하며
    /// <see cref="DungeonResultReporter.SetEndBossSpawn"/>로 <b>최종 페이즈만</b> clearBoss로 등록한다.
    /// HP 바는 <c>BossHpPresenter</c>가 다음 페이즈의 <c>FireBossAppeared</c>로 자동 리바인딩한다(2페이즈가 등장한 뒤
    /// 1페이즈를 걷어내므로 바가 깜빡이지 않는다).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Boss))]
    public class BossPhaseTransition : MonoBehaviour
    {
        [Tooltip("다음 페이즈 보스 프리팹(어드레서블). 스킬·애니메이션·이펙트가 다른 별개 프리팹을 연결한다. " +
                 "HP 바가 튀지 않으려면 이 프리팹의 monsterId(스탯 행)를 1페이즈와 같게 두어 MaxHp를 맞춘다.")]
        [SerializeField] private AssetReferenceGameObject nextPhasePrefab;

        [Tooltip("이 비율에 HP가 닿으면 다음 페이즈로 전환한다(0.5=절반). 하한도 이 값으로 걸려 그 밑으로 죽지 않는다.")]
        [SerializeField, Range(0.05f, 0.95f)] private float thresholdRatio = 0.5f;

        [Tooltip("다음 페이즈가 마지막 페이즈인지. 켜면 다음 페이즈를 clearBoss(결과창 트리거)로 등록한다. " +
                 "3페이즈 이상이면 끄고, 다음 페이즈 프리팹에 또 이 컴포넌트를 달아 체인으로 잇는다.")]
        [SerializeField] private bool nextPhaseIsFinal = true;

        [Tooltip("네트워크(파티 레이드)용 다음 페이즈 프리팹 — 직접 참조. 서버가 NetworkServer.Spawn으로 띄운다. " +
                 "★ GameNetworkManager의 raidBoss 목록에 등록된 프리팹이어야 클라가 스폰할 수 있다. " +
                 "솔로(비네트워크)는 위의 어드레서블 nextPhasePrefab을 쓴다.")]
        [SerializeField] private Boss networkedNextPhasePrefab;

        private Boss boss;

        // 전환은 한 번만. 하한에 닿은 뒤에도 피격 HP 이벤트가 계속 와서, 가드가 없으면 매 타격마다 스폰을 시도한다.
        private bool transitioned;

        private void Awake()
        {
            boss = GetComponent<Boss>();
        }

        /// <summary>
        /// 이 보스가 <b>네트워크로 스폰된 서버 인스턴스</b>인지. 솔로(로컬 어드레서블 스폰)는 프리팹에 NetworkIdentity가
        /// 있어도 <c>NetworkServer.Spawn</c>을 안 거쳐 netId가 0이라 false — 그래서 솔로는 기존 로컬 경로를 그대로 탄다.
        /// (관찰자 클라에서는 이 컴포넌트 자체가 <see cref="BossServerAuthority"/>로 꺼져 있어 여기까지 오지 않는다.)
        /// </summary>
        private bool IsNetworkedOnServer =>
            NetworkServer.active && TryGetComponent(out NetworkIdentity identity) && identity.netId != 0;

        // async void는 진입점(Start)에서만 예외적으로 허용(EnemyStats.Start와 같은 방침).
        private async void Start()
        {
            // ① 1페이즈가 임계 밑으로 죽지 않게 하한을 건다. 이게 없으면 큰 일격 한 방에 임계를 지나쳐 죽어(즉사 레이스)
            //    2페이즈가 안 뜬다.
            boss.Stats.SetDamageFloorRatio(thresholdRatio);

            // 네트워크 전환은 직접 참조 프리팹(networkedNextPhasePrefab)을 NetworkServer.Spawn 하므로 어드레서블 프리로드가 불필요.
            if (IsNetworkedOnServer) return;

            // ② 다음 페이즈 프리팹을 전투 시작 시 미리 로드해 둔다. 전환 순간 SpawnOne이 즉시 성공하도록(히치·실패 방지).
            await PreloadNextAsync();
        }

        private void OnEnable() => CombatEvents.OnEnemyHealthChanged += OnEnemyHealthChanged;

        private void OnDisable() => CombatEvents.OnEnemyHealthChanged -= OnEnemyHealthChanged;

        // 이 보스의 HP가 임계(=하한)에 닿는 순간 전환한다. ratio 대신 절대 수치로 비교해, CeilToInt 하한과
        // 정확히 같은 지점에서 걸리게 한다(ratio는 반올림 때문에 임계보다 미세하게 커서 <= 비교를 놓칠 수 있다).
        private void OnEnemyHealthChanged(EnemyStats stats, float ratio)
        {
            if (transitioned || stats != boss.Stats) return;

            int floorHp = Mathf.CeilToInt(stats.MaxHp * thresholdRatio);
            if (stats.CurrentHp <= floorHp) Transition();
        }

        // 다음 페이즈를 스폰하고, HP를 이어주고, 결과창 트리거를 넘긴 뒤, 1페이즈를 걷어낸다.
        // 네트워크(파티 레이드)면 서버가 NetworkServer.Spawn으로, 솔로면 기존 로컬(어드레서블) 경로로 간다.
        private void Transition()
        {
            transitioned = true;

            int carried = boss.Stats.CurrentHp;   // 이어받을 남은 HP(하한 값 ≈ MaxHp*threshold)

            if (IsNetworkedOnServer) TransitionNetworked(carried);
            else TransitionLocal(carried);
        }

        /// <summary>
        /// 서버 권위 페이즈 전환. 2페이즈를 <see cref="NetworkServer.Spawn"/>으로 띄워 모든 클라가 보게 하고,
        /// 1페이즈는 <see cref="NetworkServer.Destroy"/>로 걷어낸다(로컬 Destroy면 클라에 언스폰이 안 간다).
        /// </summary>
        private void TransitionNetworked(int carriedHp)
        {
            if (networkedNextPhasePrefab == null)
            {
                Debug.LogWarning("[BossPhaseTransition] networkedNextPhasePrefab 미할당 → 네트워크 페이즈 전환 불가. " +
                                 "1페이즈가 하한에 멈춘 채 남는다. 인스펙터에 다음 페이즈(네트워크 프리팹)를 할당하라.", this);
                return;
            }

            GameObject next = Instantiate(networkedNextPhasePrefab.gameObject, transform.position, transform.rotation);

            // ★ 파티 전용 additive 인스턴스에 있어야 한다 — 1페이즈와 같은 씬으로 옮기지 않으면 활성 씬으로 가
            //   다른 파티 인스턴스에 섞이거나 NavMesh/스폰포인트가 어긋난다(PartyManager의 보스 스폰과 같은 규칙).
            SceneManager.MoveGameObjectToScene(next, gameObject.scene);

            // 이어받은 HP는 스폰 통지 '전에' 주입한다(2페이즈 Start의 테이블 로딩 뒤에도 유지된다).
            Enemy nextEnemy = next.GetComponent<Enemy>();
            if (nextEnemy != null && nextEnemy.Stats != null) nextEnemy.Stats.SetSpawnHp(carriedHp);

            NetworkServer.Spawn(next);

            // 관찰자 바가 이어받은 HP로 시작하게 서버 HP를 즉시 복제한다. 안 하면 센티넬(-1) 때문에
            // 관찰자는 자기 로컬 풀피로 그려 "2페이즈가 풀피로 보이는" 사고가 난다.
            if (next.TryGetComponent(out BossNetSync nextSync)) nextSync.ServerSetHp(carriedHp);

            if (nextPhaseIsFinal && nextEnemy is Boss nextBoss) RegisterEndBoss(nextBoss);

            // ★ 2페이즈를 먼저 스폰한 뒤에 1페이즈를 걷어낸다 — 클라에서 등장(FireBossAppeared)이 퇴장보다 먼저 와야
            //   BossHpPresenter가 바를 새 보스로 갈아끼운 뒤 퇴장을 무시해 바가 깜빡이지 않는다.
            NetworkServer.Destroy(gameObject);
        }

        // 기존 로컬(솔로·비네트워크) 경로. 어드레서블 프리로드 → EnemySpawner.SpawnOne → Destroy.
        private void TransitionLocal(int carriedHp)
        {
            EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
            if (spawner == null || nextPhasePrefab == null || !nextPhasePrefab.RuntimeKeyIsValid())
            {
                Debug.LogWarning($"[BossPhaseTransition] 다음 페이즈로 전환하지 못함 — " +
                    $"EnemySpawner={(spawner != null ? "있음" : "없음")}, nextPhasePrefab 유효={nextPhasePrefab?.RuntimeKeyIsValid()}. " +
                    $"1페이즈가 하한(HP {thresholdRatio:P0})에 멈춘 채 남는다.", this);
                return;
            }

            // 2페이즈를 같은 위치·방향에 스폰. SpawnOne은 프리로드된 프리팹만 성공한다(Start의 PreloadNextAsync).
            Enemy spawned = spawner.SpawnOne(nextPhasePrefab, transform.position, transform.rotation);
            if (spawned == null)
            {
                Debug.LogWarning("[BossPhaseTransition] 다음 페이즈 스폰 실패(프리로드 미완료 가능). 1페이즈가 하한에 남는다.", this);
                return;
            }

            // 이어받은 HP 주입. SpawnOne 직후(=2페이즈 Start 전)라 테이블 로딩 후에도 이 값이 유지된다.
            spawned.Stats.SetSpawnHp(carriedHp);

            // 결과창 트리거(clearBoss)를 최종 페이즈에만 넘긴다. 3페이즈 이상이면 다음 페이즈의 BossPhaseTransition이 이어 처리.
            if (nextPhaseIsFinal && spawned is Boss nextBoss) RegisterEndBoss(nextBoss);

            // 1페이즈는 죽이지 않고 즉시 걷어낸다. Destroy는 사망/소멸(OnDespawn→FireBossDisappeared) 경로를 타지 않아
            // 결과창을 건드리지 않고, 2페이즈가 이미 등장(FireBossAppeared)해 HP 바도 그대로 이어진다.
            Destroy(gameObject);
        }

        /// <summary>
        /// 최종 페이즈를 결과창 트리거(clearBoss)로 등록한다. 파티 전용 additive 인스턴스가 여럿일 수 있으므로
        /// <b>이 보스가 속한 씬</b>의 리포터를 먼저 찾고, 없을 때만 전역 탐색으로 폴백한다
        /// (전역 탐색만 쓰면 다른 파티 인스턴스의 리포터를 잡아 남의 결과창을 건드릴 수 있다).
        /// </summary>
        private void RegisterEndBoss(Boss nextBoss)
        {
            DungeonResultReporter reporter = FindReporterInScene(gameObject.scene) ?? FindAnyObjectByType<DungeonResultReporter>();

            if (reporter != null) reporter.SetEndBossSpawn(nextBoss);
            else Debug.LogWarning("[BossPhaseTransition] DungeonResultReporter가 없어 최종 페이즈를 clearBoss로 등록하지 못함 — 결과창이 안 열린다.", this);
        }

        // 지정 씬의 루트들만 훑어 리포터를 찾는다(다른 인스턴스 씬을 건드리지 않기 위함).
        private static DungeonResultReporter FindReporterInScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                DungeonResultReporter reporter = root.GetComponentInChildren<DungeonResultReporter>(true);
                if (reporter != null) return reporter;
            }
            return null;
        }

        private async Task PreloadNextAsync()
        {
            if (nextPhasePrefab == null || !nextPhasePrefab.RuntimeKeyIsValid()) return;

            EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
            if (spawner == null) return;   // 씬에 스포너가 없으면 전환 자체가 불가 — Transition에서 경고한다.

            await spawner.PreloadAsync(new[] { nextPhasePrefab });
        }
    }
}
