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

        [Header("던전 네비게이션")]
        [Tooltip("방 순번. 나침반이 '이 방 다음(index+1)' 방을 자동으로 찾아 가리킨다(Room1=1, Room2=2 … 연속 정수).\n" +
                 "이 값만 매기면 방-대-방 유도가 자동으로 이어진다. 순번은 던전 안에서 유일해야 한다.")]
        [SerializeField] private int roomIndex;

        private bool isStart = false;

        // 소환을 한 번만 돌리는 가드(첫 입장 = 소환). 문 앞 트리거에 들어오는 순간 켜진다.
        private bool triggered;

        // 잠금(전투 시작)을 한 번만 돌리는 가드(문 통과 = 잠금). 문 앞 트리거를 벗어나는 순간 켜진다.
        // 소환(Enter)과 잠금(Exit)을 서로 다른 두 시점으로 나누기 위해 triggered와 별도로 둔다.
        private bool encounterStarted;

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

        /// <summary>이 방을 클리어(몬스터 전멸 → 문 개방)했는지. 던전 네비게이션이 전투 중엔 안내를 숨기는 게이트로 쓴다.</summary>
        public bool IsCleared => cleared;

        /// <summary>방 순번. 나침반이 '이 방 다음(RoomIndex+1)' 방을 자동으로 찾는 키다.</summary>
        public int RoomIndex => roomIndex;

        /// <summary>
        /// 던전 네비게이션이 이 방을 클리어한 뒤 화살표로 가리킬 '다음 지역' 지점을 정한다. 우선순위는:
        ///   ① <see cref="roomIndex"/>+1 방(자동 체이닝) → ② 열린 exitDoor 최근접(폴백).
        ///
        /// ①이 핵심 — 각 방에 순번만 매기면 "Room1 → Room2 → Room3"이 자동으로 이어진다. 다음 방 오브젝트가
        /// 씬에 있으므로 그 위치를 그대로 조준점으로 쓴다(문이 아니라 방 자체를 가리켜 "다음 방으로" 유도).
        /// 전투 중에는 exitDoors가 모두 잠겨(폴백 경로도 막힘) 있고, 최종 방은 다음 방·문이 없어 false가 된다
        /// (그 경우 위젯은 안내를 숨긴다).
        /// </summary>
        /// <param name="from">거리 비교 기준(보통 플레이어 위치).</param>
        /// <param name="pos">가리킬 지점의 월드 위치(못 찾으면 <see cref="Vector3.zero"/>).</param>
        /// <param name="targetTransform">가리키는 대상(다음 방·문의 Transform. 못 찾으면 null). 디버그·연출용.</param>
        /// <returns>안내할 대상이 있으면 true.</returns>
        public bool TryGetExitTarget(Vector3 from, out Vector3 pos, out Transform targetTransform)
        {
            pos = Vector3.zero;
            targetTransform = null;

            // ① 순번 체이닝: 다음 방(RoomIndex+1)이 씬에 있으면 그 방을 가리킨다(Room1 → Room2 자동 유도).
            EnemyRoom next = DungeonNav.GetRoom(roomIndex + 1);
            if (next != null)
            {
                targetTransform = next.transform;
                pos = next.transform.position;
                return true;
            }

            // ② 다음 방이 없으면(순번 미설정·최종 방) 열린 exitDoor 중 최근접으로 폴백한다.
            if (exitDoors == null) return false;

            float bestSqr = float.PositiveInfinity;

            for (int i = 0; i < exitDoors.Length; i++)
            {
                AutoDoor candidate = exitDoors[i];
                if (candidate == null || candidate.IsLocked) continue;   // 아직 잠긴(정리 중) 문은 목표에서 제외

                Vector3 p = candidate.transform.position;
                float sqr = (p - from).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    pos = p;
                    targetTransform = candidate.transform;
                }
            }

            return targetTransform != null;
        }

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

        // 순번 레지스트리 등록/해제는 씬 수명과 맞춘다. 등록돼 있어야 이전 방이 GetRoom(index+1)로 이 방을
        // 다음 목표로 찾을 수 있고, 씬 언로드로 방이 사라지면 함께 빠져 파괴된 방을 가리키지 않는다.
        private void OnEnable() => DungeonNav.Register(this);

        private void OnDisable() => DungeonNav.Unregister(this);

        public void Bind(System.Action<EnemyRoom> callback) => onPlayerEnter = callback;

        // 플레이어가 문 앞 트리거에 '입장' => 소환만 한다. 잠금은 방 안쪽 RoomLockZone이 담당한다.
        // 소환(앞 트리거) → 문 통과 → 잠금(안쪽 트리거) 순서를 만들기 위해 두 트리거로 나눴다.
        private void OnTriggerEnter(Collider other)
        {
            // 준비되지 않았다면 이벤트 종료
            if (isStart == false) return;

            if (triggered || !other.CompareTag("Player")) return;
            triggered = true;

            // '현재 방' 등록은 여기(소환 트리거=문 앞/통로)서 하지 않는다. 소환 트리거가 통로까지 나와 있어,
            // 여기서 CurrentRoom을 바꾸면 이전 방을 막 클리어하고 통로를 걷는 순간 이미 이 방으로 넘어가
            // "다음 방으로" 나침반 안내가 뜨자마자 꺼진다. 방에 실제 진입하는 잠금존(BeginEncounterFromLockZone)에서 바꾼다.

            // TODO(멀티): 트리거는 클라 감지 → Command로 서버에 요청, 스폰은 서버가.
            onPlayerEnter?.Invoke(this);    // 권위(컨트롤러)에 위임 → 스폰 동기 실행, RegisterSpawned로 목록이 채워진다.

            trigger.enabled = false;    // 소환은 1회면 충분. 잠금은 방 안 RoomLockZone이 담당한다.

            // 잠금은 여기서 하지 않는다 — 문을 완전히 통과해 방에 들어온 순간(RoomLockZone)에 BeginEncounterFromLockZone으로 건다.
            // 트리거가 문 앞에서 끝나 문 통과 '전에' 잠기던 문제를 피하려고, 소환(앞)과 잠금(안쪽)을 두 트리거로 나눴다.
        }

        /// <summary>
        /// 방 안쪽 잠금 트리거(<see cref="RoomLockZone"/>)가 플레이어 방 진입을 감지했을 때 호출한다.
        /// 문을 완전히 통과한 뒤에야 잠기도록, 소환(앞 트리거)과 잠금(이 진입점)을 두 지점으로 나눈 것이다.
        /// </summary>
        /// <remarks>소환이 아직 안 됐으면(<see cref="triggered"/>=false) 배선 순서가 어긋난 것이라 경고만 남기고 무시한다
        /// (몹 목록이 빈 채 잠그면 <see cref="AllEnemiesDead"/>가 즉시 참이 되어 방이 곧바로 클리어된다).
        /// 이미 잠갔으면(<see cref="encounterStarted"/>) 중복 호출을 막는다.</remarks>
        public void BeginEncounterFromLockZone()
        {
            if (encounterStarted) return;

            if (!triggered)
            {
                ProjectS.Debugging.DevLog.Warning($"{name}: 잠금존 진입했지만 아직 소환 전(triggered=false) — 이 잠금존의 room 참조가 엉뚱한 방을 가리키거나, 소환 트리거(문 앞)가 잠금존(방 안)보다 앞에 있는지 배선을 확인하세요. 잠금 생략.", this);
                return;
            }

            encounterStarted = true;

            // 이 방을 던전 네비게이션의 '현재 방'으로 등록한다. 소환(앞 트리거)이 아니라 방에 실제로 들어온
            // 이 시점에 바꿔야, 이전 방을 클리어하고 통로를 걷는 동안엔 CurrentRoom이 이전 방(클리어됨)으로
            // 남아 나침반이 '다음 방'을 계속 가리킨다. 위젯은 CurrentRoom의 클리어 상태·나가는 문으로 방향을 잡는다.
            DungeonNav.SetCurrentRoom(this);

            // 스폰이 끝나 목록이 확정된 뒤 전투를 시작한다(문 잠금).
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

