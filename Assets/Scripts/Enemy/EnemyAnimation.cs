using UnityEngine;

/// <summary>
/// Animator 파라미터 브릿지. 몬스터 Animator는 이 클래스를 통해서만 제어한다.
/// 파라미터 이름은 애니메이터 담당자와 공유하는 계약이므로 여기에만 정의한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyAnimation : MonoBehaviour
{
    // 파라미터 이름을 매번 문자열로 넘기면 내부 해싱 비용 + 오타 위험이 있다.
    // 시작 시 1회 해싱해 int로 캐싱한다. static readonly: 인스턴스 공통·불변.
    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
    private static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    private static readonly int DoDetect = Animator.StringToHash("doDetect");
    private static readonly int DoAttack = Animator.StringToHash("doAttack");
    private static readonly int DoHit = Animator.StringToHash("doHit");
    private static readonly int DoDie = Animator.StringToHash("doDie");

    // 이동 블렌드 값이 튀지 않게 부드럽게 따라가는 감쇠 시간.
    private const float Damp = 0.1f;

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>현재 이동 속력. 이동/걷기 애니메이션 블렌드 값으로 쓴다.</summary>
    public void SetSpeed(float speed) => animator.SetFloat(Speed, speed, Damp, Time.deltaTime);

    /// <summary>감쇠 없이 이동 블렌드 값을 즉시 바꾼다. 공격 진입처럼 모션을 바로 끊을 때 쓴다.</summary>
    public void SetSpeedImmediate(float speed) => animator.SetFloat(Speed, speed);

    /// <summary>
    /// 대기 애니메이션 2종 중 어떤 것을 재생할지 전달한다.
    /// IdleState가 진입 시 0/1 중 하나를 고르고, Animator는 IdleVariant 값으로 대기 클립을 분기한다.
    /// </summary>
    public void SetIdleVariant(int index) => animator.SetFloat(IdleVariant, index);

    /// <summary>플레이어 발견 연출 트리거. DetectState 진입 시 1회 호출한다.</summary>
    public void PlayDetect() => animator.SetTrigger(DoDetect);

    /// <summary>
    /// 공격 트리거. AttackIndex로 공격 1/2/3을 고른 뒤 doAttack을 켠다.
    /// Animator 전이는 AttackIndex 값에 따라 각 공격 클립으로 분기시키면 된다.
    /// 인덱스 유효성은 EnemyCombat의 공격 슬롯과 Animator Controller 설정이 함께 맞아야 하는 계약이다.
    /// </summary>
    public void PlayAttack(int attackIndex)
    {
        animator.SetInteger(AttackIndex, attackIndex);
        animator.SetTrigger(DoAttack);
    }

    /// <summary>피격 경직 트리거. HitState 진입 시 1회 호출한다.</summary>
    public void PlayHit() => animator.SetTrigger(DoHit);

    /// <summary>사망 트리거. DeadState 진입 시 1회 호출한다.</summary>
    public void PlayDie() => animator.SetTrigger(DoDie);
}
