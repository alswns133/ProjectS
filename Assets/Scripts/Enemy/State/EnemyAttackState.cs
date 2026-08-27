using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 공격 상태. 진입 시 현재 거리에서 가능한 공격 패턴 하나를 고르고 공격 애니메이션을 재생한다.
    /// 실제 데미지 판정은 공격 클립의 Animation Event(EnemyCombat.OnAttackHit)가 담당한다.
    /// 공격이 시작되면 방향과 쿨다운을 확정하는 '커밋' 상태라, 모션 중에는 플레이어를 계속 따라 돌지 않는다.
    /// </summary>
    public class EnemyAttackState : EnemyBaseState
    {
        // 공격 클립에 실제로 진입했는지. 진입 전(전환 중/이전 로코모션)에는 종료 판정을 하지 않는다.
        // 안 그러면 트리거를 켠 첫 프레임에 로코모션의 normalizedTime을 보고 즉시 빠져나간다.
        private bool enteredAttack;

        // 안전장치용 경과 시간. 클립 종료는 애니메이터에서 읽지만, 태그 누락 등으로 판정이
        // 영영 성립하지 않을 때 상태가 멈추지 않게 강제 진행 타임아웃을 함께 둔다.
        private float elapsed;

        // 이번 공격이 돌진(Charge)인지. 진입 시 Combat에서 읽어, 전방 루트모션 전진과 슬라이드 연속 히트를
        // 이 공격 동안만 굴린다. Exit에서 루트모션을 되돌릴 때도 이 플래그로 "켰던 것만 끈다".
        private bool isCharging;

        // 진입 감지 실패(공격 상태에 못 들어감) 시 강제로 Chase로 넘기는 시간. 전환 시간보다 넉넉하게.
        private const float EnterTimeout = 1f;

        // 종료 감지 실패(클립이 루프이거나 태그 누락 등) 시 강제로 넘기는 상한. 정상 공격 길이보다 길게.
        private const float MaxAttackTime = 15f;

        public EnemyAttackState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;
            enteredAttack = false;

            // 공격 커밋: 시작 순간 대상을 바라본 방향으로 고정한다.
            // 모션 중 계속 추적하지 않아 플레이어의 위치 이동이 의미 있게 작동한다.
            // 경로까지 지우는 이유: Stop()만 하면 공격 전 목적지가 남아, 공격이 끝나고
            // ChaseState.Enter가 Resume하는 순간 옛 목적지로 한 프레임 끌려간다
            // (사격 후 제자리여야 하는데 몸이 툭 밀리는 원인). 공격이 끝나면 Chase가 거리를 다시 판단한다.
            enemy.Movement.StopAndClearPath();
            enemy.Animation.SetSpeedImmediate(0f);
            if (enemy.Target != null) enemy.Movement.Face(enemy.Target.position);

            // 공격 선택과 쿨다운 소모는 Combat가 소유한다.
            // 상태는 "공격 시작"을 요청하고, 선택된 공격 번호만 Animation에 넘긴다.
            // 돌진 여부는 BeginAttack이 공격을 고른 뒤에야 알 수 있으므로 그 다음에 읽는다.
            enemy.Combat.BeginAttack(enemy.DistanceToTarget());
            enemy.Animation.PlayAttack(enemy.Combat.CurrentAttackIndex);
            // 공격 이펙트는 공격 클립의 Animation Event(OnEffect)가 실제 타격 프레임에 재생한다(클립 주도).

            // 돌진 슬롯이면 이 공격 동안만 전방 루트모션을 켜, 돌진 클립의 전진을 실제 위치에 반영한다.
            // 제자리 공격에는 켜지 않아 에이전트를 끄지 않는다(불필요한 NavMesh 이탈 방지).
            isCharging = enemy.Combat.IsCurrentAttackCharge;
            if (isCharging) enemy.Movement.BeginAttackRootMotion();
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 돌진 슬라이드 히트: 창이 열려 있는 동안(OnChargeHitBegin~OnChargeHitEnd)만 실제 판정한다.
            // 창이 닫혀 있으면 UpdateChargeHit가 즉시 반환하므로, 돌진 공격에서 매 프레임 호출해도 안전하다.
            if (isCharging) enemy.Combat.UpdateChargeHit();

            // 조준 유지 공격(원거리 등)은 히트 프레임까지 대상을 계속 바라본다.
            // 이게 없으면 조준 모션이 긴 공격에서 시작 시점의 방향으로 쏘게 되어
            // 플레이어가 이미 떠난 자리를 노린다. 커밋이 기본이고 이쪽이 예외다.
            if (enemy.Combat.ShouldTrackTarget && enemy.Target != null)
                enemy.Movement.Face(enemy.Target.position);

            // 1단계: 공격 클립에 진입할 때까지 기다린다(전환이 끝나고 "Attack" 태그 상태가 올라올 때까지).
            if (!enteredAttack)
            {
                if (enemy.Animation.IsPlaying("Attack"))
                    enteredAttack = true;
                else if (elapsed >= EnterTimeout)
                    enemy.StateMachine.ChangeState(enemy.AggroState);

                return;
            }

            // 2단계: 클립이 끝까지 재생되면(또는 안전 상한을 넘기면) Chase로 돌아가 거리/쿨타임을 다시 판단한다.
            // 클립 길이를 인스펙터에 적지 않고 애니메이터 normalizedTime으로 종료를 판정한다.
            if (enemy.Animation.IsCurrentStateFinished() || elapsed >= MaxAttackTime)
                enemy.StateMachine.ChangeState(enemy.AggroState);
        }

        public override void Exit()
        {
            // 돌진이었으면 전방 루트모션과 히트 창을 되돌린다. 피격/사망 등으로 공격이 중간에 끊겨도
            // 루트모션 전진이나 열린 히트 창이 다음 상태로 새지 않게 한다(창은 이 공격 한정이어야 한다).
            if (isCharging)
            {
                enemy.Movement.EndAttackRootMotion();
                enemy.Combat.OnChargeHitEnd();
                isCharging = false;
            }
        }
    }
}
