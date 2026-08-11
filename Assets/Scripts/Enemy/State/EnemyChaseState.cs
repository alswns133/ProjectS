using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// Chase state. Switches to attack when the target enters attack range.
    /// Once aggro is acquired, this state does not return to idle by itself.
    /// </summary>
    public class EnemyChaseState : EnemyBaseState
    {
        public EnemyChaseState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            enemy.Movement.SetMoveSpeed(enemy.ChaseSpeed);
            enemy.Movement.SetAutoRotation(false);   // 추격 동안 회전은 코드가 소유(회전-먼저-이동)
            enemy.Movement.Resume();
        }

        public override void Exit()
        {
            // 추격을 벗어나면 자동 회전을 원복한다. 순찰/발견은 에이전트 자동 회전을 그대로 쓴다.
            enemy.Movement.SetAutoRotation(true);
        }

        public override void Update()
        {
            if (enemy.Target == null) return;

            // 사거리 안이어도 벽에 가려 보이지 않으면 공격하지 않는다.
            // 원거리 몬스터가 엄폐물 너머로 쏘는 것을 막는 판정으로, 막혀 있으면 아래 추격 로직으로 흘러
            // 보이는 자리까지 파고든다(후퇴/거리 유지는 기획에서 제외).
            bool canSee = enemy.Combat.HasLineOfSight();

            float distance = enemy.DistanceToTarget();
            if (canSee && distance <= enemy.Combat.AttackRange)
            {
                if (enemy.Combat.CanAttack)
                {
                    enemy.StateMachine.ChangeState(enemy.AttackState);
                    return;
                }

                // Wait in place during attack cooldown, but keep facing the target.
                enemy.Movement.Stop();
                enemy.Movement.Face(enemy.Target.position);
                enemy.Animation.SetSpeed(0f);
                return;
            }

            if (!enemy.CanReachTarget())
            {
                enemy.Movement.Stop();
                enemy.Movement.Face(enemy.Target.position);
                enemy.Animation.SetSpeedImmediate(0f);
                return;
            }

            enemy.Movement.Resume();

            // 플레이어 위치가 아니라 개체별로 분산된 교전 지점으로 이동한다(군중 제어).
            // 교전 거리가 AttackRange보다 짧게 잡히므로 도착 전에 위의 공격 전환 판정에 걸린다.
            // 단 시야가 막혔을 때는 타겟 자체를 목적지로 삼는다 — 분산 교전 지점은 사거리 기준이라
            // 원거리 몬스터는 이미 그 거리에 서 있어 목적지에 도착한 채로 벽만 보고 멈춘다.
            enemy.Movement.SetDestination(canSee ? enemy.GetChaseDestination() : enemy.Target.position);

            // 많이 틀어져 있으면 제자리에서 먼저 돌고(정지 모션), 정렬되면 이동(이동 모션).
            bool turning = enemy.Movement.MoveWithTurnGate();
            enemy.Animation.SetSpeed(turning ? 0f : enemy.Movement.CurrentSpeed);
        }
    }
}
