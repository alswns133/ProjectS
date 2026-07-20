using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 발견 상태. Idle/Patrol에서 플레이어를 처음 발견했을 때 1회 진입한다.
    /// 발견 연출과 짧은 대시를 처리한 뒤 ChaseState로 넘긴다.
    /// "발견했다"는 순간을 추격과 분리해, 인식 연출이 추격 로직에 묻히지 않게 한다.
    /// </summary>
    public class EnemyDetectState : EnemyBaseState
    {
        private float elapsed;

        public EnemyDetectState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;

            // 발견 순간의 연출과 돌진. 속도 배수는 Exit에서 반드시 원복한다.
            enemy.Animation.PlayDetect();
            enemy.Effects?.Play(EnemyEffects.EffectCue.Detect);
            enemy.Movement.SetSpeedMultiplier(enemy.DetectDashSpeedMultiplier);

            if (enemy.Target != null)
            {
                // 발견 대시는 플레이어 쪽으로 압박하는 연출이므로 목적지를 즉시 갱신한다.
                // 목적지는 Chase와 같은 분산 교전 지점을 쓴다 → 동시 발견 시 대시부터 한 점으로 몰리지 않는다.
                // Target이 없으면 애니메이션만 재생하고 duration 후 Chase로 넘어가며, Chase가 다시 null을 처리한다.
                enemy.Movement.Resume();
                enemy.Movement.Face(enemy.Target.position);
                enemy.Movement.SetDestination(enemy.GetChaseDestination());
            }
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 대시 중에도 목적지는 갱신해 플레이어 쪽으로 압박한다.
            if (enemy.Target != null)
            {
                enemy.Movement.SetDestination(enemy.GetChaseDestination());
                enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
            }

            // 연출이 끝나면 무조건 추적으로 넘긴다.
            if (elapsed >= enemy.DetectDuration)
                enemy.StateMachine.ChangeState(enemy.ChaseState);
        }

        public override void Exit()
        {
            // 발견 대시가 끝나면 이후 추격/공격은 NavMeshAgent 기본 속도로 돌아간다.
            // Exit가 전환 경로마다 호출되므로, Hit/Dead 등 다른 상태로 끊겨도 속도가 남지 않는다.
            enemy.Movement.ResetSpeed();
        }
    }
}
