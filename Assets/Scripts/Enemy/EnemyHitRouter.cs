using Mirror;
using ProjectS.Core;
using ProjectS.Events;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터 타격을 대상에게 전달하는 단일 진입점. 원격 클라가 조종하는 플레이어 아바타면 그 플레이어 컴퓨터로 보내고,
    /// 그 외(싱글·호스트 자신·몬스터끼리)는 기존대로 그 자리에서 적용한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가(2026-09-17).</b> 아바타는 클라 권위라, 서버가 원격 플레이어 아바타의 <c>PlayerStats</c>에 데미지를 넣어도
    /// <b>서버 쪽 사본만 깎이고 실제 플레이어 HP는 그대로</b>다. 그래서 원격 플레이어는 보스에게 맞아도 피해를 받지 않았다.
    /// 플레이어→보스 방향은 <see cref="NetworkDamageRelay.TryReportBossHit"/>가 이미 서버로 보내고, 여기가 그 반대 방향이다.
    /// </para>
    /// <para>
    /// <b>맞은 사람이 최종 확인(사용자 결정).</b> 서버는 "범위에 들었다"까지만 판정하고, 실제 적용은 맞은 플레이어 컴퓨터의
    /// <c>PlayerStats.TakeDamage</c>가 한다. 구르기 무적 같은 순간 상태는 그 컴퓨터만 정확히 알기 때문이다.
    /// </para>
    /// </remarks>
    public static class EnemyHitRouter
    {
        /// <summary>
        /// 타격 하나를 대상에게 전달한다.
        /// </summary>
        /// <param name="hitCollider">판정에 걸린 콜라이더.</param>
        /// <param name="target">그 콜라이더의 피격 대상.</param>
        /// <param name="result">계산이 끝난 피해.</param>
        /// <param name="hitPoint">접점(이펙트 위치).</param>
        /// <param name="hitDirection">히트박스 중심 → 접점 방향(이펙트 회전).</param>
        /// <returns>
        /// 이 자리에서 실제로 적용돼 <b>호출측이 히트 이펙트를 내야 하면</b> true. 원격 플레이어로 보낸 타격은 적용 여부를
        /// 그 컴퓨터가 정하고 이펙트도 그쪽이 내므로 false.
        /// </returns>
        public static bool Apply(Collider hitCollider, IDamageable target, in DamageResult result, Vector3 hitPoint, Vector3 hitDirection)
        {
            if (TryGetRemoteAvatar(hitCollider, out NetworkDamageRelay relay))
            {
                relay.ServerSendEnemyHit(in result, hitPoint, hitDirection);
                return false;
            }

            return target.TakeDamage(in result);
        }

        /// <summary>
        /// 데미지 계산에 쓸 피격자 방어도. 원격 클라 아바타면 서버의 권위 스탯 방어도, 그 외에는 대상 자신의 방어도.
        /// </summary>
        /// <remarks>
        /// 몬스터 판정은 서버에서 방어 경감까지 끝낸 값을 오너에게 보내고, 오너는 그 값을 그대로 적용한다. 그래서 여기서
        /// <c>target.Defense</c>(서버 사본, 장비 없음)를 쓰면 원격 클라만 방어구가 안 먹는다. 데미지를 계산하는 곳은
        /// <c>target.Defense</c> 대신 반드시 이걸 거친다.
        /// </remarks>
        /// <param name="hitCollider">판정에 걸린 콜라이더.</param>
        /// <param name="target">그 콜라이더의 피격 대상.</param>
        public static float ResolveDefense(Component hitCollider, IDamageable target)
        {
            if (TryGetRemoteAvatar(hitCollider, out NetworkDamageRelay relay)) return relay.ServerDefense;
            return target.Defense;
        }

        /// <summary>
        /// 이 콜라이더가 <b>서버에서 본 원격 클라 소유 아바타</b>인지. 호스트 자신·싱글 캐릭터·몬스터는 false.
        /// </summary>
        /// <param name="hitCollider">판정에 걸린 콜라이더.</param>
        /// <param name="relay">원격 아바타면 그 아바타의 전달 통로.</param>
        public static bool TryGetRemoteAvatar(Component hitCollider, out NetworkDamageRelay relay)
        {
            relay = null;
            if (!NetworkServer.active || hitCollider == null) return false;

            relay = hitCollider.GetComponentInParent<NetworkDamageRelay>();
            return relay != null && relay.IsRemoteOwnedAvatar;
        }
    }
}
