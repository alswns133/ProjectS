using UnityEngine;
using Mirror;

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

        /// <summary>이 프로젝트 타입으로 접근하기 위한 캐스팅 도우미(base의 singleton 재사용).</summary>
        public static GameNetworkManager Game => singleton as GameNetworkManager;

        public override void Awake()
        {
            base.Awake();

            // 접속 순간 자동 씬 전환 방지(위 주석 참조). 인스펙터에서 비워도 되지만 코드로도 못박는다.
            onlineScene = string.Empty;
            offlineScene = string.Empty;
        }

        public override void Start()
        {
            base.Start();

            // 전용 서버 프로세스(-batchmode -nographics 또는 커맨드라인 -server)면 서버로 기동한다.
            // 클라는 여기서 아무것도 하지 않는다 — 마을 진입 시 ConnectFromVillage로 접속한다.
            if (IsDedicatedServer())
            {
                StartServer();
                // TODO(운영): 전용 서버는 부팅 시 마을 씬을 로드한 상태로 대기해야 한다
                //             (ServerChangeScene(마을) 또는 서버 부팅 씬 자체를 마을로).
            }
        }

        /// <summary>커맨드라인/배치모드로 전용 서버 여부를 판정한다.</summary>
        private bool IsDedicatedServer()
        {
            if (Application.isBatchMode) return true;
            // TODO: 필요하면 커맨드라인 인자(-server) 파싱 추가.
            return false;
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
                Debug.Log("[Chat/Net] 에디터 Host 모드로 시작(hostInEditor).");
                StartHost();
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
