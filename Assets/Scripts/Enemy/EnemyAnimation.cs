using UnityEngine;

/// <summary>
/// Animator 파라미터 브릿지. 몬스터 Animator는 이 클래스를 통해서만 제어한다.
/// 파라미터 이름은 애니메이터 담당자와 공유하는 계약이므로 여기에만 정의한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyAnimation : MonoBehaviour
{
    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int DoAttack = Animator.StringToHash("doAttack");
    private static readonly int DoDie = Animator.StringToHash("doDie");

    private const float Damp = 0.1f; // 이동 블렌드 값이 튀지 않게 부드럽게 따라가는 감쇠 시간

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>이동 속력을 블렌드 파라미터에 반영한다. 상태들이 매 프레임 호출한다.</summary>
    public void SetSpeed(float speed) => animator.SetFloat(Speed, speed, Damp, Time.deltaTime);

    /// <summary>공격 트리거. 공격 상태 진입 시 1회 호출한다.</summary>
    public void PlayAttack() => animator.SetTrigger(DoAttack);

    /// <summary>사망 트리거. 사망 상태 진입 시 1회 호출한다.</summary>
    public void PlayDie() => animator.SetTrigger(DoDie);
}
