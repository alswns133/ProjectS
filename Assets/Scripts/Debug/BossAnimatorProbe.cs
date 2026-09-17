using System.Text;
using Mirror;
using ProjectS.Enemies;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 진단 전용: 보스 애니메이터 상태를 일정 시간 주기적으로 기록한다. 서버·클라가 같은 순간 같은 모습인지 비교하기 위함이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 2026-09-17 "연출 뒤 위치는 움직이는데 애니메이션은 대기만 재생" 조사용. <c>BossIntroDirector</c>가 연출이 끝날 때 붙인다.
    /// </para>
    /// <para>
    /// <b>원격 클라의 기록은 서버 콘솔에도 나온다</b>(<c>ServerForwardLogSink</c>가 클라 로그 전체를 서버로 보낸다). 서버 콘솔 한 곳에서
    /// <c>[Host]</c>/<c>[Server]</c> 줄과 <c>[원격클라 …][Client]</c> 줄을 시간순으로 나란히 비교할 수 있다.
    /// </para>
    /// <para>
    /// 확인 포인트: <c>clip</c>이 계속 대기 클립인데 <c>Speed</c>가 0보다 크면 → 전이 조건 문제(컨트롤러). <c>params=0</c>이면 →
    /// 애니메이터가 컨트롤러로 안 돌아옴. 클라만 <c>Speed=0</c>이면 → 동기화 문제(<c>NA</c> 꺼짐 등).
    /// </para>
    /// </remarks>
    public class BossAnimatorProbe : MonoBehaviour
    {
        private const float Interval = 0.5f;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");

        private Animator animator;
        private Enemy enemy;
        private NetworkAnimator networkAnimator;
        private NetworkIdentity identity;
        private NavMeshAgent agent;

        private string label;
        private float until;
        private float nextLogAt;

        /// <summary>보스에 탐침을 붙여 <paramref name="seconds"/>초 동안 기록한다. 이미 붙어 있으면 기간만 갱신한다.</summary>
        /// <param name="boss">기록할 보스.</param>
        /// <param name="seconds">기록 시간(초).</param>
        /// <param name="tag">로그에 붙일 구간 이름(예: "연출 종료 후").</param>
        public static void Attach(Boss boss, float seconds, string tag)
        {
            if (boss == null) return;

            BossAnimatorProbe probe = boss.GetComponent<BossAnimatorProbe>();
            if (probe == null) probe = boss.gameObject.AddComponent<BossAnimatorProbe>();

            probe.label = tag;
            probe.until = Time.unscaledTime + seconds;
            probe.nextLogAt = 0f;
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>(true);
            enemy = GetComponent<Enemy>();
            networkAnimator = GetComponentInChildren<NetworkAnimator>(true);
            identity = GetComponent<NetworkIdentity>();
            agent = GetComponent<NavMeshAgent>();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now > until)
            {
                Destroy(this);
                return;
            }

            if (now < nextLogAt) return;
            nextLogAt = now + Interval;

            // 원격 클라의 이 줄은 ServerForwardLogSink가 서버 콘솔로도 보낸다(따로 전송 코드를 두지 않는다).
            Debug.Log($"[진단][BossAnim][{Role()}] {BuildLine()}", this);

        }

        private string BuildLine()
        {
            StringBuilder sb = new();
            sb.Append(label).Append(" | netId=").Append(identity != null ? identity.netId : 0u);

            if (animator == null)
                return sb.Append(" | Animator 없음").ToString();

            sb.Append(" | animEnabled=").Append(animator.enabled)
              .Append(" controller=").Append(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "없음")
              .Append(" params=").Append(animator.parameterCount);

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            sb.Append(" | state=0x").Append(state.fullPathHash.ToString("X8"))
              .Append(" t=").Append(state.normalizedTime.ToString("0.00"))
              .Append(" clip=").Append(ClipNames(animator.GetCurrentAnimatorClipInfo(0)));

            if (animator.IsInTransition(0))
                sb.Append(" →전이중→ ").Append(ClipNames(animator.GetNextAnimatorClipInfo(0)));

            sb.Append(" | Speed=").Append(ReadFloat(SpeedHash))
              .Append(" MoveX=").Append(ReadFloat(MoveXHash))
              .Append(" MoveY=").Append(ReadFloat(MoveYHash));

            sb.Append(" | NA=").Append(networkAnimator == null ? "없음" : networkAnimator.enabled.ToString());

            // AI 쪽: 연출 뒤 어느 상태로 들어갔고, 대상을 잡았는지, 감지 거리 안인지.
            if (enemy != null)
            {
                sb.Append(" | AI=").Append(enemy.enabled ? "on" : "off")
                  .Append(" state=").Append(enemy.StateMachine?.Current?.GetType().Name ?? "없음");

                if (enemy.Target != null)
                {
                    Vector3 targetPos = enemy.Target.position;
                    float distance = Vector3.Distance(transform.position, targetPos);
                    sb.Append(" target=").Append(enemy.Target.name)
                      .Append(" dist=").Append(distance.ToString("0.0"))
                      .Append("/감지").Append(enemy.DetectionRange.ToString("0.0"))
                      .Append(" alive=").Append(enemy.IsTargetAlive)
                      .Append(" targetPos=").Append(targetPos.ToString("F1"));

                    // 대상이 NavMesh 위(2m 이내)에 있는가. 없으면 보스가 그쪽으로 경로를 못 만든다.
                    sb.Append(" targetOnMesh=").Append(NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 2f, NavMesh.AllAreas)
                        ? $"예({Vector3.Distance(targetPos, hit.position):0.0}m)"
                        : "아니오");
                }
                else
                {
                    sb.Append(" target=없음");
                }
            }

            if (agent != null)
            {
                sb.Append(" | agent=").Append(agent.enabled ? $"on vel={agent.velocity.magnitude:0.00}" : "off");
                if (agent.enabled)
                    sb.Append(" onMesh=").Append(agent.isOnNavMesh)
                      .Append(agent.isOnNavMesh
                          ? $" stopped={agent.isStopped} hasPath={agent.hasPath} pending={agent.pathPending} " +
                            $"status={agent.pathStatus} speed={agent.speed:0.0} dest={agent.destination:F1}"
                          : "");
            }

            sb.Append(" pos=").Append(transform.position.ToString("F1"));
            return sb.ToString();
        }

        // 파라미터가 없을 때 GetFloat를 부르면 경고가 쏟아지므로 존재를 먼저 확인한다.
        private string ReadFloat(int hash)
        {
            if (animator.parameterCount == 0) return "-";

            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == hash) return animator.GetFloat(hash).ToString("0.00");

            return "없음";
        }

        private static string ClipNames(AnimatorClipInfo[] clips)
        {
            if (clips == null || clips.Length == 0) return "(없음)";

            StringBuilder sb = new();
            for (int i = 0; i < clips.Length; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(clips[i].clip != null ? clips[i].clip.name : "?")
                  .Append('(').Append(clips[i].weight.ToString("0.0")).Append(')');
            }

            return sb.ToString();
        }

        private static string Role()
        {
            if (NetworkServer.active && NetworkClient.active) return "Host";
            if (NetworkServer.active) return "Server";
            if (NetworkClient.active) return "Client";
            return "Solo";
        }
    }
}
