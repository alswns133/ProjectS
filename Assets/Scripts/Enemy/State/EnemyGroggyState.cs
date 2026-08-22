using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 무력화(그로기) 상태. <see cref="EnemyGroggy"/>의 게이지가 0이 되면 <see cref="Enemy.EnterGroggy"/>를 통해 진입한다.
    /// 무력화 모션을 재생하고 <see cref="Enemy.GroggyDuration"/>초 동안 이동·AI·공격을 멈춘 채 가만히 있다가,
    /// 시간이 끝나면 그로기 게이지를 리필하고 다시 전투 흐름(Chase)으로 복귀한다.
    ///
    /// 데미지 적용 자체는 이 상태와 무관하다(무력화 중에도 EnemyStats.TakeDamage가 그대로 피해를 넣는다) —
    /// 이 상태는 "행동 정지 + 무방비" 연출만 담당한다. 보스는 보통 슈퍼아머(useHitStun=off)라 무력화 중 피격으로
    /// 끊기지 않고, <see cref="Enemy.OnDamaged"/>도 이 상태에서는 진입을 건너뛴다(약몹 오작동 방지 가드).
    /// </summary>
    public class EnemyGroggyState : EnemyBaseState
    {
        private float elapsed;

        public EnemyGroggyState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;

            // 이동·추격을 즉시 멈추고 무력화 모션으로 갈아탄다(Speed를 감쇠 없이 0으로 끊는다).
            enemy.Movement.Stop();
            enemy.Animation.SetSpeedImmediate(0f);
            enemy.Animation.PlayGroggy();

            // 진행 중이던 오라·장판 잔상을 걷어낸다(무방비 연출이 지속 이펙트에 묻히지 않게).
            enemy.Effects?.StopAll();
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed < enemy.GroggyDuration) return;

            // 무력화 종료: 게이지를 되돌리고(다시 그로기 가능) 전투 흐름으로 복귀한다.
            // Refill이 그로기 변화 이벤트를 발행해 UI 바도 다시 차오른다.
            enemy.Groggy?.Refill();
            enemy.StateMachine.ChangeState(enemy.ChaseState);
        }

        // 무력화에서 빠져나갈 때 isGroggy를 내려 애니메이터가 로코모션으로 복귀하게 한다
        // (Groggy→로코모션 전이 조건). ChangeState가 새 상태 Enter 전에 이 Exit를 먼저 부른다.
        public override void Exit() => enemy.Animation.EndGroggy();
    }
}
