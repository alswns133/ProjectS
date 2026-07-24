using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ProjectS.Core;
using ProjectS.Events;

namespace ProjectS.Effects
{
    /// <summary>
    /// 투사체를 쏜 진영. 적중 이펙트 이벤트를 어느 쪽으로 발행할지 가른다.
    /// 프리팹 단위 속성으로 두는 이유: 대상 레이어(targetMask)가 이미 프리팹마다 다르므로
    /// "누가 쏘는 물건인가"는 발사 시점 인자가 아니라 프리팹의 성질이다.
    /// </summary>
    public enum ProjectileOwner
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// 직진 투사체 1개(검기·총알 등). 발사 방향으로 직진하며 경로 위의 IDamageable에게 데미지를 준다.
    /// 종류 차이(비주얼, 판정 크기, 속도)는 전부 프리팹·인스펙터 값으로 표현하고,
    /// 중력 낙차·유도처럼 "움직임 자체"가 달라질 때만 클래스를 나눈다.
    /// 충돌은 트리거/Rigidbody 대신 "이전 위치 → 현재 위치" BoxCast로 판정한다
    /// → 근접 히트박스와 같은 NonAlloc 방침을 따르고, 빠른 속도에서도 적을 건너뛰는 터널링이 없다.
    /// 생성/풀 관리는 ProjectileSpawner가, 비행·판정·수명은 자신이 담당한다(HitEffect와 같은 계약).
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speed = 15f;
        [SerializeField, Min(0f)] private float maxRange = 12f;

        // 판정 박스의 전체 크기(가로 X, 세로 Y, 진행 방향 두께 Z).
        // 보이는 이펙트보다 살짝 후하게 잡는 편이 적중 손맛에 좋다.
        // 예: 검기는 가로로 넓은 참격이라 X를 크게, 총알은 X/Y를 작게 잡는다.
        [SerializeField] private Vector3 hitBoxSize = new Vector3(2f, 1f, 0.5f);

        // 이 투사체가 때릴 대상 레이어. 플레이어용 프리팹은 몬스터 레이어를,
        // 몬스터용 프리팹은 플레이어 레이어를 넣는다.
        [SerializeField, FormerlySerializedAs("enemyMask")] private LayerMask targetMask;

        // 발행할 적중 이펙트 이벤트를 가른다. targetMask와 반드시 짝을 맞춘다
        // (플레이어 레이어를 때리는데 Player로 두면 플레이어가 때린 타격 이펙트가 난다).
        [SerializeField] private ProjectileOwner owner = ProjectileOwner.Player;

        // 벽·지형 마스크. 여기에 맞으면 관통 여부와 무관하게 그 자리에서 소멸한다.
        [SerializeField] private LayerMask obstacleMask;

        // 벽에 막혔을 때 접점에서 재생할 이펙트 프리팹(탄흔·스파크 등). 비워 두면 연출 없이 소멸한다.
        // 씬 스포너가 아니라 투사체가 소유하는 이유: 벽 충돌 연출은 총알 종류마다 다른데,
        // 스포너가 프리팹을 들면 종류가 늘 때마다 씬 오브젝트를 늘려야 한다.
        // 재생과 풀 관리는 씬의 ProjectileImpactSpawner가 이 프리팹을 키로 처리한다.
        [SerializeField] private HitEffect blockedEffect;

        // 관통 모드일 때 최대 적중 수. 무한 관통으로 밀집 웨이브가 통째로 지워지는 것을 막는 상한.
        [SerializeField, Min(1)] private int maxPierceTargets = 5;

        // 트레일이 있으면 발사 시 Clear한다. 풀 재사용 시 직전 소멸 위치에서
        // 새 발사 위치로 잔상이 한 줄 그어지는 것을 막기 위함(선택 사항).
        [SerializeField] private TrailRenderer trail;

        // 매 프레임 캐스트마다 할당이 생기지 않도록 재사용하는 버퍼(근접 판정과 같은 방침).
        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        // 한 투사체가 같은 적을 프레임마다 다시 때리지 않게 기억한다. 발사 때마다 비워 재사용한다.
        private readonly HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>();

        private Action<Projectile> onFinished;
        private Action<float> onTargetHit;
        private Vector3 startPosition;
        // 완성된 데미지 숫자가 아니라 계산 재료를 들고 다닌다.
        // 방어 경감은 맞는 대상마다 다르므로, 최종 피해는 적중 시점에 대상별로 산출해야 한다.
        private AttackContext attack;
        private float gaugeGain;
        private bool canPierce;
        private int hitCount;

        /// <summary>
        /// 투사체를 발사한다. ProjectileSpawner의 Fire를 통해서만 호출된다.
        /// </summary>
        /// <param name="attack">데미지 계산 재료. 최종 피해는 적중 시점에 대상별로 산출한다.</param>
        /// <param name="canPierce">true면 경로 위 여러 적을 연속 타격, false면 첫 적중에 소멸.</param>
        /// <param name="onTargetHit">적중 1회당 호출. 인자는 회복할 스킬 게이지 양.</param>
        /// <param name="onFinished">수명 종료 시 풀 반환 콜백.</param>
        public void Launch(
            Vector3 position,
            Quaternion rotation,
            in AttackContext attack,
            float gaugeGain,
            bool canPierce,
            Action<float> onTargetHit,
            Action<Projectile> onFinished)
        {
            this.attack = attack;
            this.gaugeGain = gaugeGain;
            this.canPierce = canPierce;
            this.onTargetHit = onTargetHit;
            this.onFinished = onFinished;

            transform.SetPositionAndRotation(position, rotation);
            startPosition = position;
            hitCount = 0;
            alreadyHit.Clear();

            gameObject.SetActive(true);
            if (trail != null) trail.Clear();
        }

