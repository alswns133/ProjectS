using UnityEngine;
using ProjectS.Players;
using ProjectS.Movement;

namespace ProjectS.Combat
{
    /// <summary>
    /// [참조 전용 전투 조율자] FreeMoveController(이동)와 민준님의 전투 계열(PlayerCombat/PlayerStats/
    /// PlayerAnimation)을 잇는다. 민준님 스크립트는 절대 수정하지 않고 public 메서드/이벤트만 참조·재사용한다.
    ///
    /// 핵심: Player 컴포넌트는 '비활성' 상태로 두어 이동을 이중 구동하지 않지만,
    /// Player.LockMovement()/UnlockMovement()/IsMovementLocked는 컴포넌트가 꺼져 있어도 동작하므로
    /// '이동 잠금 수명주기'만 그대로 빌려 쓴다. 잠금 해제는 애니메이터의 ComboResetBehaviour(SMB)가
    /// 로코모션 복귀 시 자동으로 Player.UnlockMovement()를 호출해 처리한다.
    ///
    /// 현재 구현 범위: 좌클릭 라우팅(공중 공격 / 대시 공격 / 지상 콤보) + 우클릭 강공격(올려치기→공중 연계)
    /// + 공격 간 연계(캔슬 창) + 콤보 잠금 갱신 + 적중 게이지 회복.
    /// (스킬·피격/사망 반응·구르기 무적은 다음 단계)
    ///
    /// 공격 간 연계(캔슬 창)는 이 클래스가 아니라 공격 State에 붙이는 <see cref="AttackCancelBehaviour"/>(SMB)가
    /// State별로 처리한다. 그래야 공격마다 연계 타이밍을 애니메이터에서 따로 튜닝할 수 있다.
    /// </summary>
    public class FreeCombatController : MonoBehaviour
    {
        [Header("참조 (비우면 같은 오브젝트에서 자동 탐색)")]
        [SerializeField] private PlayerInputHandler input;
        // Player는 잠금 machinery 참조용. 컴포넌트 자체는 비활성이어야 한다(이동 이중 구동 방지).
        [SerializeField] private Player player;
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private PlayerStats stats;
        [SerializeField] private FreeMoveController move;

        // Player.Update의 이동 잠금 안전장치가 이 구조에선 안 돌기 때문에 여기서 대체한다.
        // 로코모션 복귀 신호(SMB의 UnlockMovement)를 놓쳐도 이 시간 뒤 자동 해제된다.
        [Header("안전장치")]
        [SerializeField] private float maxActionLockTime = 3f;
        private float actionLockTimer;

        [Header("공격 상태 판정")]
        // 이동을 잠글 Animator State Tag 목록. 공격 종류마다 다른 태그를 붙여 메커니즘을 구분할 수 있다
        // (예: 평타 "Attack", 스킬 "Skill", 강공격 "StrongAttack"). 여기 등록된 태그는 전부 이동을 잠근다.
        // 제약: State 하나에 Tag는 하나만 지정할 수 있고, 비교는 정확히 일치해야 한다(접두어 매칭 불가).
        [SerializeField] private string[] lockingStateTags = { "Attack", "Hit" };
        // 트리거를 쏜 직후엔 아직 공격 State에 진입하기 전이라 태그가 안 잡힌다. 그 짧은 공백을 메우는 유예 시간.
        [SerializeField] private float attackEnterGrace = 0.15f;

        [Header("피격 상태 판정")]
        // 피격 모션 State에 붙이는 Tag(Hit / Hit_Large / Hit_Jump 모두 같은 태그를 쓴다).
        // 공중 피격 중 체공 유지 판정에 쓴다.
        [SerializeField] private string hitStateTag = "Hit";

        [Header("지속 조준(검기 등)")]
        // 대부분의 공격은 "타수 시작 시점에만" 카메라를 스냅하고 그 타수 동안은 고정한다(카메라를 돌려도
        // 그 타 자체는 안 꺾임). 다만 스킬 3번(검기 투사체)처럼 시전 내내 조준 방향을 바꿀 수 있어야 하는
        // 예외가 있다. 이 Bool 파라미터가 켜져 있는 State에서만 매 프레임 카메라 방향을 갱신한다.
        // State에 StateBoolFlagBehaviour(재사용 SMB)를 붙이고 이 이름과 같은 파라미터를 켜고 끄게 하면 된다.
        [SerializeField] private string continuousAimParam = "continuousAim";

