using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Core;
using ProjectS.Effects;
using ProjectS.Events;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 몬스터 공격 판정과 쿨다운. 히트 프레임은 공격 클립의 Animation Event(OnAttackHit)로 연결한다.
    /// PlayerCombat과 같은 방식: 미리 배치한 히트박스 Transform + NonAlloc 판정으로 런타임 할당을 없앤다.
    /// 입력은 받지 않고 EnemyAttackState가 호출하며, 이 클래스는 "어떤 공격을 쓸지"와 "맞았는지"만 책임진다.
    /// 근접·원거리는 컴포넌트를 나누지 않고 공격 패턴 슬롯의 종류로 구분한다
    /// → 궁수처럼 "평소엔 활, 붙으면 근접"인 몬스터가 슬롯 조합만으로 표현된다.
    /// </summary>
    public class EnemyCombat : MonoBehaviour
    {
        /// <summary>공격 슬롯이 히트를 전달하는 방식.</summary>
        private enum AttackKind
        {
            /// <summary>히트박스 Transform으로 그 자리에서 판정한다.</summary>
            Melee,

            /// <summary>총구에서 투사체를 발사하고, 판정은 투사체가 스스로 한다.</summary>
            Projectile,

            /// <summary>
            /// 전진하며 스치는 대상을 훑는 돌진. 이동은 공격 클립의 전방 루트모션이 담당하고
            /// (EnemyAttackState가 돌진 슬롯일 때만 <see cref="EnemyMovement.BeginAttackRootMotion"/>을 켠다),
            /// 판정은 Melee와 같은 hitBox를 쓰되 히트 프레임 1회가 아니라 슬라이드 구간 동안 연속으로 훑는다
            /// (같은 대상 중복 방지). 히트 구간은 클립의 Animation Event <c>OnChargeHitBegin</c>/<c>OnChargeHitEnd</c>로 연다.
            /// </summary>
            Charge,
        }

        // 공격 1개에 필요한 데이터 묶음. 공격별 사거리/쿨타임/데미지/히트박스를 에디터에서 따로 세팅한다.
        // animationIndex는 EnemyAnimation이 Animator에 넘겨 공격 클립 분기에 사용한다.
        // 슬롯 단위로 묶는 이유: 공격 1/2/3은 모션 길이, 판정 위치, 데미지, 선택 확률이 서로 다르기 때문.
        //
        // 근접/원거리를 클래스로 나누지 않고 kind + ShowIfEnum으로 가른 이유:
        // Unity 직렬화가 다형성을 지원하지 않아 클래스를 나누려면 [SerializeReference]가 필요하고,
        // 그러면 직렬화 포맷이 바뀌어 기존 몬스터 프리팹의 공격 세팅이 전부 사라진다.
        // 배열을 근접/원거리로 쪼개는 방법도 "한 목록에서 거리 조건이 맞는 것들끼리 가중치 경쟁"이라는
        // 선택 규칙을 깨뜨린다 — 궁수는 활과 근접이 같은 룰렛에 있어야 하는 것이 핵심이다.
        [Serializable]
        private class AttackPattern
        {
            // 인스펙터에서 식별하기 위한 표시 이름. 로직 키로 쓰지 않는다.
            public string name = "Attack";

            // 히트 전달 방식. Melee는 hitBox로 그 자리 판정, Projectile은 muzzle에서 발사한다.
            // 기본값이자 enum 0번이 Melee라, 이 필드가 없던 시절의 기존 프리팹은 전부 근접으로 읽힌다.
            // 아래 슬롯별 설정이 이 값에 따라 인스펙터에서 갈린다.
            public AttackKind kind = AttackKind.Melee;

            // Animator의 AttackIndex 파라미터로 전달되는 값.
            // 예: 0=Attack1, 1=Attack2, 2=Attack3. 실제 전이 규칙은 Animator Controller가 소유한다.
            [Min(0)] public int animationIndex;

            // 이 공격이 선택될 수 있는 최대 거리. ChaseState의 공격 진입 거리는 전체 공격 중 최대값을 쓴다.
            [Min(0.01f)] public float range = 1.8f;

            // 이 공격이 선택되기 위한 최소 거리. 0이면 붙어 있어도 쓸 수 있다.
            // 궁수의 "붙으면 근접"은 이 값으로 표현한다: 활 슬롯에 minRange를 주면
            // 플레이어가 파고들었을 때 활이 후보에서 빠지고 근접 슬롯만 남는다.
            [Min(0f)] public float minRange;

            // 공격 시작 시 바로 쿨다운을 소모한다. 빗나가도 쿨다운은 돌아가는 '공격 커밋' 규칙.
            [Min(0.01f)] public float cooldown = 2f;

            // 이 공격의 계수. 피해 = EnemyStats.AttackPower × 계수 로 계산된다.
            // 공격력(테이블)과 모션별 세기(인스펙터)를 분리해, 같은 몬스터의 강한 패턴/약한 패턴을
            // 스탯 테이블을 건드리지 않고 표현한다.
            public float coef = 1f;

            // 같은 거리 조건을 만족하는 공격들 중 선택 확률. 0이면 사실상 비활성화된다.
            [Min(0)] public float weight = 1f;

            // 공격이 진행되는 동안 대상을 계속 조준할지.
            // 기본값(끔)은 시작 순간의 방향으로 커밋하는 기존 규칙이다 — 근접은 휘두르는 순간이
            // 곧 판정이라 시차가 거의 없고, 플레이어의 회피 이동이 의미 있게 작동한다.
            // 조준 모션이 긴 원거리 공격은 켠다. 끄면 발사 시점엔 이미 낡은 방향이라
            // 플레이어가 지나간 자리로 총알이 날아간다(피격 넉백 직후가 특히 눈에 띈다).
            // Projectile 슬롯에서는 몸 회전뿐 아니라 발사 방향도 발사 순간의 대상 위치로 다시 잡는다.
            public bool trackTarget;

            // ── 이하 kind별 설정. 해당 kind일 때만 인스펙터에 나타난다 ──────────
            // 숨겨진 필드도 값은 직렬화되어 남는다(표시만 감춘다) → kind를 되돌리면 이전 참조가 그대로 살아난다.

            // 이 Transform의 위치/회전/스케일이 곧 판정 박스다.
            // 공격 클립별 손/무기 위치에 맞춘 자식 오브젝트를 연결한다.
            // 돌진(Charge)도 같은 hitBox를 쓴다 — 다른 건 "1회 판정 vs 슬라이드 연속 판정"뿐이라 박스 지정은 동일하다.
            [ShowIfEnum(nameof(kind), (int)AttackKind.Melee, (int)AttackKind.Charge)]
            public Transform hitBox;

            // 발사할 투사체 프리팹. 반드시 owner가 Enemy이고 targetMask가 플레이어 레이어인
            // 프리팹이어야 한다(플레이어용 검기와 프리팹을 나눈다).
            [ShowIfEnum(nameof(kind), (int)AttackKind.Projectile)]
            public Projectile projectilePrefab;

            // 발사 위치·방향 기준 Transform. 보통 활을 든 손이나 가슴 높이의 자식 오브젝트.
            // 투사체는 이 Transform의 forward로 직진하므로, 플레이어 높이에 맞춘 각도로 배치해야 한다
            // (AttackState가 진입 시 대상을 바라보게 하므로 수평 방향은 자동으로 맞는다).
            [ShowIfEnum(nameof(kind), (int)AttackKind.Projectile)]
            public Transform muzzle;

            // muzzle 회전에 더할 각도(오일러). 발사각 미세 조정용.
            [ShowIfEnum(nameof(kind), (int)AttackKind.Projectile)]
            public Vector3 rotationOffset;

            // 발사 순간 총구에서 터지는 이펙트(총구 화염 등). 보통 muzzle의 자식으로 붙인다.
            // EnemyEffects(공용 이펙트 통로)로 빼지 않는 이유: 총구 위치·발사 방향·총구 화염은 한 세트라
            // 발사 로직과 같은 슬롯에 두는 편이 응집도가 높고, 발사 프레임과 정확히 맞물린다.
            [ShowIfEnum(nameof(kind), (int)AttackKind.Projectile)]
            public ParticleSystem muzzleFlash;

            // 관통 여부. 몬스터 투사체는 보통 false(첫 적중에 소멸).
            [ShowIfEnum(nameof(kind), (int)AttackKind.Projectile)]
            public bool canPierce;
        }

        // 조우 패턴(Detect)의 공격 설정. 공격 슬롯 배열과 분리한 이유:
        // Detect는 근접/원거리 타입을 안 타는 공용 상태이고, 조우 공격은 거리·가중치로 '선택'되는 것이
        // 아니라 발견 시 그 클립에서 나가는 고정 동작이다. 그래서 룰렛(attacks)에 섞지 않고 전용으로 둔다.
        // 일반 공격 슬롯과 같은 AttackPattern이라 kind로 근접/원거리를 똑같이 고른다:
        //   - 근접 돌진: kind=Melee, hitBox 지정 → 대시 클립 이벤트가 그 자리에서 박스 판정.
        //   - 조우 사격: kind=Projectile, muzzle+prefab 지정 → Detect(사격) 클립 이벤트가 총알 발사.
        //     클립에 이벤트를 여러 개 찍으면 첫 조우에 여러 발이 나간다.
        // 히트박스/총구가 비어 있으면 데미지 없이 지나간다(연출만 하는 몬스터도 있으므로 선택 사항).
        // range/minRange/cooldown/weight/animationIndex는 조우 공격에선 쓰이지 않는다.
        [Header("조우 패턴")]
        [SerializeField] private AttackPattern detectAttack = new AttackPattern { name = "Detect" };

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

        // ── 시야 ─────────────────────────────────────────────────────────
        // 공격 진입 전 "대상이 실제로 보이는지" 검사에 쓰는 설정.
        // NavMesh 경로 검사(Enemy.CanReachTarget)는 갈 수 있는지만 보므로 벽 너머 사격을 못 막는다.
        // 비워 두면(마스크 0) 항상 보이는 것으로 판정되어 기존 근접 몬스터 동작은 그대로다.
        [Header("시야")]
        [SerializeField] private LayerMask obstacleMask;

        // 시야 레이를 쏘는 눈높이와 겨냥 높이(각각 자기 발밑/대상 발밑 기준).
        // 0으로 두면 바닥을 긁어 지면 콜라이더에 막힌 것으로 오판한다.
        [SerializeField, Min(0f)] private float eyeHeight = 1.5f;
        [SerializeField, Min(0f)] private float targetAimHeight = 1f;

        // 투사체 풀 스포너. 씬에 하나 있는 것을 Start에서 찾아 캐싱한다.
        // 인스펙터로 받지 않는 이유: 몬스터는 프리팹 에셋이라 씬 오브젝트를 직렬화할 수 없다
        // (플레이어는 씬에 배치돼 있어 PlayerCombat은 인스펙터로 받을 수 있다).
        private ProjectileSpawner projectileSpawner;

        // 매 히트 이벤트마다 할당이 생기지 않도록 NonAlloc 쿼리용 버퍼를 재사용한다.
        private readonly Collider[] buffer = new Collider[16];

        // 다음 공격이 가능한 시각. 공격별 cooldown을 시작 시점에 더해 저장한다.
        private float nextAttackTime;

        // 이번 AttackState에서 선택된 공격. OnAttackHit Animation Event가 이 값을 기준으로 판정한다.
        private AttackPattern currentAttack;
        private Enemy enemy;

        // 돌진(Charge) 슬라이드 히트 창이 열려 있는지. 클립의 Animation Event(OnChargeHitBegin/End)로 열고 닫으며,
        // 열려 있는 동안 EnemyAttackState.Update가 매 프레임 UpdateChargeHit를 호출해 스친 대상을 훑는다.
        private bool chargeHitActive;

        // 이번 돌진에서 이미 때린 대상. 슬라이드로 여러 프레임 겹쳐 있어도 같은 대상을 중복 타격하지 않게 막는다.
        // 돌진 시작(OnChargeHitBegin)마다 비운다.
        private readonly HashSet<IDamageable> chargeHitTargets = new HashSet<IDamageable>();

        private void Awake()
        {
            enemy = GetComponent<Enemy>();
        }

        private void Start()
        {
            // 투사체 슬롯이 하나도 없으면(순수 근접 몬스터) 스포너를 찾지 않는다.
            // Start인 이유: 스포너가 자기 Awake에서 프리웜 풀을 만들므로 그 뒤에 잡아야 안전하다.
            if (HasProjectileAttack()) projectileSpawner = FindAnyObjectByType<ProjectileSpawner>();
        }

        /// <summary>등록된 공격 중 가장 긴 사거리. ChaseState가 공격 전환 판정에 쓴다.</summary>
        public float AttackRange => GetMaxAttackRange();
        /// <summary>현재 선택된 공격 번호. Animator의 AttackIndex 파라미터로 전달된다.</summary>
        public int CurrentAttackIndex => currentAttack != null ? currentAttack.animationIndex : 0;

        /// <summary>
        /// 현재 선택된 공격이 진행 중에도 대상을 계속 조준하는지 여부.
        /// AttackState가 매 프레임 확인해 몸 방향을 갱신한다.
        /// </summary>
        public bool ShouldTrackTarget => currentAttack != null && currentAttack.trackTarget;
        /// <summary>공격 쿨다운이 끝났고 유효한 공격 패턴이 있는지 여부.</summary>
        public bool CanAttack => Time.time >= nextAttackTime && GetDefaultAttack() != null;

        /// <summary>
        /// 이번에 선택된 공격이 돌진(Charge)인지 여부. EnemyAttackState가 진입 시 이 값으로
        /// 전방 루트모션(<see cref="EnemyMovement.BeginAttackRootMotion"/>)을 켤지 판단한다.
        /// </summary>
        public bool IsCurrentAttackCharge => currentAttack != null && currentAttack.kind == AttackKind.Charge;

        /// <summary>
        /// 공격 상태 진입 시 호출된다. 현재 거리에서 가능한 공격 중 하나를 가중치 기반으로 고르고 쿨다운을 시작한다.
        /// 실제 애니메이션 재생은 EnemyAttackState가 CurrentAttackIndex를 읽어 EnemyAnimation에 위임한다.
        /// </summary>
        public void BeginAttack(float distanceToTarget)
        {
            // 이전 돌진의 히트 창이 어떤 이유로든 닫히지 못한 채 남았어도, 새 공격을 시작하며 확실히 닫는다
            // (열린 채로 남으면 돌진이 아닌 공격 중에도 UpdateChargeHit가 판정할 수 있다).
            chargeHitActive = false;

            currentAttack = SelectAttack(distanceToTarget);
            if (currentAttack == null) return;

            nextAttackTime = Time.time + currentAttack.cooldown;
        }

        /// <summary>
        /// 돌진 슬라이드 히트 창을 연다. 돌진 클립에서 몸이 전진하기 시작하는 프레임에 Animation Event로 찍는다.
        /// 이 시점부터 <see cref="OnChargeHitEnd"/>(또는 공격 종료)까지, EnemyAttackState.Update가 매 프레임
        /// <see cref="UpdateChargeHit"/>를 호출해 hitBox에 겹친 대상을 훑는다(같은 대상은 이번 돌진에 1회만).
        /// </summary>
        public void OnChargeHitBegin()
        {
            // 돌진이 아닌 공격 클립에 실수로 찍혔거나, 공격 상태가 끝난 뒤 블렌드 아웃 중 도착한 이벤트는 무시한다.
            if (enemy == null || enemy.Stats.IsDead || enemy.StateMachine.Current != enemy.AttackState) return;
            if (currentAttack == null || currentAttack.kind != AttackKind.Charge) return;

            chargeHitActive = true;
            chargeHitTargets.Clear();
        }

        /// <summary>돌진 슬라이드 히트 창을 닫는다. 돌진이 멈추는 프레임에 Animation Event로 찍는다.</summary>
        public void OnChargeHitEnd() => chargeHitActive = false;

        /// <summary>
        /// 돌진 히트 창이 열려 있는 동안 매 프레임 hitBox 범위를 훑어, 아직 안 맞은 대상에게 데미지를 준다.
        /// EnemyAttackState.Update가 호출한다. 창이 닫혀 있으면 즉시 반환하므로 항상 호출해도 안전하다.
        /// 단발 히트 프레임(OnAttackHit)과 달리 슬라이드로 스쳐 지나가는 대상을 놓치지 않게 연속 판정한다.
        /// </summary>
        public void UpdateChargeHit()
        {
            if (!chargeHitActive || currentAttack == null || currentAttack.hitBox == null) return;

            int count = Physics.OverlapBoxNonAlloc(
                currentAttack.hitBox.position,
                currentAttack.hitBox.lossyScale * 0.5f,
                buffer,
                currentAttack.hitBox.rotation,
                targetMask);

            AttackContext attackContext = BuildAttackContext(currentAttack.coef);

            for (int i = 0; i < count; i++)
            {
                // 현재 프로젝트 규칙: 피격 레이어 콜라이더와 IDamageable은 같은 루트 GameObject에 둔다.
                if (!buffer[i].TryGetComponent<IDamageable>(out var target)) continue;

                // 이번 돌진에 이미 때린 대상이면 건너뛴다(Add가 false면 이미 들어 있던 것).
                if (!chargeHitTargets.Add(target)) continue;

                DamageResult result = DamageCalculator.Calculate(in attackContext, target.Defense, target.IsBoss);

                // 데미지가 실제로 들어갔을 때만 히트 이펙트를 낸다(구르기 무적에 씹힌 스침에 이펙트가 나오지 않게).
                if (target.TakeDamage(in result))
                    CombatEvents.FireEnemyHitLanded(buffer[i].ClosestPoint(currentAttack.hitBox.position));
            }
        }

        /// <summary>
        /// 대상이 실제로 보이는지 검사한다. 벽 뒤의 플레이어에게 활을 쏘지 않게 하는 판정으로,
        /// ChaseState가 공격 진입 직전에 호출한다. 막혀 있으면 공격 대신 추격을 계속한다.
        /// obstacleMask가 비어 있으면 항상 true라 기존 근접 몬스터 동작은 바뀌지 않는다.
        /// </summary>
        public bool HasLineOfSight()
        {
            if (enemy == null || enemy.Target == null) return false;
            if (obstacleMask.value == 0) return true;

            Vector3 origin = transform.position + Vector3.up * eyeHeight;
            Vector3 aim = enemy.Target.position + Vector3.up * targetAimHeight;
            Vector3 delta = aim - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return true;

            // Ignore: 이 프로젝트의 피격 콜라이더는 Is Trigger라, 트리거를 함께 때리면
            // 대상 자신이나 다른 몬스터의 피격 판정이 시야를 막은 것으로 오판한다.
            return !Physics.Raycast(
                origin,
                delta / distance,
                distance,
                obstacleMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 공격 상태의 히트 프레임. 공격 클립의 Animation Event(OnAttackHit)로 연결한다.
        /// </summary>
        public void OnAttackHit()
        {
            // 피격·사망 등으로 공격 상태가 끝난 뒤 블렌드 아웃 중 도착한 이벤트는 무시한다.
            if (enemy == null || enemy.Stats.IsDead || enemy.StateMachine.Current != enemy.AttackState)
                return;

            // Animation Event가 공격 선택 직후가 아닌 시점에 와도 마지막 선택 공격 기준으로 판정한다.
            ExecuteAttackHit(currentAttack ?? GetDefaultAttack());
        }

        /// <summary>
        /// 조우 패턴(Detect)의 히트 프레임. Detect(대시/사격) 클립의 Animation Event로 연결한다.
        /// 근접/원거리 타입을 안 타는 공용 Detect 상태에서 쓰는 전용 조우 공격이라,
        /// 공격 슬롯 배열이 아니라 detectAttack 설정을 쓴다.
        /// OnAttackHit과 분리한 이유: 그쪽은 AttackState에서만 유효하도록 막혀 있어
        /// DetectState 중에 오는 이 이벤트를 그대로 넘기면 조용히 씹힌다.
        /// 클립에 이벤트를 여러 개 찍으면(원거리 첫 조우) 한 번에 여러 발이 나간다.
        /// </summary>
        public void OnDetectHit()
        {
            // 조우 연출이 끝난 뒤(다른 상태로 전환된 뒤) 블렌드 아웃 중 도착한 이벤트는 무시한다.
            if (enemy == null || enemy.Stats.IsDead || enemy.StateMachine.Current != enemy.DetectState)
                return;

            ExecuteAttackHit(detectAttack);
        }

        // 선택된 공격 패턴 하나의 히트 프레임을 실행한다. 근접이면 그 자리 박스 판정,
        // 원거리면 총알 발사. OnAttackHit(공격 상태)과 OnDetectHit(조우 상태)이 공유한다.
        private void ExecuteAttackHit(AttackPattern attack)
        {
            if (attack == null) return;

            // 원거리는 여기서 판정하지 않는다. 투사체를 내보내고 판정은 투사체가 스스로 한다.
            if (attack.kind == AttackKind.Projectile)
            {
                FireProjectile(attack);
                return;
            }

            if (attack.hitBox == null)
            {
                Debug.LogWarning($"Enemy hit box is not assigned. Attack: {attack.name}", this);
                return;
            }

            ApplyBoxHit(attack.hitBox, attack.coef);
        }

        // 히트박스 Transform 범위를 OverlapBox로 훑어 겹친 IDamageable에게 데미지를 준다.
        // 근접 공격 슬롯과 돌진이 같은 경로를 타게 하려고 뽑아낸 공유 판정이다.
        // 계수만 다르고, 방어 경감은 맞는 대상마다 계산기가 알아서 처리한다.
        private void ApplyBoxHit(Transform hitBox, float coef)
        {
            int count = Physics.OverlapBoxNonAlloc(
                hitBox.position,
                hitBox.lossyScale * 0.5f,
                buffer,
                hitBox.rotation,
                targetMask);

            if (count == buffer.Length)
                Debug.LogWarning($"Enemy hit buffer is full ({count}). Some targets may have been skipped.", this);

            // 몬스터도 플레이어와 같은 계산기를 탄다. 다른 건 계수의 출처(인스펙터)뿐이다.
            AttackContext attackContext = BuildAttackContext(coef);

            for (int i = 0; i < count; i++)
            {
                // 현재 프로젝트 규칙: 피격 레이어 콜라이더와 IDamageable은 같은 루트 GameObject에 둔다.
                if (buffer[i].TryGetComponent<IDamageable>(out var target))
                {
                    DamageResult result = DamageCalculator.Calculate(in attackContext, target.Defense, target.IsBoss);

                    // 데미지가 실제로 들어갔을 때만 히트 이펙트를 낸다.
                    // 구르기 무적에 씹힌 공격에도 이펙트가 나오면 플레이어가 맞은 것으로 오인한다.
                    if (target.TakeDamage(in result))
                        CombatEvents.FireEnemyHitLanded(buffer[i].ClosestPoint(hitBox.position));
                }
            }
        }

        // 원거리 슬롯의 히트 프레임 처리. 근접과 달리 이 시점에 맞았는지 알 수 없으므로
        // 완성된 데미지가 아니라 계산 재료(AttackContext)를 투사체에 들려 보낸다
        // (방어 경감은 맞는 대상마다 달라 적중 시점에 대상별로 굴려야 한다).
        private void FireProjectile(AttackPattern attack)
        {
            if (attack.muzzle == null)
            {
                Debug.LogWarning($"Enemy projectile muzzle is not assigned. Attack: {attack.name}", this);
                return;
            }

            if (attack.projectilePrefab == null)
            {
                Debug.LogWarning($"Enemy projectile prefab is not assigned. Attack: {attack.name}", this);
                return;
            }

            if (projectileSpawner == null)
            {
                Debug.LogWarning("ProjectileSpawner is not found in the scene.", this);
                return;
            }

            AttackContext attackContext = BuildAttackContext(attack.coef);
            Quaternion rotation = GetFireRotation(attack);

            // 게이지 회복(gaugeGain)과 적중 콜백은 플레이어 스킬 게이지 전용이라 몬스터는 쓰지 않는다.
            projectileSpawner.Fire(
                attack.projectilePrefab,
                attack.muzzle.position,
                rotation,
                in attackContext,
                0f,
                attack.canPierce,
                null);

            // 총구 화염은 총알이 실제로 나간 뒤에만 재생한다.
            // 위의 가드로 발사가 취소됐는데 화염만 터지면 "쐈는데 안 나간" 것처럼 보인다.
            PlayMuzzleFlash(attack);
        }

        // 발사 방향을 정한다.
        // trackTarget이 꺼져 있으면 총구가 향한 방향 그대로 = 공격 시작 순간에 커밋된 방향이다.
        // 켜져 있으면 발사 '순간'의 대상 위치로 다시 조준한다. 몸 회전(AttackState)만으로는
        // 회전이 프레임 단위로 따라가고 muzzle이 몸 중심에서 떨어져 있어 미세하게 빗나가기 때문에,
        // 총알 방향은 총구에서 대상까지의 실제 벡터로 다시 잡는다.
        // 겨냥 높이는 시야 판정과 같은 targetAimHeight를 쓴다 — 보이는 지점과 쏘는 지점을 일치시킨다.
        private Quaternion GetFireRotation(AttackPattern attack)
        {
            Quaternion aimRotation = attack.muzzle.rotation;

            if (attack.trackTarget && enemy != null && enemy.Target != null)
            {
                Vector3 aim = enemy.Target.position + Vector3.up * targetAimHeight;
                Vector3 direction = aim - attack.muzzle.position;

                if (direction.sqrMagnitude > 0.0001f) aimRotation = Quaternion.LookRotation(direction);
            }

            return aimRotation * Quaternion.Euler(attack.rotationOffset);
        }

        // 총구 이펙트 재생. EnemyEffects와 같은 규칙으로 매번 처음부터 다시 재생한다
        // → 연사 시 이전 재생의 잔상이 이어지지 않는다.
        private static void PlayMuzzleFlash(AttackPattern attack)
        {
            if (attack.muzzleFlash == null) return;

            attack.muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            attack.muzzleFlash.Play(true);
        }

        /// <summary>
        /// 공격 패턴과 몬스터 스탯을 합쳐 데미지 계산 입력을 만든다.
        /// 랜덤 편차·치명타·관통은 몬스터용 데이터가 아직 없어 "편차 없음, 치명타 없음"으로 고정한다.
        /// (몬스터 치명타 유무는 기획 확인 대기 항목 — 값이 정해지면 여기와 MonsterStatTable만 고치면 된다.)
        /// </summary>
        private AttackContext BuildAttackContext(float coef)
        {
            // 호출부(ApplyBoxHit/FireProjectile)가 enemy·Stats 유효성을 이미 확인한 뒤에만 들어온다.
            return new AttackContext
            {
                AttackPower = enemy.Stats.AttackPower,
                Coef = coef,

                // 1.0~1.0 = 편차 없음. 계산기가 평타·스킬과 같은 경로를 타게 하려고 자리는 채운다.
                RandomMin = 1f,
                RandomMax = 1f,

                CritChance = 0f,
                CritDamage = 1f,
                Penetration = 0f,
                DamageBonus = 0f,
                BossBonus = 0f,
            };
        }

        private AttackPattern SelectAttack(float distanceToTarget)
        {
            if (attacks == null) return null;

            // 1차 루프: 거리 조건을 만족하는 공격들의 가중치 합을 구한다.
            float totalWeight = 0f;

            for (int i = 0; i < attacks.Length; i++)
            {
                AttackPattern attack = attacks[i];
                if (!IsInRange(attack, distanceToTarget)) continue;

                totalWeight += attack.weight;
            }

            // 거리 조건에 맞는 공격이 없으면 기본 공격으로 폴백한다.
            if (totalWeight <= 0f) return GetDefaultAttack();

            // 2차 루프: 가중치 룰렛으로 실제 공격을 고른다.
            float pick = UnityEngine.Random.Range(0f, totalWeight);

            for (int i = 0; i < attacks.Length; i++)
            {
                AttackPattern attack = attacks[i];
                if (!IsInRange(attack, distanceToTarget)) continue;

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

        // 거리 조건 판정. minRange가 활을 후보에서 빼는 순간이 "붙으면 근접"의 전부다.
        private static bool IsInRange(AttackPattern attack, float distanceToTarget)
            => IsValidAttack(attack)
                && distanceToTarget <= attack.range
                && distanceToTarget >= attack.minRange;

        // 투사체를 쏘는 공격이 하나라도 있는지. 스포너를 찾을지 판단하는 데만 쓴다.
        // 조우 사격(detectAttack)만 원거리인 몬스터도 있으므로 그쪽도 함께 본다.
        private bool HasProjectileAttack()
        {
            if (detectAttack != null && detectAttack.kind == AttackKind.Projectile) return true;

            if (attacks == null) return false;

            for (int i = 0; i < attacks.Length; i++)
            {
                if (IsValidAttack(attacks[i]) && attacks[i].kind == AttackKind.Projectile)
                    return true;
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            // 공격별 판정 미리보기. Enemy 감지 반경(노랑)과 구분되게 근접 히트박스는 빨간색을 쓴다.
            if (attacks != null)
            {
                for (int i = 0; i < attacks.Length; i++)
                {
                    AttackPattern attack = attacks[i];
                    if (attack == null) continue;

                    if (attack.kind == AttackKind.Projectile)
                    {
                        DrawMuzzleGizmo(attack);
                        continue;
                    }

                    DrawHitBoxGizmo(attack.hitBox, Color.red);
                }
            }

            // 조우 패턴 미리보기. 근접이면 히트박스(주황), 원거리면 발사 방향(하늘색).
            if (detectAttack != null)
            {
                if (detectAttack.kind == AttackKind.Projectile)
                    DrawMuzzleGizmo(detectAttack);
                else
                    DrawHitBoxGizmo(detectAttack.hitBox, new Color(1f, 0.5f, 0f, 1f));
            }

            DrawLineOfSightGizmo();
        }

        // 히트박스 Transform의 실제 판정 부피(위치·회전·스케일)를 와이어큐브로 그린다.
        private static void DrawHitBoxGizmo(Transform hitBox, Color color)
        {
            if (hitBox == null) return;

            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(hitBox.position, hitBox.rotation, hitBox.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.matrix = Matrix4x4.identity;
        }

        // 발사 방향 미리보기(하늘색). 투사체는 muzzle의 forward로 직진하므로,
        // 이 선이 플레이어 몸통 높이를 지나는지가 곧 명중 여부다.
        private void DrawMuzzleGizmo(AttackPattern attack)
        {
            if (attack.muzzle == null) return;

            Quaternion rotation = attack.muzzle.rotation * Quaternion.Euler(attack.rotationOffset);
            Vector3 origin = attack.muzzle.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(origin, 0.1f);
            Gizmos.DrawLine(origin, origin + rotation * Vector3.forward * attack.range);
        }

        // 시야 레이 미리보기. 초록=보임, 빨강=막힘(막힘 판정은 타겟이 잡히는 플레이 중에만).
        // 눈높이/겨냥 높이가 지형에 걸리지 않는지 눈으로 튜닝할 때 쓴다.
        private void DrawLineOfSightGizmo()
        {
            if (obstacleMask.value == 0) return;

            Vector3 origin = transform.position + Vector3.up * eyeHeight;

            // Enemy는 Awake에서 캐싱되므로 에디터 모드에서는 타겟을 알 수 없다.
            // 그때는 눈높이 위치만 표시해 오프셋을 확인할 수 있게 한다.
            if (enemy == null || enemy.Target == null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(origin, 0.12f);
                return;
            }

            Gizmos.color = HasLineOfSight() ? Color.green : Color.red;
            Gizmos.DrawLine(origin, enemy.Target.position + Vector3.up * targetAimHeight);
        }
    }
}
