using UnityEngine;

/// <summary>
/// 순찰 상태. Enemy 인스펙터에 Patrol Points가 있을 때 시작 상태로 사용된다.
/// 순찰 중에도 감지 반경 안에 플레이어가 들어오면 DetectState로 전환한다.
/// 순찰 지점 데이터는 Enemy가 소유하고, 이 상태는 현재 인덱스와 도착 대기 시간만 관리한다.
/// </summary>
public class EnemyPatrolState : EnemyBaseState
{
    private int pointIndex;
    private float waitTimer;

    public EnemyPatrolState(Enemy enemy) : base(enemy) { }

    public override void Enter()
    {
        waitTimer = 0f;
        // 현재 pointIndex 목적지로 이동 시작. pointIndex는 상태가 재진입해도 이어져 자연스럽게 순찰한다.
        enemy.Movement.Resume();
        enemy.Movement.SetDestination(enemy.GetPatrolPoint(pointIndex));
    }

    public override void Update()
    {
        // 순찰보다 발견이 우선이다.
        if (enemy.CanDetectTarget())
        {
            enemy.StateMachine.ChangeState(enemy.DetectState);
            return;
        }

        if (!enemy.HasPatrol)
        {
            // 런타임에 순찰 지점이 비워져도 안전하게 Idle로 폴백한다.
            enemy.StateMachine.ChangeState(enemy.IdleState);
            return;
        }

        if (enemy.Movement.ReachedPathEnd)
        {
            // 지점에 도착하면 잠깐 대기한 뒤 다음 지점으로 이동한다.
            enemy.Movement.Stop();
            enemy.Animation.SetSpeed(0f);
            // 순찰 지점 대기 중에는 고정 대기 모션을 쓴다. 완전 IdleState가 아니므로 랜덤 변경은 하지 않는다.
            enemy.Animation.SetIdleVariant(0);

            waitTimer += Time.deltaTime;
            if (waitTimer < enemy.PatrolWaitTime) return;

            waitTimer = 0f;
            pointIndex = (pointIndex + 1) % enemy.PatrolPointCount;
            enemy.Movement.Resume();
            enemy.Movement.SetDestination(enemy.GetPatrolPoint(pointIndex));
            return;
        }

        enemy.Movement.Resume();
        // NavMeshAgent 속도를 그대로 애니메이션 블렌드에 넘긴다.
        // 실제 걷기/달리기 임계값은 Animator Controller 쪽에서 조정한다.
        enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
    }
}