        private Animator animator;
        private float attackGraceUntil;
        // Tag 비교는 매 프레임이라 문자열 해싱을 피해 Awake에서 1회 캐싱한다.
        private int[] lockingTagHashes;
        private int hitTagHash;
        private int continuousAimHash;

        // 회피(구르기/공중 대시) 시작 순간을 잡아 진행 중이던 공격을 캔슬하기 위한 엣지 검출.
        private bool wasDodging;

        // 공중 공격은 점프 1회당 1회만 허용한다(기획). 착지하면 회복된다.
        private bool jumpAttackUsed;
        private bool wasGrounded;

        // 공격/스킬에서 로코모션으로 나가는 블렌드가 '막 시작된' 프레임만 잡기 위한 엣지 검출.
        private bool wasBlendingOutOfAttack;

        // 다음 Update에 카메라 방향 스냅을 적용할지. Animation Event 단계에서 곧바로 회전시키면
        // 애니메이터가 덮어써 무효가 되기 때문에(자세한 이유는 OnComboStepStarted 주석 참고)
        // 여기에 예약해 두고 Update 단계에서 적용한다.
        private bool pendingAimSnap;

        // combat.IsCastingSkill의 이전 프레임 값. 스킬/강공격/대시공격의 캔슬 창이 열리는
        // '순간'(true→false 엣지)을 잡기 위한 용도 — OnAttack()은 클릭 이벤트로만 불려서,
        // 캔슬 창이 열리기 전부터 좌클릭을 꾹 누르고 있으면 새 클릭 이벤트가 없어 콤보 진입을
        // 놓친다(짧게 클릭하면 되는 이유는 그 클릭 자체가 새 이벤트라서다).
        private bool wasCastingSkill;

        // 평타(콤보) 전용 캔슬 창. combat.IsCastingSkill은 스킬/강공격/대시공격/공중공격만 켜고
        // 평타(CombatAction.Combo)는 켜지 않아서, AttackCancelBehaviour가 Attack_1~3에 붙어도
        // 그 첫 줄(!IsCastingSkill)에서 항상 막혀 슬라이더가 무효했다. 같은 SMB·같은 슬라이더가
        // 평타에서도 동작하도록, 콤보 전용으로 별도 플래그를 둔다.
        private bool inCombo;
        private bool comboCancelWindowOpen;


        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputHandler>();
            if (player == null) player = GetComponent<Player>();
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (stats == null) stats = GetComponent<PlayerStats>();
            if (move == null) move = GetComponent<FreeMoveController>();
            animator = GetComponent<Animator>();

            int tagCount = lockingStateTags != null ? lockingStateTags.Length : 0;
            lockingTagHashes = new int[tagCount];
            for (int i = 0; i < tagCount; i++)
                lockingTagHashes[i] = Animator.StringToHash(lockingStateTags[i]);

            hitTagHash = Animator.StringToHash(hitStateTag);
            continuousAimHash = Animator.StringToHash(continuousAimParam);

            // 참조 누락은 초기화 시점에 명확히 알린다(런타임 NRE로 늦게 터지지 않게).
            // 특히 Player는 '비활성(체크 해제) 상태로 오브젝트에 존재'해야 한다 — 제거하면 안 된다.
            // 잠금 machinery(LockMovement/UnlockMovement)와 ComboResetBehaviour가 Player를 참조하기 때문.
            if (input == null || player == null || combat == null || stats == null || move == null)
            {
                Debug.LogError(
                    "[FreeCombatController] 참조 누락. 같은 오브젝트에 다음이 있어야 합니다 — " +
                    $"PlayerInputHandler={input != null}, Player={player != null}(비활성으로 존재), " +
                    $"PlayerCombat={combat != null}, PlayerStats={stats != null}, FreeMoveController={move != null}",
                    this);
                enabled = false;   // 잘못된 상태로 Update가 계속 NRE 내는 것을 막는다
            }
        }

        // 입력·전투 이벤트 구독은 OnEnable↔OnDisable 짝으로. Player가 비활성이라 Player.OnEnable의
        // 구독이 안 걸리므로, 여기서 대신 구독한다(중복 없음).
        private void OnEnable()
        {
            input.Attacked += OnAttack;
            input.StrongAttacked += OnStrongAttack;
            input.SkillPressed += OnSkillPressed;
            combat.ComboStepStarted += OnComboStepStarted;
            combat.TargetHit += OnTargetHit;
        }

