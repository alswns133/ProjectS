using ProjectS.Core;
using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 투사체 적중을 그 자리에서 적용하기 전에 "다른 곳으로 보낼 타격인가"를 묻는 통로.
    /// 투사체는 네트워크를 모르고, 쏜 쪽이 이 통로를 들려 보낸다.
    /// </summary>
    /// <remarks>
    /// 멀티에서 근접 타격과 같은 전달 경로를 타게 하기 위함이다(2026-09-17). 플레이어 투사체가 서버 권위 보스를 맞히면 서버로 보내고
    /// (<c>NetworkDamageRelay.TryReportBossHit</c>), 보스 투사체가 원격 클라 플레이어를 맞히면 그 플레이어 컴퓨터로 보낸다
    /// (<c>EnemyHitRouter</c>). 보낼 대상이 아니면 false를 돌려 투사체가 기존대로 그 자리에서 적용한다.
    /// </remarks>
    public interface IProjectileHitRouter
    {
        /// <summary>이 적중을 다른 곳으로 보낼지 판단하고, 보냈다면 처리까지 한다.</summary>
        /// <param name="hitCollider">맞은 콜라이더.</param>
        /// <param name="result">계산이 끝난 피해.</param>
        /// <param name="skillId">쏜 슬롯의 스킬 ID(플레이어 투사체의 서버 재계산용). 몬스터는 0.</param>
        /// <param name="point">접점.</param>
        /// <param name="direction">투사체 진행 방향.</param>
        /// <param name="showHitFeedback">보냈을 때 이 자리에서 히트 이펙트·게이지 회복을 낼지.
        /// 플레이어가 보스를 맞힌 경우는 손맛을 위해 즉시 내고, 원격 플레이어로 보낸 경우는 그쪽이 적용 여부를 정하므로 내지 않는다.</param>
        /// <returns>보냈으면 true(투사체는 그 자리 적용을 건너뛴다).</returns>
        bool TryRoute(Collider hitCollider, in DamageResult result, int skillId, Vector3 point, Vector3 direction, out bool showHitFeedback);
    }
}
