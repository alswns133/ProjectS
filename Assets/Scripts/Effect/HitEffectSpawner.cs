using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Effects
{
    /// <summary>
    /// CombatEvents의 타격 접점 이벤트를 받아 맞은 부위에 히트 이펙트를 재생한다.
    /// source로 어느 쪽 접점을 들을지 고른다: 플레이어의 공격 적중(적 몸에 붙는 타격 이펙트)과
    /// 몬스터의 공격 적중(플레이어 몸에 붙는 피격 이펙트)은 연출이 달라야 하므로,
    /// 씬에 이 스포너를 방향별로 하나씩 두고 각각 다른 프리팹을 할당한다.
    /// OnDamageDealt가 아닌 접점 이벤트를 구독하는 이유: 전자의 좌표는 데미지 텍스트용
    /// 머리 위 고정 높이라, 맞은 부위에 붙어야 하는 피격 이펙트에는 접점 좌표가 필요하다.
    /// </summary>
    public class HitEffectSpawner : PooledSpawner<HitEffect>
    {
        // 어느 방향의 타격 접점을 구독할지. 기존 씬의 스포너는 기본값(PlayerAttack)이라
        // 분리 이전과 동일하게 "플레이어가 적을 때린" 이펙트를 재생한다.
        private enum HitSource
        {
            PlayerAttack,   // 플레이어의 공격이 적에게 적중 → 적 몸의 타격 이펙트
            EnemyAttack,    // 몬스터의 공격이 플레이어에게 적중 → 플레이어 몸의 피격 이펙트
        }

        [SerializeField] private HitSource source = HitSource.PlayerAttack;

        private void OnEnable()
        {
            if (source == HitSource.PlayerAttack) CombatEvents.OnPlayerHitLanded += OnHitLanded;
            else CombatEvents.OnEnemyHitLanded += OnHitLanded;
        }

        private void OnDisable()
        {
            if (source == HitSource.PlayerAttack) CombatEvents.OnPlayerHitLanded -= OnHitLanded;
            else CombatEvents.OnEnemyHitLanded -= OnHitLanded;
        }

        private void OnHitLanded(Vector3 hitPos) => GetFromPool().Play(hitPos, ReturnToPool);
    }
}
