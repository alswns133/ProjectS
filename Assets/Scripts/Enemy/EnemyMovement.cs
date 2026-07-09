using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent 기반 이동. 추적 목적지 설정과 정지만 담당한다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMovement : MonoBehaviour
{
    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }
}
