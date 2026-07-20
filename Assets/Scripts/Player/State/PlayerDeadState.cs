using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 사망 상태. 진입 시 사망 애니를 재생하고, 모든 조작을 차단한다.
    /// Update를 비워 입력·이동을 무시하는 '막다른 상태'.
    /// 부활은 외부(리스폰 처리)가 다른 상태로 전환시켜 빠져나간다.
    /// </summary>
    public class PlayerDeadState : BaseState
    {
        public PlayerDeadState(Player player) : base(player) { }

        public override void Enter()
        {
            // 강한 공격에 죽었으면 별도 사망 모션(doDieLarge)으로 분기한다.
            player.Animation.PlayDie(player.Stats.LastHitWasStrong);
            // 공격/스킬 도중 사망하면 재생 중이던 이펙트가 남으므로 함께 정리한다.
            player.Effect.AllStopEffect();
            // 점프 공격 호버링 중 사망하면 공중에 뜬 채 굳으므로 여기서도 해제한다.
            player.Movement.SetHover(false);
            // 이동 입력을 받지 않으므로 Update는 비워둔다 → 조작 잠금이 자동 성립
        }

        // Update 의도적으로 비움: 죽은 동안 이동·회전·전환 판단을 하지 않는다.
        // (BaseState의 빈 virtual을 그대로 사용 → override조차 생략)
    }
}
