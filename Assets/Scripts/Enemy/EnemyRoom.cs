using ProjectS.Scenes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ProjectS.Enemies
{
    /// <summary>방 하나 = 스폰 포인트 묶음 + 발동 트리거. 방마다 종류가 섞여도 됨.
    /// ★ 방은 "직접 스폰" 안 하고, 권위(컨트롤러)에 요청만 한다 — 멀티 대비.</summary>
    public class EnemyRoom : MonoBehaviour
    {
        // 이 방의 포인트들(종류 혼합). Awake에서 자식들로부터 채우므로 인스펙터 직렬화가 필요 없다.
        private EnemySpawnPoint[] spawnPoints;

        // 전투방 문. 둘 다 선택(비워 두면 문 없는 일반 방으로 동작). 배선은 인스펙터 슬롯으로 한다.
        // 들어온 문: 평소 근접 자동문이었다가 방 진입 즉시 Lock()으로 등 뒤에서 닫힌다.
        // 나가는 문: 인스펙터에서 startLocked=true로 처음부터 닫아 두고, 클리어 시 Unlock()으로 연다.
        // ★ 두 문 모두 감지는 자식 트리거에, 실제 막는 솔리드 콜라이더는 doorPivot(문 본체)에 둬야 한다
        //   (AutoDoor 주석 참고 — 감지 콜라이더는 강제로 isTrigger가 되어 플레이어를 못 막는다).
        [Header("전투방 문")]
        [Tooltip("플레이어가 들어온 문들. 방 진입 즉시 모두 닫히고, 클리어 시 다시 열린다(백트랙 허용). 비우면 문 처리를 건너뛴다.")]
        [SerializeField] private AutoDoor[] entranceDoors;

        [Tooltip("다음 방으로 나가는 문들. 인스펙터에서 각 문 startLocked=true로 두고, 방 몬스터를 다 잡으면 모두 열린다. 비우면 문 처리를 건너뛴다.")]
        [SerializeField] private AutoDoor[] exitDoors;

        private bool isStart = false;
        private bool triggered;

        // 이 방에서 스폰된 몬스터들. 컨트롤러(스폰 권위)가 생성 직후 RegisterSpawned로 넘겨준다.
        // 전부 IsDead가 되면 방 클리어로 보고 문을 연다.
        private readonly List<Enemy> encounterEnemies = new();

        // 전투 진행 중인지(문 잠금~클리어 사이). Update의 클리어 감시는 이 구간에서만 돈다.
        private bool encounterActive;

        // 이미 클리어 처리했는지. 문을 두 번 여는 것과 클리어 후 재감시를 막는다.
        private bool cleared;

        private Collider trigger;

        public EnemySpawnPoint[] Points => spawnPoints;

        // 프리로드 수집용: 이 방이 쓰는 몬스터들
        public IEnumerable<AssetReferenceGameObject> EnemyRefs => spawnPoints.Select(p => p.EnemyRef);

        private System.Action<EnemyRoom> onPlayerEnter; // 컨트롤러가 주인

        // 최종 보스를 넘길 결과 감시자. 씬에 하나 있는 것을 Awake에서 찾아 캐싱한다(없으면 null → SetEndBoss가 경고).
        private DungeonResultReporter dungeonReporter;

        private void Awake()
        {
            spawnPoints = GetComponentsInChildren<EnemySpawnPoint>();
            trigger = GetComponent<Collider>();
            dungeonReporter = FindAnyObjectByType<DungeonResultReporter>();
            trigger.isTrigger = true;
        }

        public void Bind(System.Action<EnemyRoom> callback) => onPlayerEnter = callback;

        // 플레이어 입장 감지 => 컨트롤러(권위)에 "이 방 스폰" 요청. 직접 생성하지 않음.
        private void OnTriggerEnter(Collider other)
        {
            // 준비되지 않았다면 이벤트 종료
            if (isStart == false) return;

            if (triggered || !other.CompareTag("Player")) return;
            triggered = true;
            // TODO(멀티): 트리거는 클라 감지 → Command로 서버에 요청, 스폰은 서버가.
            onPlayerEnter?.Invoke(this);    // 권위(컨트롤러)에 위임 → 스폰 동기 실행, RegisterSpawned로 목록이 채워진다.
            trigger.enabled = false;

            // 스폰이 끝나 목록이 확정된 뒤 전투를 시작한다(문 잠금). invoke 뒤에 와야 몹 수를 정확히 안다.
            BeginEncounter();
        }

        // 준비가 됐는지 체크하는 메서드
        public void EnableTrigger()
        {
            isStart = true;
        }

        /// <summary>
        /// 이 방에서 방금 스폰된 몬스터를 클리어 감시 목록에 등록한다.
        /// 스폰 권위(<see cref="ProjectS.Scenes.DungeonGather.RequestRoomSpawn"/>)가 <see cref="EnemySpawner.SpawnOne"/>
        /// 직후 호출한다 — 그 인스턴스를 아는 곳이 컨트롤러뿐이라, 방은 넘겨받아 생사만 지켜본다.
        /// </summary>
        /// <remarks>SpawnOne은 프리팹 로드 실패 시 null을 돌려줄 수 있어, null은 목록에서 걸러낸다
        /// (그대로 넣으면 AllEnemiesDead가 죽은 것으로 세어 방이 즉시 클리어된다).</remarks>
        /// <param name="enemy">이 방에 스폰된 몬스터. null이면 무시한다.</param>
        public void RegisterSpawned(Enemy enemy)
        {
            if (enemy == null) return;
            encounterEnemies.Add(enemy);
        }

        // 방 진입 즉시(스폰 직후) 호출. 두 문을 닫아 플레이어를 방 안에 가둔다.
        // 몬스터가 하나도 없는 방이면 가둘 이유가 없으니 곧바로 클리어 처리해 문을 연다
        // (빈 전투방이 영영 안 열리는 상황 방지).
        private void BeginEncounter()
        {
            encounterActive = true;

            // 들어온 문은 등 뒤에서 쾅 닫고 잠근다. 나가는 문은 인스펙터 startLocked=true로 이미 닫혀 있지만,
            // 배선을 빠뜨렸을 때를 대비해 여기서도 명시적으로 잠근다.
            SetLocked(entranceDoors, true);
            SetLocked(exitDoors, true);

            if (AllEnemiesDead()) OnCleared();
        }

        // 전투 중일 때만 클리어를 감시한다. 등록된 몬스터를 훑는 가벼운 루프라
        // (한 방 몹 수만큼, 보통 한 자리) AutoDoor의 occupants prune과 같은 수준의 비용이다.
        private void Update()
        {
            if (!encounterActive || cleared) return;
            if (AllEnemiesDead()) OnCleared();
        }

        // 등록된 몬스터가 전부 죽었는지. 파괴/비활성으로 참조가 사라진 것(null)도 죽은 것으로 본다.
        // IsDead는 사망 타격 즉시 true라(오브젝트가 despawn되기 3초 전) 클리어가 곧바로 잡힌다.
        private bool AllEnemiesDead()
        {
            for (int i = 0; i < encounterEnemies.Count; i++)
            {
                Enemy enemy = encounterEnemies[i];
                if (enemy != null && !enemy.Stats.IsDead) return false;
            }
            return true;
        }

        // 방 클리어. 두 문을 모두 열어 다음 방으로 나가게 하고(기획: 백트랙 허용이라 들어온 문도 연다),
        // 이후 재감시를 막는다. Unlock은 근접 시 열리므로, 문 앞에 서 있으면 곧바로 열린다.
        private void OnCleared()
        {
            cleared = true;
            encounterActive = false;

            SetLocked(exitDoors, false);
            SetLocked(entranceDoors, false);
        }

        // 문 배열을 한꺼번에 잠그거나(닫힘) 풀어(근접 시 열림) 준다.
        // 배열/원소 null을 모두 걸러, 슬롯을 비워 뒀거나 일부만 연결해도 안전하게 동작한다.
        private static void SetLocked(AutoDoor[] doors, bool locked)
        {
            if (doors == null) return;

            for (int i = 0; i < doors.Length; i++)
            {
                AutoDoor door = doors[i];
                if (door == null) continue;

                if (locked) door.Lock();
                else door.Unlock();
            }
        }

        /// <summary>
        /// 방금 이 방에서 스폰된 최종 보스를 결과 감시자(<see cref="DungeonResultReporter"/>)에 넘긴다.
        /// 스폰 권위(<see cref="ProjectS.Scenes.DungeonGather.RequestRoomSpawn"/>)가 IsEndBoss 포인트의
        /// 몬스터를 만든 직후 호출한다 — 그 보스가 사라지면 던전 클리어로 보고 결과창을 연다.
        /// </summary>
        /// <remarks>
        /// 최종 보스라도 스폰되는 것은 일반 <see cref="Enemy"/>라 Boss가 아닌 대상은 <c>as Boss</c>가 null이 되고,
        /// <see cref="dungeonReporter"/>가 없으면(씬에 감시자 미배치·비활성) 넘길 곳이 없어 결과창이 열리지 않는다.
        /// 어느 쪽이든 배선 실수라 경고를 남긴다 — 씬에 감시자를 활성으로 두고 최종 보스 프리팹이 Boss인지 확인해야 한다.
        /// </remarks>
        /// <param name="enemy">이 포인트에서 스폰된 몬스터. Boss이고 감시자가 있을 때만 최종 보스로 등록된다.</param>
        public void SetEndBoss(Enemy enemy)
        {
            if (enemy == null) return;

            Boss boss = enemy as Boss;

            if (boss != null && dungeonReporter != null)
                dungeonReporter.SetEndBossSpawn(boss);
            else
                ProjectS.Debugging.DevLog.Warning($"{name}: DungeonResultReporter가 씬에 없어 결과창이 안 열린다.", this);
        }
    }
}

