using UnityEngine;

/// <summary>
/// CombatEvents의 타격 접점 이벤트(OnHitLanded)를 받아 맞은 부위에 히트 이펙트를 재생한다.
/// OnDamageDealt가 아닌 OnHitLanded를 구독하는 이유: 전자의 좌표는 데미지 텍스트용
/// 머리 위 고정 높이라, 맞은 부위에 붙어야 하는 피격 이펙트에는 접점 좌표가 필요하다.
/// </summary>
public class HitEffectSpawner : PooledSpawner<HitEffect>
{
    private void OnEnable() => CombatEvents.OnHitLanded += OnHitLanded;
    private void OnDisable() => CombatEvents.OnHitLanded -= OnHitLanded;

    private void OnHitLanded(Vector3 hitPos) => GetFromPool().Play(hitPos, ReturnToPool);
}
