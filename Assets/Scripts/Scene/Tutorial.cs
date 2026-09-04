using ProjectS.Managers;
using ProjectS.UI;
using ProjectS.Players;
using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 튜토리얼 씬. 신규 캐릭터가 처음 접속할 때(<see cref="ProjectS.Data.CharacterSaveData.tutorialState"/>가
    /// Undone/Ongoing) <see cref="Bootstrap"/>가 이 씬으로 보낸다. 전투가 필요한 과제가 있어 던전 모드
    /// (공격·스킬 on)로 플레이어를 켜지만, 몬스터 스폰이 없어 <see cref="DungeonGather"/>를 상속하지 않고
    /// <see cref="BaseScene"/>만 상속한다. 완료/건너뜀 전이(→ 마을)는 이 씬이 아니라 과제 성공 이벤트에
    /// 연결된 완료 처리기가 담당한다.
    /// </summary>
    public class Tutorial : BaseScene
    {
        /// <summary>
        /// 튜토리얼 진입 처리. HUD를 켜고, 최초 진입이면 진행 상태를 Ongoing으로 넘겨(다음 접속 시 이어하기 판정용)
        /// 저장한 뒤, 지속 플레이어를 스폰 지점으로 옮겨 던전 모드(전투 on)로 켜고 스탯을 다시 발행한다.
        /// </summary>
        public override void Enter()
        {
            // 튜토리얼은 던전이 아니다(던전 ID 없음). 몬스터를 손으로 배치하므로 ID 오프셋(ResolveMonsterId)이
            // 필요 없고, 이전 판의 던전 ID가 남아 나침반이 엉뚱한 곳을 가리키지 않게 명시적으로 비운다(마을과 동일 방침).
            // 전투·구르기는 아래 EnterDungeon()의 combatEnabled가 담당하며, 이 값과는 무관하다.
            DungeonContext.ClearDungeon();

            UIManager.Instance.ShowPanel<HUDPanel>();

            // 최초 진입(Undone)이면 진행중으로 넘긴다. 상태 전이 자체는 Firebase 유무와 무관하게 항상 하고,
            // 저장만 매니저가 있을 때 시도한다(로그인 없이 씬 직접 테스트해도 진행 상태가 정상적으로 흐르게).
            // 이어하기(Ongoing)로 재진입한 경우는 다시 저장할 필요가 없어 건너뛴다.
            var ch = GameSession.SelectedCharacter;
            if (ch != null && ch.tutorialState == Core.TutorialState.Undone)
            {
                ch.tutorialState = Core.TutorialState.Ongoing;
                if (FirebaseManager.Instance != null)
                    _ = FirebaseManager.Instance.SaveCharacter(ch);
            }

            // 지속 플레이어를 이 씬 스폰 지점으로 옮겨 활성화한 뒤 던전 모드로 전환.
            Player player;
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.WarpToSpawn();
                player = PlayerManager.Instance.Player;
                // 튜토리얼은 공격이나 스킬사용이 가능함
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

            // JSON에서 로드된 실제 스탯을 HUD에 다시 반영(마을·던전과 동일 방침).
            PlayerEvents.FireStatsRefreshRequested();


        }

        /// <summary>튜토리얼 이탈 처리. 전환 동안 지속 플레이어를 잠시 꺼 월드에 방치되지 않게 한다.</summary>
        public override void Exit()
        {
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();

            if(UIManager.Instance != null) UIManager.Instance.GetPanel<HUDPanel>()?.SetHitComboVisible(false);
        }

        /// <summary>초기화 훅. 튜토리얼은 씬 생성 시 미리 준비할 것이 없어 비워 둔다.</summary>
        public override void Initialize()
        {

        }

        /// <summary>로딩 연출 훅. 튜토리얼은 진행도에 반응하는 로딩 연출이 없어 비워 둔다.</summary>
        public override void Progress(float progress)
        {

        }
    }
}