        private void OnDisable()
        {
            input.Attacked -= OnAttack;
            input.StrongAttacked -= OnStrongAttack;
            input.SkillPressed -= OnSkillPressed;
            combat.ComboStepStarted -= OnComboStepStarted;
            combat.TargetHit -= OnTargetHit;
        }

        private void Update()
        {
            // 안전장치: 로코모션 복귀를 놓쳐도 최대 시간 뒤 자동 해제(Player.Update 대체).
            if (player.IsMovementLocked)
            {
                actionLockTimer += Time.deltaTime;
                if (actionLockTimer >= maxActionLockTime) player.UnlockMovement();
            }

            // 공격 상태를 애니메이터 State Tag로 판단하되, '공격 본편'과 '로코모션으로 빠져나가는 블렌드'를 구분한다.
            //
            // 이동 잠금은 블렌드가 시작되면 바로 푼다. 블렌드 중에도 잠가두면 코드 이동은 막힌 채
            // 들어오는 로코모션 클립의 루트모션만 살아 있어, 캐릭터가 '바라보던 옛 방향'으로 강제로 끌려간다.
            //
            // 반면 점프는 블렌드가 끝날 때까지 계속 막는다. 전이 중에 점프하면
            // Any State → Jump_Start가 밀려 점프 모션이 공중에서 뒤늦게 재생되기 때문이다.
            bool inAttack = IsInAttackMotion();
            bool blendingOut = IsBlendingOutOfAttack();
            bool grace = Time.time < attackGraceUntil;

            move.ActionLocked = (inAttack && !blendingOut) || grace;
            move.JumpBlocked = inAttack || grace;

            // Animation Event 단계에서 예약된 카메라 방향 스냅을 여기(Update)서 적용한다.
            // 이벤트 단계에서 직접 돌리면 애니메이터가 되돌려버리고, LateUpdate에서 돌리면
            // CameraPivotController와 충돌한다(자세한 이유는 OnComboStepStarted 주석 참고).
            if (pendingAimSnap)
            {
                move.SnapToCameraForward();
                pendingAimSnap = false;
            }

            // ★ 카메라 = 조준이지만, 갱신 시점은 '타수가 시작되는 순간'뿐이다(매 프레임 아님).
            // 한 타수가 재생되는 동안엔 그 타수가 시작될 때의 카메라 방향을 그대로 유지하고,
            // 다음 타수(OnComboStepStarted 등)가 시작될 때 그 시점의 카메라 방향으로 다시 스냅한다.
            // 매 프레임 갱신하면 한 타수 재생 도중에도 방향이 계속 바뀌어, 예를 들어 Attack_1이
            // 재생되는 동안 카메라를 돌리면 그 타 자체가 새 방향으로 꺾여버린다(기획 의도 아님).
            // 이 게이트는 삭제하고, 각 발동 지점(OnAttack/OnStrongAttack/OnSkillPressed/
            // OnComboStepStarted)의 개별 SnapToCameraForward() 호출만으로 충분하다.
            //
            // 예외: continuousAimParam이 켜진 State(스킬 3번 검기 등)는 재생 내내 계속 갱신한다.
            // 투사체 조준은 시전 도중에도 카메라를 따라가야 하므로, 발동 시점 1회 스냅으로는 부족하다.
            if (inAttack && !blendingOut && animator.GetBool(continuousAimHash))
                move.SnapToCameraForward();

            // 공격 태그 자체를 벗어나면(로코모션 복귀·회피 등) 콤보도 끝난 것이다.
            // 다른 공격으로 캔슬해 나간 경우엔 OnStrongAttack/OnSkillPressed 성공 시점에 이미 꺼둔다.
            if (!inAttack) inCombo = false;

            // ★ 선입력 제거: 공격/스킬 State가 로코모션으로 '나가는' 블렌드가 막 시작된 순간,
            //   아직 소비 안 된 Attack 트리거·버퍼를 확실히 지운다.
            //
            //   클립 안에 ClearAttackBuffer 이벤트를 찍어두는 것만으로는 부족하다 — 그 이벤트는
            //   찍힌 프레임 '이전'의 클릭만 지운다. 그 이후(모션 막바지)에 들어온 클릭은 여전히
            //   Attack 트리거로 래치되고, 대부분 즉시 소비되지도 않는다 — 이미 로코모션으로 빠져나가는
            //   전이가 시작된 뒤라 Interruption Source가 기본 None이면 다른 전이가 못 끼어들기 때문이다.
            //   그 트리거는 로코모션에 완전히 도착할 때까지 살아남았다가, Idle/Walk_Loop/Walk_Stop/
            //   Run_Stop 자신이 가진 Attack 조건부 전이(콤보 진입용)에 뒤늦게 소비되어, 클릭하지
            //   않았는데도 모션이 끝나자마자 Attack_1이 저절로 나가는 것처럼 보인다.
            //
            //   블렌드 시작 '순간'(엣지)에 한 번만 지우면 충분하다 — 매 프레임 지우면 그 블렌드
            //   구간에 정상적으로 발동해야 할 다음 액션(예: 캔슬 창이 열려 있는 스킬/강공격 연계)까지
            //   같이 지워버린다. 지금 태그가 전부 "Attack" 하나로 통일돼 있어서, 이 수정 하나로
            //   Attack_1~3·Run_Attack·Strong_Attack·Skill_1~3 전부에 동시 적용된다.
            if (blendingOut && !wasBlendingOutOfAttack) combat.ClearAttackBuffer();
            wasBlendingOutOfAttack = blendingOut;

            // 스킬/강공격/대시공격의 캔슬 창이 열리는 순간(IsCastingSkill true→false), 좌클릭을
            // 꾹 누르고 있었다면 그 순간 클릭이 들어온 것처럼 OnAttack()을 대신 호출해 콤보로
            // 이어지게 한다. OnAttack()은 클릭 이벤트로만 불려서, 캔슬 창이 열리기 전부터 이미
            // 누르고 있으면 새 이벤트가 없어 콤보 진입 자체를 놓친다 — 짧게 다시 클릭하면 되던 건
            // 그 클릭이 새 이벤트였기 때문. OnAttack()을 그대로 재사용하므로 공중/대시/일반 콤보
            // 라우팅과 가드(사망·구르기·피격 등)가 클릭 경로와 완전히 동일하게 적용된다.
            if (wasCastingSkill && !combat.IsCastingSkill && input.AttackHeld)
                OnAttack();
            wasCastingSkill = combat.IsCastingSkill;

            // 공중 피격 체공: 피격 모션이 재생되는 동안 맞은 높이를 유지한다.
            // 빠져나가는 블렌드가 시작되면 바로 풀어, 낙하가 그 시점부터 자연스럽게 이어지게 한다
            // (공격 잠금을 blendingOut에서 푸는 것과 같은 이유 — 블렌드 내내 붙잡으면 공중에서 굳어 보인다).
            move.HitHovering = IsInHitMotion() && !IsBlendingOutOfHit() && !move.IsEffectivelyGrounded();

            // 착지하면 공중 공격 사용권을 회복한다(점프 1회당 1회).
            bool grounded = move.IsGrounded;
            if (grounded && !wasGrounded) jumpAttackUsed = false;
            wasGrounded = grounded;


            // 회피(지상 구르기 / 공중 대시)가 시작되는 순간, 진행 중이던 공격을 캔슬하고 잠금을 푼다.
            // 회피가 공격을 캔슬하는 최우선 동작이며, FreeMoveController가 잠금 중에도 이 둘을 허용한다.
            bool dodging = move.IsRolling || move.IsJumpDashing;
            if (dodging && !wasDodging)
            {
                combat.CancelAction();
                player.UnlockMovement();
                move.StrongAttackRising = false;     // 올려치기 상승 상태도 해제
            }
            wasDodging = dodging;
        }

