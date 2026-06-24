using UnityEngine;

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
        player.Animation.PlayDie();   // 사망 애니 트리거
        // 이동 입력을 받지 않으므로 Update는 비워둔다 → 조작 잠금이 자동 성립
    }

    // Update 의도적으로 비움: 죽은 동안 이동·회전·전환 판단을 하지 않는다.
    // (BaseState의 빈 virtual을 그대로 사용 → override조차 생략)
}
