using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.UI;

namespace ProjectS.Effects
{
    /// <summary>
    /// 프리팹 풀링 스포너 공통 베이스. 시작 시 initialPoolSize만큼 미리 만들어두고,
    /// 꺼내 쓰고(GetFromPool) 연출이 끝나면 반환(ReturnToPool)하는 계약만 제공한다.
    /// 어떤 이벤트를 구독해 언제/어디에 띄울지는 구체 스포너가 결정한다.
    /// (DamageTextSpawner·HitEffectSpawner·DeathEffectSpawner의 중복 풀링 코드를 한곳에 모음)
    /// </summary>
    public abstract class PooledSpawner<T> : MonoBehaviour where T : Component
    {
        // FormerlySerializedAs: 베이스 도입 전 각 스포너가 쓰던 필드 이름(textPrefab/effectPrefab)의
        // 인스펙터 연결이 끊기지 않게 유지한다. 씬/프리팹을 다시 저장하기 전까지 필요하다.
        [FormerlySerializedAs("textPrefab")]
        [FormerlySerializedAs("effectPrefab")]
        [SerializeField] private T prefab;
        [SerializeField] private int initialPoolSize = 10;

        private readonly Queue<T> pool = new Queue<T>();

        protected virtual void Awake()
        {
            for (int i = 0; i < initialPoolSize; i++)
                pool.Enqueue(CreateItem());
        }

        /// <summary>
        /// 풀에서 하나 꺼낸다. 풀이 비었을 때만 추가 생성하므로
        /// 일반 상황에서는 Instantiate가 반복되지 않는다.
        /// </summary>
        protected T GetFromPool() => pool.Count > 0 ? pool.Dequeue() : CreateItem();

        /// <summary>
        /// 연출이 끝난 항목을 풀로 되돌린다. 항목의 수명 종료 콜백(Action&lt;T&gt;)으로 넘겨 쓴다.
        /// </summary>
        protected void ReturnToPool(T item)
        {
            // 수명 종료와 비활성화 타이밍이 겹쳐도 같은 인스턴스가 중복 등록되지 않게 방어한다.
            if (item != null && !pool.Contains(item))
                pool.Enqueue(item);
        }

        private T CreateItem()
        {
            T item = Instantiate(prefab, transform);
            item.gameObject.SetActive(false);
            return item;
        }
    }
}
