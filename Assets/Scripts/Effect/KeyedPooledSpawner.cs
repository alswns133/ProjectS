using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 프리팹을 키로 풀을 나누는 스포너 공통 베이스. 씬에 하나만 두고 종류를 여러 개 다룬다.
    /// <para>
    /// <see cref="PooledSpawner{T}"/>와의 차이: 그쪽은 스포너 하나가 프리팹 하나를 소유하므로
    /// 종류가 늘면 씬 오브젝트가 함께 늘어난다. 데미지 텍스트처럼 종류가 하나로 고정된 연출에는 그게 맞다.
    /// 반면 투사체·충돌 이펙트는 종류가 계속 늘어나므로(검기 가로/세로, 화살, 총알…)
    /// 프리팹을 호출 시점에 받아 풀만 나누는 이 구조를 쓴다.
    /// </para>
    /// <para>
    /// 꺼낸 뒤 무엇을 호출할지(Launch/Play)는 타입마다 인자가 달라 공통화할 수 없으므로,
    /// 여기서는 "꺼내기(GetFromPool)"와 "되돌리기 콜백(GetReturnCallback)"까지만 제공하고
    /// 실제 재생은 구체 스포너가 담당한다.
    /// </para>
    /// </summary>
    public abstract class KeyedPooledSpawner<T> : MonoBehaviour where T : Component
    {
        // 씬 시작 시 미리 만들어 둘 프리팹과 개수. 첫 사용 프레임의 Instantiate 비용을
        // 피하고 싶을 때만 채운다(비워 두면 각 프리팹의 첫 사용 때 지연 생성).
        [Serializable]
        private class PrewarmEntry
        {
            public T prefab;
            [Min(1)] public int count = 4;
        }

        [SerializeField] private PrewarmEntry[] prewarm;

        // 프리팹별 풀. 서로 다른 프리팹의 인스턴스는 재사용이 호환되지 않으므로 풀을 나눈다.
        private readonly Dictionary<T, Queue<T>> pools = new Dictionary<T, Queue<T>>();

        // 프리팹별 반환 콜백 캐시. 사용할 때마다 람다를 새로 만들지 않기 위함
        // (프레임마다 여러 발이 나가는 연출이라 여기서 생기는 할당이 그대로 GC 부담이 된다).
        private readonly Dictionary<T, Action<T>> returnCallbacks = new Dictionary<T, Action<T>>();

        protected virtual void Awake()
        {
            if (prewarm == null) return;

            foreach (PrewarmEntry entry in prewarm)
            {
                if (entry == null || entry.prefab == null) continue;

                Queue<T> pool = GetPool(entry.prefab);
                for (int i = 0; i < entry.count; i++)
                    pool.Enqueue(CreateItem(entry.prefab));
            }
        }

        /// <summary>
        /// 지정 프리팹의 풀에서 하나 꺼낸다. 풀이 비었을 때만 추가 생성한다.
        /// 처음 보는 프리팹이면 풀을 그 자리에서 만들므로 prewarm 등록은 선택 사항이다.
        /// </summary>
        protected T GetFromPool(T prefab)
        {
            Queue<T> pool = GetPool(prefab);
            return pool.Count > 0 ? pool.Dequeue() : CreateItem(prefab);
        }

        /// <summary>
        /// 지정 프리팹 전용 반환 콜백을 돌려준다. 꺼낸 항목의 수명 종료 콜백으로 넘겨 쓴다.
        /// 프리팹마다 하나씩 캐싱된 것이라 호출할 때마다 새로 할당되지 않는다.
        /// </summary>
        protected Action<T> GetReturnCallback(T prefab)
        {
            // GetPool이 풀과 콜백을 함께 등록하므로, 꺼내기 전에 불려도 안전하다.
            GetPool(prefab);
            return returnCallbacks[prefab];
        }

        // 프리팹의 풀을 돌려준다. 처음 보는 프리팹이면 풀과 반환 콜백을 함께 1회 생성한다.
        private Queue<T> GetPool(T prefab)
        {
            if (pools.TryGetValue(prefab, out Queue<T> pool)) return pool;

            pool = new Queue<T>();
            pools.Add(prefab, pool);
            returnCallbacks.Add(prefab, item => ReturnToPool(prefab, item));
            return pool;
        }

        private T CreateItem(T prefab)
        {
            T item = Instantiate(prefab, transform);
            item.gameObject.SetActive(false);
            return item;
        }

        private void ReturnToPool(T prefab, T item)
        {
            if (item == null) return;

            // 수명 종료와 비활성화 타이밍이 겹쳐도 같은 인스턴스가 중복 등록되지 않게 방어한다.
            Queue<T> pool = GetPool(prefab);
            if (!pool.Contains(item)) pool.Enqueue(item);
        }
    }
}
