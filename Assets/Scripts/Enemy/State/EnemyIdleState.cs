using UnityEngine;

/// <summary>
/// 대기 상태. 감지 반경 안에 플레이어가 들어오면 추적 상태변환
/// </summary>
public class EnemyIdleState : EnemyBaseState
{
    public EnemyIdleState(Enemy enemy) : base(enemy) { }
}
