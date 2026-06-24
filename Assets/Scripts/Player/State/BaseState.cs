/// <summary>
/// 모든 플레이어 상태의 공통 베이스. Player 컨텍스트 참조를 보유하고,
/// 생명주기 메서드(Enter/Update/Exit)의 빈 기본 구현을 제공한다.
/// 자식 상태는 필요한 메서드만 override 한다.
/// </summary>
public abstract class BaseState : IState
{
    // 상태가 컴포넌트(Movement, Animation 등)에 닿기 위한 통로.
    // protected: 자식 상태에서만 접근. readonly: 생성 시 1회 주입 후 불변.
    protected readonly Player player;

    // 받은 player를 부모(여기)가 보관. 자식은 : base(player)로 이 생성자에 위임한다.
    protected BaseState(Player player) => this.player = player;

    // virtual + 빈 본문: 자식이 "필요한 것만" override 하게 하는 장치.
    // 안 그러면 모든 상태가 안 쓰는 메서드까지 빈 {}로 강제 구현해야 한다.
    public virtual void Enter() { }
    public virtual void Update() { }
    public virtual void Exit() { }
}
