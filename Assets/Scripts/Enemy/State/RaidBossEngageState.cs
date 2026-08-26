using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스 교전 로코모션 상태. 플레이어와의 거리에 따라 Run/Jog/Walk로 직진 접근하고,
    /// 사거리 안(AttackRange)에 들면 쿨다운이 찼을 때 공격 상태로 전환한다.
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

            // --- 거리 밴드로 속도/목적지 결정 ---
            if (dist > config.JogDist)              // 아주 멀다 → Run 직진
            {
                enemy.Movement.SetMoveSpeed(config.RunSpeed);
                enemy.Movement.Resume();
                enemy.Movement.SetDestination(enemy.Target.position);
            }
            else if (dist > config.EngageDist)      // 멀다 → Jog 접근
            {
                enemy.Movement.SetMoveSpeed(config.JogSpeed);
                enemy.Movement.Resume();
                enemy.Movement.SetDestination(enemy.Target.position);
            }
            else if (dist > config.AttackRange)     // 교전권 → 걸어서 접근
            {
                enemy.Movement.SetMoveSpeed(config.WalkSpeed);
                enemy.Movement.Resume();
                enemy.Movement.SetDestination(enemy.Target.position);
            }
            else // 사거리 안 → 공격 시도, 아니면 정지
            {
                enemy.Movement.Stop();

                // 이 밴드(dist <= config.AttackRange) 자체가 이미 사거리 게이트라
                // ChaseState처럼 거리 재검사는 불필요. 쿨다운·시야만 확인하고 넘긴다.
                if (enemy.Combat.CanAttack && enemy.Combat.HasLineOfSight())
                {
                    enemy.StateMachine.ChangeState(enemy.AttackState);
                    return; // 상태가 바뀌었으니 이 프레임의 나머지 로코모션 갱신(Face/SetMove/SetSpeed)은 건너뛴다
                }
            }

            // --- 항상 플레이어 바라보기 ---
            enemy.Movement.Face(enemy.Target.position);

            // --- 애니메이터: 방향 → MoveX/MoveY, 속력 → Speed ---
            Vector3 d = enemy.Movement.LocalMoveDirection();
            enemy.Animation.SetMove(d.x, d.z);
            enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
        }
    }
}
