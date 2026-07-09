using UnityEngine;

/// <summary>
/// Animator 파라미터 브릿지. 몬스터 Animator는 이 클래스를 통해서만 제어한다.
/// </summary>
[RequireComponent(typeof(Animator))]
public class EnemyAnimation : MonoBehaviour
{
    private Animator animator;
    
    void Awake()
    {
        animator  = GetComponent<Animator>();
    }
}