        // 좌클릭 중재. Player.OnAttack의 라우팅을 미러한다(대시 공격 / 지상 콤보).
        private void OnAttack()
        {
            if (stats.IsDead) return;
            if (move.IsRolling) return;             // 구르기 중 공격 금지(회피 커밋 유지)
            if (IsInHitMotion()) return;             // 피격 경직 중 공격 금지(회피 외 모든 조작 차단)

            // 스킬/단타 시전 중 클릭 차단. 막지 않으면 트리거가 래치돼 시전 종료 직후 저절로 발동한다.
            if (combat.IsCastingSkill) return;

            // 공중 클릭 = 공중 공격(내려찍기). 점프 1회당 1회만(기획), 높이 제한 없음.
            // 카메라 = 조준이므로 발동 순간 카메라 정면으로 고정하고, 잠금으로 그 방향을 유지한다.
            // Start 구간은 체공, Loop 진입 시 하강(BeginDive)으로 전환된다.
            //
            // ★ 접지 판정은 IsEffectivelyGrounded로 한다. controller.isGrounded만 보면 Jump_Attack_End처럼
            //   이미 착지한 모션에서도 false로 잡혀, 좌클릭 시 공중 공격이 무한 재발동된다.
            if (!move.IsEffectivelyGrounded())
            {
                if (jumpAttackUsed) return;

                jumpAttackUsed = true;
                combat.UseJumpAttack();
                move.SnapToCameraForward();
                move.BeginDiveHover();
                Lock();
                move.StrongAttackRising = false;   // 점프공격으로 연계됐으니 올려치기 상승 상태 해제(이후 공중 대시 차단)
                return;
            }

            // 달리는 중 클릭 = 대시 공격(단타). 콤보로 이어지지 않는다(기획) — 단, 이건 '처음' 발동될
            // 때만이다. IsInAttackMotion()도 같이 확인해야 하는 이유: 방향키를 계속 쥔 채 연타하면
            // 캔슬 창이 열려 클릭이 통과해도 input.IsRunning은 여전히 true라서, 이 분기가 매번
            // 다시 걸려 combat.UseRunAttack()만 재호출하고 아래 콤보 분기(Attack 트리거)에 영영
            // 도달하지 못한다 — 방향키를 놓기 전까진 대시공격이 무한 재발동되는 것처럼 보였다.
            // 이미 공격 중(Attack 태그)이면 대시공격 재발동이 아니라 콤보로 이어져야 한다(연계 원칙).
            // 카메라 = 조준: 발동 순간 카메라 정면으로 스냅하고, 잠금으로 그 방향을 유지한다.
            // 잠금 동안 이동·회전이 막히며, 캔슬은 구르기(Shift)로만 가능하다(ActionLocked 블록의 후딜 캔슬).
            if (input.IsRunning && !IsInAttackMotion())
            {
                combat.UseRunAttack();
                move.SnapToCameraForward();   // 공격을 카메라 방향으로(발동 순간 고정)
                Lock();                        // 공격 중 이동·회전 차단
                return;
            }

            // 그 외(지상 정지/걷기) = 일반 콤보. 이어치기는 Animation Event가 처리한다.
            //
            // ★ 연타 시 comboStep 갱신 시차 버그 방지: PlayerCombat.OnAttackInput()은 comboStep이 아직
            //   0일 때(=OnAttackStart가 실제로 그 타수에 진입하기 전) 클릭마다 매번 '즉시' Attack 트리거를
            //   세운다(안 그러면 이 시차 동안의 클릭이 그냥 버려지기 때문 — 원래 설계 의도). 그런데 전이
            //   Duration(약 0.25초) 안에 빠르게 연타하면 comboStep이 계속 0인 채로 트리거가 여러 번 래치되고,
            //   Attack_1~3 체인 전이(Has Exit Time OFF, 진행도 게이트 없음)가 그걸 즉시 소비해 State를
            //   재생하지도 못한 채 건너뛴다.
            //
            //   게이트는 IsInAttackMotion()이 아니라 inCombo로 잡는다 — Run_Attack(대시공격)도 Tag가
            //   같은 "Attack"이라 IsInAttackMotion()은 대시공격 재생 중 내내 true다. 그걸 쓰면 대시공격
            //   캔슬 창이 열린 뒤 콤보로 넘어가는 첫 번째 정당한 트리거까지 같은 프레임에 지워버려서,
            //   대시공격 → 평타 연계가 아예 씹히거나 어쩌다 타이밍이 맞아야만 성공했다. inCombo는
            //   OnComboStepStarted(=OnAttackStart 이벤트가 실제로 그 타수에 진입해야 켜짐)에만 true가
            //   되므로, "콤보 체인에 이미 진입한 뒤의 재발동"만 정확히 걸러내고 다른 공격에서 콤보로
            //   처음 넘어오는 트리거는 건드리지 않는다.
            //
            //   attackBuffered는 그대로 둬서, comboStep이 실제로 갱신된 뒤 OnComboWindowOpen이
            //   정상적으로 소비하게 한다(입력 자체를 버리지 않는다).
            //
            //   재발동으로 판정해 트리거를 지운 경우엔 SnapToCameraForward/Lock도 건너뛴다 — 새 모션이
            //   시작되는 게 아닌데 방향만 카메라 쪽으로 스냅되거나 잠금 타이머만 갱신되면, 화면상
            //   "아무 모션도 안 나가는데 캐릭터만 홱 돈다"는 부수효과가 그대로 보인다.
            bool alreadyAttacking = inCombo;
            combat.OnAttackInput();
            if (alreadyAttacking)
            {
                player.Animation.ResetAttackTrigger();
                return;
            }

            move.SnapToCameraForward();
            Lock();
        }

