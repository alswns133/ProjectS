using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Players;

namespace ProjectS.Effects
{
    /// <summary>
    /// 직진 투사체(검기·총알 등) 풀 스포너. 씬에 하나만 두고, 프리팹별 풀을 내부 Dictionary로 관리한다.
    /// 투사체 종류가 늘어나도(검기 가로/세로, 권총 총알 등) 스포너 오브젝트를 늘릴 필요 없이
    /// 슬롯(PlayerCombat의 ProjectileSlot)에 프리팹만 할당하면 된다.
    /// PooledSpawner를 상속하지 않는 이유: 그 베이스는 "프리팹 1개당 스포너 1개" 구조라
    /// 투사체 종류마다 씬 오브젝트가 늘어난다 → 여기서는 풀을 프리팹 키로 나눠 하나가 전부 담당한다.
    /// 주의: 씬 루트(움직이지 않는 오브젝트)에 배치할 것 — 풀 항목이 이 오브젝트의 자식으로
    /// 생성되므로, 플레이어 자식에 두면 날아가는 투사체가 플레이어를 따라 움직인다.
    /// </summary>
    public class ProjectileSpawner : MonoBehaviour
    {
        // 씬 시작 시 미리 만들어 둘 프리팹과 개수. 첫 발사 프레임의 Instantiate 비용을
        // 피하고 싶을 때만 채운다(비워 두면 각 프리팹의 첫 발사 때 지연 생성).
        [Serializable]
        private class PrewarmEntry
        {
            public Projectile prefab;
            [Min(1)] public int count = 4;
        }

        [SerializeField] private PrewarmEntry[] prewarm;

        // 프리팹별 풀. 서로 다른 프리팹의 인스턴스는 재사용이 호환되지 않으므로 풀을 나눈다.
        private readonly Dictionary<Projectile, Queue<Projectile>> pools
            = new Dictionary<Projectile, Queue<Projectile>>();

        // 프리팹별 반환 콜백 캐시. 발사마다 람다를 새로 만들지 않기 위함(PlayerCombat.relayProjectileHit과 같은 방침).
        private readonly Dictionary<Projectile, Action<Projectile>> returnCallbacks
            = new Dictionary<Projectile, Action<Projectile>>();

        private void Awake()
        {
            if (prewarm == null) return;

            foreach (PrewarmEntry entry in prewarm)
            {
                if (entry == null || entry.prefab == null) continue;

                Queue<Projectile> pool = GetPool(entry.prefab);
                for (int i = 0; i < entry.count; i++)
                    pool.Enqueue(CreateItem(entry.prefab));
            }
        }

        /// <summary>
        /// 지정 프리팹의 투사체 하나를 위치·방향으로 발사한다.
        /// </summary>
        /// <param name="prefab">발사할 투사체 프리팹. 슬롯이 종류(검기 가로/세로, 총알 등)를 고른다.</param>
        /// <param name="attack">데미지 계산 재료. 최종 피해는 투사체가 적중 시점에 대상별로 산출한다.</param>
        /// <param name="canPierce">true면 여러 적 관통, false면 첫 적중에 소멸.</param>
        /// <param name="onTargetHit">적중 1회당 호출. 인자는 회복할 스킬 게이지 양.</param>
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

            Queue<Projectile> pool = GetPool(prefab);
            Projectile projectile = pool.Count > 0 ? pool.Dequeue() : CreateItem(prefab);

            projectile.Launch(position, rotation, in attack, gaugeGain, canPierce, onTargetHit, returnCallbacks[prefab]);
        }

        // 프리팹의 풀을 돌려준다. 처음 보는 프리팹이면 풀과 반환 콜백을 함께 1회 생성한다.
        private Queue<Projectile> GetPool(Projectile prefab)
        {
            if (pools.TryGetValue(prefab, out Queue<Projectile> pool)) return pool;

            pool = new Queue<Projectile>();
            pools.Add(prefab, pool);
            returnCallbacks.Add(prefab, item => ReturnToPool(prefab, item));
            return pool;
        }

        private Projectile CreateItem(Projectile prefab)
        {
            Projectile item = Instantiate(prefab, transform);
            item.gameObject.SetActive(false);
            return item;
        }

        private void ReturnToPool(Projectile prefab, Projectile item)
        {
            if (item == null) return;

            // 수명 종료와 비활성화 타이밍이 겹쳐도 같은 인스턴스가 중복 등록되지 않게 방어한다.
            Queue<Projectile> pool = GetPool(prefab);
            if (!pool.Contains(item)) pool.Enqueue(item);
        }
    }
}
