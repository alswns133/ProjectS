using System.Collections.Generic;
using UnityEngine;
using ProjectS.Effects;
using ProjectS.Enemies;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// CombatEvents.OnEnemyHealthChanged를 받아 일반 몬스터의 월드 HP 바를 띄운다.
    /// 데미지 텍스트와 달리 바는 특정 적에 붙어 유지되므로, "어느 적이 어떤 바를 빌렸나"를
    /// 딕셔너리로 잠깐 들고 있다가 풀피/사망 시 그 적의 바만 정확히 풀로 반납한다.
    /// 바는 익명 재고이지만(빌려줄 땐 아무거나), 반납은 그 적이 빌린 것만 돌려준다.
    /// </summary>
    public class EnemyHpBarSpawner : PooledSpawner<EnemyHpBar>
    {
        // "지금 어떤 적이 어떤 바를 빌렸나". 같은 적이 또 맞으면 새 바를 꺼내지 않고 이 바를 갱신한다.
        private readonly Dictionary<EnemyStats, EnemyHpBar> active = new Dictionary<EnemyStats, EnemyHpBar>();

        private void OnEnable() => CombatEvents.OnEnemyHealthChanged += OnEnemyHealthChanged;
        private void OnDisable() => CombatEvents.OnEnemyHealthChanged -= OnEnemyHealthChanged;

        private void OnEnemyHealthChanged(EnemyStats stats, float ratio)
        {
            // 보스는 전용 HP UI가 따로 담당한다. 같은 이벤트를 그 UI가 별도로 구독하면 된다.
            if (stats == null || stats.IsBoss) return;

            // 풀피(>=1)와 사망(<=0)은 하는 일이 같다 — 이 적 바를 치운다.
            if (ratio <= 0f || ratio >= 1f)
            {
                if (active.TryGetValue(stats, out EnemyHpBar bar))
                    bar.Deactivate();   // 반환 콜백(OnBarReturned)이 딕셔너리 제거 + 풀 반납을 처리
                return;
            }

            // 그 사이: 처음 맞았으면 풀에서 하나 빌려 붙이고, 이미 있으면 비율만 갱신한다.
            if (active.TryGetValue(stats, out EnemyHpBar existing))
            {
                existing.SetRatio(ratio);
            }
            else
            {
                EnemyHpBar bar = GetFromPool();
                bar.Show(stats, ratio, OnBarReturned);
                active[stats] = bar;
            }
        }

        // 바가 반납될 때(풀피·사망은 Deactivate 경유, 대상 소실은 바 자체 감지) 딕셔너리에서 지우고 풀로 되돌린다.
        // 반납 경로가 어디든 여기로 모여 매핑과 풀 상태가 어긋나지 않는다.
        private void OnBarReturned(EnemyHpBar bar)
        {
            // 파괴된 적은 Unity의 '가짜 null'(== null이 true지만 C# 참조는 살아 있음)이라
            // != null 가드로는 제거를 건너뛰어 죽은 키가 남는다. Dictionary는 참조로 비교하므로
            // ReferenceEquals로 진짜 null만 걸러내고, 파괴된 적 키도 그대로 참조 제거한다.
            if (!ReferenceEquals(bar.Target, null))
                active.Remove(bar.Target);

            ReturnToPool(bar);
        }
    }
}
