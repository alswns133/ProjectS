using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스 로코모션(추격·간보기) 튜닝값 모음. 값만 담고 로직은 <see cref="RaidBossEngageState"/>가 읽어 쓴다.
    /// 이 컴포넌트가 붙어 있는 보스만 발견 후 일반 추격 대신 레이드 교전(거리밴드+간보기)으로 동작한다(옵트인 신호).
    /// </summary>
    public class RaidBossLocomotion : MonoBehaviour
    {
        [Header("거리 밴드 (안쪽 < 바깥 순서 필수)")]
        [SerializeField, Min(0f)] private float attackRange = 3f;   // 이 안 → 정지(Idle)
        [SerializeField, Min(0f)] private float engageDist = 12f;   // attackRange~engageDist → 좌우 간보기(Walk)
        [SerializeField, Min(0f)] private float jogDist = 20f;      // engageDist~jogDist → Jog, 초과 → Run

        [Header("속도 (애니 Threshold와 일치)")]
        [SerializeField, Min(0f)] private float walkSpeed = 4f;
        [SerializeField, Min(0f)] private float jogSpeed = 8f;
        [SerializeField, Min(0f)] private float runSpeed = 12f;     // Run threshold(10)보다 높게 = 여유

        [Header("간보기")]
        [SerializeField, Min(0f)] private float strafeRadius = 7f;      // 플레이어와 유지할 거리
        [SerializeField, Min(0f)] private float strafeLookahead = 2f;   // 옆으로 밀 목적지 거리
        [SerializeField] private Vector2 flipInterval = new Vector2(1.5f, 3f); // 방향 전환 간격(랜덤)

        public float AttackRange => attackRange;
        public float EngageDist => engageDist;
        public float JogDist => jogDist;
        public float WalkSpeed => walkSpeed;
        public float JogSpeed => jogSpeed;
        public float RunSpeed => runSpeed;
        public float StrafeRadius => strafeRadius;
        public float StrafeLookahead => strafeLookahead;
        public Vector2 FlipInterval => flipInterval;
    }
}
