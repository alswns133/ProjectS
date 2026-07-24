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
        // 발견 클립에 실제로 진입했는지. 진입 전(전환 중/이전 로코모션)에는 종료 판정을 하지 않는다.
        // 안 그러면 트리거를 켠 첫 프레임에 로코모션의 normalizedTime을 보고 즉시 빠져나간다.
        private bool enteredDetect;

        // 안전장치용 경과 시간. 클립 종료는 애니메이터에서 읽지만, 태그 누락 등으로 판정이
        // 영영 성립하지 않을 때 상태가 멈추지 않게 강제 진행 타임아웃을 함께 둔다.
        private float elapsed;

        // 진입 감지 실패(발견 상태에 못 들어감) 시 강제로 Chase로 넘기는 시간. 전환 시간보다 넉넉하게.
        private const float EnterTimeout = 1f;

        // 종료 감지 실패(클립이 루프이거나 태그 누락 등) 시 강제로 넘기는 상한. 정상 발견 연출보다 길게.
        private const float MaxDetectTime = 5f;

        // 이번 발견에서 대시를 할지. Enter에서 한 번 정하고 상태가 끝날 때까지 유지한다.
        // 매 프레임 다시 판정하면 사거리 경계에서 대시가 켜졌다 꺼졌다 하며 몸이 떤다.
        private bool isDashing;

        public EnemyDetectState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;
            enteredDetect = false;

            // 발견 연출은 대시 여부와 무관하게 항상 재생한다.
            enemy.Animation.PlayDetect();
            enemy.Effects?.Play(EnemyEffects.EffectCue.Detect);

            // Target이 없으면 애니메이션만 재생하고 duration 후 Chase로 넘어가며, Chase가 다시 null을 처리한다.
            if (enemy.Target == null)
            {
                isDashing = false;
                return;
            }

            // 발견했으면 일단 쳐다본다. 이것도 대시 여부와 무관하다.
            enemy.Movement.Face(enemy.Target.position);

            // 대시 여부는 두 가지를 함께 본다.
            // 1) UseDetectDash: 발견 모션이 달려드는 연출인지. 애니메이션과 한 몸인 몬스터별 설정이다.
            //    원거리 몬스터는 발견 즉시 제자리에서 겨누는 모션이라, 이동하면 애니메이션과 따로 논다.
            // 2) 이미 공격 사거리 안이면 대시하지 않는다. 대시 목적지(GetChaseDestination)는
            //    AttackRange 기준이라 사거리가 긴 몬스터는 그 지점이 자기 뒤쪽이 되어 뒷걸음질 친다.
            //    설정을 켜 둔 채 사거리가 긴 몬스터를 만들었을 때를 위한 안전장치다.
            isDashing = enemy.UseDetectDash && enemy.DistanceToTarget() > enemy.Combat.AttackRange;

            if (!isDashing)
            {
                // 이전 상태(순찰 등)의 경로가 남아 있으면 Chase가 Resume할 때 그쪽으로 끌려간다.
                enemy.Movement.StopAndClearPath();
                enemy.Animation.SetSpeedImmediate(0f);
                return;
            }

            // 발견 대시는 플레이어 쪽으로 압박하는 연출이므로 목적지를 즉시 갱신한다.
            // 목적지는 Chase와 같은 분산 교전 지점을 쓴다 → 동시 발견 시 대시부터 한 점으로 몰리지 않는다.
            // 속도 배수는 Exit에서 반드시 원복한다.
            enemy.Movement.SetSpeedMultiplier(enemy.DetectDashSpeedMultiplier);
            enemy.Movement.Resume();
            enemy.Movement.SetDestination(enemy.GetChaseDestination());
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 대시 중에만 목적지를 갱신해 플레이어 쪽으로 압박한다.
            // 대시를 건너뛴 경우는 제자리에서 발견 모션만 재생한다.
            if (isDashing && enemy.Target != null)
            {
                enemy.Movement.SetDestination(enemy.GetChaseDestination());
                enemy.Animation.SetSpeed(enemy.Movement.CurrentSpeed);
            }

            // 1단계: 발견 클립에 진입할 때까지 기다린다(전환이 끝나고 "Detect" 태그 상태가 올라올 때까지).
            if (!enteredDetect)
            {
                if (enemy.Animation.IsPlaying("Detect"))
                    enteredDetect = true;
                else if (elapsed >= EnterTimeout)
                    enemy.StateMachine.ChangeState(enemy.ChaseState);

                return;
            }

            // 2단계: 발견 클립이 끝까지 재생되면(또는 안전 상한을 넘기면) 추적으로 넘긴다.
            // 원거리 조우 사격은 이 클립에 OnDetectHit 이벤트가 여러 개 있어, 클립이 끝날 때까지
            // 상태가 유지돼야 모든 발이 나간다 — 애니메이터 종료 판정이 그걸 정확히 맞춘다.
            if (enemy.Animation.IsCurrentStateFinished() || elapsed >= MaxDetectTime)
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
