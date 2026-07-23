using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 피격 경직 상태. 데미지가 실제로 적용됐을 때(PlayerStats.Damaged) 진입한다.
    /// 진행 중이던 공격/스킬을 캔슬하고 피격 모션을 재생하며, 경직 동안 이동·공격 입력을 막는다.
    /// 강한 피격(LastHitWasStrong)은 별도 모션과 더 긴 경직이 적용된다.
    /// 구르기는 경직을 캔슬할 수 있다(회피 최우선 철학 — Player.TryRoll이 경직을 확인하지 않음).
    /// 연타를 맞아도 경직이 리셋되지 않는다(상태 머신의 같은 상태 재진입 방지).
    /// </summary>
    public class PlayerHitState : BaseState
    {
        private float elapsed;

        public PlayerHitState(Player player) : base(player) { }

        public override void Enter()
        {
            elapsed = 0f;

            // 피격은 진행 중이던 동작을 강제로 끊는다. 구르기 캔슬과 같은 정리 절차:
            // 콤보/버퍼/트리거 정리 + 이펙트 제거 + 이동 잠금·호버링 해제.
            player.Combat.CancelAction();
            player.Effect.AllStopEffect();
            player.UnlockMovement();   // 내부에서 SetHover(false)도 함께 처리(점프 공격 중 피격 대비)
            player.Movement.CancelJump();          // 상승 중이던 점프 속도 제거(피격했는데 계속 떠오르는 것 방지)

            player.Animation.PlayHit(player.Stats.LastHitWasStrong);

            // 경직 동안 수평 이동이 멈추므로 로코모션 bool도 함께 내린다. 켜둔 채로 두면
            // 3단 로코모션 컨트롤러가 피격 모션 도중 걷기 Loop로 새어나간다.
            // Enter에서 1회면 충분하다: 경직 중에는 이 값을 다시 켜는 상태가 돌지 않는다.
            player.Animation.SetLocomotion(false, false);
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 경직 중 수평 이동은 없고 중력·접지만 유지한다(RollState와 같은 zero 입력 호출).
            player.Movement.Move(Vector2.zero);

            if (elapsed >= player.Stats.CurrentStaggerDuration)
                player.ChangeState(player.FreeState);
        }

        public override void Exit()
        {
            // 경직 중 사망/구르기로 끊겨도 래치된 피격 트리거가 남지 않게 정리한다.
            player.Animation.ResetHitTriggers();
        }
    }
}
