using System.Threading.Tasks;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.Enemies
{
    ﻿using UnityEngine;

    /// <summary>
    /// 몬스터 HP와 사망 판정. 피격 진입점(IDamageable)을 구현한다.
    /// </summary>
    public class EnemyStats : MonoBehaviour, IDamageable
    {
        // 이 ID로 MonsterStatTable을 조회한다. 예: 1101 = 던전1 노말 A.
        [SerializeField] private int monsterId = 1101;

        // 아래 전투 스탯은 MonsterStatTable이 덮어쓴다. 인스펙터 값은 테이블 로딩 전과
        // 행 조회 실패 시에만 쓰이는 폴백이다(0으로 두면 로딩 대기 중 즉시 사망 판정이 난다).
        [SerializeField] private int maxHp = 100;
        [SerializeField] private float attackPower = 10f;
        [SerializeField] private float defense;
        [SerializeField] private bool isBoss;

        [SerializeField] private float damageTextHeight = 0.5f;
        private int currentHp;
        private Enemy enemy;

        public bool IsDead => currentHp <= 0;

        /// <summary>몬스터의 총 AD. 공격 패턴의 계수와 곱해져 피해가 된다.</summary>
        public float AttackPower => attackPower;

        /// <summary>IDamageable. 방어 경감은 때린 쪽이 이 값을 읽어 계산한다.</summary>
        public float Defense => defense;

        /// <summary>IDamageable. 보스면 공격자의 보스 추가뎀%가 적용된다.</summary>
        public bool IsBoss => isBoss;

        private void Awake()
        {
            currentHp = maxHp;
            // 상태 머신 없이 단독 배치된 대상(테스트용)도 있을 수 있어 null을 허용한다.
            enemy = GetComponent<Enemy>();
        }

        // async void는 Awake/Start 같은 진입점에서만 예외적으로 허용한다(JsonManager와 같은 방침).
        private async void Start()
        {
            await ApplyStatTableAsync();
        }

        /// <summary>
        /// MonsterStatTable에서 monsterId 행을 읽어 스탯에 반영한다.
        /// 테이블이나 행이 없으면 인스펙터 폴백을 유지해 몬스터가 무적/즉사가 되지 않게 한다.
        /// </summary>
        private async Task ApplyStatTableAsync()
        {
            JsonManager json = JsonManager.Instance;
            if (json == null) return;

            if (!json.IsReady) await json.ReadyTask;

            // 로딩을 기다리는 동안 처치·씬 전환 등으로 파괴됐을 수 있다.
            if (this == null) return;

            MonsterStatTable row = json.Get<MonsterStatTable>(monsterId);
            if (row == null)
            {
                Debug.LogWarning($"[EnemyStats] MonsterStatTable에 MonsterId {monsterId} 행이 없습니다. 인스펙터 폴백을 사용합니다.", this);
                return;
            }

            maxHp = row.MaxHp;
            attackPower = row.AttackPower;
            defense = row.Defense;
            isBoss = row.IsBoss;

            // 최대치가 폴백에서 테이블 값으로 바뀌었으므로 다시 채운다.
            // 스폰 직후(Start)에만 실행되므로 전투 중 회복으로 동작할 일은 없다.
            currentHp = maxHp;
        }

        /// <summary>
        /// 데미지 적용. 몬스터는 무적이 없어 살아 있으면 항상 적용된다.
        /// </summary>
        /// <returns>실제 적용됐으면 true. 이미 죽은 대상이면 false(IDamageable 계약).</returns>
        public bool TakeDamage(in DamageResult result)
        {
            if (IsDead) return false;                 // 이미 죽었으면 무시(1회 사망 보장)

            currentHp = Mathf.Max(0, currentHp - result.Amount);

            // 연출은 이벤트로만 알린다(데미지 텍스트·이펙트가 각자 구독).
            // 받은 쪽이 발행하는 이유: 방어력까지 반영된 '실제 적용된' 수치를 아는 곳이 여기이기 때문.
            // 치명타 여부는 때린 쪽만 알 수 있어 DamageResult에 실려 온다.
            CombatEvents.FireDamageDealt(
                transform.position + Vector3.up * damageTextHeight,
                result.Amount,
                result.IsCritical ? DamageTextKind.Critical : DamageTextKind.Normal);

            if (IsDead)
            {
                // 비활성화 '전에' 발행해야 구독자(처치 이펙트 등)가 위치를 신뢰할 수 있다.
                CombatEvents.FireEnemyDied(transform.position);

                // 퀘스트 처치 목표 집계용. 어떤 몬스터를 잡았는지(종류)를 알린다.
                CombatEvents.FireEnemyKilled(monsterId);

                // 사망 연출(애니메이션·AI/충돌 해제·제거 타이밍)은 DeadState가 담당한다.
                // 상태 머신이 없는 단독 배치 대상만 예전처럼 즉시 비활성화한다.
                if (enemy != null) enemy.OnDied();
                else gameObject.SetActive(false);
            }
            else
            {
                enemy?.OnDamaged();
            }

            return true;
        }
    }
}
