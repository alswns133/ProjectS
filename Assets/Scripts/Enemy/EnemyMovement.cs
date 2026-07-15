﻿using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent 기반 이동. 추적 목적지 설정과 정지/재개, 사망 시 에이전트 비활성화를 담당한다.
/// 플레이어(CharacterController)와 달리 길 찾기가 필요해 NavMesh를 사용한다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyMovement : MonoBehaviour
{
    [SerializeField, Min(0f)] private float animationSpeedDeadZone = 0.3f;
    [SerializeField, Min(0f)] private float reachSampleHeightTolerance = 0.5f;

    // 회피 우선순위 범위. 전원이 같은 값(유니티 기본 50)이면 밀집 시 서로 대칭으로
    // 밀어내는 회피 계산이 반복되어 제자리 떨림이 생긴다. 개체마다 다른 값을 주면
    // 낮은 숫자(높은 우선순위) 쪽이 길을 얻고 나머지가 비켜서며 떨림이 크게 줄어든다.
    [Header("군중 회피")]
    [SerializeField, Range(0, 99)] private int avoidancePriorityMin = 30;
    [SerializeField, Range(0, 99)] private int avoidancePriorityMax = 70;

    private NavMeshAgent agent;
    private NavMeshPath path;

    // 발견 대시처럼 일시적으로 속도를 바꾼 뒤 원래 값으로 되돌리기 위한 기준 속도.
    // NavMeshAgent의 기본 speed는 에디터 튜닝 값이므로 Awake에서 보관한다.
    private float baseSpeed;

    /// <summary>현재 이동 속력. 아주 작은 잔여 속도는 대기 모션으로 취급한다.</summary>
    public float CurrentSpeed
    {
        get
        {
            if (!agent.enabled || agent.isStopped) return 0f;

            float speed = agent.velocity.magnitude;
            return speed < animationSpeedDeadZone ? 0f : speed;
        }
    }

    /// <summary>현재 경로의 끝에 도달했는지 여부. 순찰 상태가 지점 도착 판정에 쓴다.</summary>
    public bool ReachedPathEnd => agent.enabled && !agent.pathPending
        && agent.remainingDistance <= agent.stoppingDistance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        baseSpeed = agent.speed;

        // Random.Range(int)는 max가 배타적이므로 +1로 최댓값도 포함시킨다.
        agent.avoidancePriority = Random.Range(
            avoidancePriorityMin, Mathf.Max(avoidancePriorityMin, avoidancePriorityMax) + 1);
    }

    /// <summary>목적지를 갱신한다. 추적 상태가 매 프레임 호출한다.</summary>
    public void SetDestination(Vector3 worldPos)
    {
        // 사망(에이전트 꺼짐)이나 NavMesh 밖 스폰 직후 호출돼도 예외가 나지 않게 방어한다.
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.SetDestination(worldPos);
    }

    /// <summary>지정 위치까지 완전한 NavMesh 경로가 있는지 검사한다.</summary>
    public bool CanReach(Vector3 worldPos, float sampleRadius)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return false;

        if (!NavMesh.SamplePosition(
            worldPos, // 검사할 목표 위치: 보통 플레이어 위치
            out NavMeshHit hit, // 목표 주변에서 찾은 가장 가까운 NavMesh 지점
            sampleRadius, // 검색 반경: 플레이어가 NavMesh에서 살짝 떠 있거나 가장자리에 있을 때 보정할 거리
            agent.areaMask)) // 이 에이전트가 이동 가능한 NavMesh Area 마스크
            return false;

        // 점프로 올라간 높은 지형 아래의 NavMesh가 잡히면 갈 수 있다고 오판할 수 있다.
        if (Mathf.Abs(hit.position.y - worldPos.y) > reachSampleHeightTolerance) return false;
        if (!agent.CalculatePath(hit.position, path)) return false;

        return path.status == NavMeshPathStatus.PathComplete;
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

    /// <summary>
    /// 기본 NavMeshAgent 속도에 배수를 적용한다.
    /// DetectState의 발견 대시처럼 짧게 속도를 바꾸는 상태에서만 사용한다.
    /// </summary>
    public void SetSpeedMultiplier(float multiplier)
    {
        // DetectState 전용 튜닝. multiplier가 음수로 들어와도 역주행하지 않도록 0 이상으로 제한한다.
        if (agent == null) return;
        agent.speed = baseSpeed * Mathf.Max(0f, multiplier);
    }

    /// <summary>
    /// NavMeshAgent 속도를 Awake에서 저장한 기본값으로 되돌린다.
    /// 발견 대시가 다른 상태로 끊겨도 속도 변경이 남지 않게 Exit에서 호출한다.
    /// </summary>
    public void ResetSpeed()
    {
        // DetectState가 끝날 때 반드시 호출해 추격/공격 상태가 원래 이동 속도를 쓰게 한다.
        if (agent == null) return;
        agent.speed = baseSpeed;
    }

    /// <summary>에이전트를 완전히 끈다. 사망 시 1회 호출하며 되돌리지 않는다.</summary>
    public void DisableAgent() => agent.enabled = false;
}
