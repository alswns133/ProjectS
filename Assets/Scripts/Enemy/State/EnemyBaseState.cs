using ProjectS.Core;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 모든 몬스터 상태의 공통 베이스. Enemy 컨텍스트 참조를 보유하고,
    /// 생명주기 메서드의 빈 기본 구현을 제공한다.
    /// </summary>
    public abstract class EnemyBaseState : IState
    {

        protected readonly Enemy enemy;

        protected EnemyBaseState(Enemy enemy) => this.enemy = enemy;

        public virtual void Exit() { }

        public virtual void Update() { }

        public virtual void Enter() { }
    }
}
