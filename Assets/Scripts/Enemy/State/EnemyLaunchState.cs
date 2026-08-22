using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 공중 런처 피격 상태. 하루 강공격의 Launch() 신호로 진입한다.
    /// Hit_Air 클립(떠오름 → 착지 → 기상)을 재생하는 동안 NavMeshAgent를 멈춰
    /// 연출이 이동에 끌리지 않게 하고, 클립이 끝나면 Chase로 복귀한다.
    /// 공중/지상 판정은 '실제 높이'가 아니라 '클립 진행도(normalizedTime)'로 한다.
    /// Hit_Air의 떠오름은 transform을 올리는 루트모션이 아니라 제자리(in-place) 애니메이션이라
    /// transform.y가 움직이지 않는다(높이로는 착지를 영영 감지 못 함).
    /// 클립상 착지 프레임(30번째 프레임)을 지나면 '기상 중이라도 지상'으로 취급해,
    /// 그 구간의 피격/사망이 지상 Hit/Die로 나오게 한다.
    /// </summary>
    public class EnemyLaunchState : EnemyBaseState
    {
        // Hit_Air가 실제로 땅에 닿는 지점(착지 30프레임 / 총 110프레임 ≈ 0.27).
        // 이 지점을 지나면 hasDescended를 세워 지상으로 취급한다.
        private const float GroundContactTime = 30f / 110f;

        // 클립이 사실상 끝났다고 볼 진행도. 여기서 Chase로 복귀한다.
        private const float ClipEndTime = 0.98f;

        // 진행도 판정이 성립 안 할 때(전이 꼬임 등) 영영 갇히지 않게 하는 강제 복귀 상한(초).
        private const float MaxAirTime = 5f;

        // 착지 지점을 지났는지 래치. 한 번 true면 기상 구간 내내 유지된다.
        // IsAirborne(공중/지상 판정)의 근거 — 기상 애니메이션이 남아 있어도 지상으로 본다.
        private bool hasDescended;

        // 강제 복귀 상한용 경과 시간.
        private float elapsed;

        /// <summary>
        /// 지금 공중에 떠 있는 것으로 볼지 여부. 사망(Die/Die_Air)·피격(지상 Hit/런처 유지) 선택에 쓴다.
        /// '상태가 LaunchState'와 다르다 — 착지 프레임을 지난 기상 구간은 상태가 아직 Launch여도 지상으로 취급한다.
        /// </summary>
        public bool IsAirborne => !hasDescended;

        public EnemyLaunchState(Enemy enemy) : base(enemy) { }

        public override void Enter()
        {
            elapsed = 0f;
            hasDescended = false;

            // 연출 동안 NavMeshAgent를 멈춘다(제자리 클립이 추격 이동에 끌리지 않게).
            enemy.Movement.BeginRootMotion();
            enemy.Animation.PlayHitAir();
            // 런치 피격 이펙트는 Hit_Air 클립의 Animation Event(OnEffect)가 재생한다(클립 주도).
        }

        /// <summary>
        /// 이미 런처 중일 때 강공격을 또 맞으면 Hit_Air를 처음부터 다시 재생한다(재런처 저글링).
        /// 상태 머신이 같은 상태 재진입을 막으므로 상태 내부에서 진행도·래치만 초기화한다.
        /// </summary>
        public void Relaunch()
        {
            elapsed = 0f;
            hasDescended = false;
            enemy.Animation.PlayHitAir();
        }

        public override void Update()
        {
            elapsed += Time.deltaTime;

            // Hit_Air 재생 중이 아니면 -1. 그동안은 래치/복귀 판정을 하지 않는다.
            float t = enemy.Animation.HitAirNormalizedTime;

            // 착지 프레임을 지나면 지상으로 래치 → 기상 구간의 피격/사망이 지상 모션으로.
            if (!hasDescended && t >= GroundContactTime)
                hasDescended = true;

            // 기상까지 다 재생됐거나 안전 상한이면 Chase 복귀.
            if (t >= ClipEndTime || elapsed >= MaxAirTime)
                Land();
        }

        private void Land()
        {
            enemy.Movement.EndRootMotionAndLand();
            enemy.StateMachine.ChangeState(enemy.ChaseState);
        }
    }
}
