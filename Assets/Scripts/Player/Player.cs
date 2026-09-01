using UnityEngine;
using ProjectS.Core;
using ProjectS.Events;

namespace ProjectS.Players
{
    /// <summary>
    /// 플레이어의 중앙 컨텍스트(뇌). 각 기능 컴포넌트를 한곳에서 보유하고,
    /// 상태 머신을 구동하며, 외부 입력 이벤트를 상태 전환으로 중재한다.
    /// 상태들은 이 Player를 통해 컴포넌트(Movement, Animation 등)에 접근한다.
    ///
    /// 이동 잠금·연계 판정은 두 모델을 캐릭터별로 가른다(<see cref="PlayerAnimation.UsesMotionTags"/>):
    ///  - 자유 이동(3단 로코모션) 캐릭터: 공격 State Tag 기반(FreeCombat 이관). 블렌드 아웃 시 즉시 이동 해제,
    ///    점프는 블렌드 종료까지 차단, 콤보 캔슬 창·지속 조준·공중 대시 캔슬까지 태그로 판정한다.
    ///  - 기존(Z 블렌드) 캐릭터: ComboResetBehaviour + 안전장치 타이머 기반의 기존 잠금 모델을 그대로 쓴다.
    /// </summary>
    // RequireComponent: Player를 붙이면 아래 부품들이 자동으로 함께 추가된다.
    // → 팀원이 컴포넌트를 빠뜨리는 실수를 '구조'가 막아준다.
    [RequireComponent(typeof(PlayerInputHandler))]
    [RequireComponent(typeof(PlayerMovement))]
    [RequireComponent(typeof(PlayerAnimation))]
    [RequireComponent(typeof(PlayerCombat))]
    [RequireComponent(typeof(PlayerStats))]
    [RequireComponent(typeof(PlayerEffects))]
    // 미니맵 등록도 부품처럼 강제한다. 자동 추가 시 type 기본값이 Enemy이므로,
    // 플레이어는 인스펙터에서 MinimapMarkerSource의 type을 Player로 한 번 바꿔 줘야 한다.
    [RequireComponent(typeof(MinimapMarkerSource))]
    [RequireComponent(typeof(PlayerHitCombo))]
    public class Player : MonoBehaviour
    {
        // 컴포넌트 참조: 외부에선 읽기만(접근은 허용, 교체는 금지) → { get; private set; }
        public PlayerInputHandler Input { get; private set; }
        public PlayerMovement Movement { get; private set; }
        public PlayerAnimation Animation { get; private set; }
        public PlayerCombat Combat { get; private set; }

        public PlayerStats Stats { get; private set; }

        // 상태 인스턴스도 외부(상태끼리 전환)에서 참조하므로 읽기 전용 공개
        public PlayerFreeState FreeState { get; private set; }

        public PlayerDeadState DeadState { get; private set; }

        public PlayerRollState RollState { get; private set; }
        public PlayerHitState HitState { get; private set; }

        /// <summary>공중 대시(점프 대시) 상태. 공중 회피 입력 시 Player가 이 상태로 전환한다.</summary>
        public PlayerJumpDashState JumpDashState { get; private set; }

        /// <summary>보스에게 잡힌 구속 상태. 보스 잡기 히트가 성공하면 <see cref="OnGrabbed"/>가 전환한다.</summary>
        public PlayerGrabbedState GrabbedState { get; private set; }

        public PlayerEffects Effect { get; private set; }

        /// <summary>연속 유효타 카운터(히트 콤보). <see cref="OnTargetHit"/>가 적중마다 AddHit을 호출한다.</summary>
        public PlayerHitCombo HitCombo { get; private set; }

        private PlayerStateMachine sm; // 전환(Exit→Enter)을 책임지는 머신. 내부 전용

        /// <summary>
        /// 구르기 중 여부. 구르는 동안 점프·공격·스킬·재구르기 입력을 차단하는 게이트.
        /// 별도 플래그 대신 상태 머신의 현재 상태로 판정 → 플래그 동기화 실수가 원천 차단된다.
        /// </summary>
        public bool IsRolling => sm.Current == RollState;

        /// <summary>
        /// 피격 경직 중 여부. 경직 동안 점프·공격·스킬 입력을 차단하는 게이트.
        /// 구르기는 차단하지 않는다(경직을 회피로 캔슬하는 조작 허용 — 회피 최우선 철학).
        /// </summary>
        public bool IsStaggered => sm.Current == HitState;

        /// <summary>
        /// 공중 대시(점프 대시) 중 여부
        /// </summary>
        public bool IsJumpDashing => sm.Current == JumpDashState;

        /// <summary>
        /// 보스에게 잡혀 있는지 여부. 잡힘 동안 이동·점프·공격·회피 입력을 전부 막고,
        /// 피격 데미지는 받되 경직(HitState)으로는 새지 않게 하는 게이트다(구속 유지).
        /// </summary>
        public bool IsGrabbed => sm.Current == GrabbedState;

        /// <summary>
        /// 지금 몬스터를 통과해야 하는 상태인지(구르기·공중대시·각성기 시전·보스에게 잡힘).
        /// PlayerEnemySeparation이 true면 그 프레임 소프트 분리를 건너뛴다.
        /// 잡힘을 포함하는 이유: 소프트 분리가 보스 앵커로 스냅한 위치를 밀어내 구속이 떨리기 때문.
        /// </summary>
        public bool PassThroughEnemies => IsRolling || IsJumpDashing || Combat.IsCastingUltimate || IsGrabbed;

        // 공격/스킬/강공격 입력을 피격 중 막는 기준. 캐릭터별로 다르다:
        //  - 태그 캐릭터: 원본(FreeCombatController)처럼 실제 Hit 애니메이션(Animator Hit 태그)을 기준으로 한다.
        //    HitState 타이머와 Hit 클립 길이가 어긋나도 입력 허용 구간이 모션과 정확히 일치한다
        //    (타이머가 짧으면 애니메이션 도중 공격이 새고, 길면 애니메이션 종료 후에도 막히던 문제 해소).
        //  - 기존 Z 블렌드 캐릭터: PlayerHitState 타이머(IsStaggered)를 그대로 쓴다(기존 동작 유지).
        // 점프(TryJump)는 원래대로 IsStaggered만 본다 — 원본에서도 점프는 타수 태그 판정 대상이 아니었다.
        private bool IsHitBlocked => usesTags ? IsInHitMotion() : IsStaggered;

        /// <summary>
        /// 태그 기반 캐릭터 여부. 상태 클래스가 "클립 종료 기준" 탈출을 쓸 수 있는지 가를 때 읽는다.
        /// (PlayerHitState가 이동 잠금 해제를 타이머가 아니라 Hit 모션 종료에 맞추는 데 쓴다.)
        /// </summary>
        public bool UsesMotionTags => usesTags;

        /// <summary>
        /// 현재 Hit 애니메이션 모션(또는 그 전이) 중인지. PlayerHitState가 클립 종료 기준 탈출에 쓴다.
        /// 공격 입력 게이트(IsHitBlocked)와 같은 기준이라, 이동 해제와 공격 허용이 정확히 같은 프레임에 열린다.
        /// </summary>
        public bool IsPlayingHitMotion => IsInHitMotion();

        /// <summary>
        /// 구르기·피격·사망으로 전투 동작이 중단된 상태인지 여부.
        /// 입력(OnAttack/OnSkill)은 이미 이 조건으로 막지만, 애니메이션 이벤트(이펙트·검기)는
        /// 클립이 블렌드 아웃되며 뒤늦게 도착할 수 있어 같은 조건으로 한 번 더 막아야 한다.
        /// 안 막으면 구르기 캔슬 순간 뒤늦은 이벤트가 구르기 방향으로 이펙트/검기를 내보낸다.
        /// </summary>
        public bool IsActionInterrupted => IsRolling || IsStaggered || Stats.IsDead;

