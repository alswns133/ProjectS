using System;
using UnityEngine;
using ProjectS.Core;

namespace ProjectS.Effects
{
    /// <summary>
    /// 직진 투사체(검기·총알 등) 풀 스포너. 씬에 하나만 두고, 프리팹별 풀은 베이스가 관리한다.
    /// 투사체 종류가 늘어나도(검기 가로/세로, 권총 총알 등) 스포너 오브젝트를 늘릴 필요 없이
    /// 슬롯(PlayerCombat의 ProjectileSlot, EnemyCombat의 공격 패턴)에 프리팹만 할당하면 된다.
    /// 주의: 씬 루트(움직이지 않는 오브젝트)에 배치할 것 — 풀 항목이 이 오브젝트의 자식으로
    /// 생성되므로, 플레이어 자식에 두면 날아가는 투사체가 플레이어를 따라 움직인다.
    /// </summary>
    public class ProjectileSpawner : KeyedPooledSpawner<Projectile>
    {
        /// <summary>
        /// 지정 프리팹의 투사체 하나를 위치·방향으로 발사한다.
        /// </summary>
        /// <param name="prefab">발사할 투사체 프리팹. 슬롯이 종류(검기 가로/세로, 총알 등)를 고른다.</param>
        /// <param name="attack">데미지 계산 재료. 최종 피해는 투사체가 적중 시점에 대상별로 산출한다.</param>
        /// <param name="canPierce">true면 여러 적 관통, false면 첫 적중에 소멸.</param>
        /// <param name="onTargetHit">적중 1회당 호출. 인자는 회복할 스킬 게이지 양. 몬스터는 null을 넘긴다.</param>
        public void Fire(
            Projectile prefab,
            Vector3 position,
            Quaternion rotation,
            in AttackContext attack,
            float gaugeGain,
            bool canPierce,
            Action<float> onTargetHit)
        {
            if (prefab == null) return;

            Projectile projectile = GetFromPool(prefab);
            projectile.Launch(position, rotation, in attack, gaugeGain, canPierce, onTargetHit, GetReturnCallback(prefab));
        }
    }
}
