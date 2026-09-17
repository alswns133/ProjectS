using System.Collections.Generic;
using Mirror;
using ProjectS.Core;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.Enemies
{
    ﻿using UnityEngine;

    /// <summary>
    /// 몬스터의 중앙 컨텍스트. 기능 컴포넌트를 한곳에서 보유하고 상태 머신을 구동한다.
    /// 순찰/발견/추격/공격/피격/사망의 큰 흐름만 중재하고,
    /// 실제 이동·애니메이션·판정은 EnemyMovement/EnemyAnimation/EnemyCombat과 각 State가 담당한다.
    /// 상태들은 이 Enemy를 통해 필요한 컴포넌트와 튜닝 값에 접근한다(Player와 같은 컨텍스트 패턴).
    /// </summary>
    // RequireComponent: Enemy를 붙이면 필수 부품들이 자동으로 함께 추가된다.
    // → 프리팹 제작 중 컴포넌트 누락으로 런타임 null이 나는 실수를 구조적으로 줄인다.
    [RequireComponent(typeof(EnemyStats))]
    [RequireComponent(typeof(EnemyMovement))]
    [RequireComponent(typeof(EnemyAnimation))]
    [RequireComponent(typeof(EnemyCombat))]
    // 미니맵 등록도 부품처럼 강제한다. MinimapMarkerSource의 type 기본값이 Enemy라
    // 몬스터는 자동 추가만으로 별도 설정 없이 미니맵에 잡힌다.
    [RequireComponent(typeof(MinimapMarkerSource))]
    public class Enemy : MonoBehaviour, ILaunchable
    {
        // ── 순찰 ─────────────────────────────────────────────────────────
        // 순찰 지점. 비워두면 제자리 대기(Idle)로 시작하고,
        // 하나 이상 넣으면 PatrolState에서 지점들을 순서대로 돈다.
        // 순찰 경로 판단은 상태가 담당하고, "어디를 돌지"라는 데이터만 컨텍스트가 소유한다.
        [Header("순찰")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolWaitTime = 1f;

        // 스폰 지점 기준 랜덤 배회 반경. patrolPoints를 비워 두고 이 값을 주면
        // 고정 경로 없이 스폰 주변을 무작위로 돈다(여러 마리가 뭉쳐도 목적지가 흩어져 한 점에 안 몰린다).
        // 기본 0 = 배회 안 함(기존 몬스터 동작 유지). 배회를 원하는 프리팹에만 반경을 넣어 켠다.
        [SerializeField, Min(0f)] private float patrolRadius = 0f;

        // 순찰 목적지까지 이 시간 안에 도착하지 못하면 "막혔다"로 보고 새 지점을 다시 뽑는다.
        // 30마리가 밀집해 서로 경로가 겹치면 도착 판정이 영영 안 서서 순찰이 멈추는데,
        // 이 타임아웃이 있어야 막힌 개체가 다른 목적지로 흩어져 대열이 자연히 풀린다.
        [SerializeField, Min(0.5f)] private float patrolStuckTimeout = 4f;

        // ── 이동 속도 ────────────────────────────────────────────────────
        // 상태별 이동 속도. 순찰은 Walk, 추격은 Run으로 애니메이션이 명확히 갈리도록
        // 블렌드 트리 임계값에 맞춰 값을 잡는다(순찰≈Walk 클립 속도, 추격≈Run 클립 속도).
        [Header("이동 속도")]
        [SerializeField, Min(0f)] private float patrolSpeed = 1f;
        [SerializeField, Min(0f)] private float chaseSpeed = 4f;

        // ── 발견 ─────────────────────────────────────────────────────────
        // 감지 설정. Idle/Patrol에서 플레이어가 detectionRange 안에 들어오면 DetectState로 진입한다.
        // 바로 Chase로 가지 않는 이유: "플레이어 발견" 연출과 짧은 대시를 하나의 커밋 동작으로 보장하기 위함.
        // 발견 상태 유지 시간은 별도 값이 아니라 발견 클립 길이를 따른다(DetectState가 애니메이터에서 읽음).
        [Header("감지")]
        [SerializeField] private float detectionRange = 8f;

        // 발견 순간 플레이어 쪽으로 짧게 달려들지 여부. 발견 애니메이션과 한 몸인 설정이다.
        // 근접 몬스터는 발견 모션이 달려드는 연출이라 켜 두고,
        // 원거리 몬스터는 발견 즉시 제자리에서 겨누는 모션이라 꺼야 한다.
        // 켜 두면 겨누는 모션을 재생하면서 몸이 미끄러져 애니메이션과 이동이 따로 논다.
        [SerializeField] private bool useDetectDash = true;
        [SerializeField] private float detectDashSpeedMultiplier = 1.8f;
        [SerializeField, Min(0f)] private float targetNavMeshSampleRadius = 0.5f;
        [SerializeField, Min(0.02f)] private float targetPathCheckInterval = 0.25f;

        // 경계 상황(플레이어 점프, NavMesh 가장자리)에서 경로 검사 결과가 매번 뒤집히며
        // 추격↔대기가 떨리는 것을 막는다. 반대 결과가 이 횟수만큼 연속돼야 판정을 바꾼다.
        // targetPathCheckInterval × 이 값 = 판정이 바뀌는 데 필요한 최소 시간.
        [SerializeField, Min(1)] private int reachResultConfirmCount = 3;

        // ── 피격 경직 ────────────────────────────────────────────────────
        // 피격 경직 설정. 일반 몬스터는 켜고, 보스/슈퍼아머 몬스터는 끄는 식으로 쓴다.
        // hitStunCooldown은 연타를 맞을 때 매 프레임 HitState로 재진입하는 것을 막는 안전장치다.
        // "피격마다 무조건 경직"으로 두면 다단히트/장판에 갇혀 공격과 추격을 영영 못 할 수 있다.
        [Header("피격 반응")]
        [SerializeField] private bool useHitStun = true;
        [SerializeField] private float hitStunDuration = 0.35f;
        [SerializeField] private float hitStunCooldown = 0.2f;

        [Header("사망")]
        [SerializeField] private float despawnDelay = 3f;

        // ── 무력화(그로기) ────────────────────────────────────────────────
        // 그로기 게이지가 0이 됐을 때 무력화 상태로 굳어 있는 시간. 클립 길이와 무관한 기획 밸런스 값이라
        // 인스펙터에 둔다(무력화 모션은 이 시간 동안 루프한다). 그로기 자체가 없는 일반몹은 쓰지 않는다.
        [Header("무력화(그로기)")]
        [SerializeField, Min(0f)] private float groggyDuration = 5f;

        // ── 군중 제어 ────────────────────────────────────────────────────
        // 여러 근접 몬스터가 플레이어 한 점으로 몰려 겹치고 떠는 것을 막는 설정.
        // 슬롯 같은 중앙 관리 시스템 없이 "개체별 선호 교전 거리 + 앞이 막히면 옆으로 돌기"만으로
        // 분산시킨다 → 슬롯 배정/반납 상태가 없어 누수·경합으로 망가질 여지가 없다.
        // 교전 거리는 AttackRange의 배수라 몬스터 크기(사거리)에 자동으로 비례한다.
        [Header("군중 제어")]
        // 선호 교전 거리 = AttackRange × 이 범위에서 뽑은 개체별 랜덤 배수.
        // Min~Max 폭이 넓을수록 서는 거리가 흩어져 자연스럽고, 좁을수록 정렬된 느낌이 난다.
        // 상한을 0.95로 막은 이유: 1.0 이상이면 목적지가 공격 사거리 밖이 되어 영영 공격을 못 한다.
        [SerializeField, Range(0.5f, 0.95f)] private float combatDistanceFactorMin = 0.7f;
        [SerializeField, Range(0.5f, 0.95f)] private float combatDistanceFactorMax = 0.9f;

        // 아군 차단 SphereCast의 두께(반경). 몬스터 몸통 폭에 맞춘다.
        // 너무 크면 옆을 스쳐 가는 아군에도 막힌 것으로 판정해 불필요하게 옆으로 돈다.
        [SerializeField, Min(0f)] private float allyBlockRadius = 0.4f;

        // 아군 차단을 검사하는 전방 거리. 이 안에 아군이 있으면 "막혔다"로 판정한다.
        // 짧으면 이미 몸이 닿은 뒤에야 돌기 시작하고, 길면 멀리 있는 아군에도 미리 돌아 대열이 벌어진다.
        [SerializeField, Min(0f)] private float allyBlockDistance = 1.2f;

        // SphereCast 시작점의 높이(발밑 기준). 지면 콜라이더나 낮은 장식물에 걸리지 않게
        // 몸통 높이쯤에서 쏘기 위한 오프셋이다. 0이면 바닥을 긁어 오탐이 날 수 있다.
        [SerializeField, Min(0f)] private float allyBlockProbeHeight = 0.5f;

        // 앞이 막혔을 때 목적지를 트는 각도(타겟 중심 접선 방향). 방향은 개체별 좌/우 랜덤 고정.
        // 작으면 옆으로 도는 티가 안 나서 계속 뒤에서 밀고, 크면 크게 우회해 포위가 빨라지는 대신 동선이 과장돼 보인다.
        [SerializeField, Range(0f, 90f)] private float allySidestepAngle = 45f;

        // 개체별 포위 각도 범위(±도). 접근 방향에 개체마다 고정 랜덤 각을 더해
        // 같은 방향에서 온 개체들도 플레이어 주위 서로 다른 방위로 갈라져 들어간다.
        // 0이면 접근 방향 그대로 목적지를 잡아 한쪽에 뭉치고, 클수록 뒤까지 도는 완전 포위에 가까워진다.
        [SerializeField, Range(0f, 180f)] private float surroundAngleRange = 80f;

        [Header("경직 시간")]
        // Hit 모션 후 다음 행동까지의 추가 경직
        [SerializeField] private float hitRecoveryDelay = 0.4f;
        public float HitRecoveryDelay => hitRecoveryDelay;

        /// <summary>HP와 사망 판정을 소유하는 스탯 컴포넌트.</summary>
        public EnemyStats Stats { get; private set; }
        /// <summary>NavMeshAgent 기반 이동과 정지/재개를 담당하는 이동 컴포넌트.</summary>
        public EnemyMovement Movement { get; private set; }
        /// <summary>Animator 파라미터와 트리거를 제어하는 애니메이션 브릿지.</summary>
        public EnemyAnimation Animation { get; private set; }
        /// <summary>공격 패턴 선택, 쿨다운, 히트 판정을 담당하는 전투 컴포넌트.</summary>
        public EnemyCombat Combat { get; private set; }
        /// <summary>몬스터 로컬 파티클 이펙트를 의미 단위로 재생하는 선택 컴포넌트.</summary>
        public EnemyEffects Effects { get; private set; }
        /// <summary>그로기(무력화) 게이지를 소유하는 선택 컴포넌트. 보스에만 붙고, 없으면 null이다.</summary>
        public EnemyGroggy Groggy { get; private set; }
        /// <summary>현재 Enemy 상태 하나를 보유하고 전환 순서를 보장하는 상태 머신.</summary>
        public EnemyStateMachine StateMachine { get; private set; }

        // 상태 인스턴스도 상태끼리 전환할 때 참조하므로 읽기 전용 공개.
        // Awake에서 한 번만 생성해 재사용한다 → 전환 때마다 new 하지 않아 GC 부담이 없다.
        /// <summary>제자리 대기 상태. 순찰 지점이 없을 때 시작 상태로 사용된다.</summary>
        public EnemyIdleState IdleState { get; private set; }
        /// <summary>웨이포인트 순찰 상태. 순찰 지점이 있을 때 시작 상태로 사용된다.</summary>
        public EnemyPatrolState PatrolState { get; private set; }
        /// <summary>플레이어 최초 발견 연출과 대시를 처리하는 상태.</summary>
        public EnemyDetectState DetectState { get; private set; }
        /// <summary>플레이어를 추적하고 공격 진입을 판단하는 상태.</summary>
        public EnemyChaseState ChaseState { get; private set; }
        /// <summary>레이드 보스 교전 로코모션 상태. RaidBossLocomotion이 붙은 보스에만 생성된다(없으면 null).</summary>
        public RaidBossEngageState EngageState { get; private set; }
        /// <summary>발견 후 진입할 교전 상태. 레이드 보스면 EngageState, 그 외엔 기존 ChaseState.</summary>
        public EnemyBaseState AggroState => EngageState != null ? EngageState : ChaseState;
        /// <summary>선택된 공격 패턴을 재생하는 상태.</summary>
        public EnemyAttackState AttackState { get; private set; }
        /// <summary>피격 경직과 피격 연출을 처리하는 상태.</summary>
        public EnemyHitState HitState { get; private set; }

        /// <summary>하루 강공격에 맞으면 진입한다. (공중 런처 피격 상태)</summary>
        public EnemyLaunchState LaunchState { get; private set; }

        /// <summary>그로기 게이지가 0이 되면 진입하는 무력화 상태. 보스에만 실질적으로 쓰인다.</summary>
        public EnemyGroggyState GroggyState { get; private set; }

        /// <summary>사망 연출과 AI/충돌 비활성화를 처리하는 최종 상태.</summary>
        public EnemyDeadState DeadState { get; private set; }

        /// <summary>추적 대상(플레이어). 씬에 없으면 null이며, 상태들은 대상이 없으면 자연스럽게 대기한다.</summary>
        public Transform Target { get; private set; }

        // 대상의 생사 판정용. Transform만으로는 죽었는지 알 수 없어 따로 캐싱한다.
        private IDamageable targetDamageable;

        // 대상이 플레이어일 때의 참조. 원격 클라 플레이어의 생사는 서버 쪽 사본(IDamageable)으로 알 수 없어 따로 판정한다.
        private Player targetPlayer;

        /// <summary>
        /// 추적 대상이 살아 있는지 여부. 생사를 알 수 없는 대상(IDamageable이 없는 경우)은
        /// 살아 있는 것으로 본다 — 모르는 이유로 몬스터가 멈춰 서는 것보다 낫다.
        /// </summary>
        /// <remarks>
        /// 원격 클라 플레이어는 서버 쪽 사본이 데미지를 받지 않아(EnemyHitRouter가 오너에게 보냄) 사본 HP가 항상 가득이다.
        /// 그래서 오너가 올리는 HP 비율로 본다 — 안 그러면 보스가 쓰러진 플레이어를 계속 노린다.
        /// </remarks>
        public bool IsTargetAlive => targetPlayer != null
            ? IsPlayerAlive(targetPlayer)
            : targetDamageable == null || !targetDamageable.IsDead;

        /// <summary>
        /// 레이드 보스인지(교전 로코모션 <see cref="RaidBossLocomotion"/> 보유). 레이드 전용 어그로 규칙을 이 값으로 가른다.
        /// </summary>
        public bool IsRaidBoss => EngageState != null;

        /// <summary>피격용 루트 콜라이더. 사망 시 추가 피격과 물리 충돌을 막기 위해 DeadState가 끈다.</summary>
        public Collider BodyCollider { get; private set; }

        /// <summary>플레이어 감지 반경. Idle/Patrol 상태가 발견 진입 판정에 쓴다.</summary>
        public float DetectionRange => detectionRange;
        /// <summary>
        /// 순찰(배회)로 시작할지 여부. 고정 순찰 지점이 있거나 랜덤 배회 반경이 잡혀 있으면 PatrolState로,
        /// 둘 다 없으면 IdleState로 시작한다.
        /// </summary>
        public bool HasPatrol => (patrolPoints != null && patrolPoints.Length > 0) || patrolRadius > 0f;
        /// <summary>순찰 지점/배회 지점에 도착한 뒤 다음 지점으로 가기 전 대기 시간(그 사이 Idle 모션).</summary>
        public float PatrolWaitTime => patrolWaitTime;
        /// <summary>순찰 목적지에 이 시간 안에 못 가면 막힘으로 보고 새 지점을 뽑는 타임아웃.</summary>
        public float PatrolStuckTimeout => patrolStuckTimeout;
        /// <summary>순찰(배회) 이동 속도. Walk 애니메이션이 나오도록 블렌드 트리 Walk 임계값 근처로 잡는다.</summary>
        public float PatrolSpeed => patrolSpeed;
        /// <summary>추격 이동 속도. Run 애니메이션이 나오도록 블렌드 트리 Run 임계값 근처로 잡는다.</summary>
        public float ChaseSpeed => chaseSpeed;
        /// <summary>
        /// 발견 시 짧은 대시 연출을 사용하는지 여부. 발견 애니메이션이 제자리 모션인
        /// 원거리 몬스터는 꺼서 이동 없이 발견 연출만 재생하게 한다.
        /// </summary>
        public bool UseDetectDash => useDetectDash;
        /// <summary>발견 대시 중 NavMeshAgent 기본 속도에 곱할 배수.</summary>
        public float DetectDashSpeedMultiplier => detectDashSpeedMultiplier;
        /// <summary>피격 상태가 유지되는 시간.</summary>
        public float HitStunDuration => hitStunDuration;
        /// <summary>사망 연출 후 오브젝트가 사라지기까지의 시간.</summary>
        public float DespawnDelay => despawnDelay;
        /// <summary>무력화 상태로 굳어 있는 시간(초). EnemyGroggyState가 이 시간 뒤 게이지를 리필하고 복귀한다.</summary>
        public float GroggyDuration => groggyDuration;

        private float nextHitStunTime;
        private float nextPathCheckTime;
        private bool cachedCanReachTarget;
        private int reachFlipStreak;

        // 스폰(배치) 위치. 랜덤 배회의 중심으로 쓴다. Awake 시점의 위치라 씬에 놓인 자리를 기준으로 삼는다.
        private Vector3 spawnPosition;
        // 고정 순찰 지점 순환 인덱스. 랜덤 배회에서는 쓰지 않는다.
        private int patrolIndex;

        // 군중 제어 개체값. Awake에서 한 번 뽑아 고정한다.
        private float combatDistanceFactor;
        private float sidestepSign;
        private float surroundAngleOffset;
        private readonly RaycastHit[] allyBlockHits = new RaycastHit[8];

        // 네트워크 스폰 여부·서버 여부 판정용. 싱글(로컬 스폰) 적은 컴포넌트가 있어도 netId가 0이다.
        private NetworkIdentity networkIdentity;

        /// <summary>
        /// 이 컴퓨터가 이 적의 <b>전투 판정 권한</b>을 갖는가. 싱글(로컬 스폰)이거나, 네트워크 스폰이면 서버일 때만 true.
        /// </summary>
        /// <remarks>
        /// ★ Animation Event는 <b>꺼진 컴포넌트에도 전달</b>된다. 그래서 서버 권위 보스의 클립을 재생하는 모든 클라에서도
        /// 타격·잡기 이벤트가 불린다. 판정 이벤트는 반드시 이 값으로 막는다 — 안 막으면 컴퓨터마다 판정이 달라진다
        /// (2026-09-17 "잡기 후속타가 잡힌 본인 화면에만 보임"의 원인). 이펙트·카메라 흔들림 같은 연출 이벤트는 막지 않는다.
        /// </remarks>
        public bool HasGameplayAuthority
            => networkIdentity == null || networkIdentity.netId == 0 || networkIdentity.isServer;

        private void Awake()
        {
            TryGetComponent(out networkIdentity);

            // 컴포넌트 캐싱은 Awake에서 1회만. 상태들이 매 프레임 GetComponent를 하지 않게 한다.
            Movement = GetComponent<EnemyMovement>();
            Animation = GetComponent<EnemyAnimation>();
            Stats = GetComponent<EnemyStats>();
            Combat = GetComponent<EnemyCombat>();
            Effects = GetComponent<EnemyEffects>();
            Groggy = GetComponent<EnemyGroggy>();
            BodyCollider = GetComponent<Collider>();
            StateMachine = new EnemyStateMachine();

            // 랜덤 배회의 중심이 되는 스폰 위치를 처음 자리로 고정한다.
            spawnPosition = transform.position;

            // 상태를 미리 만들어 보관한다. 각 상태는 MonoBehaviour가 아니므로 Enemy 컨텍스트를 생성자 주입으로 받는다.
            IdleState = new EnemyIdleState(this);
            PatrolState = new EnemyPatrolState(this);
            DetectState = new EnemyDetectState(this);
            ChaseState = new EnemyChaseState(this);
            AttackState = new EnemyAttackState(this);
            HitState = new EnemyHitState(this);
            LaunchState = new EnemyLaunchState(this);
            GroggyState = new EnemyGroggyState(this);
            DeadState = new EnemyDeadState(this);

            // RaidBossLocomotion이 붙어 있으면 레이드 교전 상태를 만든다(옵트인 신호). 없는 몹은 null → 기존 Chase 사용.
            if (GetComponent<RaidBossLocomotion>() != null)
                EngageState = new RaidBossEngageState(this);

            // 군중 제어 개체값: 같은 프리팹이라도 교전 거리와 회피 방향이 미묘하게 달라진다.
            // 여러 마리가 몰려와도 한 원 위에 정렬된 "레일 위를 움직이는 AI" 느낌이 나지 않게 하기 위함.
            combatDistanceFactor = Random.Range(
                combatDistanceFactorMin, Mathf.Max(combatDistanceFactorMin, combatDistanceFactorMax));
            sidestepSign = Random.value < 0.5f ? -1f : 1f;
            surroundAngleOffset = Random.Range(-surroundAngleRange, surroundAngleRange);
        }

        private void OnEnable()
        {
            PlayerEvents.OnPlayerDied += OnTargetDied;
        }

        private void OnDisable()
        {
            PlayerEvents.OnPlayerDied -= OnTargetDied;
        }

        protected virtual void Start()
        {
            AcquireTarget();

            // 순찰 지점이 있으면 순찰 몬스터, 없으면 제자리 대기 몬스터로 시작한다.
            StateMachine.ChangeState(HasPatrol ? PatrolState : IdleState);
        }

        private void Update()
        {
            // 등장 연출 등으로 AI를 재운 동안엔 상태 머신을 돌리지 않는다 → 추격/공격/상태 전환이 멈추고
            // Timeline이 보스를 온전히 몬다. HaltForSceneExit(enabled=false, 되돌리지 않음)와 달리
            // 재개(ResumeAI)가 있어야 해서 enabled 대신 플래그로 이 Update만 건너뛴다(구독은 유지).
            if (aiSuspended) return;

            // 대상이 죽으면 기다리지 않고 곧바로 다른 대상을 찾는다(어그로 규칙: 죽으면 다른 플레이어에게 끌린다).
            // 살아 있는 대상이 하나도 없으면 Target이 null이 되어 아래 주기 탐색으로 넘어가므로 매 프레임 탐색하지 않는다.
            if (Target != null && !IsTargetAlive) nextTargetReacquireTime = 0f;

            // 타깃이 없거나(아바타가 아직 안 떴거나 이탈·전원 사망) 비활성이면 주기적으로 다시 찾는다.
            // 파티 레이드에서 보스가 아바타보다 먼저 스폰되거나, 부활한 플레이어를 다시 잡는 경로다.
            // 매 프레임 탐색은 비싸서 간격을 둔다.
            bool needsTarget = Target == null || !Target.gameObject.activeInHierarchy || !IsTargetAlive;
            if (needsTarget && Time.time >= nextTargetReacquireTime)
            {
                nextTargetReacquireTime = Time.time + TargetReacquireInterval;
                AcquireTarget();
            }

            if (IsRaidBoss) UpdateRaidBossEngagement();

            StateMachine.Update();
            //Debug.Log(StateMachine.Current);
        }

        // 레이드 보스 어그로 규칙(2026-09-17 사용자 확정)의 "전원 사망 → 대기 / 부활 → 교전 재개" 부분.
        // 레이드는 아레나 전체가 전장이라 감지 거리로 재발견을 기다리지 않는다 — 대상이 생기면 곧바로 교전한다.
        private void UpdateRaidBossEngagement()
        {
            if (Target == null)
            {
                // 전원 사망: 쫓던 중이면 그 자리에 멈춰 대기한다. 공격 모션 중이면 끝난 뒤 교전 상태로 돌아와 여기서 멈춘다.
                if (StateMachine.Current == EngageState)
                {
                    Movement.StopAndClearPath();
                    Animation.SetSpeedImmediate(0f);
                    StateMachine.ChangeState(IdleState);
                }
                return;
            }

            // 살아 있는 대상이 생겼다(전투 시작·부활): 대기 중이면 곧바로 교전을 재개한다.
            if (StateMachine.Current == IdleState)
                StateMachine.ChangeState(AggroState);
        }

        /// <summary>
        /// 공격 패턴 하나가 끝났다(<see cref="EnemyAttackState"/>의 Exit). 레이드 보스는 여기서 다음 대상을
        /// 살아 있는 플레이어 중 무작위로 다시 고른다(어그로 규칙: 패턴마다 랜덤).
        /// </summary>
        public void OnAttackPatternFinished()
        {
            if (IsRaidBoss && HasGameplayAuthority) AcquireTarget();
        }

        // 등장 연출 동안 AI를 재우는 게이트. SuspendAI에서 켜고 ResumeAI에서 끈다.
        private bool aiSuspended;

        // 타깃 재획득 간격(초)과 다음 시도 시각. 밸런스가 아니라 탐색 비용을 줄이는 시스템 나사라 상수로 둔다.
        private const float TargetReacquireInterval = 1f;
        private float nextTargetReacquireTime;

        /// <summary>
        /// 추격할 플레이어를 찾아 <see cref="Target"/>에 건다. 스폰 시(Start) 1회 부르고, 타깃이 없거나 비활성이면
        /// <see cref="Update"/>가 주기적으로 다시 부른다.
        ///
        /// <para>참조 단일 창구: 지속 플레이어는 PlayerManager에서 pull한다(부트스트랩 Awake에서 이미 생성됨).
        /// FindAnyObjectByType로 직접 찾으면 안 되는 이유: 스폰 전 지속 플레이어는 SetActive(false)라
        /// FindAnyObjectByType가 비활성 오브젝트를 못 찾아 Target이 null로 굳는다(부트→마을→던전에서 감지 무반응 버그).</para>
        ///
        /// <para><b>네트워크(파티 레이드) 대응 — 보스가 가만히 서 있던 원인.</b> 전용 서버는 PlayerManager가 로컬
        /// 플레이어를 아예 만들지 않고(EnsurePlayer의 IsServerMode early return), 호스트는 OwnerGate가 로컬 플레이어를
        /// Hide(SetActive(false))한다. 즉 레이드의 진짜 플레이어는 서버가 스폰한 <b>네트워크 아바타</b>다.
        /// 그래서 로컬 플레이어가 없거나 비활성이면 <b>자기 씬 안</b>의 활성 플레이어 중 가장 가까운 대상을 잡는다.
        /// 씬으로 좁히는 이유: additive로 파티 인스턴스가 여럿 열려 있을 때 다른 파티의 플레이어를 타깃으로
        /// 잡아 엉뚱한 곳으로 달려가는 것을 막기 위함이다.</para>
        /// </summary>
        private void AcquireTarget()
        {
            // 레이드 보스: 살아 있는 플레이어 중 무작위(어그로 규칙). 아무도 없으면 대상 없음 → 대기.
            if (IsRaidBoss)
            {
                SetTarget(PickRandomAlivePlayer());
                return;
            }

            Player local = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;

            // ① 로컬 지속 플레이어가 '활성'이고 살아 있으면 그대로 쓴다(싱글·마을·솔로 던전 경로 유지).
            //    지속 플레이어는 DontDestroyOnLoad라 씬 비교 대상이 아니므로 아래 씬 탐색으로는 잡히지 않는다.
            Player player = (local != null && local.gameObject.activeInHierarchy && IsPlayerAlive(local)) ? local : null;

            // ② 없으면(전용 서버=아예 없음 / 호스트=OwnerGate가 Hide) 자기 씬의 네트워크 아바타를 찾는다.
            if (player == null) player = FindNearestPlayerInScene();

            // ③ 그래도 없으면 비활성 로컬 플레이어라도 건다 — 씬 진입 전(활성화 대기) Target이 null로
            //    굳어 감지가 영영 무반응이 되는 기존 버그를 막기 위한 원래 동작이다.
            //    단 죽은 플레이어는 걸지 않는다(대상이 죽었으면 매 프레임 재탐색으로 빠지므로).
            if (player == null && local != null && IsPlayerAlive(local)) player = local;

            if (player == null) return;   // 아직 아무도 없음 → 다음 주기에 다시 시도

            SetTarget(player);
        }

        // 대상과 생사 판정용 참조를 한꺼번에 바꾼다. null이면 대상 없음.
        private void SetTarget(Player player)
        {
            targetPlayer = player;
            Target = player != null ? player.transform : null;

            // Player가 이미 Awake에서 캐싱해 둔 것을 그대로 받는다(Start는 모든 Awake 이후라 안전).
            targetDamageable = player != null ? player.Stats : null;
        }

        // 후보 수집 버퍼(할당 방지).
        private readonly List<Player> targetCandidates = new();

        // 이 적과 같은 전장의 살아 있는 플레이어 중 무작위 하나. 없으면 null.
        private Player PickRandomAlivePlayer()
        {
            targetCandidates.Clear();

            // 싱글: 로컬 지속 플레이어(DDoL이라 씬 비교 대상이 아님).
            Player local = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (local != null && local.gameObject.activeInHierarchy && IsPlayerAlive(local)) targetCandidates.Add(local);

            // 멀티: 자기 씬(파티 인스턴스)의 네트워크 아바타. 다른 파티 인스턴스의 플레이어는 제외한다.
            foreach (Player candidate in FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (candidate == null || candidate == local || !candidate.gameObject.activeInHierarchy) continue;
                if (candidate.gameObject.scene != gameObject.scene) continue;
                if (!IsPlayerAlive(candidate)) continue;

                targetCandidates.Add(candidate);
            }

            return targetCandidates.Count > 0 ? targetCandidates[Random.Range(0, targetCandidates.Count)] : null;
        }

        // 플레이어 생사. 원격 클라 플레이어는 오너가 올린 HP 비율로, 그 외는 실제 PlayerStats로 본다.
        private static bool IsPlayerAlive(Player player)
        {
            if (player == null) return false;
            if (EnemyHitRouter.TryGetRemoteAvatar(player, out NetworkDamageRelay relay)) return relay.ServerIsOwnerAlive;
            return player.Stats == null || !player.Stats.IsDead;
        }

        // 같은 씬의 활성 플레이어 중 가장 가까운 대상. 파티 레이드의 네트워크 아바타를 잡는 경로이며,
        // 2인 이상이면 가까운 쪽이 어그로 대상이 된다.
        private Player FindNearestPlayerInScene()
        {
            Player[] candidates = FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            Player nearest = null;
            float bestSqr = float.PositiveInfinity;

            foreach (Player candidate in candidates)
            {
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                if (candidate.gameObject.scene != gameObject.scene) continue;   // 다른 파티 인스턴스 배제
                if (!IsPlayerAlive(candidate)) continue;                         // 죽은 플레이어는 대상이 아니다

                float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                nearest = candidate;
            }

            return nearest;
        }

        /// <summary>
        /// 등장 연출(보스 Timeline 등) 동안 AI를 멈춰 연출이 몬스터를 온전히 제어하게 한다.
        /// 상태 머신 Update를 게이트로 끊고, NavMeshAgent를 꺼 위치 소유권을 연출/루트모션에 넘긴다
        /// (에이전트를 켜 두면 매 프레임 위치를 플레이어 쪽으로 덮어써 "연출대로 안 움직이고 추격"이 된다).
        /// 루트모션 연출을 살리려 <see cref="EnemyMovement.BeginRootMotion"/>로 켠다 —
        /// OnAnimatorMove가 클립 이동량을 위치에 더하게 해야 제자리 재생이 되지 않는다.
        /// <see cref="BossIntroDirector"/>가 Timeline Play 직전에 호출한다.
        /// </summary>
        public void SuspendAI()
        {
            aiSuspended = true;
            Movement.BeginRootMotion();
        }

        /// <summary>
        /// 등장 연출이 끝나면 AI를 재개한다. 에이전트를 연출로 옮겨진 최종 위치의 가까운 NavMesh 지점으로
        /// 복귀시키고(<see cref="EnemyMovement.EndRootMotionAndLand"/>), 곧바로 교전 흐름으로 진입한다
        /// (연출 자체가 "발견"이므로 레이드=Engage, 그 외=Chase).
        /// <see cref="BossIntroDirector"/>가 연출 종료 시 호출한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>감지 거리를 보지 않는다(2026-09-17 변경).</b> 예전에는 <see cref="CanDetectTarget"/>으로 "감지 거리 안이면 교전,
        /// 밖이면 대기"였다. 보스방 트리거를 밟아 플레이어가 보스 가까이서 연출이 시작되던 구조에 맞춘 규칙이다.
        /// 레이드가 "전원 스폰 완료 후 연출"로 바뀌면서 플레이어는 스폰 지점(보스와 약 58m)에 있고 보스 감지 거리는 20m라,
        /// 연출이 끝나면 보스가 대기 상태로 들어가 누가 다가올 때까지 멈춰 섰다. 연출로 이미 서로를 "발견"했으므로
        /// 살아 있는 대상이 있으면 거리와 무관하게 교전한다.
        /// </para>
        /// <para>
        /// 추적형(Chase) 적은 추적 범위를 벗어나면 스스로 대기로 돌아가므로, 보스방 진입형 연출에서는 결과가 예전과 같다.
        /// </para>
        /// </remarks>
        public void ResumeAI()
        {
            aiSuspended = false;
            Movement.EndRootMotionAndLand();

            // 연출 동안 대상이 바뀌었을 수 있다(파티원 이탈·사망, 스폰 순서). 지금 가장 가까운 살아 있는 대상으로 다시 잡는다.
            AcquireTarget();

            IState next;
            if (Target != null && IsTargetAlive)
                next = AggroState;
            else
                next = HasPatrol ? (IState)PatrolState : IdleState;

            StateMachine.ChangeState(next);
        }

        /// <summary>
        /// 대상까지의 거리. 대상이 없으면 무한대를 돌려주어
        /// 감지/공격 판정이 조건문 없이 자연스럽게 실패하게 한다.
        /// </summary>
        public float DistanceToTarget()
            => Target != null ? Vector3.Distance(transform.position, Target.position) : float.PositiveInfinity;

        /// <summary>
        /// 감지 거리 안에 살아 있는 대상이 있으면 발견을 허용한다.
        /// 생사를 함께 보는 이유: 플레이어가 죽은 뒤 Idle로 돌려놔도 이 판정이 거리만 보면
        /// 다음 프레임에 곧바로 시체를 다시 발견해 추격이 재개된다.
        /// </summary>
        public bool CanDetectTarget()
            => IsTargetAlive && DistanceToTarget() <= detectionRange;

        /// <summary>
        /// 타겟 위치까지 NavMesh 완전 경로가 있는지 주기적으로 검사한다.
        /// 플레이어가 점프로만 갈 수 있는 곳에 있으면 false가 되어 추격 대신 바라보기로 처리된다.
        /// 단발 검사 결과로 바로 판정을 뒤집지 않고 반대 결과가 연속으로 확정 횟수만큼 나와야 바꾼다.
        /// 점프 궤적이나 NavMesh 가장자리처럼 검사마다 결과가 흔들리는 상황에서
        /// 추격↔대기가 매 검사 주기로 떨리는 것을 막기 위함이다.
        /// </summary>
        public bool CanReachTarget()
        {
            if (Target == null) return false;
            if (Time.time < nextPathCheckTime) return cachedCanReachTarget;

            nextPathCheckTime = Time.time + targetPathCheckInterval;

            bool raw = Movement.CanReach(Target.position, targetNavMeshSampleRadius);
            if (raw == cachedCanReachTarget)
            {
                reachFlipStreak = 0;
                return cachedCanReachTarget;
            }

            reachFlipStreak++;
            if (reachFlipStreak >= reachResultConfirmCount)
            {
                cachedCanReachTarget = raw;
                reachFlipStreak = 0;
            }

            return cachedCanReachTarget;
        }

        /// <summary>
        /// 추적 목적지. 플레이어 위치가 아니라 "선호 교전 거리만큼 떨어진 지점"을 돌려준다.
        /// 모든 몬스터가 같은 점을 목표로 하는 한 어떤 회피 알고리즘도 겹침을 못 막기 때문에,
        /// 목적지 자체를 개체별로 분산시킨다. 타겟 방향 앞이 아군에 막혀 있으면
        /// 목적지를 접선 방향으로 틀어 뒤에서 밀지 않고 옆자리로 돌아 들어간다.
        /// ChaseState가 매 프레임 호출한다.
        /// </summary>
        public Vector3 GetChaseDestination()
        {
            if (Target == null) return transform.position;

            Vector3 direction = transform.position - Target.position;
            direction.y = 0f;

            // 타겟과 정확히 겹쳐 방향을 구할 수 없으면 타겟 등 뒤를 기본 방향으로 쓴다.
            if (direction.sqrMagnitude < 0.0001f) direction = -Target.forward;
            direction.Normalize();

            // 개체별 고정 포위각을 더해 같은 방향에서 온 개체들의 목적지 방위를 흩뿌린다.
            direction = Quaternion.AngleAxis(surroundAngleOffset, Vector3.up) * direction;

            // 회전 방향(sidestepSign)은 개체별로 고정한다. 매번 다시 뽑으면
            // 좌우 목적지 사이를 오가며 떨고, 전원이 같은 쪽이면 한 줄로 따라돌기만 한다.
            if (IsBlockedByAlly())
                direction = Quaternion.AngleAxis(allySidestepAngle * sidestepSign, Vector3.up) * direction;

            return Target.position + direction * (Combat.AttackRange * combatDistanceFactor);
        }

        /// <summary>
        /// 타겟 방향 앞쪽에 살아 있는 다른 몬스터가 있는지 검사한다.
        /// NavMeshAgent 회피만으로는 한 점에 몰린 근접 대열에서 뒷줄이 앞줄을 미는 것을 막지 못한다.
        /// </summary>
        public bool IsBlockedByAlly()
        {
            if (Target == null) return false;

            Vector3 direction = Target.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return false;

            direction.Normalize();
            Vector3 origin = transform.position + Vector3.up * allyBlockProbeHeight;
            int hitCount = Physics.SphereCastNonAlloc(
                origin,
                allyBlockRadius,
                direction,
                allyBlockHits,
                allyBlockDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = allyBlockHits[i].collider;
                if (hit == null || hit == BodyCollider) continue;

                Enemy other = hit.GetComponentInParent<Enemy>();
                if (other != null && other != this && !other.Stats.IsDead)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 다음 순찰 목적지를 돌려준다. 고정 지점(patrolPoints)이 있으면 순서대로 순환하고,
        /// 없으면 스폰 지점 주변 반경 안에서 NavMesh 위 무작위 지점을 뽑는다(랜덤 배회).
        /// 유효한 목적지를 못 구하면 현재 위치를 돌려 상태 머신이 멈추지 않게 안전하게 실패한다.
        /// PatrolState가 도착/막힘 때마다 호출한다.
        /// </summary>
        public Vector3 GetPatrolDestination()
        {
            // 고정 순찰 경로 우선. 지점 사이를 순서대로 돈다.
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
                Transform point = patrolPoints[patrolIndex];
                return point != null ? point.position : transform.position;
            }

            // 고정 지점이 없으면 스폰 주변 랜덤 배회.
            if (Movement.TryGetRandomPatrolPoint(spawnPosition, patrolRadius, out Vector3 random))
                return random;

            return transform.position;
        }

        /// <summary>
        /// 피격 반응 진입점. EnemyStats가 데미지를 적용하고 사망하지 않았을 때 호출한다.
        /// 경직 사용 여부와 쿨다운을 확인한 뒤 HitState로 전환한다.
        /// </summary>
        public void OnDamaged()
        {
            if (!useHitStun) return;
            if (Stats.IsDead) return;
            if (StateMachine.Current == DeadState) return;
            // 무력화 중에는 피격 경직으로 덮지 않는다(무방비 연출을 온전히 유지). 보스는 보통 useHitStun=off라
            // 이미 위에서 걸러지지만, 그로기를 쓰는 잡몹 변형이 생겨도 무력화가 끊기지 않게 명시적으로 막는다.
            if (StateMachine.Current == GroggyState) return;

            // 실제로 공중에 떠 있는 동안에만 지상 피격으로 끊지 않는다(런처 저글링 유지).
            // 착지·기상 구간(상태는 Launch, 몸은 지면)이면 아래로 흘러 지상 Hit을 낸다 — Die_Air와 같은 기준.
            bool inLaunch = StateMachine.Current == LaunchState;
            if (inLaunch && LaunchState.IsAirborne) return;

            if (Time.time < nextHitStunTime) return;
            nextHitStunTime = Time.time + hitStunCooldown;

            // ★ 착지한 런처를 지상 Hit으로 넘기기 전에 루트모션을 끝내고 에이전트를 NavMesh로 복귀시킨다.
            if (inLaunch) Movement.EndRootMotionAndLand();

            if (StateMachine.Current == HitState)
                HitState.Rehit();
            else
                StateMachine.ChangeState(HitState);
        }

        /// <summary> 공중에서 사망했는지 여부, DeadState가 Die/Die_Air를 고르는데 사용 </summary>
        public bool DiedAirborne { get; private set; }

        /// <summary>
        /// 사망 진입점. EnemyStats가 HP 0을 확정한 직후 호출한다.
        /// DeadState는 다른 상태로 전환되지 않는 최종 상태다.
        /// </summary>
        public virtual void OnDied()
        {
            // 상태가 아니라 '실제로 떠 있는가'로 지상/공중 사망을 가른다.
            DiedAirborne = StateMachine.Current == LaunchState && LaunchState.IsAirborne;
            StateMachine.ChangeState(DeadState);
        }

        /// <summary>
        /// 소멸(사망 연출이 끝나 오브젝트를 내리는) 진입점. <see cref="State.EnemyDeadState"/>가
        /// DespawnDelay 경과 후 <c>SetActive(false)</c> 대신 이 메서드를 부른다.
        /// 소멸 직전 훅(<see cref="OnDespawn"/>)을 먼저 돌리고 오브젝트를 비활성화한다 —
        /// 잡몹 공유인 DeadState에 보스 전용 분기를 넣지 않고, 파생 클래스가 이 훅만 override하게 하기 위함이다.
        /// </summary>
        public void Despawn()
        {
            OnDespawn();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 소멸 직전 훅. 기본은 no-op이며, 파생 클래스가 사라지는 시점에 할 일을 얹는다
        /// (예: <see cref="Boss"/>가 <c>BossEvents.FireBossDisappeared</c> 발행).
        /// Unity 매직 메서드가 아니라 일반 메서드라 Boss에서 override해도 상태 머신/구독에 영향이 없다.
        /// </summary>
        protected virtual void OnDespawn() { }

        /// <summary>
        /// 씬 전환(예: 던전→마을)으로 이 몬스터가 곧 사라지기 직전, AI와 이동을 즉시 멈춘다.
        /// 씬 Exit는 로딩 시작 시점에 불리고 씬 언로드까지 로딩 바가 도는 동안 이 오브젝트는 살아 있어,
        /// 그대로 두면 이미 숨겨진 플레이어의 마지막 위치로 계속 이동해 로딩 화면 사이 잔상 이동이 보인다.
        /// enabled=false로 상태머신 Update를 끊고, NavMeshAgent도 세워 잔여 경로/속도로 미끄러지지 않게 한다.
        /// 곧 씬과 함께 파괴되므로 재개(Resume)는 제공하지 않는다.
        /// </summary>
        public void HaltForSceneExit()
        {
            Movement.StopAndClearPath();   // 남은 경로·속도 제거(잔상 미끄러짐 방지)
            enabled = false;               // Update() 중단 → 상태머신이 다시 목적지를 찍지 않음
        }

        /// <summary>
        /// 플레이어 사망 시 교전을 멈추고 대기(또는 순찰)로 돌아간다.
        /// 없으면 시체를 계속 쫓아가 때리는 그림이 된다 — 데미지는 IDamageable이 막아 0이지만
        /// 추격·공격 모션은 그대로 나가기 때문이다.
        /// 재발견은 CanDetectTarget의 생사 판정이 막는다.
        /// </summary>
        private void OnTargetDied()
        {
            // 이미 죽은 몬스터에게 DeadState는 최종 상태다. 여기서 되살리면 안 된다.
            if (StateMachine.Current == DeadState) return;

            StateMachine.ChangeState(HasPatrol ? PatrolState : IdleState);
        }

        // protected virtual: 파생 보스(Boss)가 잡기 히트박스 기즈모를 덧그리되 base(감지 반경·군중 제어)를
        // 함께 유지하도록 연다. 에디터 전용이라 런타임(상태 머신·이벤트)에는 영향이 없어 안전한 유일한 seam이다.
        // 런타임 Unity 메시지(Awake/Update 등)는 파생이 재선언하면 base가 안 불려 위험하므로 열지 않는다.
        protected virtual void OnDrawGizmosSelected()
        {
            // 감지 반경 미리보기. 공격 히트박스(빨강)와 구분되게 노란색을 쓴다.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            DrawCombatDistanceGizmos();
            DrawAllyBlockGizmos();
        }

        // 선호 교전 거리 미리보기.
        // 에디터 모드: 개체값이 아직 없으므로 min/max 두 원을 자기 위치에 그려 "설 거리의 범위"를 보여준다.
        // 플레이 모드: 타겟 기준으로 이 개체가 실제 뽑은 교전 원과, 지금 향하고 있는 목적지를 그린다.
        private void DrawCombatDistanceGizmos()
        {
            // Combat은 Awake에서 캐싱되므로 에디터 모드에서는 직접 찾는다(선택된 오브젝트만 그려 부담 없음).
            EnemyCombat combat = Combat != null ? Combat : GetComponent<EnemyCombat>();
            if (combat == null) return;

            float attackRange = combat.AttackRange;
            if (attackRange <= 0f) return;

            if (Application.isPlaying && Target != null)
            {
                // 실제 교전 원(주황)과 현재 목적지(점 + 연결선). 옆으로 도는 중이면 선이 접선 방향으로 꺾인다.
                Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
                Gizmos.DrawWireSphere(Target.position, attackRange * combatDistanceFactor);

                Vector3 destination = GetChaseDestination();
                Gizmos.DrawSphere(destination, 0.15f);
                Gizmos.DrawLine(transform.position, destination);
                return;
            }

            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, attackRange * combatDistanceFactorMin);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, attackRange * combatDistanceFactorMax);
        }

        // 아군 차단 SphereCast 프로브 미리보기. 초록=뚫림, 빨강=막힘(막힘 판정은 플레이 중에만).
        // 캡슐 모양(시작 원 + 끝 원 + 연결선)이 실제 검사 부피와 일치하므로 반경/거리를 눈으로 튜닝할 수 있다.
        private void DrawAllyBlockGizmos()
        {
            Vector3 direction = transform.forward;
            if (Target != null)
            {
                Vector3 toTarget = Target.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
            }

            Gizmos.color = Application.isPlaying && IsBlockedByAlly() ? Color.red : Color.green;

            Vector3 origin = transform.position + Vector3.up * allyBlockProbeHeight;
            Vector3 end = origin + direction * allyBlockDistance;
            Gizmos.DrawWireSphere(origin, allyBlockRadius);
            Gizmos.DrawWireSphere(end, allyBlockRadius);
            Gizmos.DrawLine(origin, end);
        }

        /// <summary>
        /// 무력화 진입점. <see cref="EnemyGroggy"/>의 게이지가 0이 되면 호출한다.
        /// 죽었거나 이미 최종 상태면 무시한다. 전환은 상태 머신을 거쳐 Exit/Enter 순서를 보장한다.
        /// </summary>
        public void EnterGroggy()
        {
            if (Stats.IsDead) return;
            if (StateMachine.Current == DeadState) return;

            StateMachine.ChangeState(GroggyState);
        }

        public void Launch()
        {
            if (!useHitStun) return;
            if (Stats.IsDead) return;
            if (StateMachine.Current == DeadState) return;

            // 이미 공중 상태라면 새로 진입하지 않고 상승을 처음부터 재시작
            // 상태 머신은 같은 상태 재진입을 막으므로 재상승은 상태 내부 Relaunch로 처리
            if (StateMachine.Current == LaunchState)
                LaunchState.Relaunch();
            else
                StateMachine.ChangeState(LaunchState);
        }
    }
}