        // ── 이동 잠금(공격·스킬 중 이동 차단) ────────────────────────────
        // 해제는 동작이 끝나 로코모션으로 돌아올 때 ComboResetBehaviour가 담당한다.
        // 안전장치: 해제 신호를 놓쳐도 이 시간 뒤 자동 해제
        [SerializeField] private float maxActionLockTime = 3f;

        // 회피(구르기) 1회당 스태미나 소모량. 잔량 판정·차감은 PlayerStats가 담당하고,
        // 여기는 '얼마를 쓸지'만 안다(입력 중재자가 비용을 소유).
        [Header("구르기")]
        [SerializeField] private float rollStaminaCost = 20f;

        [Header("점프")]
        [SerializeField, Min(0f)] private float autoJumpDelay = 0.08f;

        // ── 컷신(보스 등장 등) 입력 잠금 ──────────────────────────────────
        [Header("컷신(보스 등장 등)")]
        // 컷신 종료 신호(EndCutscene)를 놓쳐도 이 시간이 지나면 입력이 스스로 풀린다.
        // "연출이 끝났는데도 조작이 안 돌아오는" 최악(신호 누락)을 막는 안전장치다.
        // 어떤 등장 연출보다 넉넉히 길게 잡는다 — 정상 흐름에선 이 타이머 전에 EndCutscene가 온다.
        [SerializeField, Min(0f)] private float maxCutsceneDuration = 12f;

        // 마우스 시점 조작까지 함께 얼릴 카메라(선택). 연결하면 컷신 동안 마우스가 회전을 쌓지 못하게 막아
        // 연출이 끝나 평소 카메라로 돌아올 때 화면이 홱 도는 것을 방지한다. 비워 두면 키 입력만 막는다.
        [SerializeField] private CameraPivotController cameraPivot;

        // 지금 컷신(전체 입력 잠금) 중인지. 잠금은 PlayerInputHandler.SetInputSuspended가 담당하고,
        // 여기서는 상태 플래그와 안전 타이머만 들고 있는다.
        private bool inCutscene;
        private float cutsceneTimer;

        /// <summary>보스 등장 연출 등으로 전체 입력이 잠긴 컷신 중인지 여부.</summary>
        public bool InCutscene => inCutscene;

        // ── 태그 기반 이동 잠금/연계 판정(자유 이동 캐릭터 전용) ──────────────
        [Header("공격 상태 판정 (태그 기반 캐릭터 전용)")]
        // 이동을 잠글 Animator State Tag 목록. 공격 종류마다 다른 태그를 붙여 구분할 수 있다.
        // State 하나에 Tag는 하나만 지정 가능하고, 비교는 정확히 일치해야 한다(접두어 매칭 불가).
        [SerializeField] private string[] lockingStateTags = { "Attack", "Hit" };
        // 트리거를 쏜 직후엔 아직 공격 State에 진입하기 전이라 태그가 안 잡힌다. 그 공백을 메우는 유예 시간.
        [SerializeField] private float attackEnterGrace = 0.15f;
        // 피격 모션 State Tag(Hit / Hit_Large / Hit_Jump 모두 같은 태그). 공중 피격 체공 판정에 쓴다.
        [SerializeField] private string hitStateTag = "Hit";
        // 이 Bool 파라미터가 켜진 State(스킬 3번 검기 등)에서만 매 프레임 카메라 방향을 갱신한다(지속 조준).
        [SerializeField] private string continuousAimParam = "continuousAim";

        // Tag 비교는 매 프레임이라 문자열 해싱을 피해 Awake에서 1회 캐싱한다.
        private int[] lockingTagHashes;
        private int hitTagHash;
        private int continuousAimHash;

        // 이동 잠금이 시작된 뒤 흐른 시간. 해제 신호(로코모션 복귀)를 놓쳐도
        // 안전장치로 잠금을 풀기 위해 잰다.
        private float actionLockTimer;
        private float stableGroundedTime;

        // 태그 기반 판정 상태값(자유 이동 캐릭터 전용).
        private float attackGraceUntil;

        /// <summary>
        /// 공격/스킬을 방금 시작해 아직 애니메이터가 공격 State로 넘어가기 전인 유예(grace) 구간인지.
        /// LockMovement가 <see cref="attackEnterGrace"/>만큼 창을 연다. 이 창 동안은 애니메이터가
        /// 잠깐 로코모션에 머물러 있어도 "동작이 끝났다"고 보면 안 된다 — ComboResetBehaviour가
        /// 이 순간 teardown을 돌리면 UseSkill이 방금 켠 시전 플래그(IsCastingSkill/currentAction)를
        /// 도로 지워, 이어지는 검기 발사 프레임이 통째로 씹힌다. 그 레이스를 막는 게이트로 쓴다.
        /// </summary>
        public bool IsInActionEnterGrace => Time.time < attackGraceUntil;
        private bool isJumpBlocked;         // 공격 본편+유예 동안 점프를 막는다(블렌드 아웃 후에도 종료까지 유지).
        private bool pendingAimSnap;        // 다음 Update에 카메라 스냅 적용 예약(Animation Event 단계 회전 무효 회피).
        private bool wasBlendingOut;        // 블렌드 아웃이 '막 시작된' 프레임(엣지)만 잡기 위한 이전값.
        private bool wasCastingSkill;       // 캔슬 창이 열리는 순간(IsCastingSkill true→false)을 잡기 위한 이전값.
        private bool wasRollHeld;           // 회피 키 눌림 엣지(공중 대시 발동)를 잡기 위한 이전값.

        // 태그 기반 잠금 모델을 쓰는 캐릭터인지. 3단 로코모션 컨트롤러만 공격 Tag를 단다.
        private bool usesTags;

        // 전투/액션 입력 허용 여부(마을=off, 던전=on). 씬 진입 시 EnterVillage/EnterDungeon이 세팅한다.
        // 원본 AnimatorZone의 CombatEnabled를 씬 경로로 옮긴 것. 마을에선 이동·점프는 허용하고,
        // 구르기·공중대시·공격/강공격/스킬은 막는다(마을 애니메이터에 구르기/공중대시 클립이 없어 기획상 미지원).
        private bool combatEnabled = true;

        // 마우스 모드(커서 해제) 상태. HUD를 클릭하는 중이므로 그 클릭이 공격으로 새면 안 된다.
        // 구르기·공중대시는 키보드라 클릭이 샐 일이 없고, 막으면 조작이 얼어붙은 느낌이 나서 그대로 둔다.
        private bool mouseMode;

        // 공격/강공격/스킬을 받을 수 있는 상태인지. 두 조건을 한곳에 모아 판정이 갈라지지 않게 한다.
        private bool CombatAllowed => combatEnabled && !mouseMode;

        /// <summary>
        /// 현재 전투 구역인지(true=던전 등 전투 허용, false=마을 등 전투 비활성).
        /// HUD 스킬 슬롯이 스폰 시 <see cref="ProjectS.Events.PlayerEvents.OnCombatZoneChanged"/>를
        /// 놓쳤을 때 초기값을 직접 끌어오기 위해 노출한다.
        /// </summary>
        public bool IsCombatEnabled => combatEnabled;

