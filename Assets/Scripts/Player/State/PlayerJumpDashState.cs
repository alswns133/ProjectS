using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 점프 대시(공중 대시) 상태. 공중에서 회피 키를 누르면 진입하며, 착지 전까지 1회만 허용된다
    /// (제한·방향은 PlayerMovement가 소유). 진행 중엔 중력을 끄고 수평으로만 이동하며 높이를 고정한다.
    ///
    /// 회피는 공격을 캔슬하는 최우선 동작이므로, 진입 시 진행 중이던 공격/스킬과 그 이펙트·이동 잠금·
    /// 올려치기 상승 상태를 모두 정리한다(구르기 상태와 같은 정리 절차). 실제 전진은
    /// Movement.TickJumpDash가 담당하고, 상태는 트리거 재생과 종료 판정만 한다.
    /// </summary>
    public class PlayerJumpDashState : BaseState
    {
        public PlayerJumpDashState(Player player) : base(player) { }

        public override void Enter()
        {
            // 회피 캔슬: 진행 중이던 공격/스킬을 강제 종료하고 잠금·버퍼·트리거·이펙트를 정리한다.
            player.Combat.CancelAction();
            player.Effect.AllStopEffect();
            player.UnlockMovement();
            player.Movement.StrongAttackRising = false;   // 올려치기 상승 상태도 해제

            player.Movement.StartJumpDash();
            // TODO(sound): 공중 대시(공중 회피) 발동음 — SoundManager.Instance.PlaySFX(<공중대시 SFX>);
            player.Animation.PlayJumpDash();
            player.Stats.SetInvincible(true);
        }

        public override void Update()
        {
            player.Movement.TickJumpDash();

            // 대시에서 빠져나갈 때(로코모션 복귀) 3단 로코모션 컨트롤러가 Idle·걷기·달리기 중
            // 어디로 갈지 정하도록 이동 bool을 계속 갱신한다(구르기 상태와 같은 이유).
            Vector2 input = player.Input.MoveInput;
            bool isMoving = input.sqrMagnitude > 0.0001f;
            player.Animation.SetLocomotion(isMoving, isMoving && player.Input.IsRunning);

            if (!player.Movement.IsJumpDashing)
                player.ChangeState(player.FreeState);
        }

        public override void Exit()
        {
            // 어떤 경로로 상태를 떠나든(정상 종료, 피격, 사망) 무적이 남지 않게해제(RollState와 동일)
            player.Stats.SetInvincible(false);
            // 대시 직후 프레임에 바로 얻어맞지 않게 잔여 무적을 부여한다(구르기와 동일한 값)
            player.Stats.GrantPostRollInvincibility();
            // 중간에 빠져나가도 대시 상태가 남지 않게 정리한다. 안하면 TickJumpDash가 더 이상 불리지 않아 isJumpDashing이 true로 남고 높이와 방향이 고정된 채 공중에 갇힘
            player.Movement.CancelJumpDash();
            // 소비되지 못한 대시 트리거가 래치된 채 남아 유령 대시가 재생되는 것을 막는다.
            player.Animation.ResetJumpDashTrigger();
        }
    }
}