        // 우클릭 강공격(올려치기) 중재. 지상에서만 발동한다.
        // 이후 공중에서 좌클릭하면 OnAttack의 !IsGrounded 분기가 공중 공격으로 자연스럽게 이어받는다.
        // 연계 창은 강공격 State에 붙인 AttackCancelBehaviour(SMB)가 진행도로 연다 — 이 공격 전용 처리는 없다.
        private void OnStrongAttack()
        {
            if (stats.IsDead) return;
            if (move.IsRolling) return;              // 회피 중 공격 금지(회피 커밋 유지)
            if (IsInHitMotion()) return;              // 피격 경직 중 공격 금지(회피 외 모든 조작 차단)
            if (combat.IsCastingSkill) return;       // 시전 중 중복 발동 차단(캔슬 창이 열리면 통과한다)
            if (inCombo && !comboCancelWindowOpen) return;  // 평타 캔슬 창(Attack_1~3의 AttackCancelBehaviour)
            if (!move.IsGrounded) return;            // 올려치기는 지상 전용(공중 우클릭은 무시)

            // 쿨타임에 걸리면 발동 실패다. 실패한 입력이 잠금으로 이어지면 안 된다(스킬과 같은 규칙).
            if (!combat.UseStrongAttack()) return;

            inCombo = false;   // 평타를 캔슬하고 나온 것이므로 콤보 상태를 명시적으로 종료
            move.SnapToCameraForward();   // 카메라 = 조준: 발동 순간 정면 고정
            Lock();                        // 공격 중 이동·회전 차단, 캔슬은 회피(Shift)로만

            // 상승은 여기서 시작하지 않는다. 준비 동작(windup) 동안 떠오르면 어색하므로,
            // 올려치기 클립의 Animation Event(OnStrongAttackRiseStart)가 발이 땅을 떠나는 프레임에
            // StrongAttackRising을 켜고, 정점의 OnStrongAttackRiseEnd가 끈다. 상승 자체는 클립 루트모션이 담당.
        }

