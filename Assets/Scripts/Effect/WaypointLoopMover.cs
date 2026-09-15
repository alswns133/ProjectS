using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 지정한 웨이포인트들을 순서대로 일정한 속도로 돌고, 마지막 지점 다음엔 첫 지점으로 돌아가 무한 반복한다.
    /// (A -> B -> C -> D -> A ...) 순찰하는 드론, 레일을 도는 화물, 배경 차량 같은 연출용.
    ///
    /// 웨이포인트를 좌표 숫자가 아니라 Transform으로 받는 이유는, 씬 뷰에서 빈 오브젝트를 끌어다 놓는 것만으로
    /// 경로를 고칠 수 있게 하기 위해서다. 숫자로 받으면 경로를 바꿀 때마다 좌표를 옮겨 적어야 한다.
    ///
    /// 주의: 웨이포인트를 이 오브젝트의 자식으로 두면 같이 따라 움직여서 영원히 도착하지 못한다.
    /// 반드시 형제나 별도 부모 아래에 둔다. 또 이 오브젝트가 Static이면 정적 배칭으로 메시가 고정돼
    /// 움직여도 화면에 보이지 않는다.
    /// </summary>
    public class WaypointLoopMover : MonoBehaviour
    {
        [Header("경로")]
        [Tooltip("돌아다닐 지점들. 배열 순서대로 이동하고, 마지막 다음엔 0번으로 돌아간다. 최소 2개 필요.")]
        [SerializeField] private Transform[] waypoints;

        [Tooltip("켜면 시작할 때 0번 지점으로 순간이동한 뒤 1번을 향해 출발한다. " +
                 "끄면 배치한 자리에서 곧장 0번 지점으로 먼저 이동한다.")]
        [SerializeField] private bool snapToFirstOnStart = true;

        [Header("이동")]
        [Tooltip("초당 이동 거리(유닛/초). 구간 길이와 무관하게 이 속도로 움직인다.")]
        [SerializeField] private float speed = 2f;

        [Tooltip("각 지점에 도착한 뒤 멈춰 있는 시간(초). 0이면 멈추지 않는다.")]
        [SerializeField] private float waitTime = 0f;

        [Header("회전")]
        [Tooltip("켜면 진행 방향을 바라보도록 회전한다.")]
        [SerializeField] private bool faceMoveDirection;

        [Tooltip("회전 속도(도/초). 0이면 방향이 바뀌는 순간 즉시 돌아본다.")]
        [SerializeField] private float turnSpeed = 360f;

        [Tooltip("켜면 위아래 기울기 없이 수평으로만 돈다. 높이가 다른 지점을 오갈 때 앞으로 숙여지는 것을 막는다.")]
        [SerializeField] private bool keepUpright = true;

        private int targetIndex;
        private float waitTimer;

        private void Awake()
        {
            if (!HasValidPath())
            {
                Debug.LogWarning($"{name}: 웨이포인트가 2개 미만이거나 빈 칸이 있어 이동하지 않는다. 비활성화한다.", this);
                enabled = false;
                return;
            }

            if (snapToFirstOnStart)
            {
                transform.position = waypoints[0].position;
                targetIndex = 1;
            }
            else
            {
                targetIndex = 0;
            }
        }

        private void Update()
        {
            if (waitTimer > 0f)
            {
                waitTimer -= Time.deltaTime;
                return;
            }

            Vector3 target = waypoints[targetIndex].position;

            if (faceMoveDirection)
            {
                RotateTowards(target - transform.position);
            }

            // MoveTowards는 목표를 넘어가지 않고 정확히 목표값에서 멈춘다.
            // 그래서 속도가 빨라도 지점을 지나쳐 되돌아오는 떨림이 없고, 아래 도착 비교가 성립한다.
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

            if (transform.position == target)
            {
                // 마지막 지점 다음은 나머지 연산으로 0번에 돌아간다.
                targetIndex = (targetIndex + 1) % waypoints.Length;
                waitTimer = waitTime;
            }
        }

        private void RotateTowards(Vector3 direction)
        {
            if (keepUpright)
            {
                direction.y = 0f;
            }

            // 방향이 0이면 LookRotation이 경고를 내고 회전이 튄다. 수직 이동 구간이나 도착 순간에 발생한다.
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion look = Quaternion.LookRotation(direction);
            transform.rotation = turnSpeed <= 0f
                ? look
                : Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        private bool HasValidPath()
        {
            if (waypoints == null || waypoints.Length < 2) return false;

            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) return false;
            }
            return true;
        }

        // 플레이하지 않아도 경로가 씬 뷰에 보여야 웨이포인트를 배치하면서 모양을 확인할 수 있다.
        private void OnDrawGizmosSelected()
        {
            if (waypoints == null) return;

            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform current = waypoints[i];
                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (current == null) continue;

                Gizmos.DrawWireSphere(current.position, 0.2f);
                if (next != null && waypoints.Length > 1)
                {
                    Gizmos.DrawLine(current.position, next.position);
                }
            }
        }
    }
}
