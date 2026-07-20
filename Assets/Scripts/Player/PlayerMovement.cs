using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// CharacterController 기반 플레이어 이동 담당.
    /// 카메라 기준 수평 이동, 회전, 중력, 점프 가능 여부를 한곳에서 관리한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float rotationDamping = 10f;
        [SerializeField] private float jumpHeight = 3f;
        [SerializeField] private float gravity = 9.8f;

        // 공중에서 입력 방향으로 관성을 꺾는 속도. 지상은 루트모션이 담당하고, 공중은 점프 순간
        // 캡처한 수평 속도(관성)를 유지하다가 이 값만큼만 서서히 조향한다.
        // 작을수록 관성 위주(둔한 조작), 클수록 즉각 조작(관성 약함).
        [SerializeField] private float airControl = 8f;

        [Header("바닥 체크")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float checkRadius = 0.25f;
        [SerializeField] private LayerMask floorLayer;

        // 구르기 튜닝. 상태 클래스(PlayerRollState)는 MonoBehaviour가 아니라
        // 인스펙터 값을 못 가지므로, 이동 소관인 여기에 두고 프로퍼티로 노출한다.
        // 이동 속도는 따로 없다: 구르기 전진은 클립의 루트 모션이 담당한다.
        [Header("구르기")]
        [SerializeField] private float rollDuration = 0.6f; // 구르기 지속 시간(초). 클립 길이와 맞출 것

        private CharacterController controller;
        private Transform cam;

        // CharacterController는 Rigidbody 속도를 들고 있지 않으므로 수직 속도를 직접 누적한다.
        private float verticalVelocity;

        // 지상에서 실제로 난 수평 속도(루트모션+코드 결과)를 매 프레임 측정해 둔다.
        // 점프 순간 이 값을 airVelocity로 넘겨, 공중 속도를 지상과 정확히 일치시키기 위함.
        private Vector3 lastPosition;
        private Vector3 groundHorizontalVelocity;

        // 점프 순간 캡처한 공중 관성 속도. 공중에서는 이 값을 유지하며 입력 방향으로만 서서히 조향한다.
        private Vector3 airVelocity;

        public bool IsGrounded { get; private set; }

        /// <summary>
        /// '진짜로 서 있는가' 판정. 점프 직후 몇 프레임 동안 접지 체크(IsGrounded)가
        /// true로 남는 잔존 구간을 verticalVelocity로 걸러낸다(상승 중이면 공중 취급).
        /// 점프 가능 여부와 지상 구르기 가능 여부가 이 판정을 공유한다.
        /// </summary>
        public bool IsStablyGrounded => IsGrounded && verticalVelocity <= 0f;

        public bool CanJump => IsStablyGrounded;

        /// <summary>구르기 지속 시간(초). PlayerRollState가 상태 종료 판정에 쓴다.</summary>
        public float RollDuration => rollDuration;

        /// <summary>
        /// 공중 정지(호버링) 여부. 점프 공격 모션 동안 켜져 높이가 고정된다(기획).
        /// </summary>
        public bool IsHovering { get; private set; }

        /// <summary>
        /// 호버링을 켜고 끈다. 켜는 순간 수직 속도를 0으로 만들어 그 높이에 멈추고,
        /// 끄면 멈췄던 높이에서 다시 낙하를 시작한다.
        /// 켜기: 점프 공격 발동 시(Player.OnAttack). 끄기: 모션 종료(UnlockMovement) 또는 사망.
        /// </summary>
        public void SetHover(bool value)
        {
            IsHovering = value;
            if (value) verticalVelocity = 0f;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            TryCacheMainCamera();

            // 첫 LateUpdate에서 (현재위치 - 0) 큰 델타로 속도가 튀지 않게 시작 위치로 초기화한다.
            lastPosition = transform.position;
        }

        public void Move(Vector2 input, bool isRunning = false)
        {
            // 이동이 잠긴 상태에서도 PlayerFreeState가 zero 입력으로 호출한다.
            // 그래서 중력/접지는 계속 처리되고 수평 이동만 멈춘다.
            // 기본값 false: 구르기 상태처럼 zero 입력만 넘기는 호출부는 달리기를 신경 쓸 필요 없다.
            Vector3 move;

            if (IsGrounded)
            {
                // 지상: 목표 속도를 즉시 적용한다(즉각 반응 우선 — 가속 램프를 시험했다가 되돌린 팀 결정, 2026-07).
                // 루트모션이 위치를 함께 담당하고, 출발 시 부드러움은 애니메이션 블렌드 감쇠가 맡는다.
                move = CameraRelative(input) * (isRunning ? runSpeed : moveSpeed);
            }
            else
            {
                // 공중: 점프 순간 캡처한 관성(airVelocity)을 유지하고, 입력 방향으로 airControl만큼만
                // 서서히 조향한다. 무입력이면 관성을 그대로 이어가 지상에서 내던 속도로 날아간다.
                Vector3 target = CameraRelative(input) * airVelocity.magnitude;
                if (target.sqrMagnitude < 0.0001f) target = airVelocity;
                airVelocity = Vector3.MoveTowards(airVelocity, target, airControl * Time.deltaTime);
                move = airVelocity;
            }

            FaceDirection(move);
            ApplyGravity(ref move);
            controller.Move(move * Time.deltaTime);
        }

        public bool Jump()
        {
            if (!CanJump) return false;

            // v = sqrt(2gh). 이후 ApplyGravity에서 낙하 가속을 더 크게 주기 때문에
            // jumpHeight는 정확한 높이라기보다 튜닝 기준값에 가깝다.
            verticalVelocity = Mathf.Sqrt(2f * jumpHeight * gravity);

            // 점프 직전 지상에서 내던 수평 속도를 공중 관성으로 넘긴다.
            // → 달리다 뛰면 달리기 속도로, 걷다 뛰면 걷기 속도로 자연스럽게 이어진다.
            airVelocity = groundHorizontalVelocity;
            return true;
        }

        /// <summary>
        /// 상승 중인 점프 속도를 제거한다. 점프 상승 도중 피격 등으로 동작이 끊길 때 호출해
        /// 애니메이션은 멈췄는데 몸만 계속 떠오르는 것을 막는다.
        /// 하강 중(verticalVelocity ≤ 0)에는 건드리지 않아 낙하는 자연스럽게 이어진다.
        /// </summary>
        public void CancelJump()
        {
            if (verticalVelocity > 0f) verticalVelocity = 0f;
        }

        /// <summary>
        /// 입력(x=좌우, y=앞뒤)을 카메라 기준 월드 방향으로 변환해 돌려준다.
        /// 구르기 상태가 진입 시점에 방향을 1회 확정할 때 쓴다.
        /// </summary>
        public Vector3 CameraRelativeDirection(Vector2 input) => CameraRelative(input);

        /// <summary>
        /// 지정한 월드 방향을 즉시 바라본다(수평 성분만).
        /// 구르기 시작처럼 보간(FaceDirection) 없이 한 프레임에 방향이 확정되어야 할 때 쓴다.
        /// </summary>
        public void FaceInstantly(Vector3 worldDirection)
        {
            worldDirection.y = 0;
            if (worldDirection.sqrMagnitude < 0.0001f) return;   // 0벡터 방향은 정의 불가 → 무시

            transform.rotation = Quaternion.LookRotation(worldDirection);
        }

        public void SnapToCameraForward()
        {
            // 공격/스킬 시작 순간에는 부드러운 회전보다 카메라 방향 정렬이 우선이다.
            if (!TryCacheMainCamera()) return;

            FaceInstantly(cam.forward);
        }

        // 루트모션과 controller.Move가 모두 적용된 뒤(프레임 끝) 실제 이동량으로 지상 수평 속도를 측정한다.
        // 무엇이 위치를 옮겼든(루트모션/코드/둘 다) 결과 속도라, 점프 시 넘기면 공중이 지상과 정확히 일치한다.
        // 벽에 막히면 실제 이동량이 줄어 측정 속도도 자연히 낮아진다(벽에 눌린 채 점프하면 관성이 안 실림).
        private void LateUpdate()
        {
            if (Time.deltaTime > 0f && IsGrounded)
            {
                Vector3 delta = transform.position - lastPosition;
                delta.y = 0f;
                groundHorizontalVelocity = delta / Time.deltaTime;
            }

            lastPosition = transform.position;
        }

        private void FixedUpdate()
        {
            if (groundCheck == null) return;

            // CharacterController.isGrounded 대신 별도 체크 포인트를 쓴다.
            // 경사/계단/모델 피벗 차이를 인스펙터에서 조절하기 쉽다.
            IsGrounded = Physics.CheckSphere(
                groundCheck.position,
                checkRadius,
                floorLayer,
                QueryTriggerInteraction.Ignore);
        }

        private void ApplyGravity(ref Vector3 move)
        {
            // 호버링(점프 공격) 중에는 중력을 누적하지 않고 높이를 고정한다.
            if (IsHovering)
            {
                move.y = 0f;
                return;
            }

            // 접지 중에는 작은 음수로 눌러 바닥과의 접촉을 안정화한다.
            if (IsGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;

            // 상승보다 낙하를 빠르게 해서 조작감이 늘어지지 않게 한다.
            verticalVelocity -= 2f * gravity * Time.deltaTime;
            move.y = verticalVelocity;
        }

        private Vector3 CameraRelative(Vector2 input)
        {
            // 씬 로드 순서상 카메라가 늦게 잡힐 수 있으므로 사용할 때도 한 번 더 캐싱한다.
            if (!TryCacheMainCamera()) return Vector3.zero;

            Vector3 f = cam.forward;
            f.y = 0;
            f.Normalize();

            Vector3 r = cam.right;
            r.y = 0;
            r.Normalize();

            return (f * input.y + r * input.x).normalized;
        }

        private void FaceDirection(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) return;

            // 이동 중 회전은 즉시 꺾지 않고 보간해 자연스럽게 따라가게 한다.
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                rotationDamping * Time.deltaTime);
        }

        private bool TryCacheMainCamera()
        {
            if (cam != null) return true;
            if (Camera.main == null) return false;

            cam = Camera.main.transform;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck == null) return;

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, checkRadius);
        }
    }
}
