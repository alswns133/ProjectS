using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스 교전 로코모션 상태. 플레이어와의 거리에 따라 멀면 Run/Jog로 접근,
    /// 교전권에선 좌우로 도는 "간보기"(strafe), 사거리 안이면 정지(Idle)한다.
    /// 항상 플레이어를 바라본 채 이동해 8방향 로코모션(MoveX/MoveY)이 살아난다.
    /// 튜닝값은 <see cref="RaidBossLocomotion"/>에서 읽는다. (공격/특수 전이는 이후 단계)
    /// </summary>
    public class RaidBossEngageState : EnemyBaseState
    {
        private RaidBossLocomotion config;
        private float strafeSide = 1f;   // +1 오른쪽, -1 왼쪽
        private float flipTimer;

        public RaidBossEngageState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            config = enemy.GetComponent<RaidBossLocomotion>();
            enemy.Movement.SetAutoRotation(false);   // 회전은 코드가 소유(플레이어 바라보기)
            enemy.Movement.Resume();
            if (config != null) ResetFlipTimer();
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
            Vector3 dirToPlayer = dist > 0.01f ? toPlayer / dist : enemy.transform.forward;

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
            else if (dist > config.AttackRange)     // 교전권 → 좌우 간보기(Walk)
            {
                enemy.Movement.SetMoveSpeed(config.WalkSpeed);
                enemy.Movement.Resume();

                Vector3 tangent = Vector3.Cross(Vector3.up, dirToPlayer);                    // 오른쪽
                Vector3 orbit = enemy.Target.position - dirToPlayer * config.StrafeRadius;    // 반경 유지 지점
                Vector3 dest = orbit + tangent * strafeSide * config.StrafeLookahead;
                enemy.Movement.SetDestination(dest);

                flipTimer -= Time.deltaTime;
                if (flipTimer <= 0f) { strafeSide = -strafeSide; ResetFlipTimer(); }
            }
            else                                     // 사거리 안 → 정지(Idle)
            {
                enemy.Movement.Stop();
            }

            // --- 항상 플레이어 바라보기 ---
            enemy.Movement.Face(enemy.Target.position);

            // --- 애니메이터: 방향 → MoveX/MoveY, 속력 → Speed ---
            Vector3 d = enemy.Movement.LocalMoveDirection();
            enemy.Animation.SetMove(d.x, d.z);
            enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
        }

        private void ResetFlipTimer()
            => flipTimer = Random.Range(config.FlipInterval.x, config.FlipInterval.y);
    }
}
