using UnityEngine;

namespace ProjectS.Players
{
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
            // 공격·스킬 중이면 수평 이동을 막는다.
            // 단, Move(zero)로 중력·접지 처리는 유지 → 제자리에서 공격/낙하는 정상 동작.
            if (player.IsMovementLocked)
            {
                // actionLocked: true → 원본처럼 이 잠금 경로에서만 올려치기 상승 억제가 걸린다.
                player.Movement.Move(Vector2.zero, false, true);
                player.Animation.SetForward(0f);

                // ★ 잠금 중에도 이동 '의도'는 로코모션 bool로 계속 전달한다(자유 이동 컨트롤러).
                // false로 고정하면 공격 종료(End) State에서 End→Walk_Loop(isMoving=true) 전이가
                // 절대 안 걸려, 방향키를 쥔 채 착지해도 Idle로 새어버린다. 공격 State는 별도라
                // isMoving=true여도 걷기로 새지 않고, 오직 End의 전이 분기만 이 값을 읽는다.
                Vector2 lockedInput = player.Input.MoveInput;
                bool lockedMoving = lockedInput.sqrMagnitude > 0.0001f;
                player.Animation.SetLocomotion(lockedMoving, lockedMoving && player.Input.IsRunning);
                return;
            }

            Vector2 input = player.Input.MoveInput;
            bool isRunning = player.Input.IsRunning;

            // 이동 + 진행 방향으로의 회전까지 Movement가 함께 처리한다.
            player.Movement.Move(input, isRunning);

            // 입력 크기로 이동 여부를 판단해 애니메이터 전진량(Z)에 전달.
            // sqrMagnitude 사용: 실제 거리(magnitude)는 제곱근 연산이라,
            // "움직이는가?"만 볼 때는 제곱값 비교가 더 싸다.
            // Z 규약: 정지 0 / 걷기 0.5 / 달리기 1. 컨트롤러 블렌드 트리 임계값과 맞춘 값이다.
            bool isMoving = input.sqrMagnitude > 0.0001f;
            player.Animation.SetForward(isMoving ? (isRunning ? 1f : 0.5f) : 0f);

            // 3단 로코모션 컨트롤러용 bool 규약. Z와 같은 정보를 전달하지만 표현이 달라서 함께 보낸다
            // (자세한 이유는 PlayerAnimation.SetLocomotion 주석 참고).
            // 달리기는 이동 중일 때만 켠다 → 멈춘 프레임에 Run 계열로 새는 전이를 원천 차단.
            player.Animation.SetLocomotion(isMoving, isMoving && isRunning);
        }
    }
}
