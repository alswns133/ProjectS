using ProjectS.Core;
using ProjectS.Effects;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터 투사체의 적중 통로. 원격 클라가 조종하는 플레이어를 맞히면 그 플레이어 컴퓨터로 보낸다
    /// (근접 타격의 <see cref="EnemyHitRouter"/>와 같은 경로·같은 "맞은 사람이 최종 확인" 규칙).
    /// </summary>
    public sealed class EnemyProjectileHitRouter : IProjectileHitRouter
    {
        /// <summary>상태가 없어 하나를 공유한다.</summary>
        public static readonly EnemyProjectileHitRouter Instance = new();

        private EnemyProjectileHitRouter() { }

        /// <inheritdoc/>
        public bool TryRoute(Collider hitCollider, in DamageResult result, int skillId, Vector3 point, Vector3 direction, out bool showHitFeedback)
        {
            // 원격 플레이어로 보낸 타격은 그쪽이 적용 여부(구르기 무적)와 이펙트를 정한다.
            showHitFeedback = false;

            if (!EnemyHitRouter.TryGetRemoteAvatar(hitCollider, out NetworkDamageRelay relay)) return false;

            relay.ServerSendEnemyHit(in result, point, direction);
            return true;
        }
    }
}
