using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent 기반 이동. 추적 목적지 설정과 정지/재개, 사망 시 에이전트 비활성화를 담당한다.
/// 플레이어(CharacterController)와 달리 길 찾기가 필요해 NavMesh를 사용한다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMovement : MonoBehaviour
{
    private NavMeshAgent agent;

    /// <summary>현재 이동 속력. 이동 애니메이션 블렌드 값으로 쓴다.</summary>
    public float CurrentSpeed => agent.enabled ? agent.velocity.magnitude : 0f;

    /// <summary>
    /// 목적지까지 온전한 경로가 있는지 여부. 플레이어가 점프로 지형 위에 올라가는 등
    /// NavMesh 밖에 있으면 false가 된다(PathPartial/PathInvalid).
    /// 경로 계산 중(pathPending)에는 아직 모르는 상태이므로 낙관적으로 true를 돌려준다
    /// → 계산이 끝나기 전에 추적을 성급하게 멈추지 않기 위함.
    /// </summary>
    public bool HasReachablePath => !agent.enabled || agent.pathPending
        || agent.pathStatus == NavMeshPathStatus.PathComplete;

    /// <summary>
    /// 현재 경로의 끝에 도달했는지 여부. 부분 경로일 때 이게 true면
    /// "갈 수 있는 데까지 다 갔다"는 뜻이라, 추적 상태가 대기 전환 판정에 쓴다.
    /// </summary>
    public bool ReachedPathEnd => agent.enabled && !agent.pathPending
        && agent.remainingDistance <= agent.stoppingDistance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    /// <summary>목적지를 갱신한다. 추적 상태가 매 프레임 호출한다.</summary>
    public void SetDestination(Vector3 worldPos)
    {
        // 사망(에이전트 꺼짐)이나 NavMesh 밖 스폰 직후 호출돼도 예외가 나지 않게 방어한다.
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.SetDestination(worldPos);
    }

    /// <summary>이동을 멈춘다(경로는 유지). 대기·공격 상태 진입 시 호출한다.</summary>
    public void Stop()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
    }

    /// <summary>멈췄던 이동을 재개한다. 추적 상태 진입 시 호출한다.</summary>
    public void Resume()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = false;
    }

    /// <summary>지정 위치를 즉시 바라본다(수평 성분만). 공격 시작 시 대상 정렬에 쓴다.</summary>
    public void Face(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>에이전트를 완전히 끈다. 사망 시 1회 호출하며 되돌리지 않는다.</summary>
    public void DisableAgent() => agent.enabled = false;
}