        // 숫자키 스킬(1~4) 중재. Player.OnSkill의 판정 순서(쿨타임 → 게이지 → 발동)를 그대로 미러한다.
        // 트리거 애니메이션 재생 자체는 PlayerAnimation.PlaySkill(민준님 코드, combat.UseSkill 내부에서 호출)이 담당하므로,
        // 애니메이터 State/트리거 이름은 반드시 "Skill1"~"Skill4"(밑줄 없음)와 일치해야 한다.
        private void OnSkillPressed(int n)
        {
            if (stats.IsDead) return;
            if (move.IsRolling) return;               // 구르기 중 스킬 금지(회피 커밋 유지)
            if (IsInHitMotion()) return;               // 피격 경직 중 스킬 금지(회피 외 모든 조작 차단)
            // player.IsMovementLocked가 아니라 combat.IsCastingSkill을 게이트로 쓴다. 다른 공격(OnAttack/
            // OnStrongAttack)과 같은 규칙 — 캔슬 창(AttackCancelBehaviour)이 열리면 이동은 아직 잠긴 채여도
            // 다음 공격 입력은 통과시켜야, 대시공격→스킬처럼 애니메이션이 끝나길 기다리지 않고 연계된다.
            if (combat.IsCastingSkill) return;
            if (inCombo && !comboCancelWindowOpen) return;  // 평타 캔슬 창(Attack_1~3의 AttackCancelBehaviour)
            if (!move.IsEffectivelyGrounded()) return; // 공중 스킬은 없음(기획) — Any State 전이라도 공중에선 막는다

            // 판정 순서: 쿨타임 → 게이지 → 발동. 어느 한쪽만 소모되는 사고를 막는다.
            if (!combat.CanUseSkill(n)) return;
            if (!stats.TryUseSkillGauge(combat.GetSkillGaugeCost(n))) return;
            if (!combat.UseSkill(n)) return;           // 쿨타임은 위에서 확인했으므로 사실상 항상 성공

            inCombo = false;   // 평타를 캔슬하고 나온 것이므로 콤보 상태를 명시적으로 종료
            move.SnapToCameraForward();
            Lock();                                     // 스킬이 실제로 발동할 때만 이동 잠금
        }

