using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Effects
{
    /// <summary>
    /// 투사체가 벽·지형에 막혔을 때의 충돌 이펙트(탄흔·스파크) 풀 스포너. 씬에 하나만 둔다.
    /// <para>
    /// HitEffectSpawner를 쓰지 않는 이유: 그쪽은 스포너 하나가 프리팹 하나를 소유하는 구조라
    /// 이펙트 종류가 늘 때마다 씬 오브젝트와 이벤트 분기를 함께 늘려야 한다. 벽 충돌 연출은
    /// 검기 흔적·화살 박힘·총알 스파크처럼 투사체 종류만큼 갈라지므로 그 구조로는 감당이 안 된다.
    /// 그래서 "무엇으로 터질지"는 투사체 프리팹이 소유하고(Projectile.blockedEffect),
    /// 여기서는 이벤트에 실려 온 프리팹을 키로 풀만 나눈다.
    /// </para>
    /// </summary>
    public class ProjectileImpactSpawner : KeyedPooledSpawner<HitEffect>
    {
        private void OnEnable()
        {
            CombatEvents.OnProjectileBlocked += OnProjectileBlocked;
        }

        private void OnDisable()
        {
            CombatEvents.OnProjectileBlocked -= OnProjectileBlocked;
        }

        private void OnProjectileBlocked(Vector3 hitPos, Vector3 normal, HitEffect prefab)
        {
            // 이펙트를 지정하지 않은 투사체는 연출 없이 소멸한다(설정 누락이 아니라 선택 사항).
            if (prefab == null) return;

            // 벽 탄흔은 표면 바깥으로 세워 재생한다. 그대로 두면 스파크가 벽에 파묻히거나 안쪽으로 튄다.
            // 법선이 0에 가까우면(경계 상황) LookRotation이 예외를 내므로 회전 없이 재생한다.
            Quaternion rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            GetFromPool(prefab).Play(hitPos, rotation, GetReturnCallback(prefab));
        }
    }
}
