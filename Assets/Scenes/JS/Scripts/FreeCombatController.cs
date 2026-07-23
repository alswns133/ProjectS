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

        [Header("낙하 공격")]
        // 이 높이 이상 떠 있어야 공중 공격(낙하 공격)이 나간다. 점프 직후 지면에 붙어 나가는 어색함 방지.
        [SerializeField] private float minDiveHeight = 2f;

        // 공중 공격은 점프 1회당 1회만 허용한다(기획). 착지하면 회복된다.
        private bool jumpAttackUsed;
        private bool wasGrounded;

        // 구르기 시작 순간을 잡아 진행 중이던 공격을 캔슬하기 위한 엣지 검출.
        private bool wasRolling;

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputHandler>();
            if (player == null) player = GetComponent<Player>();
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (stats == null) stats = GetComponent<PlayerStats>();
            if (move == null) move = GetComponent<FreeMoveController>();

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

            // Player의 잠금 상태를 이동 컨트롤러에 반영(수평 이동/구르기/점프/대시 차단).
            move.ActionLocked = player.IsMovementLocked;

            // 잠금이 풀렸는데 체공/다이브 상태가 남아 있으면 정리한다(안전장치).
            // 정상 흐름에선 Loop 진입(BeginDive)과 착지에서 각각 꺼지지만, Start 중 중단 등 예외 대비.
            if (!player.IsMovementLocked)
            {
                if (move.Hovering) move.Hovering = false;
                if (move.Diving) move.Diving = false;
            }

            // 착지: 공중 공격 사용권 회복(점프 1회당 1회). 내려찍기는 착지하면서 End(impact)
            // 클립으로 이어지고, 로코모션 복귀 시 SMB가 잠금을 푼다(별도 캔슬 불필요).
            bool grounded = move.IsGrounded;
            if (grounded && !wasGrounded)
                jumpAttackUsed = false;
            wasGrounded = grounded;

            // 구르기 시작 순간, 진행 중이던 공격을 캔슬하고 잠금을 푼다.
            // (후딜 캔슬 = 회피가 공격을 캔슬하는 최우선 동작. FreeMoveController가 잠금 중에도 구르기를 허용한다)
            if (move.IsRolling && !wasRolling)
            {
                combat.CancelAction();
                player.UnlockMovement();
            }
            wasRolling = move.IsRolling;
        }

        // 좌클릭 중재. Player.OnAttack의 라우팅을 미러한다(공중 공격 / 대시 공격 / 지상 콤보).
        private void OnAttack()
        {
            if (stats.IsDead) return;
            if (move.IsRolling) return;             // 구르기 중 공격 금지(회피 커밋 유지)

            // 스킬/단타 시전 중 클릭 차단. 막지 않으면 트리거가 래치돼 시전 종료 직후 저절로 발동한다.
            if (combat.IsCastingSkill) return;

            // 공중 클릭 = 공중 공격(내려찍기). 점프 1회당 1회만(기획).
            // 호버(높이 고정) 없이 ActionLocked 블록의 중력으로 떨어지며 슬램한다.
            // 3단 클립(Start→Loop→End)은 점프와 같은 원리: 하강 → 착지(isGrounded) → 임팩트.
            if (!move.IsGrounded)
            {
                if (jumpAttackUsed) return;
                if (!move.HasDiveClearance(minDiveHeight)) return;   // 너무 낮으면 낙하 공격 불가

                jumpAttackUsed = true;
                combat.UseJumpAttack();
                move.SnapToCameraForward();   // 공중에서도 카메라 방향으로 공격(기획)
                move.Hovering = true;         // Start 동안 체공 유지. Loop 진입 시 SMB(BeginDive)가 하강 시작.
                Lock();
                return;
            }

            // 달리는 중 클릭 = 대시 공격(단타). 콤보로 이어지지 않는다(기획).
            if (input.IsRunning)
            {
                combat.UseRunAttack();
                move.SnapToCameraForward();
                Lock();
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
        /// 낙하 공격 Loop State 진입 시 SMB(DiveHoverBehaviour)가 호출한다.
        /// Start 동안 유지하던 체공을 끝내고, Loop 시작과 동시에 빠른 하강(다이브)을 시작한다.
        /// </summary>
        public void BeginDive()
        {
            move.Hovering = false;
            move.Diving = true;   // 빠른 하강 시작 → isLanding → End(슬램 임팩트)
            Debug.Log("[DiveDbg] BeginDive 호출됨 (Loop 진입 → Diving ON)", this);   // TEMP DEBUG (진단 후 제거)
        }

        // 공격이 실제로 발동할 때 이동을 잠근다. 잠금은 즉시 반영하고(레이스 방지),
        // 안전장치 타이머도 함께 리셋해 콤보로 갱신될 때마다 제한 시간이 다시 시작된다.
        private void Lock()
        {
            player.LockMovement();
            move.ActionLocked = true;
            actionLockTimer = 0f;
        }
    }
}
