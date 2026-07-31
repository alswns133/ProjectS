using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// 같이 켜고 끄는 UI 묶음. 대상 리스트(<typeparamref name="T"/>)와 일괄 활성/비활성 토글을 한 데 담는다.
    /// 언제·왜 켜고 끄는지(예: NPC 대화 중 숨김)는 이 범용 타입이 알지 않는다 — 그 판단은 이걸 쓰는 쪽이 한다.
    /// 인스펙터에 <c>[SerializeField] UIGroup&lt;BasePanel&gt;</c>처럼 구체 타입으로 노출한다
    /// (Unity 6 제네릭 직렬화 지원). <typeparamref name="T"/>는 gameObject가 필요하므로 Component로 제약한다.
    /// </summary>
    /// <typeparam name="T">묶음에 담을 UI 컴포넌트 타입(BasePanel·Transform 등)</typeparam>
    [Serializable]
    public class UIGroup<T> where T : Component
    {
        [SerializeField] private List<T> items = new();

        /// <summary>묶음 대상들(읽기 전용).</summary>
        public IReadOnlyList<T> Items => items;

        /// <summary>묶음 전체의 활성 상태를 바꾼다. 대상별 GameObject를 켜고 끈다.</summary>
        /// <param name="active">켜면 true, 끄면 false</param>
        public void SetActive(bool active)
        {
            foreach (T item in items)
            {
                if (item != null) item.gameObject.SetActive(active);
            }
        }
    }
}