        /// <summary>
        /// 이동이 잠겨 있는지 여부. 공격/스킬 발동 중 true.
        /// FreeState가 매 프레임 이 값을 확인해, 잠금 중이면 수평 이동을 멈춘다.
        /// 태그 기반 캐릭터에서는 매 프레임 공격 태그로 재계산되고, 그 외엔 Lock/Unlock 호출로 관리된다.
        /// </summary>
        public bool IsMovementLocked { get; private set; }

        private bool rollAttackBuffered;   // 구르기 중 들어온 공격 클릭 예약

        private bool wasRolling;           // 구르기 종료 엣지 감지

        /// <summary>
        /// 공격/스킬이 실제로 발동하는 순간 이동을 잠근다(OnAttack/OnSkill에서 호출).
        /// 안전장치 타이머도 함께 리셋하므로, 콤보로 잠금이 갱신될 때마다 제한 시간이 다시 시작된다.
        /// 태그 기반 캐릭터에서는 유예 시간(grace)을 시작해, 아직 공격 State에 진입하기 전의 공백을 메운다.
        /// </summary>
        public void LockMovement()
        {
            IsMovementLocked = true;
            actionLockTimer = 0f;
            attackGraceUntil = Time.time + attackEnterGrace;
        }

        /// <summary>
        /// 공격/스킬 애니메이션이 끝나 로코모션 상태로 돌아왔을 때 이동 제한을 해제한다.
        /// 주로 ComboResetBehaviour(로코모션 진입 시)가 호출하며, 이 호출을 놓치면 안전장치 타이머가 대신 푼다.
        /// 스킬 시전 플래그와 점프 공격 호버링도 함께 정리한다.
        /// </summary>
        public void UnlockMovement()
        {
            IsMovementLocked = false;
            Combat.EndSkillCast();
            // 각성기(4번 스킬) 무적은 로코모션 복귀(= 이 시점)에 반드시 내린다. EndSkillCast는 캔슬 창에서도
            // 불리므로 무적 해제 창구로 쓰지 않고, 이동 잠금 해제와 같은 '진짜 종료' 경로에 묶는다.
            // 안전장치 타이머로 잠금이 풀리는 경우에도 함께 해제되어 무적이 영구히 남는 사고를 막는다.
            Combat.ReleaseSkillInvincibility();
            // 점프 공격 호버링 해제. 모션이 끝나 로코모션으로 복귀하는 이 시점이
            // "모션이 끝나면 다시 내려간다"(기획)에 해당한다. 안전장치 경로로 풀릴 때도 함께 풀린다.
            Movement.SetHover(false);
            // 올려치기 상승도 확실히 해제(중력 재개). 안 풀면 상승 억제가 남아 공중에서 굳는다.
            Movement.StrongAttackRising = false;
        }

        /// <summary>
        /// 마을 진입 처리. 씬(<see cref="ProjectS.Scenes.VillageGather"/>)의 Enter에서 호출한다.
        /// 전투 입력을 끄고 마을 애니메이터 컨트롤러로 전환하며, 진행 중이던 공격/이동 잠금을 정리한다.
        /// 원본 AnimatorZone의 마을 구역 처리(전투 조율자 off + ActionLocked/JumpBlocked 해제)를 씬 경로로 옮긴 것.
        /// 컨트롤러 대입이 State를 Idle로 되돌려 태그 잠금은 자연히 풀리지만, 잔여 콤보/시전/상승 상태까지
        /// 확실히 정리해 마을에서 조작이 굳지 않게 한다.
        /// </summary>
        public void EnterVillage()
        {
            combatEnabled = false;
            Animation.UseVillageController();
            Movement.UseVillageSpeed();

            Combat.CancelAction();
            UnlockMovement();

            // HUD 스킬 슬롯이 마을에선 아이콘을 흐리게 표시하도록 알린다(스킬 사용 불가 안내).
            PlayerEvents.FireCombatZoneChanged(combatEnabled);
        }

        /// <summary>
        /// 던전 진입 처리. 씬(<see cref="DungeonGather"/>)의 Enter에서 호출한다.
        /// 던전 애니메이터 컨트롤러(오버라이드)로 전환하고 전투 입력을 켠다.
        /// </summary>
        public void EnterDungeon()
        {
            combatEnabled = true;
            Animation.UseDungeonController();
            Movement.UseDungeonSpeed();

            // HUD 스킬 슬롯의 흐림 표시를 원래대로(사용 가능) 되돌리도록 알린다.
            PlayerEvents.FireCombatZoneChanged(combatEnabled);
        }

        /// <summary>
        /// 공격 모션이 끝나는 프레임에 Animation Event로 호출한다(예: 대시 공격 클립).
        /// 이동 잠금을 풀고 콤보·시전 상태를 정리하는 명시적 경로. ComboResetBehaviour와 함께 동작해도 무해하다.
        /// </summary>
        public void EndAttack()
        {
            UnlockMovement();
            Combat.ResetCombo();
        }

        /// <summary>
        /// 컷신(보스 등장 연출 등) 시작. 모든 게임플레이 입력을 잠그고, 진행 중이던 동작(공격/점프/이펙트·
        /// 이동 잠금)을 정리해 플레이어를 그 자리에 세운다. 연출 도중 조작이 새거나 캐릭터가 미끄러지지 않게 한다.
        ///
        /// 씬의 등장 Timeline이 <b>시작 시점</b>에 이 메서드를(Signal/UnityEvent 등으로) 호출하고,
        /// <b>종료 시점</b>에 <see cref="EndCutscene"/>를 호출하도록 배선한다. 종료 신호를 놓쳐도
        /// <see cref="maxCutsceneDuration"/> 뒤 <see cref="Update"/>의 안전 타이머가 스스로 입력을 되살린다.
        /// 이미 컷신 중에 다시 호출하면 안전 타이머만 리셋한다(연출이 이어질 때 대비).
        /// </summary>
        public void BeginCutscene()
        {
            cutsceneTimer = 0f;   // (재)호출마다 안전 타이머를 처음부터 다시 센다
            if (inCutscene) return;

            inCutscene = true;

            // 진행 중이던 동작을 끊는다(구속 진입과 같은 정리 절차). 연출 중 뒤늦은 이펙트/이동 잠금 잔재 방지.
            Combat.CancelAction();
            Effect.AllStopEffect();
            UnlockMovement();
            Movement.CancelJump();
            Animation.SetLocomotion(false, false);   // 로코모션 bool을 내려 걷기 Loop로 새지 않게 한다

            // 모든 InputAction을 Disable → 이동·점프·공격·스킬·회피·상호작용·커서 토글이 한 번에 막힌다.
            Input.SetInputSuspended(true);

            // 연결돼 있으면 마우스 시점 조작도 함께 얼린다(회전 누적 방지).
            if (cameraPivot != null) cameraPivot.SetInputLocked(true);
        }

        /// <summary>
        /// 컷신 종료. 잠갔던 입력을 되살린다. 등장 Timeline의 종료 신호에서 호출한다.
        /// 컷신 중이 아니면 무시한다(종료 신호가 두 번 와도, 안전 타이머로 이미 풀린 뒤에 와도 안전).
        /// </summary>
        public void EndCutscene()
        {
            if (!inCutscene) return;

            inCutscene = false;
            cutsceneTimer = 0f;

            Input.SetInputSuspended(false);
            if (cameraPivot != null) cameraPivot.SetInputLocked(false);
        }


