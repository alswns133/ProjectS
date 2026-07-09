using UnityEngine;

/// <summary>
/// Animator 파라미터 브릿지. 다른 컴포넌트가 Animator를 직접 만지지 않고
/// 이 클래스의 의미 있는 메서드(SetForward, PlayJump 등)를 통해서만 제어한다.
/// 파라미터 이름 문자열은 한곳(여기)에만 두어 오타·중복을 막는다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimation : MonoBehaviour
{
    // 파라미터 이름을 매번 문자열로 넘기면 내부에서 해싱 비용 + 오타 위험.
    // 시작 시 1회 해싱해 int로 캐싱한다. static readonly: 인스턴스 공통·불변.
    private static readonly int Z = Animator.StringToHash("Z");
    private static readonly int Grounded = Animator.StringToHash("isGrounded");
    private static readonly int DoJump = Animator.StringToHash("doJump");
    private static readonly int DoDie = Animator.StringToHash("doDie");
    private static readonly int DoRoll = Animator.StringToHash("doRoll");

    // 스킬 트리거 해시 테이블. [0]은 더미 — 스킬 번호(1~)를 인덱스로 바로 쓰기 위함.
    // 따라서 유효 번호는 1..Length-1. 스킬을 늘리면 여기에 추가.
    private static readonly int[] Skill =
    {
        0,
        Animator.StringToHash("Skill1"),
        Animator.StringToHash("Skill2"),
        Animator.StringToHash("Skill3"),
        Animator.StringToHash("Skill4"),
    };

    private static readonly int Attack = Animator.StringToHash("Attack");

    private const float Damp = 0.1f;   // SetFloat 감쇠 시간. 값이 즉시 안 튀고 부드럽게 따라감
    private Animator animator;
    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>전진량(Z)을 부드럽게 갱신. 자유 시점은 진행 방향 회전이라 Z만 쓴다.</summary>
    public void SetForward(float z) => animator.SetFloat(Z, z, Damp, Time.deltaTime);

    public void SetGrounded(bool v) => animator.SetBool(Grounded, v);
    public void PlayJump() => animator.SetTrigger(DoJump);

    /// <summary>
    /// 구르기 트리거. 구르기 상태 진입 시 1회 호출된다.
    /// 방향 파라미터가 없는 이유: 캐릭터가 구를 방향을 먼저 바라보고(FaceInstantly)
    /// 앞구르기 클립 하나만 재생하는 설계라, 애니메이터는 방향을 몰라도 된다.
    /// </summary>
    public void PlayRoll() => animator.SetTrigger(DoRoll);

    /// <summary>
    /// 구르기 트리거 해제. 구르기 상태 Exit에서 호출된다.
    /// 연속 회피 중 애니메이터가 트리거를 소비하지 못한 채(블렌드 중 등) 상태가 끝나면
    /// 래치된 트리거가 남아 나중에 유령 구르기가 재생되는 것을 막는다.
    /// </summary>
    public void ResetRollTrigger() => animator.ResetTrigger(DoRoll);

    /// <summary>n번 스킬 트리거. 범위를 벗어난 n은 조용히 무시(예외 대신 안전).</summary>
    public void PlaySkill(int n)
    {
        if (n >= 1 && n < Skill.Length)
            animator.SetTrigger(Skill[n]);
    }

    public void PlayAttackTrigger()
    {
        animator.SetTrigger(Attack);
    }

    public void ResetAttackTrigger() => animator.ResetTrigger(Attack);

    public void PlayDie() => animator.SetTrigger(DoDie);
}
