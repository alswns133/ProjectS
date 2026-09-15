using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;
using ProjectS.Managers;
using ProjectS.Scenes;
using ProjectS.Players;
using ProjectS.Enemies;
using Unity.VisualScripting;

namespace ProjectS.Networking
{
    /// <summary>
    /// 프로젝트 전용 NetworkManager. 접속·플레이어 스폰·씬 전환 권위를 이 프로젝트 규칙에 맞춘다.
    ///
    /// 설계 전제(DUNGEON_AND_MULTIPLAYER.md + 이후 결정):
    /// - 서버 모델 = 전용(headless) 서버 하나. 서버는 마을 씬에 떠 있고, 클라는 마을 진입 시 접속한다.
    /// - onlineScene/offlineScene은 비운다 — 접속 순간 자동 씬 전환이 일어나면 안 되기 때문.
    ///   (onlineScene=던전으로 두면 접속하자마자 전원이 던전으로 끌려간다.) 던전 이동은
    ///   파티 레디 완료 시 <see cref="GoToDungeon"/>가 ServerChangeScene으로 수동 처리한다.
    /// - Player Prefab = 채팅 전용 경량 네트워크 오브젝트(NetworkIdentity + ChatManager). 접속 시
    ///   커넥션마다 자동 스폰되어 소유권을 갖는다 → 로컬 클라의 채팅 Command가 허용된다.
    ///   (마을의 보이는/조종하는 캐릭터는 여전히 로컬 PlayerManager.Player. 서로 별개다 — A안.)
    /// </summary>
    public class GameNetworkManager : NetworkManager
    {
        [Header("개발용")]
        [Tooltip("에디터에서 혼자 테스트할 때, 서버 접속 대신 Host(서버+클라)로 켠다. " +
                 "전용 서버가 없으면 클라 접속은 실패하므로, 채팅을 혼자 확인하려면 이걸 켠다. 빌드/전용서버엔 영향 없음.")]
        [SerializeField] private bool hostInEditor = true;

        [SerializeField] private CharacterRoster roster;

        /// <summary>이 프로젝트 타입으로 접근하기 위한 캐스팅 도우미(base의 singleton 재사용).</summary>
        public static GameNetworkManager Game => singleton as GameNetworkManager;

        [Header("레이드 보스 프리팹")]
        [SerializeField] private Boss[] raidBoss;

        // 필드 — 클라가 방금 additive로 들어간 던전 씬 이름(로드 후 마을을 가려낼 때 쓴다). 비면 던전 진입 아님.
        private string enteringDungeonScene;

        private bool awaitingNetworkSceneLoad;

        public override void Awake()
        {
            base.Awake();

            // 접속 순간 자동 씬 전환 방지(위 주석 참조). 인스펙터에서 비워도 되지만 코드로도 못박는다.
            onlineScene = string.Empty;
            offlineScene = string.Empty;

            if (roster == null) return;

            Player[] list = roster.GetByList();
            if (list == null) return;                 // ① GetByList null 방어

            foreach (var p in list)
            {
                if (p == null) continue;              // ② 빈 슬롯 방어
                spawnPrefabs.Add(p.gameObject);
            }

            if (raidBoss == null) return;
            foreach(var p in raidBoss)
            {
                if(p == null) continue;
                spawnPrefabs.Add(p.gameObject);
            }

        }

        public override void Start()
        {
            base.Start();

            // 전용 서버 프로세스(-batchmode)면 서버로 기동한다.
            // 클라는 여기서 아무것도 하지 않는다 — 마을 진입 시 ConnectFromVillage로 접속한다.
            // ★ 마을 월드 로드는 데이터(JsonManager)가 준비된 뒤라야 안전하므로 여기서 하지 않고
            //   Bootstrap이 ReadyTask 이후 처리한다(B안: 커스텀 SceneManager 로드, Mirror ServerChangeScene 아님).
            if (IsServerMode)
            {
                // ★ 헤드리스 서버는 렌더가 없어 프레임 루프가 무제한(수천 FPS)으로 돈다 →
                //   CPU 코어 100% 스핀 + TempJob이 4프레임 안에 소비 안 돼 "deleting an allocation
                //   older than 4 frames" 경고가 쏟아진다. 서버 틱레이트를 고정해 둘 다 잡는다.
                Application.targetFrameRate = 30;   // 서버 시뮬 틱(필요 시 60까지)
                QualitySettings.vSyncCount = 0;     // vSync가 targetFrameRate를 덮지 않게(헤드리스도 명시)

                StartServer();
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            GameSceneManager.SceneEntered += OnSceneEnteredForNetwork;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            GameSceneManager.SceneEntered -= OnSceneEnteredForNetwork;
        }

        // 로드 "전" — 어디로 들어가는지 기억만 해둔다(마을 언로드는 로드가 끝난 뒤에 해야 하므로).
        public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
        {
            base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);

            if (sceneOperation == SceneOperation.LoadAdditive && customHandling)
            {
                enteringDungeonScene = newSceneName;
                awaitingNetworkSceneLoad = true;              // ← SceneEntered를 기다리는 중이라 표시
                if (newSceneName == nameof(Raid))
                    GameSceneManager.Instance.RequestSceneChange<Raid>();
            }

        }

