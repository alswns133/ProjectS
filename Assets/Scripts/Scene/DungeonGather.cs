using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;


public class DungeonGather : ProjectS.Scenes.BaseScene
{
    public override void Enter()
    {
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

        // ★ 하드코딩 초기화 대신, JSON에서 로드된 실제 스탯을 HUD에 반영한다(마을과 동일 방침).
        // 플레이어 활성화 뒤 요청해야 PlayerStats·InventoryManager가 받아 로드된 값을 다시 발행한다.
        PlayerEvents.FireStatsRefreshRequested();
    }

    public override void Exit()
    {
        // 씬 전환 로딩이 도는 동안(던전 씬은 아직 언로드 전) 몬스터가 이미 숨겨진 플레이어의
        // 마지막 위치로 계속 이동하는 것을 막는다. 곧 씬과 함께 파괴되지만, 로딩 화면 사이 잔상 이동을 없앤다.
        foreach (Enemy enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
            enemy.HaltForSceneExit();

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
