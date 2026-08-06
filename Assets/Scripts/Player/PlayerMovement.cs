using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectS.Players
{
    /// <summary>
    /// CharacterController 기반 플레이어 이동 담당.
    /// 카메라 기준 수평 이동, 회전, 중력, 점프 가능 여부를 한곳에서 관리한다.
    /// 자유 이동(FreeMove) 이관으로 점프 대시(공중 대시), 공중 공격 체공/하강,
    /// 공중 피격 체공, 강공격 올려치기 상승, 착지 예고까지 이동 프리미티브로 흡수한다.
    /// 상태별 실행은 상태 클래스가 Move/TickJumpDash를 호출하고, Animator 제어는 하지 않는다
    /// (트리거 발사는 Player/상태가 PlayerAnimation을 통해 담당 — Animator 단일 통로 규칙 유지).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        // 걷기/달리기 속도는 마을/던전에서 다르게 튜닝한다(기획). 씬 진입 시 EnterVillage/EnterDungeon가
        // UseVillageSpeed/UseDungeonSpeed로 아래 세트 중 하나를 활성 속도(moveSpeed/runSpeed)에 복사한다.
        // 애니메이터 컨트롤러를 마을/던전으로 가르는 것(Animation.UseVillageController/UseDungeonController)과 같은 패턴.
        [Header("이동 속도 - 마을")]
        [SerializeField, FormerlySerializedAs("moveSpeed")] private float villageMoveSpeed = 3f;   // 마을 걷기
        [SerializeField, FormerlySerializedAs("runSpeed")] private float villageRunSpeed = 6f;     // 마을 달리기

        [Header("이동 속도 - 던전")]
        [SerializeField] private float dungeonMoveSpeed = 3f;   // 던전 걷기
        [SerializeField] private float dungeonRunSpeed = 6f;    // 던전 달리기

        // 현재 씬에 맞춰 선택된 활성 속도. 인스펙터가 아니라 UseVillageSpeed/UseDungeonSpeed가 세팅한다.
        // Awake에서 마을 세트로 초기화해, 씬 진입 호출 전에도 0속도로 굳지 않게 한다.
        private float moveSpeed;
        private float runSpeed;

        [Header("이동 - 공통")]
        [SerializeField] private float rotationDamping = 10f;
        [SerializeField] private float jumpHeight = 3f;
        [SerializeField] private float gravity = 9.8f;

        // 공중에서 입력 방향으로 관성을 꺾는 속도. 지상은 루트모션이 담당하고, 공중은 점프 순간
        // 캡처한 수평 속도(관성)를 유지하다가 이 값만큼만 서서히 조향한다.
        // 작을수록 관성 위주(둔한 조작), 클수록 즉각 조작(관성 약함).
        [SerializeField] private float airControl = 8f;

        // 공중에서 입력을 뗐을 때 관성이 줄어드는 속도. airControl보다 크게 잡아야 착지 시 미끄러지지 않는다.
        [SerializeField] private float airBrake = 30f;

        [Header("바닥 체크")]
        // 접지 판정은 CharacterController.isGrounded를 그대로 쓴다(FreeMove 이관). 발밑 잔틈에 false가 되는
        // 특성은 회피/공격 분기에서 IsEffectivelyGrounded의 짧은 레이캐스트로 메운다.
        // groundLayer는 착지 예고·회피 접지·공중 공격 착지 레이캐스트의 대상 레이어다(반드시 지정).
        [SerializeField] private LayerMask groundLayer;

        // 구르기 튜닝. 상태 클래스(PlayerRollState)는 MonoBehaviour가 아니라
        // 인스펙터 값을 못 가지므로, 이동 소관인 여기에 두고 프로퍼티로 노출한다.
        // 이동 속도는 따로 없다: 구르기 전진은 클립의 루트 모션이 담당한다.
        [Header("구르기")]
        [SerializeField] private float rollDuration = 0.6f; // 구르기 지속 시간(초). 클립 길이와 맞출 것

        [Header("점프 대시(공중 대시)")]
        [SerializeField] private float jumpDashSpeed = 12f;
        [SerializeField] private float jumpDashDuration = 0.55f;

        [Header("공중 공격(내려찍기)")]
        // 시전 순간 살짝 떠오르는 높이. 점프 직후 저공에서 시전해도 어색하지 않게 해준다(명조식).
        [SerializeField] private float diveRiseHeight = 0.25f;
        // 위 높이까지 올라가는 속도(m/s). Start 클립이 재생되는 동안 도달하도록 맞춘다.
        [SerializeField] private float diveRiseSpeed = 2f;
        // Loop 구간 하강 속도. 클수록 빠르게 내려꽂는다.
        [SerializeField] private float diveSpeed = 8f;
        // 하강 중 착지 예고(isLanding) 감지 거리. 일반 점프와 요구 타이밍이 달라 따로 둔다.
        [SerializeField] private float diveLandingCheckDistance = 0.4f;

        [Header("착지 예고")]
        // 지면까지 이 거리 안으로 들어오면 착지 모션을 미리 시작한다(모션 길이에 맞춰 조정).
        [SerializeField] private float landingCheckDistance = 1.5f;
        // 회피 판정용 '사실상 접지' 거리. controller.isGrounded는 발밑이 0.x cm만 떠도 false가 되어,
        // 그 틈에 지상 구르기가 공중 대시로 새는 문제가 있다. 발밑 이 거리 안에 지면이 있으면 접지로 본다.
        [SerializeField] private float dodgeGroundCheckDistance = 0.3f;

        private CharacterController controller;
        private PlayerInputHandler inputHandler;   // 점프 관성 방향·대시 방향 산출에 현재 입력을 읽는다(값만 소비)
        private Transform cam;

        // CharacterController는 Rigidbody 속도를 들고 있지 않으므로 수직 속도를 직접 누적한다.
        private float verticalVelocity;

        // 지상에서 실제로 난 수평 속도(루트모션+코드 결과)를 매 프레임 측정해 둔다.
        // 점프 순간 이 크기를 관성으로 넘겨, 공중 속도를 지상과 정확히 일치시키기 위함.
        private Vector3 lastPosition;
        private float groundSpeedMagnitude;

        // 점프 순간 캡처한 공중 관성 속도. 공중에서는 이 값을 유지하며 입력 방향으로만 서서히 조향한다.
        private Vector3 airVelocity;
        private bool wasGrounded;

        // 점프 대시 상태. 진행 중엔 중력을 끄고 수평으로만 이동한다(체공 높이 고정).
        private bool isJumpDashing;
        private float jumpDashTimer;
        private Vector3 jumpDashDirection;
        private float jumpDashHeight;
        // 공중 대시는 착지 전까지 1회만 허용. 착지하면 재사용 가능해진다.
        private bool hasAirDashed;

        // 공중 공격 체공(Start 구간). 켜는 순간 높이를 캡처하고 diveRiseHeight만큼 올라간 뒤 유지한다.
        private bool hovering;
        private float hoverHeight;
        private float hoverTargetHeight;

        // 공중 피격 체공. 맞은 높이에 '멈추는' 것(공중 공격의 '떠오르는' Hovering과 목적이 달라 분리).
        private bool hitHovering;
        private float hitHoverHeight;

        // 올려치기 상승이 실제로 공중에 진입한 적이 있는지. 착지 안전망 전용 엣지 검출용이다.
        // verticalVelocity 부호로는 판정할 수 없다 — StrongAttackRising && actionLocked 동안 ApplyGravity가
        // vv를 계속 0으로 눌러, 루트모션으로 착지해도 vv<0이 성립하지 않기 때문. 그래서 "공중에 나갔다가
        // 다시 접지"라는 엣지를 이 플래그로 직접 잡는다(리프트오프 첫 프레임은 아직 접지라 false로 유지).
        private bool strongAttackWasAirborne;

        /// <summary>현재 접지 여부. CharacterController.isGrounded를 그대로 노출한다.</summary>
        public bool IsGrounded => controller != null && controller.isGrounded;

        /// <summary>
        /// '진짜로 서 있는가' 판정. 점프 직후 몇 프레임 동안 접지 체크(IsGrounded)가
        /// true로 남는 잔존 구간을 verticalVelocity로 걸러낸다(상승 중이면 공중 취급).
        /// 점프 가능 여부와 지상 구르기 가능 여부가 이 판정을 공유한다.
        /// </summary>
        public bool IsStablyGrounded => IsGrounded && verticalVelocity <= 0f;

        /// <summary>
        /// 회피(구르기) 발동용 접지 판정. IsStablyGrounded와 목적은 같지만 접지 '근거'를
        /// controller.isGrounded 대신 짧은 레이캐스트(IsEffectivelyGrounded)로 삼는다.
        /// controller.isGrounded는 프레임당 하강 이동량(move*deltaTime)에 의존해, 고프레임에서
        /// 그 이동량이 작아지면 바닥을 충분히 파고들지 못해 간헐적으로 false가 된다. 그 프레임에
        /// TryRoll이 씹히던 문제(고사양 PC에서 스킬을 회피로 캔슬할 수 없던 직접 원인)를 없애기 위해
        /// 프레임레이트와 무관한 레이캐스트 기반으로 바꾼다.
        /// 상승 중(점프)엔 지상 회피가 아니라 공중 대시(TryJumpDash)가 맞으므로 verticalVelocity>0은 제외한다.
        /// </summary>
        public bool IsGroundedForDodge => IsEffectivelyGrounded() && verticalVelocity <= 0f;

        public bool CanJump => IsStablyGrounded;

        /// <summary>
        /// 현재 수직 속도(+상승 / -하강). 애니메이터가 점프 상승·낙하 모션을
        /// 트리거 없이 상태값 전이로 분기할 때 쓴다(Player가 매 프레임 전달).
        /// </summary>
        public float VerticalVelocity => verticalVelocity;

        /// <summary>구르기 지속 시간(초). PlayerRollState가 상태 종료 판정에 쓴다.</summary>
        public float RollDuration => rollDuration;

        /// <summary>현재 점프 대시(공중 대시) 중 여부. PlayerJumpDashState가 상태 종료 판정에 읽는다.</summary>
        public bool IsJumpDashing => isJumpDashing;

        /// <summary>공중 대시를 이번 점프에서 이미 썼는지. Player가 공중 대시 가능 판정에 읽는다.</summary>
        public bool HasAirDashed => hasAirDashed;

        /// <summary>
        /// 공중 공격 Loop 구간의 하강 여부. true면 중력 대신 diveSpeed 고정 속도로 내려간다.
        /// 착지하면 자동 해제된다. 착지 예고 거리 선택에 Player가 읽는다.
        /// </summary>
        public bool Diving { get; private set; }

        /// <summary>
        /// 우클릭 강공격(올려치기)으로 떠오른 상태인지. 상승 구간 동안 코드 중력을 억제하고,
        /// 이 구간에서만 공중 대시 캔슬을 허용하기 위한 구분값이다(점프공격/착지 직후엔 꺼져 있어 오발 방지).
        /// Animation Event(OnStrongAttackRiseStart/End)가 켜고 끈다.
        /// </summary>
        public bool StrongAttackRising { get; set; }

        /// <summary>
        /// 공중 정지(호버링) 여부. 점프 공격 모션 동안 켜져 높이가 고정된다(기획).
        /// </summary>
        public bool IsHovering => hovering;

        /// <summary>
        /// 공중에서 피격 모션이 재생되는 동안 높이를 고정한다. 켜는 순간 높이를 캡처해 그 자리에 멈춘다.
        /// 체공 중에는 수직 속도를 0으로 눌러, 체공이 풀리는 순간 누적 속도로 갑자기 떨어지지 않게 한다.
        /// </summary>
        public bool HitHovering
        {
            get => hitHovering;
            set
            {
                if (value && !hitHovering) hitHoverHeight = transform.position.y;
                hitHovering = value;
            }
        }

        /// <summary>
        /// 호버링을 켜고 끈다(단순 유지형 — 상승 없음). 켜는 순간 현재 높이를 그대로 잡아 멈추고,
        /// 끄면 낙하를 재개한다. 기존(Z 블렌드) 캐릭터의 점프 공격이 쓴다.
        /// 켜기: 점프 공격 발동 시. 끄기: 모션 종료(UnlockMovement) 또는 사망.
        /// </summary>
        public void SetHover(bool value)
        {
            hovering = value;
            Diving = false;
            if (value)
            {
                hoverHeight = transform.position.y;
                hoverTargetHeight = hoverHeight;   // 상승 없이 현재 높이 유지
                verticalVelocity = 0f;
            }
        }

        /// <summary>
        /// 공중 공격 Start 구간 체공을 켠다(자유 이동 캐릭터). 시전 높이에서 diveRiseHeight만큼
        /// 서서히 올라간 뒤 그 높이를 유지한다(LateUpdate가 루트모션을 덮어써 고정). 하강 전환은 <see cref="BeginDive"/>.
        /// </summary>
        public void BeginDiveHover()
        {
            Diving = false;
            hovering = true;
            hoverHeight = transform.position.y;
            hoverTargetHeight = hoverHeight + diveRiseHeight;
            verticalVelocity = 0f;
        }

        /// <summary>
        /// 공중 공격 Loop 진입 시 호출한다(JumpAttackLoopBehaviour). 체공을 끝내고 하강을 시작한다.
        /// 이미 체공이 풀렸다면(캔슬 등) 아무 것도 하지 않는다.
        /// </summary>
        public void BeginDive()
        {
            if (!hovering) return;

            hovering = false;
            Diving = true;
        }

        /// <summary>
        /// 올려치기 상승 구간 시작. <b>올려치기 클립의 Animation Event</b>로 발이 땅을 떠나는 프레임에 호출한다.
        /// 여기부터 정점(OnStrongAttackRiseEnd)까지 코드 중력을 억제해, 클립의 루트모션이 캐릭터를 띄우게 한다.
        /// (Animation Event가 문자열로 이 이름을 참조하므로 메서드명 변경에 주의.)
        /// </summary>
        public void OnStrongAttackRiseStart()
        {
            hovering = false;
            Diving = false;
            verticalVelocity = 0f;
            StrongAttackRising = true;
            strongAttackWasAirborne = false;   // 새 상승 시작 → 엣지 추적 초기화(이전 판의 잔여값이 리프트오프를 오판하지 않게)
        }

        /// <summary>
        /// 올려치기 상승 구간 끝. <b>선택적</b> Animation Event. 보통은 StrongAttack→Jump_Loop 전이가
        /// 잠금을 벗어나며 중력을 자동 재개하므로 필요 없다. 특정 프레임에서 미리 중력을 재개하고 싶을 때만 배치한다.
        /// (Animation Event가 문자열로 이 이름을 참조하므로 메서드명 변경에 주의.)
        /// </summary>
        public void OnStrongAttackRiseEnd()
        {
            StrongAttackRising = false;
            verticalVelocity = 0f;   // 정점에서 0부터 낙하 시작(부드러운 인계)
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            inputHandler = GetComponent<PlayerInputHandler>();
            TryCacheMainCamera();

            // 씬 진입 호출(EnterVillage/EnterDungeon) 전에도 유효한 속도가 있도록 마을 세트로 초기화한다.
            UseVillageSpeed();

            // 첫 LateUpdate에서 (현재위치 - 0) 큰 델타로 속도가 튀지 않게 시작 위치로 초기화한다.
            lastPosition = transform.position;
        }

        /// <summary>
        /// 활성 걷기/달리기 속도를 마을 세트로 전환한다. <see cref="Player.EnterVillage"/>가 호출한다.
        /// </summary>
        public void UseVillageSpeed()
        {
            moveSpeed = villageMoveSpeed;
            runSpeed = villageRunSpeed;
        }

        /// <summary>
        /// 활성 걷기/달리기 속도를 던전 세트로 전환한다. <see cref="Player.EnterDungeon"/>가 호출한다.
        /// </summary>
        public void UseDungeonSpeed()
        {
            moveSpeed = dungeonMoveSpeed;
            runSpeed = dungeonRunSpeed;
        }

        public void Move(Vector2 input, bool isRunning = false, bool actionLocked = false)
        {
            // 이동이 잠긴 상태에서도 PlayerFreeState가 zero 입력으로 호출한다.
            // 그래서 중력/접지는 계속 처리되고 수평 이동만 멈춘다.
            // 기본값 false: 구르기 상태처럼 zero 입력만 넘기는 호출부는 달리기를 신경 쓸 필요 없다.
            //
            // actionLocked: 공격/스킬로 이동이 잠긴 경로에서만 true(FreeState의 잠금 분기가 넘긴다).
            // 원본(FreeMoveController)이 올려치기 상승 억제를 ActionLocked 블록 안에서만 걸던 것을 그대로
            // 재현하기 위한 신호다. 잠금이 풀리면 억제가 사라져 코드 중력이 자동 재개된다(정점→낙하).

            // 올려치기 상승이 실제로 공중에 나갔는지 추적한다. 리프트오프 첫 프레임은 아직 접지라 false로 남아,
            // 아래 착지 안전망이 상승을 즉시 꺼버리지 않는다(요구사항: 리프트오프 즉시 해제 금지).
            if (StrongAttackRising && !IsGrounded) strongAttackWasAirborne = true;

            // 착지하면 공중 대시 사용권과 공중 공격 하강, 공중 피격 체공을 초기화한다.
            if (IsGrounded)
            {
                hasAirDashed = false;
                Diving = false;
                HitHovering = false;
                // hovering(점프 공격 체공)은 여기서 끄지 않는다. 끄면 Player.Update의 착지 정리 블록
                // (IsStablyGrounded && IsHovering → CancelAction/UnlockMovement)이 신호를 잃어 기존 캐릭터의
                // 점프 공격 잠금이 안전장치 타이머까지 남는다. 그 블록이 UnlockMovement→SetHover(false)로 끈다.

                // 올려치기 상승 착지 안전망: '공중에 나갔다가 다시 접지'하는 엣지에서만 해제한다.
                // 정상 해제는 blend-out(StrongAttack→Jump_Loop, Player.UpdateTagLock)이 담당하고, 이건 그 신호를
                // 놓쳐(예: 전이 미구성/타이밍) 루트모션으로 착지한 경우의 실패 대비 안전망이다. vv 부호로 판정하지
                // 않는 이유: 억제 중 vv가 0으로 고정돼 vv<0이 성립하지 않기 때문(리뷰 지적). 엣지 플래그로 잡는다.
                if (strongAttackWasAirborne)
                {
                    StrongAttackRising = false;
                    strongAttackWasAirborne = false;
                }
            }

            // 공중 피격 체공 중에는 수직 속도를 계속 0으로 눌러둔다(LateUpdate는 위치만 고정, 속도는 안 건드림).
            if (hitHovering) verticalVelocity = 0f;

            // 착지 순간(공중 → 접지)에 공중 관성을 확실히 제거한다.
            if (IsGrounded && !wasGrounded) airVelocity = Vector3.zero;
            wasGrounded = IsGrounded;

            // ── 공격/스킬 잠금 분기(원본 FreeMoveController의 ActionLocked 블록 재현) ──────────────
            // 잠금 중에는 수평 관성을 즉시 제거하고 수직(중력/억제/체공/하강)만 처리한다.
            // 수평 이동·FaceDirection 회전을 적용하지 않아, 상승/공중 공격 중 옛 airVelocity 방향으로
            // 미끄러지거나 캐릭터가 홱 도는 것을 막는다. 일반 지상/공중 계산보다 먼저 처리한다.
            if (actionLocked)
            {
                airVelocity = Vector3.zero;

                Vector3 locked = Vector3.zero;
                ApplyGravity(ref locked, true);   // hovering/hitHovering/StrongAttackRising/Diving/중력은 그대로
                controller.Move(locked * Time.deltaTime);
                return;
            }

            Vector3 move;

            if (IsGrounded)
            {
                // 지상: 목표 속도를 즉시 적용한다(즉각 반응 우선 — 가속 램프를 시험했다가 되돌린 팀 결정, 2026-07).
                // 루트모션이 위치를 함께 담당하고, 출발 시 부드러움은 애니메이션 블렌드 감쇠가 맡는다.
                bool moving = input.sqrMagnitude > 0.0001f;
                move = moving ? CameraRelative(input) * (isRunning ? runSpeed : moveSpeed) : Vector3.zero;
            }
            else
            {
                // 공중: 입력이 있으면 그 방향으로 조향(airControl), 없으면 관성을 빠르게 줄인다(airBrake).
                bool moving = input.sqrMagnitude > 0.0001f;
                Vector3 target = moving ? CameraRelative(input) * airVelocity.magnitude : Vector3.zero;
                float rate = moving ? airControl : airBrake;
                airVelocity = Vector3.MoveTowards(airVelocity, target, rate * Time.deltaTime);
                move = airVelocity;
            }

            FaceDirection(move);
            ApplyGravity(ref move, actionLocked);
            controller.Move(move * Time.deltaTime);
        }

        public bool Jump()
        {
            if (!CanJump) return false;

            // v = sqrt(2gh). 이후 ApplyGravity에서 낙하 가속을 더 크게 주기 때문에
            // jumpHeight는 정확한 높이라기보다 튜닝 기준값에 가깝다.
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);

            // 방향은 현재 입력에서, 크기는 지상에서 측정한 실제 속도에서 가져온다.
            // → 달리다 뛰면 달리기 속도로, 걷다 뛰면 걷기 속도로 자연스럽게 이어진다.
            Vector2 moveInput = inputHandler != null ? inputHandler.MoveInput : Vector2.zero;
            airVelocity = moveInput.sqrMagnitude > 0.0001f
                ? CameraRelative(moveInput) * groundSpeedMagnitude
                : Vector3.zero;
            return true;
        }

        /// <summary>
        /// 상승 중인 점프 속도를 제거한다. 점프 상승 도중 피격 등으로 동작이 끊길 때 호출해
        /// 애니메이션은 멈췄는데 몸만 계속 떠오르는 것을 막는다.
        /// 하강 중(verticalVelocity ≤ 0)에는 건드리지 않아 낙하는 자연스럽게 이어진다.
        /// </summary>
        public void CancelJump()
        {
            if (verticalVelocity > 0f)
            {
                verticalVelocity = 0f;
            }
        }

        /// <summary>
        /// 씬 스폰 워프 시 호출(PlayerManager). 이전 씬에서 남은 수직/수평 관성과 공중 동작 상태를 전부 리셋해,
        /// 활성화 직후 곧바로 낙하하거나 미끄러지거나 유령 상태(체공·상승·대시 잔재)가 이어지지 않게 한다.
        /// 위치·회전 세팅은 호출측(PlayerManager)이 비활성 상태에서 담당하고, 여기는 속도·상태만 정리한다.
        /// </summary>
        public void ResetMotion()
        {
            verticalVelocity = 0f;
            airVelocity = Vector3.zero;
            isJumpDashing = false;
            hovering = false;
            Diving = false;
            hitHovering = false;
            StrongAttackRising = false;
            strongAttackWasAirborne = false;
            hasAirDashed = false;

            // 순간이동으로 위치가 크게 바뀌므로, LateUpdate의 지상 속도 측정이 큰 델타로 튀지 않게 기준점을 맞춘다.
            wasGrounded = false;
            lastPosition = transform.position;
        }

        /// <summary>
        /// 점프 대시(공중 대시)를 시작한다. 입력 방향(없으면 바라보는 방향)으로 대시하며,
        /// 착지 전까지 1회만 허용된다. 트리거 재생은 호출측(PlayerJumpDashState)이 담당한다.
        /// </summary>
        public void StartJumpDash()
        {
            isJumpDashing = true;
            hasAirDashed = true;                     // 착지 전까지 재사용 금지
            jumpDashTimer = 0f;
            jumpDashHeight = transform.position.y;   // 이 높이를 대시 내내 유지

            jumpDashDirection = ResolveDashDirection();
            transform.rotation = Quaternion.LookRotation(jumpDashDirection);

            verticalVelocity = 0f;
        }

        /// <summary>
        /// 점프 대시 진행 프레임 처리. 중력을 끄고 수평으로만 이동한다(체공 높이 고정은 LateUpdate가 담당).
        /// 대시 시간이 끝나면 자동 종료되고, 남은 속도를 공중 관성으로 넘긴다.
        /// PlayerJumpDashState가 매 프레임 호출한다.
        /// </summary>
        public void TickJumpDash()
        {
            if (!isJumpDashing) return;

            jumpDashTimer += Time.deltaTime;
            verticalVelocity = 0f;

            // 대시 방향은 시작 순간에 고정한다. 대시 중 다른 방향키를 눌러도 바꾸지 않는다.
            controller.Move(jumpDashDirection * jumpDashSpeed * Time.deltaTime);

            if (jumpDashTimer >= jumpDashDuration)
            {
                isJumpDashing = false;
                airVelocity = jumpDashDirection * jumpDashSpeed;
            }
        }

        /// <summary>
        /// 하강 중이고 지면이 가까운지(착지 예고). Player가 매 프레임 Animation.SetLanding으로 연결한다.
        /// 공중 공격 하강 중에는 전용 거리를 써 임팩트 타이밍을 일반 점프와 따로 조절한다.
        /// 판정은 한 점 레이캐스트가 아니라 CharacterController.radius만큼의 SphereCast를 쓴다 —
        /// 중심 한 점만 보면 캐릭터가 모서리에 걸쳐 착지하는 순간(중심 아래는 허공, 발끝은 지면)을
        /// 놓쳐 착지 예고가 안 뜨기 때문. 캐릭터의 실제 발판 부피로 지면을 훑어야 진짜 착지 범위가 나온다.
        /// </summary>
        public bool IsNearGround()
        {
            if (verticalVelocity >= 0f) return false;

            float dist = landingCheckDistance;
            if (Diving)
            {
                // 한 프레임 낙하량보다 짧으면 빠른 하강에서 감지 구간을 건너뛸 수 있어 하한을 둔다.
                float perFrameDrop = -verticalVelocity * Time.deltaTime * 2f;
                dist = Mathf.Max(diveLandingCheckDistance, perFrameDrop);
            }

            // 스피어 반지름 = 캐릭터 발판 반지름. 시작 구가 지면에 이미 겹치면 SphereCast가 거리 0으로
            // 오작동하므로, 구의 바닥이 발밑 +0.1m가 되도록 중심을 (radius + 0.1)만큼 올려서 여유를 준다.
            float radius = controller != null ? controller.radius : 0.3f;
            Vector3 origin = transform.position + Vector3.up * (radius + 0.1f);
            bool hit = Physics.SphereCast(
                new Ray(origin, Vector3.down),
                radius,
                dist,
                groundLayer,
                QueryTriggerInteraction.Ignore);

#if UNITY_EDITOR
            // 착지 예고 판정 시각화(에디터 전용). Scene 뷰에서 지면을 맞히면 초록, 빗나가면 빨강.
            // 중심 한 줄뿐 아니라 발판 가장자리(±radius, 앞뒤좌우)까지 그려 실제 검사 부피를 보여준다.
            // 착지해야 할 위치인데도 계속 빨강이면 바닥이 groundLayer 마스크에 없다는 뜻(isLanding 안 켜지는 원인).
            Color rayColor = hit ? Color.green : Color.red;
            Vector3 down = Vector3.down * dist;
            Debug.DrawRay(origin, down, rayColor);
            Debug.DrawRay(origin + Vector3.forward * radius, down, rayColor);
            Debug.DrawRay(origin + Vector3.back * radius, down, rayColor);
            Debug.DrawRay(origin + Vector3.right * radius, down, rayColor);
            Debug.DrawRay(origin + Vector3.left * radius, down, rayColor);
#endif

            return hit;
        }

        /// <summary>
        /// 회피·공격 분기용 '사실상 접지' 판정. IsGrounded(발밑 잔틈에 false)의 빈틈을 짧은 레이캐스트로 메운다.
        /// 지상 회피가 공중 대시로 새거나, 이미 착지한 모션에서 공중 공격이 재발동되는 것을 막는다.
        /// 물리/중력 판정에는 쓰지 않는다.
        /// </summary>
        public bool IsEffectivelyGrounded()
        {
            if (IsGrounded) return true;

            return Physics.Raycast(
                transform.position + Vector3.up * 0.1f,
                Vector3.down,
                0.1f + dodgeGroundCheckDistance,
                groundLayer,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 입력(x=좌우, y=앞뒤)을 카메라 기준 월드 방향으로 변환해 돌려준다.
        /// 구르기 상태가 진입 시점에 방향을 1회 확정할 때 쓴다.
        /// </summary>
        public Vector3 CameraRelativeDirection(Vector2 input) => CameraRelative(input);

        /// <summary>
        /// 지정한 월드 방향을 즉시 바라본다(수평 성분만).
        /// 구르기 시작처럼 보간(FaceDirection) 없이 한 프레임에 방향이 확정되어야 할 때 쓴다.
        /// </summary>
        public void FaceInstantly(Vector3 worldDirection)
        {
            worldDirection.y = 0;
            if (worldDirection.sqrMagnitude < 0.0001f) return;   // 0벡터 방향은 정의 불가 → 무시

            transform.rotation = Quaternion.LookRotation(worldDirection);
        }

        public void SnapToCameraForward()
        {
            // 공격/스킬 시작 순간에는 부드러운 회전보다 카메라 방향 정렬이 우선이다.
            // ★ 반드시 Update 단계에서 호출해야 한다(LateUpdate로 미루면 CameraPivotController 보정과 충돌해 깜빡임).
            if (!TryCacheMainCamera()) return;

            FaceInstantly(cam.forward);
        }

        // 루트모션과 controller.Move가 모두 적용된 뒤(프레임 끝)에 실행된다.
        private void LateUpdate()
        {
            // 대시 중에는 루트모션이 끌어내린 높이를 여기서 되돌려 체공을 유지한다.
            // Update에서 고정하면 그 뒤에 적용되는 루트모션이 다시 끌어내리므로 반드시 LateUpdate에서 처리한다.
            if (isJumpDashing)
            {
                Vector3 dashPos = transform.position;
                dashPos.y = jumpDashHeight;
                transform.position = dashPos;
            }

            // 공중 공격 Start 구간: 시전 높이에서 diveRiseHeight까지 서서히 올라간 뒤 그 높이를 유지한다.
            if (hovering)
            {
                hoverHeight = Mathf.MoveTowards(hoverHeight, hoverTargetHeight, diveRiseSpeed * Time.deltaTime);

                Vector3 hoverPos = transform.position;
                hoverPos.y = hoverHeight;
                transform.position = hoverPos;
            }

            // 공중 피격 체공: 맞은 높이를 그대로 유지한다(상승 없음).
            if (hitHovering)
            {
                Vector3 hitPos = transform.position;
                hitPos.y = hitHoverHeight;
                transform.position = hitPos;
            }

            // 지상에서 이동 중일 때만 갱신. 최댓값을 유지해 회전 중인 느린 프레임에 끌려가지 않게 한다.
            // 무엇이 위치를 옮겼든(루트모션/코드/둘 다) 결과 속도라, 점프 시 넘기면 공중이 지상과 정확히 일치한다.
            if (Time.deltaTime > 0f && IsGrounded && inputHandler != null && inputHandler.MoveInput.sqrMagnitude > 0.0001f)
            {
                Vector3 delta = transform.position - lastPosition;
                delta.y = 0f;
                float speed = delta.magnitude / Time.deltaTime;
                groundSpeedMagnitude = Mathf.Max(groundSpeedMagnitude * 0.9f, speed);
            }

            lastPosition = transform.position;
        }

        // 현재 입력 방향(카메라 기준)을 수평으로 반환. 입력이 없으면 현재 바라보는 방향.
        private Vector3 ResolveDashDirection()
        {
            Vector2 moveInput = inputHandler != null ? inputHandler.MoveInput : Vector2.zero;
            if (moveInput.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = CameraRelative(moveInput);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) return dir.normalized;
            }

            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            return fwd.normalized;
        }

        private void ApplyGravity(ref Vector3 move, bool actionLocked)
        {
            // 체공(공중 공격 Start)·공중 피격 체공은 각자 신뢰할 수 있는 해제 경로가 있어(hovering은
            // BeginDive/착지 정리, hitHovering은 UpdateTagLock 매 프레임 재판정) 잠금과 무관하게 억제한다.
            //
            // 올려치기 상승(StrongAttackRising)만은 원본(FreeMoveController)처럼 '이동 잠금 중'에만 억제한다.
            // 이 플래그는 blend-out(Attack 태그)에 의존해 해제되는데, 그 신호를 놓치면 true로 남아
            // 공중에 영구히 굳었다(강공격→무한 점프 루프의 직접 원인). 억제를 잠금 안으로 되돌리면,
            // 잠금이 풀리는 순간(정점의 blend-out/유예 만료) 아래 코드 중력이 곧바로 재개돼 낙하로 이어지므로
            // 플래그가 뒤늦게 안 꺼져도 굳지 않는다.
            if (hovering || hitHovering || (StrongAttackRising && actionLocked))
            {
                verticalVelocity = 0f;
                move.y = 0f;
                return;
            }

            // 공중 공격 Loop 하강: 중력 대신 고정 속도로 내려꽂는다.
            if (Diving && !IsGrounded)
            {
                verticalVelocity = -diveSpeed;
                move.y = verticalVelocity;
                return;
            }

            // 접지 중에는 작은 음수로 눌러 바닥과의 접촉을 안정화한다.
            if (IsGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
                airVelocity = Vector3.zero;   // 착지 시 관성 제거 → 미끄러지지 않음
            }

            // 상승보다 낙하를 빠르게 해서 조작감이 늘어지지 않게 한다.
            verticalVelocity -= 2f * gravity * Time.deltaTime;
            move.y = verticalVelocity;
        }

        private Vector3 CameraRelative(Vector2 input)
        {
            // 씬 로드 순서상 카메라가 늦게 잡힐 수 있으므로 사용할 때도 한 번 더 캐싱한다.
            if (!TryCacheMainCamera()) return Vector3.zero;

            Vector3 f = cam.forward;
            f.y = 0;
            f.Normalize();

            Vector3 r = cam.right;
            r.y = 0;
            r.Normalize();

            return (f * input.y + r * input.x).normalized;
        }

        private void FaceDirection(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) return;

            // 이동 중 회전은 즉시 꺾지 않고 보간해 자연스럽게 따라가게 한다.
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                rotationDamping * Time.deltaTime);
        }

        private bool TryCacheMainCamera()
        {
            if (cam != null) return true;
            if (Camera.main == null) return false;

            cam = Camera.main.transform;
            return true;
        }
    }
}
