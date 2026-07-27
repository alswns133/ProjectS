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

            PlayerEvents.FireHpChanged(100, 100);
            PlayerEvents.FireSgChanged(50, 50);
            PlayerEvents.FireExpChanged(0, 100);
            PlayerEvents.FireGoldChanged(50000);
            PlayerEvents.FireLevelUp(5);

            // 지속 플레이어를 이 씬 스폰 지점으로 옮겨 활성화한 뒤, 마을 모드(전투 off + 마을 컨트롤러)로 전환.
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.WarpToSpawn();
                PlayerManager.Instance.Player?.EnterVillage();
            }
            else
            {
                // 부트스트랩 없이 직접 씬 테스트: 씬에 배치된 플레이어를 그대로 사용(워프 없음).
                Object.FindAnyObjectByType<Player>()?.EnterVillage();
            }
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

