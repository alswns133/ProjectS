using UnityEngine;

/// <summary>
/// 구르기(회피) 상태. 진입 시점의 이동 입력 방향을 바라보게 한 뒤 앞구르기를 재생한다.
/// 실제 전진은 구르기 클립의 루트 모션이 담당하므로 상태는 방향 정렬과 중력만 처리한다.
/// 무입력(Idle) 진입은 Player.TryRoll에서 차단된다.
/// 회피 컨셉: 구르는 동안 일반 공격은 전부 무시하고 즉사기만 맞는다(PlayerStats 참조).
/// 공격/스킬 도중에도 진입 가능한 최우선 동작(회피 캔슬)이라, 진입하며 진행 중이던
/// 전투 동작을 정리한다. 도중에 방향을 꺾을 수 없는 '커밋형' 회피다.
/// </summary>
public class PlayerRollState : BaseState
{
    private float elapsed;

    public PlayerRollState(Player player) : base(player) { }

    public override void Enter()
    {
        elapsed = 0f;

        // 회피 캔슬: 진행 중이던 공격/스킬을 강제 종료하고 잠금·버퍼·트리거를 정리한다.
        // 안 하면 캔슬된 동작의 트리거가 래치된 채 남아 구르기 직후 저절로 발동한다.
        player.Combat.CancelAction();
        player.UnlockMovement();

        // 입력 방향(카메라 기준)으로 구른다. 무입력 진입은 Player.TryRoll에서 막히지만,
        // 진입과 키 해제가 같은 프레임에 겹치는 경우를 대비해 전방 폴백을 남겨둔다.
        Vector2 input = player.Input.MoveInput;
        Vector3 rollDirection = input.sqrMagnitude > 0.0001f
            ? player.Movement.CameraRelativeDirection(input)
            : player.transform.forward;

        // 카메라가 아직 없는 극초반 프레임 등으로 방향을 못 구했으면 전방으로 대체
        if (rollDirection.sqrMagnitude < 0.0001f)
            rollDirection = player.transform.forward;

        // 구를 방향을 즉시 바라본다 → 이후 전진은 앞구르기 클립의 루트 모션이 담당,
        // 몸이 바라보는 방향 = 구르는 방향이 되므로 여기서 방향이 확정된다.
        player.Movement.FaceInstantly(rollDirection);
        player.Animation.PlayRoll();

        player.Stats.SetInvincible(true);
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // 수평 이동은 루트 모션이 담당하므로 여기서 밀지 않는다(밀면 이중 이동).
        // 중력·접지 유지를 위해 zero 입력으로 Move만 호출한다.
        player.Movement.Move(Vector2.zero);

        if (elapsed >= player.Movement.RollDuration)
            player.ChangeState(player.FreeState);
    }

    public override void Exit()
    {
        // 어떤 경로로 상태를 떠나든(정상 종료·사망 전환) 무적이 남지 않게 여기서 해제.
        // ChangeState가 Exit 호출을 보장하므로 이 한 곳이면 충분하다.
        player.Stats.SetInvincible(false);

        // 애니메이터가 소비하지 못한 doRoll이 래치된 채 남으면
        // 나중에 조건이 맞는 순간 유령 구르기가 재생된다 → 상태를 떠날 때 반드시 정리.
        player.Animation.ResetRollTrigger();
    }
}
