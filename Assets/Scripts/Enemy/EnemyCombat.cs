using System;
using UnityEngine;

/// <summary>
/// 몬스터 공격 판정과 쿨다운. 히트 프레임은 공격 클립의 Animation Event(OnAttackHit)로 연결한다.
/// PlayerCombat과 같은 방식: 미리 배치한 히트박스 Transform + NonAlloc 판정으로 런타임 할당을 없앤다.
/// 입력은 받지 않고 EnemyAttackState가 호출하며, 이 클래스는 "어떤 공격을 쓸지"와 "맞았는지"만 책임진다.
/// </summary>
public class EnemyCombat : MonoBehaviour
{
    // 공격 1개에 필요한 데이터 묶음. 공격별 사거리/쿨타임/데미지/히트박스를 에디터에서 따로 세팅한다.
    // animationIndex는 EnemyAnimation이 Animator에 넘겨 공격 클립 분기에 사용한다.
    // 슬롯 단위로 묶는 이유: 공격 1/2/3은 모션 길이, 판정 위치, 데미지, 선택 확률이 서로 다르기 때문.
    [Serializable]
    private class AttackPattern
    {
        // 인스펙터에서 식별하기 위한 표시 이름. 로직 키로 쓰지 않는다.
        public string name = "Attack";

        // Animator의 AttackIndex 파라미터로 전달되는 값.
        // 예: 0=Attack1, 1=Attack2, 2=Attack3. 실제 전이 규칙은 Animator Controller가 소유한다.
        [Min(0)] public int animationIndex;

        // 이 공격이 선택될 수 있는 최대 거리. ChaseState의 공격 진입 거리는 전체 공격 중 최대값을 쓴다.
        [Min(0.01f)] public float range = 1.8f;

        // 공격 시작 시 바로 쿨다운을 소모한다. 빗나가도 쿨다운은 돌아가는 '공격 커밋' 규칙.
        [Min(0.01f)] public float cooldown = 2f;

        // 공격 상태가 끝났다고 간주하는 시간. 클립 길이/후딜과 맞춘다.
        // Animation Event 없이도 상태 흐름이 돌아가게 타이머 기반으로 둔다.
        [Min(0.01f)] public float duration = 1.2f;

        public int damage = 10;

        // 이 Transform의 위치/회전/스케일이 곧 판정 박스다.
        // 공격 클립별 손/무기 위치에 맞춘 자식 오브젝트를 연결한다.
        public Transform hitBox;

        // 같은 거리 조건을 만족하는 공격들 중 선택 확률. 0이면 사실상 비활성화된다.
        [Min(0)] public float weight = 1f;
    }

    // 기본 3타 구성. 배열 크기를 늘리면 공격 종류를 더 추가할 수 있다.
    // 각 슬롯의 animationIndex와 Animator Controller 분기 값이 반드시 맞아야 한다.
    [SerializeField] private AttackPattern[] attacks =
    {
        new AttackPattern { name = "Attack 1", animationIndex = 1 },
        new AttackPattern { name = "Attack 2", animationIndex = 2 },
        new AttackPattern { name = "Attack 3", animationIndex = 3 },
    };

    // 플레이어 루트 콜라이더가 있는 레이어만 넣는다.
    // 현재 프로젝트 규칙: 피격 레이어 콜라이더와 IDamageable은 같은 루트 GameObject에 둔다.
    [SerializeField] private LayerMask targetMask;

    // 매 히트 이벤트마다 할당이 생기지 않도록 NonAlloc 쿼리용 버퍼를 재사용한다.
    private readonly Collider[] buffer = new Collider[16];

    // 다음 공격이 가능한 시각. 공격별 cooldown을 시작 시점에 더해 저장한다.
    private float nextAttackTime;

    // 이번 AttackState에서 선택된 공격. OnAttackHit Animation Event가 이 값을 기준으로 판정한다.
    private AttackPattern currentAttack;

    /// <summary>등록된 공격 중 가장 긴 사거리. ChaseState가 공격 전환 판정에 쓴다.</summary>
    public float AttackRange => GetMaxAttackRange();
    /// <summary>현재 선택된 공격 모션 길이. AttackState가 상태 종료 판정에 쓴다.</summary>
    public float AttackDuration
    {
        get
        {
            AttackPattern attack = currentAttack ?? GetDefaultAttack();
            return attack != null ? attack.duration : 0.1f;
        }
    }
    /// <summary>현재 선택된 공격 번호. Animator의 AttackIndex 파라미터로 전달된다.</summary>
    public int CurrentAttackIndex => currentAttack != null ? currentAttack.animationIndex : 0;
    /// <summary>공격 쿨다운이 끝났고 유효한 공격 패턴이 있는지 여부.</summary>
    public bool CanAttack => Time.time >= nextAttackTime && GetDefaultAttack() != null;

