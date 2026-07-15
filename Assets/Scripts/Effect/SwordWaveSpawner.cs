using System;
using UnityEngine;

/// <summary>
/// 검기 투사체 풀 스포너. PlayerCombat의 Animation Event(OnProjectileFrame)가 Fire를 호출한다.
/// 생성/재사용은 PooledSpawner가, 비행·판정·수명은 SwordWaveProjectile이 담당한다.
/// 주의: 씬 루트(움직이지 않는 오브젝트)에 배치할 것 — 풀 항목이 이 오브젝트의 자식으로
/// 생성되므로, 플레이어 자식에 두면 날아가는 검기가 플레이어를 따라 움직인다.
/// </summary>
public class SwordWaveSpawner : PooledSpawner<SwordWaveProjectile>
{
    /// <summary>
    /// 지정 위치·방향으로 검기 하나를 발사한다.
    /// </summary>
    /// <param name="canPierce">true면 여러 적 관통, false면 첫 적중에 소멸.</param>
    /// <param name="onTargetHit">적중 1회당 호출. 인자는 회복할 스킬 게이지 양.</param>
    public void Fire(
        Vector3 position,
        Quaternion rotation,
        int damage,
        float gaugeGain,
        bool canPierce,
        Action<float> onTargetHit)
    {
        GetFromPool().Launch(position, rotation, damage, gaugeGain, canPierce, onTargetHit, ReturnToPool);
    }
}
