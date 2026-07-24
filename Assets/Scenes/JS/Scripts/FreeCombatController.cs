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
    /// 현재 구현 범위: 좌클릭 라우팅(공중 공격 / 대시 공격 / 지상 콤보) + 콤보 잠금 갱신 + 적중 게이지 회복.
    /// (스킬·강공격·피격/사망 반응·구르기 무적은 다음 단계)
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
        [SerializeField] private string[] lockingStateTags = { "Attack" };
        // 트리거를 쏜 직후엔 아직 공격 State에 진입하기 전이라 태그가 안 잡힌다. 그 짧은 공백을 메우는 유예 시간.
        [SerializeField] private float attackEnterGrace = 0.15f;

        private Animator animator;
        private float attackGraceUntil;
        // Tag 비교는 매 프레임이라 문자열 해싱을 피해 Awake에서 1회 캐싱한다.
        private int[] lockingTagHashes;

        // 회피(구르기/공중 대시) 시작 순간을 잡아 진행 중이던 공격을 캔슬하기 위한 엣지 검출.
        private bool wasDodging;

        // 공중 공격은 점프 1회당 1회만 허용한다(기획). 착지하면 회복된다.
        private bool jumpAttackUsed;
        private bool wasGrounded;

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
            combat.ComboStepStarted += OnComboStepStarted;
            combat.TargetHit += OnTargetHit;
        }

        private void OnDisable()
        {
            input.Attacked -= OnAttack;
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
            }
            wasDodging = dodging;
        }

        // 좌클릭 중재. Player.OnAttack의 라우팅을 미러한다(대시 공격 / 지상 콤보).
        private void OnAttack()
        {
            if (stats.IsDead) return;
            if (move.IsRolling) return;             // 구르기 중 공격 금지(회피 커밋 유지)

            // 스킬/단타 시전 중 클릭 차단. 막지 않으면 트리거가 래치돼 시전 종료 직후 저절로 발동한다.
            if (combat.IsCastingSkill) return;

            // 공중 클릭 = 공중 공격(내려찍기). 점프 1회당 1회만(기획), 높이 제한 없음.
            // 카메라 = 조준이므로 발동 순간 카메라 정면으로 고정하고, 잠금으로 그 방향을 유지한다.
            // Start 구간은 체공, Loop 진입 시 하강(BeginDive)으로 전환된다.
            if (!move.IsGrounded)
            {
                if (jumpAttackUsed) return;

                jumpAttackUsed = true;
                combat.UseJumpAttack();
                move.SnapToCameraForward();
                move.BeginDiveHover();
                Lock();
                return;
            }

            // 달리는 중 클릭 = 대시 공격(단타). 콤보로 이어지지 않는다(기획).
            // 카메라 = 조준: 발동 순간 카메라 정면으로 스냅하고, 잠금으로 그 방향을 유지한다.
            // 잠금 동안 이동·회전이 막히며, 캔슬은 구르기(Shift)로만 가능하다(ActionLocked 블록의 후딜 캔슬).
            if (input.IsRunning)
            {
                combat.UseRunAttack();
                move.SnapToCameraForward();   // 공격을 카메라 방향으로(발동 순간 고정)
                Lock();                        // 공격 중 이동·회전 차단
                return;
            }

            // 그 외(지상 정지/걷기) = 일반 콤보. 이어치기는 Animation Event가 처리한다.
            combat.OnAttackInput();
            move.SnapToCameraForward();
            Lock();
        }

        // 콤보 타수가 실제 시작될 때마다(OnAttackStart Animation Event 경유) 잠금을 갱신한다.
        // 꾹 누르기 콤보는 클릭(OnAttack)을 거치지 않으므로, 이 갱신이 없으면 안전장치 타이머가
        // 콤보 도중 잠금을 풀어버린다. Player.OnComboStepStarted와 동일한 처리.
        private void OnComboStepStarted()
        {
            move.SnapToCameraForward();
            Lock();
        }

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
