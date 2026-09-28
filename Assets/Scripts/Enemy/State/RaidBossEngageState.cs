using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스 교전 로코모션 상태. 플레이어와의 거리에 따라 Run/Jog/Walk로 직진 접근하고,
    /// 사거리 안(AttackRange)에 들면 쿨다운이 찼을 때 공격 상태로 전환하고, 쿨다운 중이면 제자리에서 주시한다.
    /// 항상 플레이어를 바라본 채 이동해 8방향 로코모션(MoveX/MoveY)이 살아난다.
    /// 튜닝값은 <see cref="RaidBossLocomotion"/>에서 읽는다.
    /// </summary>
    public class RaidBossEngageState : EnemyBaseState
    {
        private RaidBossLocomotion config;

        public RaidBossEngageState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            config = enemy.GetComponent<RaidBossLocomotion>();
            enemy.Movement.SetAutoRotation(false);   // 회전은 코드가 소유(플레이어 바라보기)
            enemy.Movement.Resume();
        }

        public override void Exit()
        {
            // 다른 상태(순찰/발견 등)는 에이전트 자동 회전을 쓰므로 원복한다.
            enemy.Movement.SetAutoRotation(true);
        }

        public override void Update()
        {
            if (enemy.Target == null || config == null) return;

            Vector3 toPlayer = enemy.Target.position - enemy.transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            // --- 공격 판단 (사다리 밖, 독립): 사거리 안 + 시야면 이동하지 않는다 ---
            bool inRange = dist <= enemy.Combat.AttackRange && enemy.Combat.HasLineOfSight();
            if (inRange)
            {
                // 쿨다운이 찼으면 그 자리에서 공격
                if (enemy.Combat.CanAttack)
                {
                    enemy.Movement.Stop();
                    enemy.StateMachine.ChangeState(enemy.AttackState);
                    return;
                }

                // 쿨다운 중이면 제자리에서 주시만 한다. 이 분기가 없으면 아래 접근 로직으로 흘러
                // 사거리 안에서도 플레이어 발밑까지 계속 달라붙는다.
                // Stop()만 쓰면 관성으로 미끄러지므로 속도·경로까지 지운다(사거리를 벗어나면 아래에서 Resume으로 재개).
                enemy.Movement.StopAndClearPath();
                enemy.Movement.Face(enemy.Target.position);
                enemy.Animation.SetMove(0f, 0f);
                enemy.Animation.SetSpeed(0f);
                return;
            }

            // --- 이동 속도: 공격 사거리와 무관한 순수 거리 밴드 ---
            if (dist > config.JogDist) enemy.Movement.SetMoveSpeed(config.RunSpeed);
            else if (dist > config.EngageDist) enemy.Movement.SetMoveSpeed(config.JogSpeed);
            else enemy.Movement.SetMoveSpeed(config.WalkSpeed);

            enemy.Movement.Resume();
            enemy.Movement.SetDestination(enemy.Target.position);
            enemy.Movement.Face(enemy.Target.position);

            Vector3 d = enemy.Movement.LocalMoveDirection();
            enemy.Animation.SetMove(d.x, d.z);
            enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
        }
    }
}