        private void Awake()
        {
            // 컴포넌트 캐싱은 Awake에서 1회만. 매 프레임 GetComponent를 피하기 위함.
            Input = GetComponent<PlayerInputHandler>();
            Movement = GetComponent<PlayerMovement>();
            Animation = GetComponent<PlayerAnimation>();
            Combat = GetComponent<PlayerCombat>();
            Stats = GetComponent<PlayerStats>();
            Effect = GetComponent<PlayerEffects>();
            HitCombo = GetComponent<PlayerHitCombo>();

            sm = new PlayerStateMachine();
            // 상태를 미리 생성해 보관 → 전환할 때마다 new 하지 않으므로 GC 부담이 없다.
            FreeState = new PlayerFreeState(this);
            DeadState = new PlayerDeadState(this);
            RollState = new PlayerRollState(this);
            HitState = new PlayerHitState(this);
            JumpDashState = new PlayerJumpDashState(this);
            GrabbedState = new PlayerGrabbedState(this);

            usesTags = Animation.UsesMotionTags;

            int tagCount = lockingStateTags != null ? lockingStateTags.Length : 0;
            lockingTagHashes = new int[tagCount];
            for (int i = 0; i < tagCount; i++)
                lockingTagHashes[i] = Animator.StringToHash(lockingStateTags[i]);

            hitTagHash = Animator.StringToHash(hitStateTag);
            continuousAimHash = Animator.StringToHash(continuousAimParam);
        }

        // 이벤트 구독/해제는 OnEnable↔OnDisable 짝으로. 짝을 안 맞추면 중복 구독이 쌓인다.
        private void OnEnable()
        {
            Input.SkillPressed += OnSkill;
            Input.Attacked += OnAttack;
            Input.StrongAttacked += OnStrongAttack;
            Input.CursorTogglePressed += OnCursorToggle;
            Combat.ComboStepStarted += OnComboStepStarted;
            Combat.TargetHit += OnTargetHit;
            Stats.Damaged += OnDamaged;
            PlayerEvents.OnPlayerDied += OnDied;   // 죽음 구독
        }
        private void OnDisable()
        {
            Input.SkillPressed -= OnSkill;
            Input.Attacked -= OnAttack;
            Input.StrongAttacked -= OnStrongAttack;
            Input.CursorTogglePressed -= OnCursorToggle;
            Combat.ComboStepStarted -= OnComboStepStarted;
            Combat.TargetHit -= OnTargetHit;
            Stats.Damaged -= OnDamaged;
            PlayerEvents.OnPlayerDied -= OnDied;   // 죽음 구독 해제
        }

        private void Start()
        {
            // 시작은 커서 잠금 상태(= Alt를 눌러 카메라 조작 모드에 들어간 상태). 이후 Alt로 토글.
            SetCursorLocked(true);
            sm.ChangeState(FreeState); // 시작 상태 진입
        }

        // Alt(커서 토글 키)를 누를 때마다 잠금 ↔ 해제를 오간다.
        // 현재 상태를 별도 플래그가 아닌 Cursor.lockState로 판정 → 에디터 ESC 등
        // 외부 요인으로 잠금이 풀려도 다음 토글이 실제 상태 기준으로 동작한다.
        private void OnCursorToggle()
        {
            SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
        }

        /// <summary>
        /// 커서 모드를 직접 지정한다. HUD 단축키가 창을 열면서 마우스 모드로 함께 전환할 때 쓴다
        /// (Alt 토글과 같은 경로를 타야 <see cref="PlayerEvents.OnCursorModeChanged"/>가 빠지지 않는다).
        /// </summary>
        /// <param name="useMouse">true=커서 해제(마우스 조작), false=커서 잠금(TPS 조작)</param>
        public void SetCursorMode(bool useMouse) => SetCursorLocked(!useMouse);

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            mouseMode = !locked;

            // HUD 위젯(퀘스트 트래커 등)이 클릭 가능 여부를 이 신호로 가른다.
            PlayerEvents.FireCursorModeChanged(!locked);
        }

        private void Update()
        {
            // 컷신(전체 입력 잠금) 안전장치: 종료 신호(EndCutscene)를 놓쳐도 최대 시간이 지나면 스스로 입력을
            // 되살린다. "연출이 끝났는데 조작이 안 돌아오는" 최악을 막는다. unscaled로 세는 이유 — 등장 연출은
            // 히트스톱/컷신으로 timeScale이 낮아진 순간과 겹치기 쉬워, deltaTime으로 재면 안전 해제가 한없이 늦어진다.
            // (입력이 잠긴 동안에도 아래 폴링은 그대로 돌지만, 모든 InputAction이 Disable돼 값이 0/false라 무해하다.)
            if (inCutscene)
            {
                cutsceneTimer += Time.unscaledDeltaTime;
                if (cutsceneTimer >= maxCutsceneDuration)
                {
                    Debug.LogWarning($"[Player] 컷신 종료 신호(EndCutscene)를 {maxCutsceneDuration}초 안에 못 받아 " +
                                     "안전장치로 입력을 되살립니다. 등장 Timeline의 종료 신호 배선을 확인하세요.", this);
                    EndCutscene();
                }
            }

            // 태그 기반 캐릭터: 공격 태그로 이동 잠금/점프 차단/연계 상태를 매 프레임 재계산한다.
            if (usesTags)
            {
                UpdateTagLock();
            }
            else
            {
                // 기존 캐릭터: 해제 신호를 놓쳐도 최대 시간이 지나면 스스로 풀린다.
                if (IsMovementLocked)
                {
                    actionLockTimer += Time.deltaTime;
                    if (actionLockTimer >= maxActionLockTime) UnlockMovement();
                }
            }

            UpdateStableGroundedTime();

            // 점프 버튼을 누르고 있으면 착지할 때마다 자동으로 다시 점프(꾹 누르면 연속 점프)
            TryJump();

            // 회피 버튼을 누르고 있으면 회피가 끝날 때마다 자동으로 다시 회피(꾹 누르면 연속 회피)
            TryRoll();

            // 공중 회피 → 점프 대시(태그 기반 캐릭터 전용). 지상은 TryRoll이 담당한다.
            TryJumpDash();

            wasRollHeld = Input.RollHeld;

            Animation.SetRollHeld(Input.RollHeld || IsRolling);

            sm.Update(); // 현재 상태의 Update 위임 실행

            // 구르기가 끝나는 바로 그 프레임에 예약된 공격 발동.
            // 이 프레임엔 애니메이터가 아직 Roll 클립이라 Roll→Attack1이 직행한다.
            if (wasRolling && !IsRolling && rollAttackBuffered)
            {
                rollAttackBuffered = false;
                OnAttack();                  // 이제 IsRolling=false → 정상 콤보 경로로 라우팅
            }
            wasRolling = IsRolling;

            // 접지 여부와 수직 속도는 상태와 무관하게 매 프레임 애니메이터에 반영.
            // 점프/낙하/착지 모션은 이 두 값의 조건 전이(또는 트리거)로 재생된다.
            Animation.SetGrounded(Movement.IsGrounded);
            Animation.SetVerticalVelocity(Movement.VerticalVelocity);

            // 착지 예고: 하강 중 지면이 가까우면 미리 착지 모션을 시작한다(파라미터 없으면 무시됨).
            Animation.SetLanding(Movement.IsNearGround());

            // 착지하면 점프 공격 사용권 회복. IsStablyGrounded를 쓰는 이유:
            // 점프 직후 접지 체크가 몇 프레임 true로 남는 잔존 구간에 리셋되는 것을 막는다.
            if (Movement.IsStablyGrounded)
            {
                jumpAttackUsed = false;

                // 착지 직전 공중 클릭 레이스 정리: 트리거가 세팅됐지만 애니메이터가 전환을
                // 평가하기 전에 착지하면 점프 공격이 발동되지 못한다. 래치된 트리거와
                // 호버링·시전 플래그가 그대로 남는 것을 접지 상태에서 직접 정리한다.
                Animation.ResetJumpAttackTrigger();
                if (Movement.IsHovering)
                {
                    Combat.CancelAction();
                    UnlockMovement();   // 내부에서 SetHover(false)도 함께 처리
                }
            }
        }