    /// <summary>
    /// 공격 상태 진입 시 호출된다. 현재 거리에서 가능한 공격 중 하나를 가중치 기반으로 고르고 쿨다운을 시작한다.
    /// 실제 애니메이션 재생은 EnemyAttackState가 CurrentAttackIndex를 읽어 EnemyAnimation에 위임한다.
    /// </summary>
    public void BeginAttack(float distanceToTarget)
    {
        currentAttack = SelectAttack(distanceToTarget);
        if (currentAttack == null) return;

        nextAttackTime = Time.time + currentAttack.cooldown;
    }

    public void OnAttackHit()
    {
        // Animation Event가 공격 선택 직후가 아닌 시점에 와도 마지막 선택 공격 기준으로 판정한다.
        AttackPattern attack = currentAttack ?? GetDefaultAttack();
        if (attack == null) return;

        if (attack.hitBox == null)
        {
            Debug.LogWarning($"Enemy hit box is not assigned. Attack: {attack.name}", this);
            return;
        }

        int count = Physics.OverlapBoxNonAlloc(
            attack.hitBox.position,
            attack.hitBox.lossyScale * 0.5f,
            buffer,
            attack.hitBox.rotation,
            targetMask);

        if (count == buffer.Length)
            Debug.LogWarning($"Enemy hit buffer is full ({count}). Some targets may have been skipped.", this);

        for (int i = 0; i < count; i++)
        {
            // 현재 프로젝트 규칙: 피격 레이어 콜라이더와 IDamageable은 같은 루트 GameObject에 둔다.
            if (buffer[i].TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(attack.damage);
                CombatEvents.FireHitLanded(buffer[i].ClosestPoint(attack.hitBox.position));
            }
        }
    }

    private AttackPattern SelectAttack(float distanceToTarget)
    {
        if (attacks == null) return null;

        // 1차 루프: 거리 조건을 만족하는 공격들의 가중치 합을 구한다.
        float totalWeight = 0f;

        for (int i = 0; i < attacks.Length; i++)
        {
            AttackPattern attack = attacks[i];
            if (!IsValidAttack(attack)) continue;
            if (distanceToTarget > attack.range) continue;

            totalWeight += attack.weight;
        }

        // 거리 조건에 맞는 공격이 없으면 기본 공격으로 폴백한다.
        if (totalWeight <= 0f) return GetDefaultAttack();

        // 2차 루프: 가중치 룰렛으로 실제 공격을 고른다.
        float pick = UnityEngine.Random.Range(0f, totalWeight);

        for (int i = 0; i < attacks.Length; i++)
        {
            AttackPattern attack = attacks[i];
            if (!IsValidAttack(attack)) continue;
            if (distanceToTarget > attack.range) continue;

            pick -= attack.weight;
            if (pick <= 0f) return attack;
        }

        return GetDefaultAttack();
    }

    private AttackPattern GetDefaultAttack()
    {
        // 인스펙터 세팅이 비어 있거나 거리 조건에 맞는 공격이 없을 때 쓰는 안전 폴백.
        // 첫 번째 유효 슬롯을 돌려 상태 흐름이 멈추지 않게 한다.
        if (attacks == null) return null;

        for (int i = 0; i < attacks.Length; i++)
        {
            if (IsValidAttack(attacks[i]))
                return attacks[i];
        }

        return null;
    }

    private float GetMaxAttackRange()
    {
        // ChaseState는 아직 어떤 공격이 선택될지 모르므로,
        // 전체 공격 중 가장 긴 사거리를 "공격 상태 진입 가능 거리"로 사용한다.
        float maxRange = 0f;

        if (attacks != null)
        {
            for (int i = 0; i < attacks.Length; i++)
            {
                if (IsValidAttack(attacks[i]))
                    maxRange = Mathf.Max(maxRange, attacks[i].range);
            }
        }

        return maxRange;
    }

    private static bool IsValidAttack(AttackPattern attack)
        => attack != null && attack.weight > 0f;

    private void OnDrawGizmosSelected()
    {
        // 공격별 히트박스 미리보기. Enemy 감지 반경(노랑)과 구분되게 빨간색을 쓴다.
        if (attacks == null) return;

        Gizmos.color = Color.red;

        for (int i = 0; i < attacks.Length; i++)
        {
            Transform hitBox = attacks[i]?.hitBox;
            if (hitBox == null) continue;

            Gizmos.matrix = Matrix4x4.TRS(hitBox.position, hitBox.rotation, hitBox.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}
