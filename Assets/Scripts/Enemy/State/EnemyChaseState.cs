using UnityEngine;

/// <summary>
/// 추적상태. 공격 사거리에 들어오면 공격 상태변환, 추적 거리에 벗어났다면 대기 상태변환
///</summary>

public class EnemyChaseState : EnemyBaseState
{
    public EnemyChaseState(Enemy enemy) : base(enemy) { }
}