        // 태그 기반 이동 잠금/연계 판정을 한곳에서 갱신한다(자유 이동 캐릭터 전용, FreeCombat 이관).
        private void UpdateTagLock()
        {
            // 공격 '본편'과 로코모션으로 '빠져나가는 블렌드'를 구분한다.
            // 이동 잠금은 블렌드가 시작되면 바로 푼다(잠근 채 두면 코드 이동은 막힌 채 들어오는
            // 로코모션 클립의 루트모션만 살아 옛 방향으로 끌려간다). 반면 점프는 블렌드 종료까지 계속 막는다.
            bool inAttack = IsInAttackMotion();
            bool blendingOut = IsBlendingOutOfAttack();
            bool grace = Time.time < attackGraceUntil;

            IsMovementLocked = (inAttack && !blendingOut) || grace;
            isJumpBlocked = inAttack || grace;

            // Animation Event 단계에서 예약된 카메라 방향 스냅을 여기(Update)서 적용한다
            // (이벤트 단계에서 직접 돌리면 애니메이터가 되돌리고, LateUpdate는 CameraPivotController와 충돌).
            if (pendingAimSnap)
            {
                Movement.SnapToCameraForward();
                pendingAimSnap = false;
            }

            // 지속 조준 State(검기 등)는 발동 시점 1회 스냅으로 부족하므로 재생 내내 카메라 방향을 따라간다.
            if (inAttack && !blendingOut && Animation.GetBool(continuousAimHash))
                Movement.SnapToCameraForward();

            // 공격 태그를 벗어나면(로코모션 복귀·회피 등) 콤보도 끝난 것이다.
            if (!inAttack) Combat.SetInCombo(false);

            // 선입력 제거: 로코모션으로 빠져나가는 블렌드가 막 시작된 순간(엣지), 미소비 Attack 트리거·버퍼를 지운다.
            // 매 프레임 지우면 캔슬 창에서 정상 발동해야 할 다음 액션까지 지워지므로 엣지에서만 1회.
            if (blendingOut && !wasBlendingOut)
            {
                // 캔슬 창에서 입력이 들어왔으면 즉시 발동해 콤보로 이어지게 한다.
                // 콤보 정리는 블렌드 '완료'(ComboResetBehaviour.OnStateUpdate)로 미뤘다.
                Combat.TryConsumeComboInput();
            }
            wasBlendingOut = blendingOut;

            // 공격에서 빠져나가면 올려치기 상승도 끝난 것 → 중력 재개. 강공격(StrongAttack)→Jump_Loop 전이가
            // 정확히 이 시점이라, 정점에서 낙하로 자연히 이어진다(상승 중에는 blendingOut이 아니라 유지된다).
            if (blendingOut) Movement.StrongAttackRising = false;

            // 캔슬 창이 열리는 순간(IsCastingSkill true→false) 좌클릭을 꾹 누르고 있었다면,
            // 그 순간 클릭이 들어온 것처럼 OnAttack을 호출해 콤보로 잇는다(꾹 누르기 연계 진입 누락 방지).
            if (wasCastingSkill && !Combat.IsCastingSkill && Input.AttackHeld) OnAttack();
            wasCastingSkill = Combat.IsCastingSkill;

            // 공중 피격 체공은 쓰지 않는다(2026-08 기획 변경). 예전에는 피격 모션이 재생되는 동안
            // 맞은 높이를 유지했지만, 공중에서 약피격을 맞으면 모션 내내 공중에 멈춰 있어 어색했다.
            // 이제 맞아도 중력이 그대로 적용되어 자연스럽게 떨어진다.
            // (공중 강피격은 애초에 Attack 태그 State라 이 경로를 타지 않았고, 낙하로 동작 중이다.)
            // 매 프레임 false로 눌러 두는 이유: 켜진 채로 남으면 수직 속도가 0에 묶여 영영 못 떨어진다.
            Movement.HitHovering = false;
        }

        /// <summary>현재 상태를 next로 전환한다. 상태들이 자기 전환을 요청하는 공개 창구.</summary>
        public void ChangeState(IState next) => sm.ChangeState(next);

        // Update에서 매 프레임 호출(이벤트가 아닌 폴링). 점프 버튼을 '누르고 있는 동안'
        // 접지할 때마다 다시 점프시켜, 꾹 누르면 연속 점프가 되게 한다(기획 의도).
        private void TryJump()
        {
            if (!Input.JumpHeld) return;       // 버튼을 안 누르고 있으면 점프 안 함
            if (Stats.IsDead) return;          // ★ 죽었으면 무시
            if (IsGrabbed) return;             // 보스에게 잡힌 동안 점프 금지
            if (IsRolling) return;             // 구르기 중 점프 금지(회피 커밋 유지)
            if (IsStaggered) return;           // 피격 경직 중 점프 금지
            if (IsMovementLocked) return;      // 이동 잠금 상태면 점프 무시(공격/스킬 중 점프 방지)
            if (isJumpBlocked) return;         // 태그 기반: 블렌드 아웃 이후에도 종료까지 점프 차단
            if (stableGroundedTime < autoJumpDelay) return;  // 착지 직후 모션이 정리될 짧은 여유

            // 접지/상승 판정은 Movement가 단일 소유(CanJump). 성공 시 점프 트리거를 쏜다
            // (조건 전이 방식 컨트롤러에는 트리거가 없어 무시되고, 조건 전이가 그대로 물리 점프를 따라온다).
            if (!Movement.Jump()) return;
            Animation.PlayJump();
            stableGroundedTime = 0f;
        }

        private void UpdateStableGroundedTime()
        {
            // 코요테 접지(IsGroundedForJump)를 기준으로 쌓는다. raw IsStablyGrounded를 쓰면 계단을 내려갈 때
            // controller.isGrounded가 한 프레임씩 false로 튀며 타이머가 계속 0으로 리셋돼, autoJumpDelay를
            // 못 채워 점프키가 씹혔다. 발밑 코앞에 지면이 있으면 접지로 쳐서 타이머가 이어지게 한다.
            if (Movement.IsGroundedForJump)
            {
                stableGroundedTime += Time.deltaTime;
                return;
            }

            stableGroundedTime = 0f;
        }

