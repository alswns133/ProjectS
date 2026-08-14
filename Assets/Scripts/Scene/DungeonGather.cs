using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ProjectS.Scenes
{
    public class DungeonGather : BaseScene
    {
        [Tooltip("직접 씬 테스트용 폴백 던전 ID. 실제 진입은 GameSession.SelectedDungeonId(선택/생성 시 세팅)를 우선한다.")]
        [SerializeField] private int dungeonId;

        private EnemySpawner enemySpawner;  // 씬에서 참조 캐싱
        private EnemyRoom[] rooms;
        private readonly List<Enemy> alive = new();


        public override void Enter()
        {
            // 던전은 런타임 자동 생성이라 씬 컨트롤러가 인스펙터로 던전 ID를 알 수 없다. 던전 선택/생성 코드가 세션에
            // 실어둔 값을 읽어 현재 던전 표식을 세팅한다(나침반의 '목표 던전 도착' 판정 기준). 세션이 비면 인스펙터 폴백(직접 씬 테스트).
            int currentDungeonId = GameSession.SelectedDungeonId != 0 ? GameSession.SelectedDungeonId : dungeonId;
            ProjectS.Scenes.DungeonContext.SetDungeon(currentDungeonId);

            UIManager.Instance.ShowPanel<HUDPanel>();

            // 지속 플레이어를 이 씬 스폰 지점으로 옮겨 활성화한 뒤, 던전 모드(전투 on + 던전 컨트롤러)로 전환.
            Player player;
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.WarpToSpawn();
                player = PlayerManager.Instance.Player;
                player?.EnterDungeon();
            }
            else
            {
                // 부트스트랩 없이 직접 씬 테스트: 씬에 배치된 플레이어를 그대로 사용(워프 없음).
                player = Object.FindAnyObjectByType<Player>();
                player?.EnterDungeon();
            }

            // 기획: 씬 진입마다 HP·SG를 최대치로 회복한다. 발행 전에 값을 먼저 세팅한다.
            player?.Stats.RefillOnSceneEnter();

            // 기획: 던전 한 판마다 부활 기회 1회. 여기서 채우지 않으면 사망 팝업이 항상 "기회 없음"으로 떠
            // 첫 죽음에 바로 마을로 쫓겨난다.
            ReviveBudget.GrantOnDungeonEnter();

            // ★ 하드코딩 초기화 대신, JSON에서 로드된 실제 스탯을 HUD에 반영한다(마을과 동일 방침).
            // 플레이어 활성화 뒤 요청해야 PlayerStats·InventoryManager가 받아 로드된 값을 다시 발행한다.
            PlayerEvents.FireStatsRefreshRequested();

            enemySpawner = FindAnyObjectByType<EnemySpawner>(); // Enter에서, 씬 로드 후
            rooms = FindObjectsByType<EnemyRoom>(FindObjectsSortMode.None);
            foreach (var r in rooms) 
                r.Bind(RequestRoomSpawn); // 각 방에 스폰 권위 연결
            _ = PreloadThenEnableAsync();
        }

        public override void Exit()
        {
            // 이 던전을 떠나므로 현재 던전 표식을 지운다(다음 씬이 마을이면 목표 안내가 다시 게이트로 돌아간다).
            DungeonContext.ClearDungeon();

            // 씬 전환 로딩이 도는 동안(던전 씬은 아직 언로드 전) 몬스터가 이미 숨겨진 플레이어의
            // 마지막 위치로 계속 이동하는 것을 막는다. 곧 씬과 함께 파괴되지만, 로딩 화면 사이 잔상 이동을 없앤다.
            foreach (Enemy enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.HaltForSceneExit();

            // 다음 씬으로 전환하는 동안 지속 플레이어를 잠시 끈다(월드에 방치돼 떨어지지 않게).
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();

            enemySpawner?.ClearAndRelease(); // 이탈 정리
            alive.Clear();
        }

        public override void Initialize()
        {
            dungeonId = 1;
        }

        public override void Progress(float progress)
        {

        }

        /// <summary>★ 권위: 방이 요청 → 여기서만 스폰. 나중에 [Server] 가드.</summary>
        public void RequestRoomSpawn(EnemyRoom room)
        {
            // TODO(멀티): [Server]
            foreach (var p in room.Points)
            {
                p.PlaySpawnEffects();
                for (int i = 0; i < p.Count; i++)
                {
                    alive.Add(enemySpawner.SpawnOne(p.EnemyRef, p.Position, p.Rotation));
                }
            }
        }

        private async Task PreloadThenEnableAsync()
        {
            try
            {
                await enemySpawner.PreloadAsync(rooms.SelectMany(r => r.EnemyRefs).Distinct());
                foreach (var r in rooms) r.EnableTrigger();   // 프리로드 끝난 뒤에야 트리거 켬
            }
            catch (System.Exception e) { Debug.LogException(e); }  // fire-and-forget은 예외가 삼켜지니 로깅
        }


    }

}
