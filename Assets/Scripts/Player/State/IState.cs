/// <summary>
/// 상태가 지켜야 할 계약. 생명주기 메서드(Enter/Update/Exit)만 정의하며,
/// 구현은 갖지 않는다. StateMachine은 이 인터페이스에만 의존하므로,
/// BaseState를 거치지 않는 상태도 이 계약만 지키면 그대로 사용할 수 있다.
/// </summary>
public interface IState
{
    /// <summary>상태 진입 시 1회 호출. 초기화·연출 시작 등.</summary>
    void Enter();
    /// <summary>상태가 활성인 동안 매 프레임 호출. 입력 처리·전환 판단.</summary>
    void Update();
    /// <summary>상태 이탈 시 1회 호출. 정리·연출 종료 등.</summary>
    void Exit();
}