        private void OnSkill(int n)
        {
            // 마을 등 전투 비활성 구역에서는 스킬을 쓸 수 없음을 문구로 안내한다(슬롯 흐림 표시와 짝).
            if (!combatEnabled)
            {
                UIEvents.FireToast("해당 지역에서 사용할 수 없습니다.");
                return;
            }
            if (mouseMode) return;             // 마우스 모드(HUD 클릭 등)에서는 기존대로 조용히 무시
            if (Stats.IsDead) return;
            if (IsGrabbed) return;             // 보스에게 잡힌 동안 스킬 금지
            if (IsRolling) return;             // 구르기 중 스킬 금지(회피 커밋 유지)
            if (IsHitBlocked) return;          // 피격 중 스킬 금지(태그 캐릭터=Hit 태그 / 기존=경직 타이머)

            // 다운(넘어짐) 구간 전체를 막는다. IsHitBlocked는 Hit 태그만 보는데, 공중 강피격
            // 낙하·다운 State들은 Hit 태그를 쓸 수 없다 — Hit 태그를 붙이면 Movement.HitHovering이
            // 켜져 수직 속도가 0으로 묶이고, 그러면 바닥에 닿지 못해 isLanding이 영영 오지 않는다.
            // HitState에 머무는 동안(=넘어져 있는 동안)이라는 사실 자체로 판정하므로 태그와 무관하게 동작한다.
            if (IsStaggered) return;

            if (usesTags)
            {
                // 캔슬 창(AttackCancelBehaviour)이 열리면 이동은 잠긴 채여도 다음 입력은 통과시켜야
                // 대시공격→스킬처럼 연계된다. 그래서 IsMovementLocked가 아니라 IsCastingSkill을 게이트로 쓴다.
                if (Combat.IsCastingSkill) return;

                // 평타 콤보 도중 스킬 입력은 캔슬 창(진행도 게이트)을 기다리지 않고, 아무 프레임에서나
                // 즉시 평타를 하드 캔슬하고 스킬로 넘어간다(기획 확정 2026-08-10 — 이쪽이 손맛이 산다).
                // 아래 줄이 예전의 진행도 게이트다: 캔슬 창(AttackCancelBehaviour)이 열려야만 스킬로
                // 이어지게 하던 제한인데, 하드 캔슬을 위해 비활성화했다. 되돌리려면 이 줄을 살리면 된다.
                //   (하드 캔슬 대상은 평타뿐이다. 강공격·대시공격·점프공격은 IsCastingSkill을 켜므로
                //    위 476줄에서 여전히 막히고, 스킬→스킬 캔슬도 같은 이유로 막힌다.)
                //if (IsInAttackMotion() && !Combat.ComboCancelWindowOpen) return;

                if (!Movement.IsEffectivelyGrounded()) return;                // 공중 스킬 없음(기획)
            }
            else
            {
                // 기존 캐릭터: 동작 중(스킬 시전·공격 콤보 = 이동 잠금 중)에는 새 스킬을 받지 않는다.
                if (IsMovementLocked) return;
            }

            // ★ 판정 순서: 쿨타임 → 게이지 → 발동. 어느 한쪽만 소모되는 사고를 막는다.
            if (!Combat.CanUseSkill(n))
            {
                // 쿨타임 중이면 안내한다. 빈 슬롯(등록 안 됨)·로딩 중은 쿨타임이 0이라 조용히 무시된다.
                if (Combat.GetRemainingCooldown(n) > 0f) UIEvents.FireToast("쿨타임입니다.");
                return;
            }
            if (!Stats.TryUseSkillGauge(Combat.GetSkillGaugeCost(n)))
            {
                UIEvents.FireToast("SG가 부족하여 사용할 수 없습니다.");
                return;
            }
            if (!Combat.UseSkill(n)) return;   // 쿨타임은 위에서 확인했으므로 사실상 항상 성공

            if (usesTags) Combat.SetInCombo(false);   // 평타를 캔슬하고 나온 것이므로 콤보 종료
            Movement.SnapToCameraForward();
            LockMovement();                    // 스킬이 실제로 발동할 때만 이동 잠금
        }

        // 점프 공격을 이미 썼는지 여부. '점프 1회당 공격 1회' 제한(기획).
        // 착지(IsStablyGrounded)하면 Update에서 리셋된다.
        private bool jumpAttackUsed;

        // 좌클릭 중재. 상황을 보고 점프 공격/달리기 공격/일반 콤보 중 하나로 라우팅한다.
        private void OnAttack()
        {
            if (!CombatAllowed) return;      // 마을(전투 비활성) 또는 마우스 모드에서는 공격 입력 무시
            if (Stats.IsDead) return;        // 죽었으면 공격 무시
            if (IsGrabbed) return;           // 보스에게 잡힌 동안 공격 금지
            if (IsRolling)
            {
                rollAttackBuffered = true;   // 버리지 않고 예약 (앞의 CombatAllowed/IsDead 가드는 이미 통과)
                return;
            }
            if (IsHitBlocked) return;        // 피격 중 공격 금지(태그 캐릭터=Hit 태그 / 기존=경직 타이머)

            // 스킬/단타 공격 시전 중 클릭 차단. 막지 않으면 Attack 트리거가 래치된 채 대기하다가
            // 시전이 끝나는 순간 1타가 자동 발동한다.
            if (Combat.IsCastingSkill) return;

            // 공중 클릭 = 점프 공격(단타). 점프 1회당 1회만 허용한다(기획).
            // 태그 기반: 접지 판정을 IsEffectivelyGrounded로 해, 이미 착지한 모션에서 재발동되는 것을 막는다.
            //
            // 단, 넘어져 떨어지는 중(IsStaggered)은 '공중'이어도 점프 공격이 아니다.
            // 이걸 안 막으면 공중에서 강피격을 당해 낙하하는 동안 누른 클릭이 점프 공격으로 소비되어,
            // Attack 트리거가 걸리지 않아 착지 후 기상 공격(Get_Up_Attack)이 나가지 않는다
            // (특히 클릭을 '누르고 있는' 경우, 입력 이벤트는 누른 순간 1회뿐이라 만회할 기회가 없다).
            // 여기서 걸러내면 아래 콤보 분기로 흘러 Attack 트리거가 래치되고, Hit_End에서 기상 공격으로 이어진다.
            bool airborne = (usesTags ? !Movement.IsEffectivelyGrounded() : !Movement.IsGrounded) && !IsStaggered;
            if (airborne)
            {
                if (jumpAttackUsed) return;

                jumpAttackUsed = true;
                Combat.UseJumpAttack();
                Movement.SnapToCameraForward();  // 공중에서도 카메라가 보는 방향으로 공격(기획)
                if (usesTags)
                {
                    Movement.BeginDiveHover();        // Start 구간 체공 → Loop 진입 시 하강(BeginDive)
                    Movement.StrongAttackRising = false;   // 점프공격 연계됐으니 올려치기 상승 해제
                }
                else
                {
                    Movement.SetHover(true);          // 모션 동안 높이 고정 → 종료 시 UnlockMovement가 낙하 재개
                }
                LockMovement();
                return;
            }

            // 달리는 중 클릭 = 달리기 공격(단타). 콤보로 이어지지 않는다(기획).
            // 태그 기반: 이미 공격 중이면 대시공격 재발동이 아니라 콤보로 이어져야 하므로 IsInAttackMotion도 확인한다.
            if (Input.IsRunning && (!usesTags || !IsInAttackMotion()))
            {
                Combat.UseRunAttack();
                Movement.SnapToCameraForward();
                LockMovement();
                return;
            }

            // 그 외(지상 정지/걷기) = 일반 콤보. 이어치기는 Animation Event가 처리한다.
            if (usesTags)
            {
                // 연타 시 comboStep 갱신 시차로 트리거가 여러 번 래치돼 State를 건너뛰는 것을 막는다.
                // 이미 콤보 체인에 진입한 뒤의 재발동이면 트리거만 지우고 스냅/잠금은 건너뛴다
                // (새 모션이 안 나가는데 캐릭터만 홱 도는 부수효과 방지).
                bool alreadyAttacking = Combat.InCombo;

                // 기상 공격처럼 '다른 Attack 태그 모션 도중에 예약된' 입력은 방향을 틀지 않는다.
                // 쓰러져 있는 동안 카메라만 반대로 돌려두면, 스냅이 몸을 그쪽으로 홱 돌려
                // 누운 채 갑자기 반대 방향을 보고 일어나는 그림이 된다.
                // (대시 공격 중 연계는 여기 오기 전에 IsCastingSkill로 이미 걸러진다.)
                bool bufferedDuringMotion = IsInAttackMotion();

                bool consumed = Combat.OnAttackInput();
                if (alreadyAttacking)
                {
                    // 발동했으면 절대 리셋 안 함
                    if (!consumed)
                        Animation.ResetAttackTrigger();
                    return;
                }

                if (!bufferedDuringMotion) Movement.SnapToCameraForward();
                LockMovement();
                return;
            }

            Combat.OnAttackInput();
            Movement.SnapToCameraForward();
            LockMovement();                    // 공격(콤보 포함) 동안 이동 잠금
        }

