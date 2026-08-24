using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 발견 상태. Idle/Patrol에서 플레이어를 처음 발견했을 때 1회 진입한다.
    /// 발견 연출을 재생한 뒤 ChaseState로 넘긴다. 이동은 NavMesh 추적이 아니라
    /// 발견 클립 자체의 루트모션으로 처리한다 — 발견 모션이 앞으로 나아가는 연출이라,
    /// NavMesh로 플레이어를 쫓으면 모션 도는 내내 몸이 플레이어에 붙어버리기 때문이다.
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

        public EnemyDetectState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;
            enteredDetect = false;

            enemy.Animation.PlayDetect();
            // 발견 이펙트는 발견 클립의 Animation Event(OnEffect)가 재생한다(클립 주도).

            // 순찰·추격에서 넘어올 때 남은 경로/속도로 끌려가지 않게 끊는다.
            enemy.Movement.StopAndClearPath();
            enemy.Animation.SetSpeedImmediate(0f);

            // 발견 순간 플레이어를 쳐다본다(수평 방향만). Target이 없으면 애니메이션만 재생한다.
            if (enemy.Target != null)
                enemy.Movement.Face(enemy.Target.position);

            // 발견 클립의 전방 루트모션을 위치에 반영한다(공격 대쉬와 같은 경로, NavMesh 클램프 유지).
            // 플레이어를 '추적'하지 않고 클립이 밀어주는 만큼만 전진 → 모션 중 플레이어에 붙지 않는다.
            enemy.Movement.BeginAttackRootMotion();
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 1단계: 발견 클립에 진입할 때까지 기다린다(전환이 끝나고 "Detect" 태그 상태가 올라올 때까지).
            if (!enteredDetect)
            {
                if (enemy.Animation.IsPlaying("Detect"))
                    enteredDetect = true;
                else if (elapsed >= EnterTimeout)
                    //enemy.StateMachine.ChangeState(enemy.ChaseState);
                    enemy.StateMachine.ChangeState(enemy.AggroState);

                return;
            }

            // 2단계: 발견 클립이 끝까지 재생되면(또는 안전 상한을 넘기면) 추적으로 넘긴다.
            if (enemy.Animation.IsCurrentStateFinished() || elapsed >= MaxDetectTime)
                //enemy.StateMachine.ChangeState(enemy.ChaseState);
                enemy.StateMachine.ChangeState(enemy.AggroState);
        }

        public override void Exit()
        {
            // 루트모션 전진을 끈다. 피격/사망 등으로 발견이 중간에 끊겨도 이후 클립에 이동량이 새지 않게.
            enemy.Movement.EndAttackRootMotion();
        }
    }
}
