using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    public class VillageGather : BaseScene
    {
        public override void Enter()
        {
            UIManager.Instance.ShowPanel<HUDPanel>();

            // 지속 플레이어를 이 씬 스폰 지점으로 옮겨 활성화한 뒤, 마을 모드(전투 off + 마을 컨트롤러)로 전환.
            Player player;
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.WarpToSpawn();
                player = PlayerManager.Instance.Player;
                player?.EnterVillage();
            }
            else
            {
                // 부트스트랩 없이 직접 씬 테스트: 씬에 배치된 플레이어를 그대로 사용(워프 없음).
                player = Object.FindAnyObjectByType<Player>();
                player?.EnterVillage();
            }

            // 기획: 씬 진입마다 HP·SG를 최대치로 회복한다. 발행 전에 값을 먼저 세팅한다.
            player?.Stats.RefillOnSceneEnter();

            // ★ 하드코딩 초기화(HP 100/SG 50/Gold 50000/Lv5) 대신, JSON에서 로드된 실제 스탯을 HUD에 반영한다.
            // 플레이어 활성화 뒤 요청해야 PlayerStats(OnEnable에서 구독)가 받아 PublishAllStats로 HP·SG·EXP·레벨을
            // 다시 쏘고, InventoryManager도 실제 골드를 다시 발행한다.
            PlayerEvents.FireStatsRefreshRequested();
        }

        public override void Exit()
        {
            // 다음 씬으로 전환하는 동안 지속 플레이어를 잠시 끈다(월드에 방치돼 떨어지지 않게).
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();
        }

        public override void Initialize()
        {

        }

        public override void Progress(float progress)
        {

        }
    }
}

