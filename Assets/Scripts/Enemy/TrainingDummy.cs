using UnityEngine;
using ProjectS.Core;
using ProjectS.Events;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 트레이닝 더미. 공격 판정·데미지 검증용 최소 대상.
    /// 이동·AI 없이 IDamageable만 구현해 "맞고 죽는" 것만 한다.
    /// </summary>
    public class TrainingDummy : MonoBehaviour, IDamageable
    {
        [SerializeField] private int maxHp = 100;

        // 검증용 대상이라 방어도는 인스펙터에서 직접 넣는다(테이블을 타지 않는다).
        // 0이면 경감 없이 계산기 출력이 그대로 들어오므로 공식 확인에 편하다.
        [SerializeField] private float defense;

        [SerializeField] private float damageTextHeight = 1.8f;   // 데미지 텍스트가 뜨는 높이(머리 위). 적 크기에 맞춰 조정
        private int currentHp;

        public bool IsDead => currentHp <= 0;

        /// <summary>IDamageable. 방어 경감은 때린 쪽이 이 값을 읽어 계산한다.</summary>
        public float Defense => defense;

        /// <summary>IDamageable. 더미는 보스 판정을 쓰지 않는다.</summary>
        public bool IsBoss => false;

        private void Awake() => currentHp = maxHp;

        /// <summary>
        /// 데미지 적용. 더미는 무적이 없어 살아 있으면 항상 적용된다.
        /// </summary>
        /// <returns>실제 적용됐으면 true. 이미 죽은 대상이면 false(IDamageable 계약).</returns>
        public bool TakeDamage(in DamageResult result)
        {
            if (IsDead) return false;                 // 이미 죽었으면 무시(1회 사망 보장)

            currentHp = Mathf.Max(0, currentHp - result.Amount);

            // 연출은 이벤트로만 알린다(데미지 텍스트·이펙트가 각자 구독).
            // 받은 쪽이 발행하는 이유: 방어력까지 반영된 '실제 적용된' 수치를 아는 곳이 여기이기 때문.
            CombatEvents.FireDamageDealt(
                transform.position + Vector3.up * damageTextHeight,
                result.Amount,
                result.IsCritical ? DamageTextKind.Critical : DamageTextKind.Normal);

            if (IsDead)
            {
                //Debug.Log($"{name} 사망", this);
                // 비활성화 '전에' 발행해야 구독자(처치 이펙트 등)가 위치를 신뢰할 수 있다.
                CombatEvents.FireEnemyDied(transform.position);
                // 지금은 그냥 비활성. 나중에 사망 연출·드롭으로 확장
                gameObject.SetActive(false);
            }

            return true;
        }
    }
}
