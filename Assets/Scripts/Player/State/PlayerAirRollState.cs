using UnityEngine;

/// <summary>
/// 공중 회피(공중 대시) 상태. 점프 등으로 공중에 뜬 상태에서 회피 키를 누르면 진입한다.
/// 대시 클립에 맞춰 회피 동안 중력을 멈추고 고도를 유지한 채 수평으로만 대시한다
/// (Movement.AirDash가 담당). 회피가 끝나면 그 지점부터 다시 낙하한다.
/// 진입 시점의 이동 입력 방향으로 대시하고, 무입력이면 바라보던 방향으로 대시한다.
/// 무적 규칙은 지상 구르기와 동일(일반 공격 무시, 즉사기만 적중).
/// 횟수 제한은 없다(기획): 회피 중 재입력만 Player.IsRolling 게이트로 막힌다.
/// </summary>
public class PlayerAirRollState : BaseState
{
    private float elapsed;

    // 진입 시 1회 확정하는 대시 방향. 도중에 꺾을 수 없는 커밋형 회피(지상 구르기와 동일).
    private Vector3 dashDirection;

    // ── 디버그(원인 확인 후 삭제) ──
    // 대시 도중 접지 판정이 한 번이라도 true가 됐는지. 낮은 고도 대시가
    // isGrounded에 묶여 모션이 안 나가는 가설(3번)을 검증하기 위해 기록한다.
    private bool dbgGroundedDuringDash;

    public PlayerAirRollState(Player player) : base(player) { }

    public override void Enter()
    {
        elapsed = 0f;

        // 회피 캔슬: 지상 구르기와 동일하게 진행 중이던 전투 동작을 정리한다.
        // 안 하면 캔슬된 동작의 트리거가 래치된 채 남아 회피 직후 저절로 발동한다.
        player.Combat.CancelAction();
        player.UnlockMovement();

        // 입력 방향(카메라 기준)으로 대시. 무입력이면 바라보던 방향 유지.
        Vector2 input = player.Input.MoveInput;
        dashDirection = input.sqrMagnitude > 0.0001f
            ? player.Movement.CameraRelativeDirection(input)
            : player.transform.forward;

        // 카메라가 아직 없는 극초반 프레임 등으로 방향을 못 구했으면 전방으로 대체
        if (dashDirection.sqrMagnitude < 0.0001f)
            dashDirection = player.transform.forward;

        // 대시 방향을 즉시 바라본다 → 몸이 바라보는 방향 = 대시 방향(지상 구르기와 동일 규칙)
        player.Movement.FaceInstantly(dashDirection);
        player.Animation.PlayAirRoll();

        player.Stats.SetInvincible(true);

        // ── 디버그(원인 확인 후 삭제) ──
        dbgGroundedDuringDash = player.Movement.IsGrounded;
        Debug.Log($"[AirDash] 시작 | 높이 y={player.transform.position.y:F2}, " +
                  $"접지={player.Movement.IsGrounded}");
    }

    public override void Update()
    {
        elapsed += Time.deltaTime;

        // 일반 Move 대신 AirDash: 중력을 멈춰 고도를 유지하고 수평 대시만 적용한다.
        player.Movement.AirDash(dashDirection);

        // ── 디버그(원인 확인 후 삭제) ── 대시 도중 접지 판정이 켜지는 순간을 포착
        if (!dbgGroundedDuringDash && player.Movement.IsGrounded)
        {
            dbgGroundedDuringDash = true;
            Debug.LogWarning($"[AirDash] 대시 중 접지 감지! 경과={elapsed:F3}s, " +
                             $"높이 y={player.transform.position.y:F2}");
        }

        if (elapsed >= player.Movement.AirRollDuration)
            player.ChangeState(player.FreeState);
    }

    public override void Exit()
    {
        // 어떤 경로로 상태를 떠나든(정상 종료·사망 전환) 무적이 남지 않게 여기서 해제.
        player.Stats.SetInvincible(false);

        // ── 디버그(원인 확인 후 삭제) ── ResetAirRollTrigger보다 반드시 먼저 읽어야 한다.
        // 트리거미소비=True면 애니메이터가 전환을 한 번도 못 탄 것 = 모션이 안 나간 대시.
        bool pending = player.Animation.IsAirRollTriggerPending;
        if (pending)
            Debug.LogWarning($"[AirDash] 종료 | 트리거미소비=True (모션 안 나감!), " +
                             $"대시중접지={dbgGroundedDuringDash}, 접지={player.Movement.IsGrounded}");
        else
            Debug.Log($"[AirDash] 종료 | 트리거 정상 소비(모션 나감), " +
                      $"대시중접지={dbgGroundedDuringDash}");

        // 애니메이터가 소비하지 못한 doAirRoll이 래치된 채 남으면
        // 착지 후 조건이 맞는 순간 유령 대시가 재생된다 → 상태를 떠날 때 반드시 정리.
        player.Animation.ResetAirRollTrigger();
    }
}
