using UnityEngine;
using ProjectS.Events;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 보스 그로기(무력화) 게이지. 스킬의 그로기 데미지(<see cref="ProjectS.Core.DamageResult.GroggyDamage"/>)를
    /// <see cref="EnemyStats.TakeDamage"/>가 여기로 넘겨 게이지를 깎고, 0이 되면 보스를 무력화(<see cref="Enemy.EnterGroggy"/>)시킨다.
    /// 보스 프리팹에만 붙인다(일반몹은 이 컴포넌트가 없어 그로기 데미지가 조용히 무시된다).
    ///
    /// 최대치는 <see cref="EnemyStats"/>가 테이블(MonsterStatTable.GroggyMax) 로딩을 마친 뒤 <see cref="ConfigureMax"/>로
    /// 주입한다. 로딩 전/조회 실패 시에는 인스펙터 폴백(maxGroggy)을 유지한다.
    ///
    /// 레이드 특수 패턴 면역: <see cref="SetGroggyLocked"/>로 잠그면 그 동안 게이지가 깎이지 않는다(일반 보스는 잠그지 않아 정상).
    /// </summary>
    [RequireComponent(typeof(EnemyStats))]
    public class EnemyGroggy : MonoBehaviour
    {
        // 테이블 GroggyMax 로딩 전/조회 실패 시 쓰는 폴백. ConfigureMax가 유효 값을 주면 덮인다.
        [SerializeField] private float maxGroggy = 100f;

        private float currentGroggy;

        // 특수 패턴(레이드 기믹) 중 잠금. 잠금이면 게이지가 깎이지 않고 UI에 자물쇠가 뜬다.
        private bool isGroggyLocked;

        private EnemyStats stats;
        private Enemy enemy;

        /// <summary>특수 패턴으로 그로기가 잠겨 있는지 여부.</summary>
        public bool IsGroggyLocked => isGroggyLocked;

        /// <summary>그로기 게이지 최대치(현재 적용값).</summary>
        public float Max => maxGroggy;

        /// <summary>현재 남은 그로기.</summary>
        public float Current => currentGroggy;

        /// <summary>남은 그로기 비율(0~1). UI 바가 이 값을 그린다.</summary>
        public float Ratio => maxGroggy > 0f ? Mathf.Clamp01(currentGroggy / maxGroggy) : 0f;

        private void Awake()
        {
            stats = GetComponent<EnemyStats>();
            enemy = GetComponent<Enemy>();
            currentGroggy = maxGroggy;   // 테이블 주입 전 폴백. ConfigureMax가 곧 덮는다.
        }

        /// <summary>
        /// 최대 그로기를 확정하고 게이지를 가득 채운다. EnemyStats가 테이블 로딩을 마친 직후 호출한다
        /// (호출 순서를 EnemyStats가 소유해 두 async Start의 경쟁을 피한다). max가 0 이하면 폴백을 유지한다.
        /// </summary>
        /// <param name="max">테이블에서 읽은 그로기 최대치.</param>
        public void ConfigureMax(float max)
        {
            if (max > 0f) maxGroggy = max;
            currentGroggy = maxGroggy;
            FireChanged();
        }

        /// <summary>
        /// 특수 패턴(레이드 기믹) 중 그로기 잠금 토글. 레이드 인카운터/패턴 스크립트가 기믹 진입 시
        /// true, 종료 시 false로 호출한다. 잠금 중에는 <see cref="AddGroggyDamage"/>가 무시되고,
        /// UI는 자물쇠 표시로 바뀐다(발행되는 locked 플래그로). 일반 보스는 호출하지 않으면 늘 false다.
        /// </summary>
        /// <param name="locked">true면 그로기 면역(특수 패턴 중), false면 정상.</param>
        public void SetGroggyLocked(bool locked)
        {
            if (isGroggyLocked == locked) return;

            isGroggyLocked = locked;
            FireChanged();   // UI 자물쇠 상태 갱신
        }

        /// <summary>
        /// 그로기 데미지 누적(게이지 감소). EnemyStats.TakeDamage가 스킬 그로기 데미지를 넘겨 호출한다.
        /// 잠금 중이거나 이미 무력화 진행 중(0)이면 깎지 않는다. 0에 도달하면 무력화로 진입시킨다.
        /// </summary>
        /// <param name="amount">깎을 그로기 양. 0 이하는 무시(평타 등).</param>
        public void AddGroggyDamage(float amount)
        {
            if (amount <= 0f) return;

            // ★ 특수 패턴 중에는 그로기가 깎이지 않는다(레이드 보스). 일반 보스는 잠금이 없어 그대로 깎인다.
            if (isGroggyLocked) return;

            // 이미 바닥(무력화 진행/대기)이면 더 깎을 것이 없다.
            if (currentGroggy <= 0f) return;

            currentGroggy = Mathf.Max(0f, currentGroggy - amount);
            FireChanged();

            // 0 도달 → 무력화 진입. 게이지 리필은 무력화 상태가 끝날 때 Refill로 되돌린다.
            if (currentGroggy <= 0f)
            {
                // TODO(sound): 보스 그로기(무력화) 돌입음 — SoundManager.Instance.PlaySFX3D(<그로기 SFX>, transform.position);
                enemy?.EnterGroggy();
            }
        }

        /// <summary>
        /// 무력화 종료 후 게이지를 최대치로 되돌린다. EnemyGroggyState가 무력화 연출을 마칠 때 호출한다.
        /// </summary>
        public void Refill()
        {
            currentGroggy = maxGroggy;
            FireChanged();
        }

        // 게이지·잠금 상태를 UI로 알린다. 바 비율과 잠금 플래그를 함께 실어 보내
        // 프레젠터가 한 번에 바 갱신 + 자물쇠 표시를 처리한다.
        private void FireChanged()
        {
            BossEvents.FireBossGroggyChanged(stats, Ratio, isGroggyLocked);
        }
    }
}