        // 우클릭 강공격 중재.
        private void OnStrongAttack()
        {
            if (!CombatAllowed) return;          // 마을(전투 비활성) 또는 마우스 모드에서는 강공격 입력 무시
            if (Stats.IsDead) return;
            if (IsGrabbed) return;               // 보스에게 잡힌 동안 강공격 금지
            if (IsRolling) return;               // 구르기 커밋 유지
            if (IsHitBlocked) return;            // 피격 중 강공격 금지(태그 캐릭터=Hit 태그 / 기존=경직 타이머)
            if (IsStaggered) return;             // 다운 구간 차단(OnSkill과 같은 이유 — 그쪽 주석 참조)
            if (Combat.IsCastingSkill) return;   // 시전 중 중복 발동 차단(캔슬 창이 열리면 통과)

            // 평타 캔슬 창 판정. OnSkill과 같은 이유로 Combat.InCombo 대신 IsInAttackMotion()을 쓴다
            // (Get_Up_Attack·Hit_End처럼 InCombo를 켜는 Animation Event가 없는 Attack 태그 State도 잡기 위함).
            if (usesTags && IsInAttackMotion() && !Combat.ComboCancelWindowOpen) return;  // 평타 캔슬 창
            if (!Movement.IsGrounded) return;    // 올려치기는 지상 전용(공중 우클릭은 무시)

            if (!Combat.UseStrongAttack()) return;   // 쿨타임 중이면 아무 일도 일어나지 않음(잠금 X)

            if (usesTags) Combat.SetInCombo(false);   // 평타를 캔슬하고 나온 것이므로 콤보 종료
            Movement.SnapToCameraForward();
            LockMovement();

            // 상승은 여기서 시작하지 않는다. 올려치기 클립의 Animation Event(OnStrongAttackRiseStart)가
            // 발이 땅을 떠나는 프레임에 켜고, 정점의 OnStrongAttackRiseEnd가 끈다(상승은 클립 루트모션이 담당).
        }

        // 구르기 입력 중재. TryJump처럼 Update에서 매 프레임 폴링한다(꾹 누르면 연속 회피 기획).
        // 이동 잠금을 확인하지 않는 이유: 구르기는 공격/스킬을 캔슬하는 최우선 회피 동작(기획).
        private void TryRoll()
        {
            if (!Input.RollHeld) return;       // 버튼을 안 누르고 있으면 회피 안 함
            if (!combatEnabled) return;        // 마을 등 전투 비활성 구역에서는 구르기 금지(기획 의도 — 마을 구르기 미지원)
            if (Stats.IsDead) return;
            if (IsGrabbed) return;             // 보스에게 잡힌 동안 회피 금지(고정 시간 탈출 — 마시 탈출 도입 전까지)
            if (IsRolling) return;             // 회피 중 재입력 무시 → 끝나는 프레임부터 다음 회피 발동
            if (Combat.IsCastingInvincibleSkill) return;   // 각성기(무적 스킬) 시전 중엔 회피로 캔슬 금지(기획)

            // 공중에서는 구르기를 발동하지 않음. 접지 근거는 controller.isGrounded(IsStablyGrounded)가
            // 아니라 레이캐스트 기반(IsGroundedForDodge)을 쓴다 — isGrounded는 고프레임에서 프레임당
            // 하강량이 작아지면 간헐적으로 false가 되어, 그 프레임에 회피가 씹혔다(고사양 PC 스킬 캔슬 실패 원인).
            if (!Movement.IsGroundedForDodge) return;

            if (Input.MoveInput.sqrMagnitude < 0.0001f) return;  // 무입력(Idle) 구르기 금지(기획)

            if (!Stats.TryUseStamina(rollStaminaCost)) return;
            ChangeState(RollState);
        }

        // 공중 회피 → 점프 대시 중재(태그 기반 캐릭터 전용). 누르는 순간(엣지)에만, 방향 입력이 있을 때만 발동한다.
        private void TryJumpDash()
        {
            if (!usesTags) return;
            if (!combatEnabled) return;        // 마을 등 전투 비활성 구역에서는 공중 대시(점프 중 쉬프트) 금지 — 구르기와 동일
            if (Stats.IsDead) return;
            if (IsGrabbed) return;             // 보스에게 잡힌 동안 공중 대시 금지
            if (IsRolling) return;
            if (Combat.IsCastingInvincibleSkill) return;   // 각성기(무적 스킬) 시전 중엔 공중대시로 캔슬 금지(기획)

            bool pressed = Input.RollHeld && !wasRollHeld;
            if (!pressed) return;
            if (Movement.IsEffectivelyGrounded()) return;    // 지상은 TryRoll 소관
            if (Movement.HasAirDashed) return;               // 공중 1회 제한
            if (Input.MoveInput.sqrMagnitude < 0.0001f) return;  // 무입력 대시 금지(지상 구르기와 동일)

            // 잠금(공격) 중에는 올려치기 상승 중일 때만 공중 대시 캔슬 허용
            // (점프공격 하강·착지 직후 오발 차단).
            if (IsMovementLocked && !Movement.StrongAttackRising) return;
            if (!Stats.TryUseStamina(rollStaminaCost)) return;
            ChangeState(JumpDashState);
        }

        // 콤보 타수가 실제 시작될 때마다 잠금을 갱신한다(OnAttackStart Animation Event 경유).
        // 꾹 누르기 콤보는 OnAttack(클릭)을 거치지 않으므로, 이 갱신이 없으면 안전장치 타이머가
        // 콤보 도중 잠금을 풀거나 콤보 루프 후 아예 잠기지 않은 채 공격한다.
        private void OnComboStepStarted()
        {
            // 태그 기반: 여기서 직접 스냅하면 Animation Event 단계 회전을 애니메이터가 되돌리므로 Update로 예약한다.
            // 기존 캐릭터는 이 문제가 없어(민준님 기존 동작) 즉시 스냅한다.
            if (usesTags) pendingAimSnap = true;
            else Movement.SnapToCameraForward();

            LockMovement();
        }

        // 공격/스킬 적중마다 스킬 게이지(SG)를 회복한다. 회복량은 SkillTable 행(SgGain)이 소유한다. 히트시 히트콤보를 올린다.
        private void OnTargetHit(float gaugeGain)
        {
            Stats.GainSkillGauge(gaugeGain);
            HitCombo.AddHit();
        }

