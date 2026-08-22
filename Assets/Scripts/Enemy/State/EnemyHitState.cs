using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 피격 상태. EnemyStats가 데미지를 받은 뒤 Enemy.OnDamaged를 통해 진입한다.
    /// 일반 몬스터의 짧은 경직용이며, 보스/슈퍼아머 몬스터는 Enemy 인스펙터에서 끌 수 있다.
    /// 데미지 적용 자체는 Stats가 이미 끝낸 뒤이므로, 이 상태는 연출/행동 중단만 담당한다.
    /// </summary>
    public class EnemyHitState : EnemyBaseState
    {
        private float elapsed;

        public EnemyHitState(Enemy enemy) : base(enemy) { }

        public override void Enter() => PlayHitStun();

        /// <summary>
        /// 이미 피격 중일 때 또 맞으면 경직을 처음부터 다시 시작한다.
        /// 상태 머신이 같은 상태 재진입(Enter)을 막으므로 LaunchState.Relaunch처럼
        /// 상태 내부에서 우회로로 재생·타이머만 초기화한다.
        /// </summary>
        public void Rehit() => PlayHitStun();

        private void PlayHitStun()
        {
            elapsed = 0f;                              // 경직 타이머 리셋 → 맞을 때마다 경직 연장
            enemy.Movement.Stop();
            enemy.Animation.SetSpeed(0f);
            enemy.Animation.PlayHit();                 // 이제 animator.Play라 매번 0프레임부터 확실히 재생
            // 피격 이펙트 재생은 피격 클립의 Animation Event(OnEffect)가 맡는다. 여기서는 반대로,
            // 보스가 공격 중 경직당할 때 진행 중이던 오라·장판 잔상을 걷어낸다(잡몹은 지속 이펙트가 없어 무해).
            enemy.Effects?.StopAll();
        }

        //public override void Enter()
        //{
        //    elapsed = 0f;

        //    // 피격 중에는 이동을 멈추고 피격 모션만 재생한다.
        //    // 공격 도중 맞으면 현재 공격 상태를 끊는 인터럽트 역할도 한다.
        //    enemy.Movement.Stop();
        //    enemy.Animation.SetSpeed(0f);
        //    enemy.Animation.PlayHit();
        //    enemy.Effects?.StopAll();
        //}

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // 경직 시간(HitStunDuration + HitRecoveryDelay)이 끝나면 다시 Chase로 돌아가 전투 흐름을 이어간다.
            if (elapsed >= enemy.HitStunDuration + enemy.HitRecoveryDelay)
                enemy.StateMachine.ChangeState(enemy.ChaseState);
        }
    }
}
