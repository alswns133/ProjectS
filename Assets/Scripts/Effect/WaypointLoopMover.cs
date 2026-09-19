using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 지정한 웨이포인트들을 순서대로 일정한 속도로 돌고, 마지막 지점 다음엔 첫 지점으로 돌아가 무한 반복한다.
    /// (A -> B -> C -> D -> A ...) 순찰하는 드론, 레일을 도는 화물, 배경 차량, 짝지어 걷는 NPC 같은 연출용.
    ///
    /// 웨이포인트를 좌표 숫자가 아니라 Transform으로 받는 이유는, 씬 뷰에서 빈 오브젝트를 끌어다 놓는 것만으로
    /// 경로를 고칠 수 있게 하기 위해서다. 숫자로 받으면 경로를 바꿀 때마다 좌표를 옮겨 적어야 한다.
    ///
    /// <b>모서리를 멈추지 않고 곡선으로 돈다.</b> 지점을 향해 곧장 가다가 도착해서 돌면, 몸이 옛 방향을 보는 채로
    /// 새 방향으로 끌려가 게걸음처럼 미끄러진다. 대신 "바라보는 방향으로 전진하면서, 경로 위의 조금 앞 지점
    /// (turnRadius만큼 앞)을 향해 방향을 트는" 방식(pure pursuit)을 쓴다. 이동 방향과 몸 방향이 항상 같아서
    /// 미끄러짐이 없고, 90° 모서리는 둥글게 안쪽을 질러 돌며, 180° 되돌아가기는 작은 원을 그리고 돌아온다.
    /// 대가로 경로선을 정확히 밟지 않고 모서리를 turnRadius 정도 안쪽으로 지나간다.
    ///
    /// <b>자식 Animator에 이동 여부를 bool로 알린다</b>(<see cref="movingBoolName"/>, 기본 isMoving).
    /// 이동 중 true, 대기(waitTime) 중 false라 Walk/Idle 전환 조건으로 쓴다. 파라미터가 없는 Animator와
    /// Animator 없는 대상(드론·차량)은 건너뛴다.
    ///
    /// 주의: 웨이포인트를 이 오브젝트의 자식으로 두면 같이 따라 움직여서 영원히 도착하지 못한다.
    /// 반드시 형제나 별도 부모 아래에 둔다. 또 이 오브젝트가 Static이면 정적 배칭으로 메시가 고정돼
    /// 움직여도 화면에 보이지 않는다. 여러 명을 묶을 때는 이 오브젝트의 원점이 회전 중심이므로
    /// 자식들의 한가운데에 둔다.
    /// </summary>
    public class WaypointLoopMover : MonoBehaviour
    {
        [Header("경로")]
        [Tooltip("돌아다닐 지점들. 배열 순서대로 이동하고, 마지막 다음엔 0번으로 돌아간다. 최소 2개 필요.")]
        [SerializeField] private Transform[] waypoints;

        [Tooltip("켜면 시작할 때 0번 지점에서 1번을 바라본 채로 출발한다. " +
                 "끄면 배치한 자리에서 0번 지점 쪽으로 먼저 합류한다.")]
        [SerializeField] private bool snapToFirstOnStart = true;

        [Header("이동")]
        [Tooltip("초당 이동 거리(유닛/초). 모서리에서도 줄지 않는다.")]
        [SerializeField] private float speed = 2f;

        [Tooltip("모서리를 도는 곡선의 반지름(유닛). 클수록 크고 완만하게 돈다. " +
                 "여러 명을 묶었다면 원점에서 가장 먼 사람까지의 거리보다 크게 둔다 — 작으면 안쪽 사람이 뒷걸음질친다.")]
        [SerializeField, Min(0.1f)] private float turnRadius = 1.5f;

        [Tooltip("각 지점을 지날 때 멈춰 있는 시간(초). 모서리를 도는 도중에 멈추므로 보통 0을 쓴다.")]
        [SerializeField] private float waitTime = 0f;

        [Header("회전")]
        [Tooltip("켜면 진행 방향을 바라본다. 끄면 방향은 그대로 두고 위치만 움직인다(드론 등).")]
        [SerializeField] private bool faceMoveDirection;

        [Tooltip("켜면 위아래 기울기 없이 수평으로만 돈다. 끄면 경사 구간에서 경사만큼 앞뒤로 기운다.")]
        [SerializeField] private bool keepUpright = true;

        [Header("애니메이션")]
        [Tooltip("자식 Animator에 이동 여부를 알릴 bool 파라미터 이름. 이동 중 true, 대기 중 false. " +
                 "이 파라미터가 없는 Animator는 건너뛴다.")]
        [SerializeField] private string movingBoolName = "isMoving";

        // 현재 따라가는 구간: waypoints[segment] -> waypoints[segment + 1].
        private int segment;

        // 실제 진행 방향(수평 단위 벡터). faceMoveDirection이 꺼져 있어도 이동은 이 방향으로 한다.
        private Vector3 heading;

        private float waitTimer;

        private Animator[] animators;
        private int movingBoolHash;
        private bool hasAppliedMoving;
        private bool appliedMoving;

        private void Awake()
        {
            if (!HasValidPath())
            {
                Debug.LogWarning($"{name}: 웨이포인트가 2개 미만이거나 빈 칸이 있어 이동하지 않는다. 비활성화한다.", this);
                enabled = false;
                return;
            }

            movingBoolHash = Animator.StringToHash(movingBoolName);

            if (snapToFirstOnStart)
            {
                segment = 0;
                transform.position = waypoints[0].position;
                heading = Flat(waypoints[1].position - waypoints[0].position);
            }
            else
            {
                // 마지막 -> 0번 구간에 합류시킨다. 경로 밖에서 시작해도 앞 지점을 쫓아 자연스럽게 붙는다.
                segment = waypoints.Length - 1;
                heading = Flat(waypoints[0].position - transform.position);
            }

            if (heading == Vector3.zero) heading = Flat(transform.forward);
            if (heading == Vector3.zero) heading = Vector3.forward;

            // 출발 순간 한 프레임 돌아보는 튐이 없도록 처음부터 진행 방향을 본다.
            if (faceMoveDirection) ApplyRotation();
        }

        private void Start()
        {
            // 파라미터 목록은 Animator가 초기화된 뒤에 믿을 수 있어 Awake가 아니라 Start에서 거른다.
            // 없는 파라미터에 SetBool하면 매 전환마다 경고가 쌓인다.
            Animator[] found = GetComponentsInChildren<Animator>(true);
            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (HasMovingBool(found[i])) found[count++] = found[i];
            }

            animators = new Animator[count];
            System.Array.Copy(found, animators, count);
        }

        private void Update()
        {
            if (waitTimer > 0f)
            {
                waitTimer -= Time.deltaTime;
                ApplyMoving(false);
                return;
            }

            ApplyMoving(true);

            Vector3 position = transform.position;

            if (AdvanceSegmentIfPassed(position) && waitTime > 0f)
            {
                waitTimer = waitTime;
                return;
            }

            // 경로 위에서 지금 위치에 가장 가까운 점부터 turnRadius만큼 앞선 점을 쫓는다.
            // 앞선 거리를 회전 반경과 같게 두면 모서리가 대략 그 반경의 곡선이 된다.
            Vector3 onPath = ClosestPointOnSegment(position, segment, out _);
            Vector3 aim = PointAhead(segment, onPath, turnRadius);

            // 회전 속도를 반경에서 역산한다(각속도 = 속도 / 반경). 속도를 바꿔도 곡선의 크기가 그대로 유지된다.
            float maxTurn = speed / turnRadius * Time.deltaTime;
            Vector3 desired = Flat(aim - position);
            if (desired != Vector3.zero)
            {
                heading = Flat(Vector3.RotateTowards(heading, desired, maxTurn, 0f));
            }

            position += heading * (speed * Time.deltaTime);

            // 높이는 경로를 따른다. 수평 이동만으로는 높이가 다른 지점을 오를 수 없다.
            position.y = Mathf.MoveTowards(position.y, onPath.y, speed * Time.deltaTime);

            transform.position = position;

            if (faceMoveDirection) ApplyRotation();
        }

        // 현재 구간 끝까지 남은 거리가 turnRadius 안으로 들어오면 다음 구간으로 넘어간다. 넘어갔으면 true(= 지점 통과).
        // 쫓는 점(turnRadius 앞)이 구간 끝을 넘기 "전에" 넘어가야 한다. 늦으면 180° 되돌아가기에서 쫓는 점이
        // 몸 뒤로 접혀, 돌다가 다시 앞을 쫓는 것을 반복하며 모서리에서 맴돈다.
        // ("다음 구간이 더 가까우면 넘어간다"는 흔한 규칙은 되돌아가기에서 두 구간이 같은 직선이라 거리가 늘 같아 쓸 수 없다.)
        private bool AdvanceSegmentIfPassed(Vector3 position)
        {
            Vector3 a = waypoints[segment].position;
            Vector3 b = waypoints[(segment + 1) % waypoints.Length].position;

            ClosestPointOnSegment(position, segment, out float t);
            float remaining = (1f - t) * Flat(b - a, normalize: false).magnitude;

            if (remaining > turnRadius) return false;

            segment = (segment + 1) % waypoints.Length;
            return true;
        }

        // index번 구간(index -> index+1) 위에서 point에 가장 가까운 점. t는 구간 안의 비율(0~1).
        private Vector3 ClosestPointOnSegment(Vector3 point, int index, out float t)
        {
            Vector3 a = waypoints[index].position;
            Vector3 b = waypoints[(index + 1) % waypoints.Length].position;

            Vector3 ab = Flat(b - a, normalize: false);
            float lengthSq = ab.sqrMagnitude;
            t = lengthSq > 0.0001f ? Mathf.Clamp01(Vector3.Dot(Flat(point - a, normalize: false), ab) / lengthSq) : 1f;

            return Vector3.Lerp(a, b, t);
        }

        // from(index번 구간 위의 점)에서 경로를 따라 distance만큼 앞선 점. 구간 끝을 넘으면 다음 구간으로 이어 간다.
        private Vector3 PointAhead(int index, Vector3 from, float distance)
        {
            for (int guard = 0; guard < waypoints.Length; guard++)
            {
                Vector3 end = waypoints[(index + 1) % waypoints.Length].position;
                float left = Vector3.Distance(from, end);

                if (distance <= left) return Vector3.MoveTowards(from, end, distance);

                distance -= left;
                from = end;
                index = (index + 1) % waypoints.Length;
            }
            return from;
        }

        private void ApplyRotation()
        {
            Vector3 look = heading;

            // 경사 구간에서만 앞뒤로 기울인다. 기울기는 지금 구간의 경사를 그대로 쓴다.
            if (!keepUpright)
            {
                Vector3 a = waypoints[segment].position;
                Vector3 b = waypoints[(segment + 1) % waypoints.Length].position;
                float run = Flat(b - a, normalize: false).magnitude;
                if (run > 0.0001f) look.y = (b.y - a.y) / run;
            }

            transform.rotation = Quaternion.LookRotation(look);
        }

        // 수평 성분만 남긴다. 진행 방향 계산은 수평면에서 하고 높이는 따로 따라간다.
        private static Vector3 Flat(Vector3 v, bool normalize = true)
        {
            v.y = 0f;
            if (!normalize) return v;
            return v.sqrMagnitude > 0.000001f ? v.normalized : Vector3.zero;
        }

        // 값이 바뀔 때만 SetBool한다. 매 프레임 같은 값을 넣을 이유가 없다.
        private void ApplyMoving(bool moving)
        {
            if (animators == null || animators.Length == 0) return;
            if (hasAppliedMoving && appliedMoving == moving) return;

            hasAppliedMoving = true;
            appliedMoving = moving;

            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] != null) animators[i].SetBool(movingBoolHash, moving);
            }
        }

        private bool HasMovingBool(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == movingBoolHash && parameter.type == AnimatorControllerParameterType.Bool)
                    return true;
            }
            return false;
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
        // 지점마다 회전 반경 원을 함께 그려, 모서리를 얼마나 안쪽으로 질러 갈지 가늠하게 한다.
        private void OnDrawGizmosSelected()
        {
            if (waypoints == null) return;

            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform current = waypoints[i];
                Transform next = waypoints[(i + 1) % waypoints.Length];
                if (current == null) continue;

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(current.position, 0.2f);
                if (next != null && waypoints.Length > 1)
                {
                    Gizmos.DrawLine(current.position, next.position);
                }

                Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
                Gizmos.DrawWireSphere(current.position, turnRadius);
            }
        }
    }
}