        // 데미지가 실제로 적용됐을 때 피격 경직으로 전환한다.
        // 구르기 중에는 진입하지 않는다(회피 커밋 유지 — 기획).
        private void OnDamaged()
        {
            if (Stats.IsDead) return;
            if (IsRolling) return;

            // 보스에게 잡혀 있으면 구속을 유지한다. 구속 중 데미지(Boss.OnGrabDamage)는 TakeDamage로
            // 이미 적용됐고, 여기서 HitState로 새면 잡힘이 풀려 버린다 → 전이만 건너뛰어 "데미지만 받고 잡힘 유지".
            if (IsGrabbed) return;

            // 각성기(무적기) 시전 중엔 강피격이라도 경직시키지 않는다. 각성기는 회피로도 못 끊는
            // '완전 커밋' 동작이라(회피 차단은 IsCastingInvincibleSkill), 피격 경직으로도 끊기면 안 된다.
            // 정상적으론 무적(Stats.IsInvincible)이라 데미지 자체가 씹혀 여기 도달하지 않지만,
            // 무적이 한 프레임 깜빡인 사이 강피격이 들어오면 슈퍼아머 예외(아래)에 걸려 HitState로 캔슬됐다.
            // 그 드문 레이스에도 각성기가 통째로 캔슬되지 않게 여기서 먼저 막는다(강피격 예외보다 상위).
            if (Combat.IsCastingUltimate) return;

            // 강공격/스킬 시전 중 슈퍼아머: 약한 피격은 데미지만 받고(TakeDamage에서 이미 적용) 경직시키지 않는다.
            // 스킬·강공격을 한 번 지르면 잡몹 평타에 계속 끊기지 않게 하려는 기획이다. 강피격(LastHitWasStrong)은
            // 슈퍼아머를 뚫고 경직시킨다. 일반/대시/점프 중에는 IsSuperArmorMove가 false라 정상 경직된다.
            if (Combat.IsSuperArmorMove && !Stats.LastHitWasStrong ) return;

            ChangeState(HitState);
        }

        private void OnDied()
        {
            sm.ChangeState(DeadState);

            // 사망 시 히트 콤보를 즉시 비운다. 안 비우면 죽은 뒤에도 감쇠 시간(comboResetDelay)만큼
            // HUD에 지난 콤보 수가 남아 있게 된다.
            HitCombo.ResetHitCombo();
        }

        /// <summary>
        /// 보스 잡기 히트 프레임(<see cref="ProjectS.Enemies.Boss.OnGrabConnect"/>)이 플레이어를 포착하면 호출한다.
        /// 잡기를 받아들이면 구속 상태(<see cref="GrabbedState"/>)로 전환하고 true를 돌려준다.
        /// 회피(구르기 i-frame)·각성기 무적 커밋·사망 중에는 잡히지 않고 false를 돌려 보스가 헛잡기로 흘리게 한다.
        /// </summary>
        /// <param name="grabber">잡은 주체(보스). 구속 중 생사·활성 감시에 쓴다.</param>
        /// <param name="anchor">잡힌 위치를 고정할 보스 앵커(손/입 등). null이면 위치 고정 없이 구속 모션만 재생.</param>
        /// <returns>잡기가 성립하면 true, 회피/무적/사망 등으로 무산되면 false.</returns>
        public bool OnGrabbed(ProjectS.Enemies.Enemy grabber, Transform anchor)
        {
            if (grabber == null) return false;
            if (Stats.IsDead) return false;
            if (IsGrabbed) return false;                   // 이미 잡힌 상태면 중복 진입 방지
            if (IsRolling) return false;                   // 회피(구르기) i-frame으로 잡기 회피(기획)
            if (Combat.IsCastingUltimate) return false;    // 각성기 무적 커밋 중엔 잡히지 않는다

            GrabbedState.Setup(grabber, anchor);
            ChangeState(GrabbedState);
            return true;
        }

        /// <summary>
        /// 보스 잡기 구속을 푼다. 보스 잡기 클립의 마무리 Animation Event(Boss.OnGrabThrow/OnGrabDrop)와,
        /// 잡은 보스가 죽거나 사라졌을 때의 <see cref="GrabbedState"/> 자체 실패 복구가 호출한다.
        /// 잡힘 상태가 아니면 무시한다(마무리 이벤트가 두 번 와도 안전).
        /// </summary>
        /// <param name="asThrow">true=던지기(피격 반응으로 넉백), false=제자리 해제(자유 상태 복귀).</param>
        /// <param name="throwHorizontalSpeed">던지기 수평 넉백 세기(asThrow일 때만 사용). 보스 잡기 설정이 넘긴다.</param>
        /// <param name="throwUpSpeed">던지기 수직 띄우기 세기(asThrow일 때만 사용).</param>
        public void ReleaseFromGrab(bool asThrow, float throwHorizontalSpeed = 0f, float throwUpSpeed = 0f)
        {
            if (!IsGrabbed) return;

            if (asThrow)
            {
                // 상태 전환이 먼저다: GrabbedState.Exit가 컨트롤러를 되살리며 ResetMotion으로 관성을 지우므로,
                // 넉백은 반드시 전환 '뒤'에 실어야 살아남는다. 잡힌 동안 보스를 바라보고 있어 -forward가 곧 뒤쪽이다.
                ChangeState(HitState);
                Movement.ApplyThrow(-transform.forward * throwHorizontalSpeed, throwUpSpeed);
            }
            else
            {
                ChangeState(FreeState);
            }
        }

        /// <summary>
        /// 플레이어를 부활시킨다. HP를 채워 사망 상태를 풀고, 사망 모션을 정리한 뒤 자유 상태로 되돌린다.
        /// 죽은 자리에서 즉시 부활하는 경로(부활 버튼)와 마을 복귀 경로가 함께 호출한다
        /// (마을 복귀는 이 뒤에 씬 전환을 요청한다). 살아 있으면 아무것도 하지 않는다(중복 호출 방지).
        /// </summary>
        public void Revive()
        {
            if (!Stats.IsDead) return;

            Stats.Revive();          // HP·자원 충전 → IsDead 해제
            Animation.ResetDeath();  // 사망 모션에서 로코모션으로 복귀
            Combat.CancelAction();   // 사망 시 남은 공격/시전 상태 정리
            UnlockMovement();        // 사망 중 걸려 있던 이동 잠금 해제
            sm.ChangeState(FreeState);
        }

        // ── 태그 판정 헬퍼(자유 이동 캐릭터 전용) ─────────────────────────────
        // Animator 접근은 PlayerAnimation으로 일원화하고, Player는 캐싱한 태그 해시로 의미를 판정한다.

        // 이동을 잠가야 하는 모션(lockingStateTags 중 하나)이 재생 중인지.
        // 공격으로 '들어가는' 전이 중엔 다음 State가 공격이므로 그것도 공격 중으로 본다.
        private bool IsInAttackMotion()
        {
            if (lockingTagHashes == null || lockingTagHashes.Length == 0) return false;

            if (IsAttackTagged(Animation.CurrentStateTagHash)) return true;

            return Animation.IsInTransition && IsAttackTagged(Animation.NextStateTagHash);
        }

        // 공격에서 로코모션으로 '빠져나가는' 블렌드 중인지(현재는 공격, 다음은 공격이 아님).
        private bool IsBlendingOutOfAttack()
        {
            if (!Animation.IsInTransition) return false;

            return IsAttackTagged(Animation.CurrentStateTagHash)
                && !IsAttackTagged(Animation.NextStateTagHash);
        }

        // 피격 모션(hitStateTag) 재생 중인지. 피격으로 '들어가는' 전이도 포함한다.
        private bool IsInHitMotion()
        {
            if (Animation.CurrentStateTagHash == hitTagHash) return true;

            return Animation.IsInTransition && Animation.NextStateTagHash == hitTagHash;
        }

        // 피격에서 피격이 아닌 State로 '빠져나가는' 블렌드 중인지. 이 구간엔 체공을 풀어 낙하가 이어진다.
        private bool IsBlendingOutOfHit()
        {
            if (!Animation.IsInTransition) return false;

            return Animation.CurrentStateTagHash == hitTagHash
                && Animation.NextStateTagHash != hitTagHash;
        }

        private bool IsAttackTagged(int hash)
        {
            for (int i = 0; i < lockingTagHashes.Length; i++)
            {
                if (hash == lockingTagHashes[i]) return true;
            }

            return false;
        }
    }
}
