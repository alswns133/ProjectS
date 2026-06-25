/// <summary>
/// 플레이어 상태 머신. 현재 상태 하나를 보유하고, 전환 시
/// 이전 상태의 Exit → 새 상태의 Enter 순서를 보장한다.
/// 매 프레임 갱신은 Player가 Update()로 위임한다.
/// </summary>
public class PlayerStateMachine
{
    // 현재 상태. 외부는 읽기만 가능(누가 무슨 상태인지 조회는 OK, 직접 교체는 금지).
    // 전환은 반드시 ChangeState를 거치게 해서 Exit/Enter 순서를 강제한다.
    public IState Current { get; private set; }

    /// <summary>현재 상태를 next로 전환한다(Exit→Enter 순서 보장).</summary>
    public void ChangeState(IState next)
    {
        // 가드 두 가지:
        // null  → 잘못된 전환 요청. 그냥 무시(예외 던지지 않고 안전하게 빠짐).
        // 같은 상태 → 재진입 막음. 안 막으면 Exit→Enter가 불필요하게 다시 돌아
        //            Enter에 든 연출/초기화가 매번 리셋되는 사고가 난다
        if (next == null || next == Current) return;

        Current?.Exit();    // 이전 상태 정리. 최초 전환 땐 Current가 null이라 ?. 로 건너뜀.
        Current = next;     // 상태 교체
        Current.Enter();    // 새 상태 진입 (여기선 next가 확정 non-null이라 ?. 불필요)
    }

    // 현재 상태의 Update를 대신 호출. Current가 아직 없으면(초기 프레임) 안전하게 무시.
    public void Update() => Current?.Update();
}