        private void Update()
        {
            Vector3 previous = transform.position;
            Vector3 next = previous + transform.forward * (speed * Time.deltaTime);
            transform.position = next;

            if (!SweepAndDamage(previous, next))
            {
                Despawn();
                return;
            }

            if ((next - startPosition).sqrMagnitude >= maxRange * maxRange)
                Despawn();
        }

        // 이번 프레임 이동 구간을 훑어 데미지를 적용한다.
        // 계속 날아가도 되면 true, (벽 충돌·비관통 적중·관통 상한으로) 소멸해야 하면 false.
        private bool SweepAndDamage(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0f) return true;

            Vector3 direction = delta / distance;

            // Collide: 이 프로젝트의 적 피격 콜라이더는 Is Trigger로 설정돼 있다.
            // 근접 판정(OverlapBox 기본값)도 트리거를 때리므로 투사체도 맞춰 트리거를 때려야 한다.
            // Ignore로 두면 근접은 되는데 투사체만 적을 통과하는 증상이 난다.
            // 박스는 진행 방향(transform.rotation)으로 정렬해 이펙트와 판정 방향을 맞춘다.
            int count = Physics.BoxCastNonAlloc(
                from,
                hitBoxSize * 0.5f,
                direction,
                hitBuffer,
                transform.rotation,
                distance,
                targetMask | obstacleMask,
                QueryTriggerInteraction.Collide);

            // 벽이 적보다 가까우면 적을 때리기 전에 멈춰야 하므로 가까운 순으로 처리한다.
            // (NonAlloc 캐스트는 정렬을 보장하지 않는다.)
            SortHitsByDistance(count);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];

                // 장애물: 관통 여부와 무관하게 그 접점에서 소멸한다.
                if (IsInMask(hit.collider.gameObject.layer, obstacleMask))
                {
                    transform.position = hit.point;

                    // 캐스트 시작 지점에 이미 겹쳐 있으면 point/normal이 원점으로 나온다.
                    // 그 경우 진행 방향의 반대를 법선으로 써서 이펙트가 쏜 쪽을 향하게 한다.
                    Vector3 normal = hit.distance > 0f ? hit.normal : -direction;
                    CombatEvents.FireProjectileBlocked(hit.point, normal, blockedEffect);
                    return false;
                }

                if (!hit.collider.TryGetComponent<IDamageable>(out IDamageable target)) continue;
                if (!alreadyHit.Add(target)) continue;

                // 방어 경감은 맞는 쪽 방어도로 계산하므로 적중 대상마다 따로 굴린다.
                DamageResult result = DamageCalculator.Calculate(in attack, target.Defense, target.IsBoss);

                // 씹힌 타격(이미 죽은 적 등)은 이펙트도 게이지 회복도 없다(근접 판정과 같은 방침).
                if (!target.TakeDamage(in result)) continue;

                // 캐스트 시작 지점에 이미 겹쳐 있던 콜라이더는 point가 원점으로 나오므로 근사치로 보정한다.
                Vector3 point = hit.distance > 0f ? hit.point : hit.collider.ClosestPoint(from);

                // 타격 이펙트는 때린 쪽 기준으로 갈라진다. 몬스터 화살이 플레이어 타격 이펙트를
                // 내면 플레이어가 적중시킨 것으로 오인한다.
                if (owner == ProjectileOwner.Player) CombatEvents.FirePlayerHitLanded(point);
                else CombatEvents.FireEnemyHitLanded(point);

                onTargetHit?.Invoke(gaugeGain);
                hitCount++;

                if (!canPierce || hitCount >= maxPierceTargets) return false;
            }

            return true;
        }

        // hitBuffer[0..count)를 distance 오름차순으로 정렬한다.
        // 적중 수가 한 자릿수라 삽입 정렬이면 충분하고, Array.Sort와 달리 비교자 할당이 없다.
        private void SortHitsByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = hitBuffer[i];
                int j = i - 1;

                while (j >= 0 && hitBuffer[j].distance > key.distance)
                {
                    hitBuffer[j + 1] = hitBuffer[j];
                    j--;
                }

                hitBuffer[j + 1] = key;
            }
        }

        private static bool IsInMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;

        private void Despawn()
        {
            gameObject.SetActive(false);
            onFinished?.Invoke(this);
        }

        // 판정 박스(hitBoxSize) 미리보기. 근접·적 히트박스와 같은 빨간색을 쓴다.
        // OnDrawGizmosSelected가 아닌 OnDrawGizmos인 이유: 풀에서 날아다니는 오브젝트라
        // 플레이 중 선택이 어렵다. 비활성(풀 대기) 인스턴스는 그려지지 않으므로
        // 실제 날아가는 투사체만 표시된다. 보이는 이펙트와 판정 크기가 맞는지 눈으로 튜닝할 때 쓴다.
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;

            // 박스는 진행 방향으로 회전시켜 그린다(판정과 같은 방향/크기). TRS로 회전 반영 후 원복.
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, hitBoxSize);
            Gizmos.matrix = Matrix4x4.identity;

            // 진행 방향 표시. 스윕이 어느 쪽으로 훑는지 보여준다.
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * (hitBoxSize.z * 0.5f + 0.5f));
        }
    }
}
