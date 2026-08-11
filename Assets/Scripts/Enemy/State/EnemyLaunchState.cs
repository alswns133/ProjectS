using System.Timers;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 공중 런처 피격 상태. 하루 강공격의 Launch() 신호로 진입한다.
    /// NavMeshAgent를 끄고 공중 피격 클립(Hit_Air)의 루트모션으로 몸을 띄웠다가,
    /// 다시 '땅에 닿으면'(모션이 남아 있어도) NavMesh로 복귀시키고 Chase로 돌아간다.
    /// 클립 종료가 아니라 착지 판정으로 빠져나오는 이유: 착지하는 순간 다음 강공격에 곧바로
    /// 다시 떠오를 수 있어야 하기 때문(재런처 저글링). 지상으로 돌아가면 Chase가 되어
    /// Enemy.Launch의 'LaunchState 중엔 금지' 가드가 풀린다.
    /// </summary>
    public class EnemyLaunchState : EnemyBaseState
    {
        // 착지 판정 기준이 되는 시작(지면) 높이. Enter에서 캡처한다.
        private float groundY;

        // 실제로 떠올랐는지. 진입 직후(아직 지면 높이)에 곧바로 착지로 오판하지 않게 한다.
        private bool hasLiftedOff;

        // 안전장치용 경과 시간. 지면 판정이 성립 안 해도 영영 공중에 갇히지 않게 한다.
        private float elapsed;

        // 떠오름/착지로 인정하는 높이 문턱.
        private const float LiftoffHeight = 0.15f;
        private const float GroundHeight = 0.05f;

        // 판정 실패(루트모션 이상 등) 시 강제 착지 상한.
        private const float MaxAirTime = 5f;

        public EnemyLaunchState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;
            hasLiftedOff = false;
            groundY = enemy.transform.position.y;

            // 에이전트를 끄고 위치를 루트모션에 넘긴 뒤, 공중 피격 클립을 재생한다.
            enemy.Movement.BeginRootMotion();
            enemy.Animation.PlayHitAir();
            enemy.Effects?.Play(EnemyEffects.EffectCue.Hit);
        }

        /// <summary>
        /// 이미 공중 상태일 때 다시 강공격을 맞으면 상승을 처음부터 재시작
        /// 새 상태 진입이 아니므로 클립만 처음부터 다시 재생하고 착지 판정 타이머만 초기화
        /// 기준 지면 높이는 처음 값을 유지하여 몇 번을 다시 띄워도 결국 원래 바닥으로 내려와 착지 판정이 성립
        /// </summary>
        public void Relaunch()
        {
            elapsed = 0f;
            hasLiftedOff = false;
            enemy.Animation.PlayHitAir(); // Can Transition To Self 때문에 Hit_Air를 처음부터 다시
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            float height = enemy.transform.position.y - groundY;

            // 1단계: 충분히 떠올랐는지 먼저 확인(진입 직후 지면 높이에서 즉시 착지로 오판 방지).
            if (!hasLiftedOff)
            {
                if (height >= LiftoffHeight) hasLiftedOff = true;
                else if (elapsed >= MaxAirTime) Land();   // 안전장치: 못 떠오르면 그냥 복귀
                return;
            }

            // 2단계: 떠오른 뒤 다시 땅에 닿으면(또는 안전 상한) 착지 → Chase 복귀.
            if (height <= GroundHeight || elapsed >= MaxAirTime)
                Land();
        }

        private void Land()
        {
            enemy.Movement.EndRootMotionAndLand();
            enemy.StateMachine.ChangeState(enemy.ChaseState);
        }
    }
}
