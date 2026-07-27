using UnityEngine;
using ProjectS.Players;

namespace ProjectS.Combat
{
    /// <summary>
    /// 공격 State에 붙여 그 State의 '넘어가는 방식'을 State별로 제어하는 SMB. 두 기능이 독립적으로 들어 있다.
    ///
    /// <b>1) 연계(캔슬) 창</b> — 진행도가 <see cref="cancelWindowStart"/>를 넘으면 다음 공격 입력을 받도록
    /// 창을 연다. 스킬/강공격/대시공격/공중공격은 PlayerCombat.EndSkillCast(IsCastingSkill)로,
    /// 평타(콤보)는 FreeCombatController.OpenComboCancelWindow(별도 플래그)로 연다 — 평타는 애초에
    /// IsCastingSkill을 켜지 않으므로 그쪽만으로는 평타에 이 기능이 전혀 동작하지 않는다.
    ///
    /// <b>2) 복귀 목적지 갈아타기</b>(선택) — Idle로 빠져나가는 블렌드 도중 이동 입력이 들어오면
    /// 목적지를 걷기/달리기로 덮어쓴다. <see cref="idleState"/>가 비어 있으면 꺼진다(기본값).
    ///
    /// 값을 <b>State마다 따로</b> 두는 것이 핵심이다 — 같은 SMB를 여러 공격 State에 붙이되 각자 값을
    /// 달리 주면, 공격별 연계 타이밍(후딜 길이)을 애니메이터에서 State를 보며 직접 튜닝할 수 있다.
    /// (대시공격은 빨리 열고, 강공격 올려치기는 준비 동작 뒤에 늦게 여는 식.)
    ///
    /// 코드 전역값 하나로 열던 방식의 두 약점을 함께 해결한다:
    ///  - 공격마다 다른 타이밍을 줄 수 없던 문제(이제 State별 값).
    ///  - 새 공격을 막 발동한 직후 '직전 공격'의 진행도로 창이 새던 문제(SMB는 자기 State 진행도만 본다).
    ///
    /// ★ 이동 잠금(ActionLocked)은 Tag 기반이라 그대로 유지된다. 여기서 여는 것은 '공격 입력'뿐이므로
    ///   후딜의 무게는 남고 다음 공격만 미리 받는다.
    /// ★ 히트 프레임보다 '뒤'에 열려야 한다. 창이 열리면 IsCastingSkill이 풀려, PlayerCombat이
    ///   그 이후 히트 프레임을 무효화한다(CanApplyHitFrame). 히트 이벤트를 넣을 때 순서에 주의.
    ///
    /// 새 공격(평타 각 타수·스킬 등)에도 이 SMB를 붙이고 값만 정하면 코드 수정 없이 연계가 된다.
    /// </summary>
    public class AttackCancelBehaviour : StateMachineBehaviour
    {
        [Header("연계(캔슬) 창")]
        [Tooltip("이 공격의 진행도(0~1)가 이 값을 넘으면 다음 공격으로 캔슬 가능해진다. 작을수록 빨리 이어진다.")]
        [Range(0f, 1f)] [SerializeField] private float cancelWindowStart = 0.5f;

        [Header("복귀 목적지 갈아타기 (비워두면 사용 안 함)")]
        // 왜 필요한가: 애니메이터 전이는 한 번 시작되면 조건이 거짓이 되어도 끝까지 진행된다.
        // Idle 전이의 Exit Time이 Walk/Run보다 이른 구성에서는(모션마다 자연스러운 지점이 달라 실제로
        // 그렇게 튜닝된다), 그 사이에 방향키를 눌러도 Walk/Run 전이는 아직 자기 Exit Time에 도달하지 못해
        // 끼어들 수 없다. 그동안 FreeCombatController는 블렌드 시작과 동시에 이동 잠금을 풀기 때문에
        // (공격 종료 시 루트모션 끌림을 막으려는 의도적 설계), 캐릭터가 Idle 포즈인 채로 미끄러지듯
        // 이동하는 것이 그대로 보인다.
        //
        // 전이 우선순위·Ordered Interruption·Interruption Source 조합으로는 이 창을 닫기 어렵다
        // (끼어드는 쪽 전이가 그 시점에 '발동 자격'을 갖춰야 하는데 Exit Time 때문에 그렇지 못하다).
        // 그래서 CrossFadeInFixedTime으로 목적지를 직접 덮어쓴다 — 진행 중인 전이를 무조건 대체하므로
        // 우선순위 규칙의 영향을 받지 않고, 애니메이터의 Exit Time·Duration 튜닝값을 하나도 바꾸지 않아도 된다.
        //
        // 로코모션과 공격이 서로 다른 애니메이션 팩이라 복귀 전이를 이동 상태별로 나눠 둔 State에서만 쓴다.
        // 같은 팩으로 통일해 Idle 단일 복귀를 쓰는 State에는 필요 없다(그 경우 비워 둔다).
        [Tooltip("이 State로 빠져나가는 블렌드 중일 때만 개입한다. 짧은 이름 또는 전체 경로(SubMachine.State). 비우면 기능 꺼짐.")]
        [SerializeField] private string idleState = "";

