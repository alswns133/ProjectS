using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 순찰 상태. 플레이어를 발견하기 전 몬스터의 기본 행동으로, 항상 Walk 모션으로 이동한다.
    /// 고정 순찰 지점(patrolPoints)이 있으면 그 지점들을 순환하고, 없으면 스폰 지점 주변을 랜덤 배회한다.
    /// 목적지에 도착하면 잠깐 Idle 모션으로 쉰 뒤 다음 목적지로 이동한다(동서남북 순찰 사이 대기 연출).
    /// 여러 마리가 뭉쳐 경로가 겹치면 도착 판정이 서지 않아 순찰이 멈추므로, 막힘 타임아웃으로
    /// 새 목적지를 다시 뽑아 대열이 자연히 풀리게 한다. 순찰 중에도 발견이 최우선이다.
    /// 목적지 데이터는 Enemy가 소유하고, 이 상태는 이동/대기/막힘 흐름만 관리한다.
    /// </summary>
    public class EnemyPatrolState : EnemyBaseState
    {
        // 도착 후 Idle 대기 경과 시간.
        private float waitTimer;
        // 현재 목적지로 이동을 시작한 뒤 경과 시간. 막힘(진척 없음) 판정에 쓴다.
        private float travelTimer;
        // 도착해 Idle로 쉬는 중인지. true면 이동하지 않고 대기 시간만 센다.
        private bool waiting;

        public EnemyPatrolState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            // 순찰 이동은 항상 Walk 속도. 추격(Run)과 애니메이션이 명확히 갈리도록 진입 때 못박는다.
            enemy.Movement.SetMoveSpeed(enemy.PatrolSpeed);

            waiting = false;
            waitTimer = 0f;
            PickNextDestination();
        }

        public override void Update()
        {
            // 순찰보다 발견이 우선이다.
            if (enemy.CanDetectTarget())
            {
                enemy.StateMachine.ChangeState(enemy.DetectState);
                return;
            }

            if (waiting)
            {
                // 도착 후 대기: 이동을 멈추고 Idle 모션. 이 구간이 있어야 순찰 지점 사이에 대기 모션이 한 번씩 나온다.
                enemy.Animation.SetSpeed(0f);

                waitTimer += Time.deltaTime;
                if (waitTimer < enemy.PatrolWaitTime) return;

                waiting = false;
                waitTimer = 0f;
                PickNextDestination();
                return;
            }

            travelTimer += Time.deltaTime;

            // 도착했거나(경로 끝) 막혀서 제 시간에 못 갔으면 → 대기 후 새 목적지.
            // 막힘 타임아웃이 핵심: 30마리가 서로 밀며 경로가 겹치면 remainingDistance가 안 줄어
            // ReachedPathEnd가 영영 성립하지 않는데, 이때 새 목적지로 흩어져 순찰이 멈추지 않게 한다.
            if (enemy.Movement.ReachedPathEnd || travelTimer >= enemy.PatrolStuckTimeout)
            {
                enemy.Movement.Stop();
                enemy.Animation.SetSpeed(0f);
                // 순찰 대기 중에는 고정 대기 모션을 쓴다(완전 IdleState가 아니라 랜덤 변경은 하지 않는다).
                enemy.Animation.SetIdleVariant(0);
                waiting = true;
                waitTimer = 0f;
                return;
            }

            enemy.Movement.Resume();
            enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
        }

        // 다음 순찰 목적지를 뽑아 이동을 시작한다. 막힘 판정용 타이머도 여기서 리셋한다.
        private void PickNextDestination()
        {
            travelTimer = 0f;
            enemy.Movement.Resume();
            enemy.Movement.SetDestination(enemy.GetPatrolDestination());
        }
    }
}