        // 콤보 타수가 실제 시작될 때마다(OnAttackStart Animation Event 경유) 잠금을 갱신한다.
        // 꾹 누르기 콤보는 클릭(OnAttack)을 거치지 않으므로, 이 갱신이 없으면 안전장치 타이머가
        // 콤보 도중 잠금을 풀어버린다. Player.OnComboStepStarted와 동일한 처리.
        private void OnComboStepStarted()
        {
            // 새 타수가 시작될 때마다 캔슬 창을 다시 닫는다 — 타수마다 그 State의
            // AttackCancelBehaviour가 자기 진행도로 다시 열어야 한다(공격별 State 튜닝 원칙과 동일).
            inCombo = true;
            comboCancelWindowOpen = false;

            // ★ 여기서 SnapToCameraForward()를 직접 부르면 안 된다 — 이 메서드는 OnAttackStart
            //   Animation Event를 거쳐 호출되는데, 애니메이션 이벤트는 Update가 전부 끝난 뒤
            //   애니메이터 업데이트 단계에서 실행된다. 그 단계에서 transform.rotation을 바꿔도,
            //   Apply Root Motion이 켜져 있으면 애니메이터가 '루트모션 계산을 시작한 시점의 회전'을
            //   기준으로 트랜스폼을 다시 쓰기 때문에 우리가 바꾼 값이 그대로 되돌려진다.
            //   (그래서 클릭 경로인 OnAttack/OnStrongAttack/OnSkillPressed의 스냅은 멀쩡한데 —
            //    그쪽은 Input System 콜백이라 Update 단계다 — 콤보 2·3타처럼 이벤트로 이어지는
            //    타수만 방향이 1타 시점에 고정돼 보였다.)
            //
            //   LateUpdate로 미루는 것도 안 된다: 카메라가 추적하는 CameraPivot이 플레이어의
            //   자식이라, CameraPivotController가 LateUpdate에서 피벗 회전을 되돌려 카메라가
            //   딸려가지 않게 막고 있다. 그 전제("캐릭터 회전은 Update에서 끝난다")를 깨면
            //   실행 순서에 따라 한 프레임씩 어긋나 화면이 깜빡인다.
            //
            //   그래서 예약만 해두고 다음 Update에서 적용한다(한 프레임 지연은 전이 블렌드보다
            //   훨씬 짧아 체감되지 않는다).
            pendingAimSnap = true;
            Lock();
        }

        /// <summary>
        /// 평타(콤보) 캔슬 창을 연다. Attack_1~3 State에 붙는 AttackCancelBehaviour(SMB)가
        /// 진행도(Cancel Window Start)를 넘는 순간 호출한다. combat.IsCastingSkill과 달리
        /// 평타 전용 상태라, 스킬/강공격 State에 붙은 같은 SMB가 호출해도 무해하다(그때는 inCombo가
        /// 이미 false라 OnStrongAttack/OnSkillPressed의 게이트에 영향이 없다).
        /// </summary>
        public void OpenComboCancelWindow() => comboCancelWindowOpen = true;

        // 공격/스킬 적중마다 스킬 게이지(SG)를 회복한다(Player.OnTargetHit 미러).
        private void OnTargetHit(float gaugeGain) => stats.GainSkillGauge(gaugeGain);

        /// <summary>
        /// 공격 모션이 끝나는 프레임에 Animation Event로 호출한다(예: 대시 공격 클립).
        /// 이동 잠금을 풀고 콤보·시전 상태를 정리한다.
        ///
        /// 애니메이터의 ComboResetBehaviour(로코모션 복귀 시 자동 해제)가 붙어 있지 않아도
        /// 확실하게 잠금이 풀리도록 하는 명시적 경로다. 잠금이 안 풀리면 이동·점프 입력이
        /// 안전장치 타이머(maxActionLockTime)가 만료될 때까지 통째로 막힌다.
        /// 두 경로가 모두 동작해도 무해하다(해제는 여러 번 불러도 안전).
        /// </summary>
        public void EndAttack()
        {
            player.UnlockMovement();
            combat.ResetCombo();
        }

