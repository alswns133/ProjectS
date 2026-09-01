using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 사망 상태. 사망 연출을 재생하고 AI/충돌을 끈다. 다른 상태로 전환되지 않는 최종 상태.
    /// 연출 시간이 지나면 오브젝트를 비활성화한다(풀링/드롭 도입 전까지의 제거 방식).
    /// </summary>
    public class EnemyDeadState : EnemyBaseState
    {
        private float elapsed;

        public EnemyDeadState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;

            // 공중에서 죽었으면 떠오르며 사망 클립이 재생, 아니면 지상 사망 클립 재생
            if (enemy.DiedAirborne)
                enemy.Animation.PlayDieAirContinuing(); // Hit_Air 모션의 진행을 이어받아 재생
            else
                enemy.Animation.PlayDie();

            // 사망 이펙트 재생은 사망 클립의 Animation Event(OnEffect)가 맡는다. 여기서는 반대로,
            // 사망 순간 진행 중이던 오라·장판 잔상을 걷어낸다(잡몹은 지속 이펙트가 없어 무해).
            enemy.Effects?.StopAll();

            // 사망 후 추가 피격이나 물리 충돌이 일어나지 않도록 이동과 충돌을 모두 끈다.
            enemy.Movement.DisableAgent();
            if (enemy.BodyCollider != null) enemy.BodyCollider.enabled = false;
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // SetActive를 직접 부르지 않고 Enemy.Despawn()을 거친다 — 소멸 직전 훅(OnDespawn)이
            // 함께 돌아야 보스 퇴장 이벤트가 이 시점에 발행된다(잡몹은 훅이 no-op이라 동작 동일).
            if (elapsed >= enemy.DespawnDelay)
                enemy.Despawn();
        }
    }
}
