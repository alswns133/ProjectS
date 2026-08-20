using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 마을 씬. 전투가 없는 허브로, 던전에서 돌아오거나 게임을 시작할 때 진입한다.
    /// 던전 씬(<see cref="DungeonGather"/>)과 대칭 구조지만, 스폰·전투를 던전에 위임하지 않고
    /// "던전 밖" 상태(던전 표식 해제, 플레이어 마을 모드)로 되돌리는 것이 이 씬의 역할이다.
    /// </summary>
    public class VillageGather : BaseScene
    {
        /// <summary>
        /// 마을 진입 처리. 던전 표식을 지우고 HUD를 켠 뒤, 지속 플레이어를 스폰 지점으로 옮겨
        /// 마을 모드(전투 off)로 전환하고 스탯을 다시 발행한다.
        /// </summary>
        public override void Enter()
        {
            // 마을은 던전 밖이므로 현재 던전 표식을 0으로 되돌린다(나침반이 목표를 다시 던전 게이트로 안내).
            DungeonContext.ClearDungeon();

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

        /// <summary>마을 이탈 처리. 전환 동안 지속 플레이어를 잠시 꺼 월드에 방치되지 않게 한다.</summary>
        public override void Exit()
        {
            // 다음 씬으로 전환하는 동안 지속 플레이어를 잠시 끈다(월드에 방치돼 떨어지지 않게).
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();
        }

        /// <summary>초기화 훅. 마을은 씬 생성 시 미리 준비할 것이 없어 비워 둔다.</summary>
        public override void Initialize()
        {

        }

        /// <summary>로딩 연출 훅. 마을은 진행도에 반응하는 로딩 연출이 없어 비워 둔다.</summary>
        public override void Progress(float progress)
        {

        }
    }
}