        // 공격이 실제로 발동할 때 이동을 잠근다. 잠금은 즉시 반영하고(레이스 방지),
        // 안전장치 타이머도 함께 리셋해 콤보로 갱신될 때마다 제한 시간이 다시 시작된다.
        private void Lock()
        {
            player.LockMovement();
            move.ActionLocked = true;
            actionLockTimer = 0f;

            // 애니메이터가 아직 공격 State에 진입하기 전이라 태그로는 잡히지 않는다.
            // 이 유예 시간 동안은 무조건 잠가 그 공백을 메운다.
            attackGraceUntil = Time.time + attackEnterGrace;
        }

        /// <summary>
        /// 지금 이동을 잠가야 하는 모션(lockingStateTags 중 하나)이 재생 중인지 여부.
        /// 공격으로 '들어가는' 전이 중에는 다음 State가 공격이므로 그것도 공격 중으로 본다.
        /// 공격에서 '나가는' 전이 중에는 현재 State가 아직 공격이라 블렌드가 끝날 때까지 true가 유지된다.
        /// </summary>
        private bool IsInAttackMotion()
        {
            if (animator == null || lockingTagHashes == null || lockingTagHashes.Length == 0) return false;

            if (IsAttackTagged(animator.GetCurrentAnimatorStateInfo(0))) return true;

            return animator.IsInTransition(0)
                && IsAttackTagged(animator.GetNextAnimatorStateInfo(0));
        }

        /// <summary>
        /// 공격에서 로코모션으로 '빠져나가는' 블렌드 중인지 여부(현재는 공격, 다음은 공격이 아님).
        /// 이 구간에는 이동 잠금을 풀어야 한다. 잠근 채로 두면 코드 이동은 막히고
        /// 들어오는 로코모션 클립의 루트모션만 작동해, 바라보던 옛 방향으로 강제 이동한다.
        /// </summary>
        private bool IsBlendingOutOfAttack()
        {
            if (animator == null || !animator.IsInTransition(0)) return false;

            return IsAttackTagged(animator.GetCurrentAnimatorStateInfo(0))
                && !IsAttackTagged(animator.GetNextAnimatorStateInfo(0));
        }

        /// <summary>
        /// 피격 모션(hitStateTag) 재생 중인지. 피격으로 '들어가는' 전이 중에는 다음 State가 피격이므로
        /// 그것도 피격 중으로 본다(공격 판정 IsInAttackMotion과 같은 규칙).
        /// </summary>
        private bool IsInHitMotion()
        {
            if (animator == null) return false;

            if (animator.GetCurrentAnimatorStateInfo(0).tagHash == hitTagHash) return true;

            return animator.IsInTransition(0)
                && animator.GetNextAnimatorStateInfo(0).tagHash == hitTagHash;
        }

        // 피격에서 피격이 아닌 State로 '빠져나가는' 블렌드 중인지. 이 구간에는 체공을 풀어야
        // 낙하가 블렌드와 함께 시작된다.
        private bool IsBlendingOutOfHit()
        {
            if (animator == null || !animator.IsInTransition(0)) return false;

            return animator.GetCurrentAnimatorStateInfo(0).tagHash == hitTagHash
                && animator.GetNextAnimatorStateInfo(0).tagHash != hitTagHash;
        }

        // 해당 State에 잠금 대상 Tag가 붙어 있는지. 비교는 Awake에서 캐싱한 해시로 한다.
        private bool IsAttackTagged(AnimatorStateInfo info)
        {
            for (int i = 0; i < lockingTagHashes.Length; i++)
            {
                if (info.tagHash == lockingTagHashes[i]) return true;
            }

            return false;
        }

        /// <summary>
        /// 지정한 Tag가 붙은 모션이 재생 중인지 여부(전이 중이면 진입할 State도 포함).
        /// 공격 종류마다 메커니즘이 다를 때 분기용으로 쓴다(예: 스킬 중에는 회피 캔슬 금지 등).
        /// </summary>
        /// <param name="tag">Animator State에 지정한 Tag 문자열</param>
        public bool IsInMotionTagged(string tag)
        {
            if (animator == null || string.IsNullOrEmpty(tag)) return false;

            int hash = Animator.StringToHash(tag);
            if (animator.GetCurrentAnimatorStateInfo(0).tagHash == hash) return true;

            return animator.IsInTransition(0)
                && animator.GetNextAnimatorStateInfo(0).tagHash == hash;
        }
    }
}
