using UnityEngine;
/// <summary>
/// 공격상태. 공격범위를 벗어나고 추적 거리에 안에 있다면 추적상태로 변환
/// </summary>
public class EnemyAttackState : EnemyBaseState
{
    public EnemyAttackState(Enemy enemy) : base(enemy) { }
}
