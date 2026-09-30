using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스 로코모션(추격) 튜닝값 모음. 값만 담고 로직은 <see cref="RaidBossEngageState"/>가 읽어 쓴다.
    /// 이 컴포넌트가 붙어 있는 보스만 발견 후 일반 추격 대신 레이드 교전(거리밴드 직진 접근)으로 동작한다(옵트인 신호).
    /// </summary>
    public class RaidBossLocomotion : MonoBehaviour
    {
        [Header("거리 밴드 (안쪽 < 바깥 순서 필수) — 공격 사거리는 Combat이 소유")]
        [SerializeField, Min(0f)] private float engageDist = 12f;   // Walk 직진 접근
        [SerializeField, Min(0f)] private float jogDist = 20f;      // engageDist~jogDist → Jog, 초과 → Run

        [Header("속도 (애니 Threshold와 일치)")]
        [SerializeField, Min(0f)] private float walkSpeed = 4f;
        [SerializeField, Min(0f)] private float jogSpeed = 10f;
        [SerializeField, Min(0f)] private float runSpeed = 15f;

        /// <summary>이 거리 안이면 Walk 속도로 직진 접근한다.</summary>
        public float EngageDist => engageDist;

        /// <summary>EngageDist~이 거리는 Jog, 이보다 멀면 Run 속도로 접근한다.</summary>
        public float JogDist => jogDist;

        /// <summary>Walk 이동 속도(애니 블렌드 Threshold와 맞춘다).</summary>
        public float WalkSpeed => walkSpeed;

        /// <summary>Jog 이동 속도(애니 블렌드 Threshold와 맞춘다).</summary>
        public float JogSpeed => jogSpeed;

        /// <summary>Run 이동 속도(애니 블렌드 Threshold와 맞춘다).</summary>
        public float RunSpeed => runSpeed;
    }
}
