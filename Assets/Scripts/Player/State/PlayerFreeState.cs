using UnityEngine;

/// <summary>
/// 자유 이동 상태. 현재 유일한 플레이어 상태로, 카메라 기준 이동과
/// 진행 방향 회전을 매 프레임 처리한다. (Dash/Hit 등은 향후 별도 상태로 추가)
/// </summary>
public class PlayerFreeState : BaseState
{
    // 생성 시 받은 player를 부모(BaseState)에 위임해 보관시킨다.
    // 본문이 비어있는 건 이 상태가 진입 시 따로 준비할 게 없기 때문.
    public PlayerFreeState(Player player) : base(player) { }

    public override void Update()
    {
        Vector2 input = player.Input.MoveInput;
        // 이동 + 진행 방향으로의 회전까지 Movement가 함께 처리한다.
        player.Movement.Move(input);

        // 입력 크기로 이동 여부를 판단해 애니메이터 전진량(Z)에 전달.
        // sqrMagnitude 사용: 실제 거리(magnitude)는 제곱근 연산이라,
        // "움직이는가?"만 볼 때는 제곱값 비교가 더 싸다.
        player.Animation.SetForward(input.sqrMagnitude > 0.0001f ? 1f : 0f);
    }
}
