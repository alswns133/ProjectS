using UnityEngine;
using ProjectS.Effects;
using ProjectS.Players;
using ProjectS.Scenes;

namespace ProjectS.Managers
{
    /// <summary>
    /// 플레이어 오브젝트의 수명·스폰·워프를 소유하는 매니저(씬을 넘어 유지).
    /// 부트스트랩에서 플레이어를 1회 생성(비활성)하고, 각 게임플레이 씬 진입 시 스폰 지점으로 옮긴 뒤 활성화한다.
    ///
    /// ★ 참조 단일 창구: 씬 오브젝트(적·트리거 등)가 플레이어를 알아야 하면 FindAnyObjectByType로 각자 찾지 말고
    ///   <see cref="Player"/>를 읽는다(pull). 플레이어는 부트스트랩에서 먼저 생성되므로 게임플레이 씬 오브젝트의
    ///   Start 시점엔 이미 존재한다. 카메라처럼 '플레이어-로컬' 의존은 씬이 아니라 플레이어 프리팹 안에 두어
    ///   내부 참조로 만들면 스폰돼도 깨지지 않는다(씬 배치 참조가 깨지는 문제의 근본 해결).
    ///
    /// ★ 캐릭터 선택/Firebase 세이브 연동(선택한 characterId로 CharacterConfig·스탯 주입)은 예정 단계다.
    ///   지금은 characterPrefabs 중 placeholderCharacterIndex로 골라 생성하고, 데이터 주입 지점만 <see cref="EnsurePlayer"/>에 seam으로 남겨둔다.
    /// </summary>
    public class PlayerManager : MonoBehaviour
    {
        /// <summary>씬을 넘어 유일한 플레이어 관리자. 씬 오브젝트가 플레이어를 참조하는 단일 창구.</summary>
        public static PlayerManager Instance { get; private set; }

        [Header("캐릭터 프리팹(직업별)")]
        // 직업 타입별 프리팹(검사/거너 …). 코드는 공용 1벌이고, 모델·히트박스·이펙트·투사체 스포너는
        // 각 프리팹 자식으로 둔다(프리팹 내부 참조라 스폰·씬전환에도 안 깨짐). 새 직업 = 이 배열에 프리팹 추가.
        // 어드레서블이 아니라 직접 참조 — CLAUDE.md: 항상 상주하는 플레이어 시스템 프리팹은 어드레서블 제외.
        [SerializeField] private Player[] characterPrefabs;

        // TODO(예정): GameSession의 선택 타입(검사/거너)으로 대체한다. 지금은 캐릭터 선택창/세션 전이라
        // 이 인덱스로 임시 선택해 부트스트랩만으로도 굴러가게 한다(직접 씬 테스트·데모 편의).
        [SerializeField] private int placeholderCharacterIndex = 0;

        /// <summary>현재 플레이어. 부트스트랩 생성 후 씬을 넘어 유지된다. 스폰 전에는 비활성 상태다.</summary>
        public Player Player { get; private set; }

        private void Awake()
        {
            // 매니저 규칙: 싱글톤 + 중복 제거 + DontDestroyOnLoad(다른 매니저와 동일 패턴).
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsurePlayer();
        }

        // 씬에 이미 플레이어가 있으면(부트스트랩 없이 직접 씬 테스트) 그걸 채택하고, 없으면 프리팹에서 생성한다.
        // 생성 직후 비활성화 — 스폰 지점으로 옮긴 뒤(WarpToSpawn) 씬이 활성화한다.
        // 비활성 상태로 두는 이유: 스폰 전까지 중력으로 떨어지거나 입력/AI 표적이 되는 것을 막기 위함.
        private void EnsurePlayer()
        {
            if (Player != null) return;

            Player existing = FindAnyObjectByType<Player>();
            if (existing != null)
            {
                Player = existing;
            }
            else
            {
                Player prefab = ResolveSelectedPrefab();
                if (prefab == null) return;
                Player = Instantiate(prefab);
            }

            DontDestroyOnLoad(Player.gameObject);
            Player.gameObject.SetActive(false);

            // TODO(예정): 인스턴스 고유 id로 Firebase 세이브(레벨/스킬/인벤토리)를 이 플레이어에 주입한다.
        }

        // 생성할 캐릭터 프리팹을 고른다. 타입 id → 프리팹 매핑을 하드코딩 없이 배열 인덱스로 둔다.
        // ★ 지금은 선택 시스템(GameSession) 전이라 placeholderCharacterIndex로 고른다.
        //   나중에: int type = GameSession.Instance.SelectedCharacterType; 로 교체하면 코드 구조는 그대로다.
        private Player ResolveSelectedPrefab()
        {
            if (characterPrefabs == null || characterPrefabs.Length == 0)
            {
                Debug.LogError("[PlayerManager] characterPrefabs가 비어 있어 플레이어를 생성할 수 없습니다.", this);
                return null;
            }

            int index = Mathf.Clamp(placeholderCharacterIndex, 0, characterPrefabs.Length - 1);
            Player prefab = characterPrefabs[index];
            if (prefab == null)
                Debug.LogError($"[PlayerManager] characterPrefabs[{index}]가 비어 있습니다.", this);

            return prefab;
        }

        /// <summary>
        /// 로딩/전환 중 플레이어를 잠시 끈다(월드에 방치돼 떨어지지 않게). 씬 Exit에서 호출한다.
        /// </summary>
        public void Hide()
        {
            if (Player != null) Player.gameObject.SetActive(false);
        }

        /// <summary>
        /// 현재 씬의 <see cref="PlayerSpawnPoint"/>로 플레이어를 옮기고 활성화한다. 씬 Enter에서 호출한다.
        /// 반드시 '비활성 상태에서' 위치를 세팅한 뒤 활성화한다 — CharacterController가 켜진 채 위치를 대입하면
        /// 물리가 순간이동을 물어 씹히고, 활성 상태로 옮기면 이전 씬 좌표가 한 프레임 렌더된 뒤 튄다.
        /// 스폰 지점이 없으면 현재 위치에서 활성화만 한다(크래시 대신 경고).
        /// </summary>
        public void WarpToSpawn()
        {
            if (Player == null) return;

            GameObject go = Player.gameObject;
            go.SetActive(false);   // CharacterController를 끄고 위치를 세팅해야 순간이동이 물리에 씹히지 않는다

            PlayerSpawnPoint spawn = FindAnyObjectByType<PlayerSpawnPoint>();
            if (spawn != null)
                Player.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
            else
                Debug.LogWarning("[PlayerManager] 현재 씬에 PlayerSpawnPoint가 없어 현재 위치에서 활성화합니다.", this);

            // 이전 씬에서 남은 낙하 속도·수평 관성·공중 동작 상태를 리셋해, 켜자마자 떨어지거나 미끄러지지 않게 한다.
            Player.Movement.ResetMotion();

            // 투사체 스포너는 '씬에 하나, 씬 전환 시 Release'하는 씬 단위 서비스라 프리팹에 넣지 않는다.
            // 지속 플레이어를 씬 진입마다 현재 씬의 스포너로 다시 연결한다(없으면 null — 마을 등 비전투 씬).
            Player.Combat.SetProjectileSpawner(FindAnyObjectByType<ProjectileSpawner>());

            go.SetActive(true);
        }
    }
}
