using UnityEngine;

/// <summary>
/// 몬스터 공격 판정과 쿨다운. 히트 프레임은 공격 클립의 Animation Event(OnAttackHit)로 연결한다.
/// PlayerCombat과 같은 방식: 미리 배치한 히트박스 Transform + NonAlloc 판정으로 런타임 할당을 없앤다.
/// </summary>
public class EnemyCombat : MonoBehaviour
{
    [SerializeField] private float attackRange = 1.8f;
    [SerializeField] private float attackCooldown = 2f;

    // 공격 모션이 끝났다고 간주하는 시간. 클립 길이와 맞춘다.
    // Animator 이벤트 대신 타이머를 쓰는 이유: 애니메이터 세팅이 아직 없어도 상태 흐름이 동작해야 해서.
    [SerializeField] private float attackDuration = 1.2f;

    [SerializeField] private int damage = 10;

    // 히트박스의 위치/회전/스케일이 곧 판정 박스(PlayerCombat과 동일 규칙).
    [SerializeField] private Transform hitBox;
    [SerializeField] private LayerMask targetMask;

    private readonly Collider[] buffer = new Collider[16];
    private float nextAttackTime;

    /// <summary>공격 사거리. 추적 상태가 공격 전환 판정에 쓴다.</summary>
    public float AttackRange => attackRange;

    /// <summary>공격 모션 길이(초). 공격 상태가 종료 판정에 쓴다.</summary>
    public float AttackDuration => attackDuration;

    /// <summary>쿨다운이 끝나 공격할 수 있는지 여부.</summary>
    public bool CanAttack => Time.time >= nextAttackTime;

    /// <summary>공격을 시작하며 쿨다운을 소모한다. 공격 상태 진입 시 1회 호출한다.</summary>
    public void BeginAttack() => nextAttackTime = Time.time + attackCooldown;

    /// <summary>
    /// 공격 클립의 히트 프레임에서 Animation Event로 호출된다.
    /// 판정 순간의 히트박스 기준이므로, 플레이어가 회피로 빠져나갔으면 자연히 빗나간다.
    /// </summary>
    public void OnAttackHit()
    {
        if (hitBox == null)
        {
            Debug.LogWarning("Enemy hit box is not assigned.", this);
            return;
        }

        int count = Physics.OverlapBoxNonAlloc(
            hitBox.position,
            hitBox.lossyScale * 0.5f,
            buffer,
            hitBox.rotation,
            targetMask);

        for (int i = 0; i < count; i++)
        {
            // 대상 쪽은 IDamageable 계약만 알면 된다. 플레이어 무적 처리도 저쪽 소관이다.
            if (buffer[i].TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(damage);

                // 맞은 부위 접점은 때린 쪽만 안다 → 피격 이펙트 이벤트 발행(플레이어 피격도 같은 스포너 재사용).
                CombatEvents.FireHitLanded(buffer[i].ClosestPoint(hitBox.position));
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (hitBox == null) return;

        Gizmos.color = Color.red;
        Gizmos.matrix = Matrix4x4.TRS(hitBox.position, hitBox.rotation, hitBox.lossyScale);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