        // OnEnable 등에서: GameSceneManager.SceneEntered += OnSceneEnteredForNetwork;  (OnDisable에서 해제)
        private void OnSceneEnteredForNetwork(string sceneName)
        {

            // ★ SceneEntered는 "모든" 씬 진입에 발행된다(마을 복귀 등 로컬 전환 포함).
            //    그래서 "내가 네트워크로 몰아서 로드한 그 던전"일 때만 미러에 통지해야 한다.
            if (!awaitingNetworkSceneLoad || sceneName != enteringDungeonScene) return;

            awaitingNetworkSceneLoad = false;

            // 미러에 클라 로드 완료 통지 (★ 정확한 호출은 미러 소스 확인)
            FinishLoadScene();
        }

        /// <summary>
        /// 이 프로세스가 전용(headless) 서버로 떠야 하는가. Bootstrap도 이 값으로 클라 흐름
        /// (로그인·튜토리얼·클라 씬 로드)을 스킵할지 가른다.
        /// <para>
        /// 판정 우선순위:
        /// <list type="number">
        /// <item><b>에디터는 항상 false</b> — 활성 빌드 타깃이 Dedicated Server(<c>UNITY_SERVER</c>)여도
        ///   에디터 Play는 서버로 뜨지 않는다. 에디터는 호스트/클라로 반복 테스트하는 곳이고(보스 등 서버
        ///   권위 로직은 에디터 Host=서버+클라로 확인), 실제 전용 서버는 빌드로만 띄운다.</item>
        /// <item><b>Dedicated Server 빌드</b>(<c>UNITY_SERVER</c>) = 항상 서버(실행 인자 무관).</item>
        /// <item>그 외 일반 빌드는 <c>-batchmode</c>로 뜬 경우에만 서버(전용 빌드 없이 헤드리스 테스트).</item>
        /// </list>
        /// </para>
        /// </summary>
        public static bool IsServerMode
        {
            get
            {
#if UNITY_EDITOR
                return false;
#elif UNITY_SERVER
                return true;
#else
                return Application.isBatchMode;
#endif
            }
        }

        // ── 클라: 마을 진입 시 접속 ──────────────────────────────────

        /// <summary>
        /// 마을 진입 시 전용 서버에 클라이언트로 접속한다(VillageGather.Enter에서 호출).
        /// 이미 접속 중/연결됨이면 무시해 중복 접속을 막는다(마을을 다시 밟아도 안전).
        /// </summary>
        /// <param name="address">서버 주소. 데모는 LAN/직접 IP(§3). null이면 인스펙터 networkAddress 사용.</param>
        public void ConnectFromVillage(string address = null)
        {
            if (NetworkClient.active || NetworkServer.active)
            {
                Debug.Log("[Chat/Net] ConnectFromVillage 건너뜀 — 이미 서버/클라 활성.");
                return;   // 이미 접속/서버면 중복 방지
            }

#if UNITY_EDITOR
            // 혼자 테스트: 전용 서버가 없으니 Host(서버+클라)로 켠다. 그래야 ChatNetworkPlayer가 스폰돼 채팅이 굴러간다.
            if (hostInEditor)
            {
                // 두 에디터 인스턴스(ParrelSync 클론 / MPPM 가상 플레이어)를 도구 무관하게 안전히 가른다.
                // 클론/가상 판별법이 도구마다 제각각(ParrelSync는 Assets 심볼릭 링크라 dataPath도 못 믿음)이라,
                // "포트를 먼저 잡은 쪽이 호스트, 이미 물려 있으면(SocketException) 클라"로 자동 판별한다.
                try
                {
                    Debug.Log("[Chat/Net] 에디터 Host 시도(hostInEditor).");
                    StartHost();
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // 포트가 이미 물려 있음 = 다른 에디터가 먼저 호스트. 부분 시작 정리 후 클라로 붙는다.
                    if (NetworkServer.active || NetworkClient.active) StopHost();

                    networkAddress = "localhost";
                    Debug.Log("[Chat/Net] 포트 사용 중 → 다른 인스턴스가 호스트. StartClient(localhost)로 전환.");
                    StartClient();
                }
                return;
            }
#endif

            if (!string.IsNullOrEmpty(address)) networkAddress = address;
            Debug.Log($"[Chat/Net] StartClient — {networkAddress} 로 접속 시도(서버가 떠 있어야 성공).");
            StartClient();
        }

        // ── 서버: 파티 레디 완료 → 던전 이동 ─────────────────────────

        /// <summary>
        /// 전원을 던전 씬으로 동기 이동시킨다. PartyManager가 전원 수락을 확인한 뒤 서버에서 호출한다.
        /// Build Settings 씬 이름 전제(§1). 지금은 진입점만 열어두고 호출부는 파티 시스템에서 채운다.
        /// </summary>
        [Server]
        public void GoToDungeon(string dungeonSceneName)
        {
            // TODO: 던전 진입 직전 정리(로컬↔네트워크 플레이어 재빌드 경계 §5)와 맞물릴 지점.
            ServerChangeScene(dungeonSceneName);
        }

        // ── 스폰 훅 ─────────────────────────────────────────────────

        // 기본 OnServerAddPlayer가 playerPrefab을 스폰 지점(또는 원점)에 생성하고 소유권을 준다.
        // 채팅 경량 오브젝트는 위치가 의미 없으므로 기본 동작으로 충분하다.
        // 나중에 파티/세이브 정보를 스폰 시 주입해야 하면 여기서 override한다.
        // public override void OnServerAddPlayer(NetworkConnectionToClient conn) { ... }
    }
}