        [SerializeField] private string walkState = "Walk_Loop";
        [SerializeField] private string runState = "Run_Loop";

        [Tooltip("갈아탈 때의 블렌드 시간(초). 원래 Walk/Run 전이의 Duration과 맞추면 이음새가 같아진다.")]
        [SerializeField] private float redirectDuration = 0.25f;

        [Header("이동 상태 판정 파라미터")]
        // 전이 조건과 같은 파라미터를 읽는다. FreeMoveController가 이동 잠금 중에도 매 프레임 갱신하므로
        // (잠금 중 isMoving을 false로 고정하지 않는 이유는 그쪽 주석 참고) 입력 시점이 그대로 반영된다.
        [SerializeField] private string movingParam = "isMoving";
        [SerializeField] private string runningParam = "isRunning";

        // SMB는 컨트롤러 에셋에 붙는 객체라, Animator마다 생성되는 인스턴스에서 첫 호출 시 1회만 캐싱한다.
        private PlayerCombat combat;
        private FreeCombatController combatController;
        private int movingHash;
        private int runningHash;
        private bool hashed;

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!hashed)
            {
                movingHash = Animator.StringToHash(movingParam);
                runningHash = Animator.StringToHash(runningParam);
                hashed = true;
            }

            TryRedirectExit(animator, layerIndex);
            TryOpenCancelWindow(animator, stateInfo, layerIndex);
        }

        // Idle로 빠져나가는 블렌드 도중 이동 입력이 들어왔을 때만 목적지를 갈아탄다.
        // 처음부터 이동 중이었다면 애니메이터가 알아서 Walk/Run 전이를 타므로 여기까지 오지 않는다
        // (그 경우 다음 State가 Idle이 아니라서 아래 검사에서 걸러진다) → 기존 전이 튜닝이 그대로 살아 있다.
        private void TryRedirectExit(Animator animator, int layerIndex)
        {
            if (string.IsNullOrEmpty(idleState)) return;

            if (!animator.IsInTransition(layerIndex)) return;
            if (!animator.GetNextAnimatorStateInfo(layerIndex).IsName(idleState)) return;
            if (!animator.GetBool(movingHash)) return;

            // 초 단위(InFixedTime)를 쓰는 이유: 전이 Duration을 Fixed Duration(초)으로 튜닝해 왔기 때문에
            // 정규화 시간으로 넘기면 목적지 클립 길이에 따라 블렌드가 들쭉날쭉해진다.
            // 갈아탄 뒤에는 다음 State가 Idle이 아니게 되어 위 검사에서 걸러지므로 매 프레임 재호출되지 않는다.
            string target = animator.GetBool(runningHash) ? runState : walkState;
            animator.CrossFadeInFixedTime(target, redirectDuration, layerIndex);
        }

        // 스킬/강공격/대시공격/공중공격(combat.IsCastingSkill)과 평타(FreeCombatController의 별도
        // 콤보 캔슬 플래그) 둘 다 여기서 같이 연다. 어느 State든 자기한테 해당 없는 쪽은 그냥 무해한
        // no-op이라(스킬 State면 콤보 플래그가, 평타 State면 IsCastingSkill이 애초에 관계없다) 분기할
        // 필요가 없다 — 같은 SMB, 같은 슬라이더가 어느 공격 종류에 붙어도 동일하게 동작한다.
        private void TryOpenCancelWindow(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (combat == null)
                combat = animator.GetComponent<PlayerCombat>();
            if (combatController == null)
                combatController = animator.GetComponent<FreeCombatController>();

            if (animator.IsInTransition(layerIndex)) return;   // 전이 중에는 진행도 판정이 의미 없다
            if (stateInfo.normalizedTime < cancelWindowStart) return;

            if (combat != null && combat.IsCastingSkill) combat.EndSkillCast();
            combatController?.OpenComboCancelWindow();
        }
    }
}
