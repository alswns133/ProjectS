using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using ProjectS.Core;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 지정한 레이어(기본 Player)의 오브젝트가 감지 범위에 들어오면 열리고, 모두 나가면 닫히는 자동문.
    ///
    /// 매 프레임 거리 계산 대신 트리거 콜라이더에 판정을 맡긴다(<see cref="DungeonGate"/>·NpcOutlineTrigger와 같은 방침).
    /// 문이 여러 개여도 Update 비용은 실제로 움직이는 동안에만 든다.
    ///
    /// <b>붙이는 위치</b>: 문 본체가 아니라 <b>감지 전용 자식 오브젝트</b>에 붙인다.
    /// 이 스크립트는 자기 BoxCollider를 강제로 isTrigger로 바꾸기 때문에, 문 본체 콜라이더에 붙이면
    /// 문이 플레이어를 막지 못하게 된다. 움직일 대상은 <see cref="doorPivot"/>으로 따로 지정한다.
    ///
    /// <b>여닫는 방식</b>은 <see cref="mode"/>로 고른다. Animator 클립이 준비되기 전에는
    /// Rotate/Slide로 바로 굴려 보고, 클립이 나오면 Animator 모드로 바꾸면 된다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class AutoDoor : MonoBehaviour
    {
        /// <summary>문을 실제로 여닫는 방식.</summary>
        public enum OpenMode
        {
            /// <summary>Animator의 bool 파라미터를 켜고 끈다. 애니메이션 클립이 있을 때 사용한다.</summary>
            Animator = 0,
            /// <summary>피벗을 로컬 축 기준으로 회전시킨다(여닫이문).</summary>
            Rotate = 1,
            /// <summary>피벗을 로컬 방향으로 밀어낸다(미닫이문).</summary>
            Slide = 2,
            /// <summary>아무것도 직접 하지 않고 <see cref="onOpen"/>/<see cref="onClose"/>만 발행한다.</summary>
            EventOnly = 3,
        }

        [Header("감지")]
        [Tooltip("이 레이어에 속한 콜라이더가 들어오면 문이 열린다. 기본값은 Player 레이어.")]
        [SerializeField] private LayerMask detectLayers = 1 << 8;

        // 실제 판정은 BoxCollider가 하고, 이 값으로 크기를 인스펙터 한곳에서 관리한다.
        [Tooltip("감지 범위(로컬 크기). 문 앞뒤를 함께 덮도록 잡는다.")]
        [SerializeField] private Vector3 detectSize = new Vector3(3f, 3f, 3f);

        [Tooltip("범위를 벗어나면 다시 닫는다. 끄면 한 번 열린 뒤 계속 열린 채로 둔다(일방통행 문 등).")]
        [SerializeField] private bool closeOnExit = true;

        [Tooltip("마지막 대상이 나간 뒤 닫히기까지의 대기 시간(초). 문턱에서 여닫힘이 떨리는 것을 막는다.")]
        [SerializeField, Min(0f)] private float closeDelay = 0.4f;

        [Header("잠금")]
        [Tooltip("켜면 시작할 때 잠긴 상태다. 잠긴 동안에는 다가가도 열리지 않고, Unlock()을 받아야 동작을 시작한다. " +
                 "튜토리얼 과제를 통과해야 열리는 문처럼 '조건을 만족해야 열리는 문'에 쓴다.")]
        [SerializeField] private bool startLocked;

        [Header("열림 방식")]
        [SerializeField] private OpenMode mode = OpenMode.Rotate;

        [Tooltip("실제로 움직일 문 오브젝트. 비워 두면 이 오브젝트 자신을 움직인다.")]
        [SerializeField] private Transform doorPivot;

        [ShowIfEnum(nameof(mode), (int)OpenMode.Animator)]
        [SerializeField] private Animator animator;

        [ShowIfEnum(nameof(mode), (int)OpenMode.Animator)]
        [Tooltip("열림 상태를 나타내는 Animator bool 파라미터 이름.")]
        [SerializeField] private string openBoolName = "isOpen";

        [ShowIfEnum(nameof(mode), (int)OpenMode.Rotate)]
        [Tooltip("닫힘 상태 기준으로 더할 로컬 회전(도). 양문형은 한쪽에 Y -90, 반대쪽에 Y 90을 준다.")]
        [SerializeField] private Vector3 openRotation = new Vector3(0f, 90f, 0f);

        [ShowIfEnum(nameof(mode), (int)OpenMode.Slide)]
        [Tooltip("닫힘 위치 기준으로 더할 로컬 이동량.")]
        [SerializeField] private Vector3 openOffset = new Vector3(2f, 0f, 0f);

        [ShowIfEnum(nameof(mode), (int)OpenMode.Rotate, (int)OpenMode.Slide)]
        [Tooltip("완전히 열리거나 닫히는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.01f)] private float moveDuration = 0.6f;

        [Header("연출 연결")]
        [Tooltip("열리기 시작할 때 1회 호출. 사운드·이펙트를 인스펙터에서 연결한다.")]
        [SerializeField] private UnityEvent onOpen = new UnityEvent();

        [Tooltip("닫히기 시작할 때 1회 호출.")]
        [SerializeField] private UnityEvent onClose = new UnityEvent();

        private BoxCollider trigger;

        // 범위 안에 있는 콜라이더들. 카운터 대신 집합으로 관리하는 이유:
        // 플레이어 한 명이 CharacterController + 자식 콜라이더로 여러 번 Enter를 일으킬 수 있고,
        // 대상이 파괴/비활성화되면 Exit이 오지 않아 카운터는 영영 0으로 돌아오지 못한다.
        private readonly HashSet<Collider> occupants = new HashSet<Collider>();

        // 만료된 항목을 지우는 동안 순회 대상이 바뀌지 않도록 쓰는 임시 버퍼(런타임 할당 회피).
        private readonly List<Collider> pruneBuffer = new List<Collider>();

        // 문의 현재 목표 상태. 실제 Transform은 openAmount가 따라간다.
        private bool isOpen;

        // 0 = 완전히 닫힘, 1 = 완전히 열림. Rotate/Slide 모드에서만 쓴다.
        private float openAmount;

        // 마지막 대상이 나간 시각. closeDelay 경과 후 닫는다. 음수면 대기 중이 아니다.
        private float closeAt = -1f;

        private Vector3 closedLocalPosition;
        private Quaternion closedLocalRotation;
        private int openBoolHash;

        // 잠금 상태. 감지 자체는 계속 하되(점유자는 기록해 둔다) 여는 것만 막는다.
        // 그래야 Unlock 시점에 이미 문 앞에 서 있던 플레이어에게 바로 열어줄 수 있다.
        private bool locked;

        /// <summary>문이 열린(또는 열리는 중인) 상태인지.</summary>
        public bool IsOpen => isOpen;

        /// <summary>잠겨 있는지. 잠긴 동안에는 근접해도 열리지 않는다.</summary>
        public bool IsLocked => locked;

        private void Awake()
        {
            locked = startLocked;
            ProjectS.Debugging.DevLog.Log($"[AutoDoor:{name}] Awake — startLocked={startLocked} (true면 근접해도 안 열림, Unlock 받아야 열림)", this);

            trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;   // 물리 충돌이 아니라 감지 전용
            trigger.size = detectSize;

            if (doorPivot == null) doorPivot = transform;

            // 닫힘 자세를 기준으로 삼는다. 씬에 배치된 초기 상태가 곧 '닫힘'이다.
            closedLocalPosition = doorPivot.localPosition;
            closedLocalRotation = doorPivot.localRotation;

            openBoolHash = Animator.StringToHash(openBoolName);
        }

        // 문이 비활성화될 때는 OnTriggerExit이 오지 않는다. 그대로 두면 다시 켰을 때
        // '범위 안에 아무도 없는데 열린 채'로 시작한다.
        private void OnDisable()
        {
            occupants.Clear();
            closeAt = -1f;
            SetOpen(false);

            // 다음 활성화 때 어중간한 각도에서 시작하지 않도록 닫힘 자세로 즉시 되돌린다.
            openAmount = 0f;
            ApplyPose();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsDetected(other)) return;

            occupants.Add(other);
            closeAt = -1f;      // 닫기 예약 취소

            ProjectS.Debugging.DevLog.Log($"[AutoDoor:{name}] 감지 진입 '{other.name}' — locked={locked} → {(locked ? "잠겨서 안 열림" : "열림")}", this);
            if (!locked) SetOpen(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsDetected(other)) return;

            occupants.Remove(other);
            if (!closeOnExit) return;

            if (!HasOccupant()) closeAt = Time.time + closeDelay;
        }

        private void Update()
        {
            // 대상이 파괴되거나 비활성화되면 Exit이 오지 않으므로, 닫힘 판정은 여기서도 확인한다.
            if (closeOnExit && isOpen && closeAt < 0f && !HasOccupant())
                closeAt = Time.time + closeDelay;

            if (closeAt >= 0f && Time.time >= closeAt)
            {
                closeAt = -1f;
                SetOpen(false);
            }

            if (mode != OpenMode.Rotate && mode != OpenMode.Slide) return;

            float target = isOpen ? 1f : 0f;
            if (Mathf.Approximately(openAmount, target)) return;

            openAmount = Mathf.MoveTowards(openAmount, target, Time.deltaTime / moveDuration);
            ApplyPose();
        }

        /// <summary>
        /// 잠금을 푼다. 이 시점에 이미 감지 범위 안에 플레이어가 있으면 곧바로 열린다
        /// (문 앞에 선 채로 조건을 만족했을 때 한 발 물러났다 다시 와야 하는 일을 막는다).
        /// 튜토리얼 과제 성공 이벤트 등에 인스펙터로 연결해 쓴다.
        /// </summary>
        public void Unlock()
        {
            ProjectS.Debugging.DevLog.Log($"[AutoDoor:{name}] Unlock() 호출 — 현재 locked={locked}, 범위내플레이어={HasOccupant()}", this);
            if (!locked) return;

            locked = false;

            if (!HasOccupant()) return;

            closeAt = -1f;
            SetOpen(true);
        }

        /// <summary>다시 잠근다. 열려 있었다면 닫는다.</summary>
        public void Lock()
        {
            ProjectS.Debugging.DevLog.Log($"[AutoDoor:{name}] Lock() 호출 — 문 닫고 잠금", this);
            locked = true;
            closeAt = -1f;
            SetOpen(false);
        }

        /// <summary>
        /// 코드나 인스펙터(스위치·퀘스트 조건 등)에서 문을 강제로 여닫는다.
        /// 감지 범위와 무관하게 상태만 바꾸므로, 열어 둔 상태에서 대상이 나가면 다시 닫힌다.
        /// </summary>
        /// <param name="open">true면 열고 false면 닫는다.</param>
        public void SetOpen(bool open)
        {
            if (isOpen == open) return;

            isOpen = open;

            if (mode == OpenMode.Animator && animator != null)
                animator.SetBool(openBoolHash, open);

            if (open) onOpen?.Invoke();
            else onClose?.Invoke();
        }

        // 레이어 판정. LayerMask는 비트 집합이라 대상 레이어의 비트가 켜져 있는지로 본다.
        private bool IsDetected(Collider other)
        {
            return (detectLayers.value & (1 << other.gameObject.layer)) != 0;
        }

        // 파괴되었거나 비활성화된 콜라이더를 걸러 내고 남은 대상이 있는지 본다.
        private bool HasOccupant()
        {
            if (occupants.Count == 0) return false;

            pruneBuffer.Clear();
            foreach (Collider c in occupants)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                    pruneBuffer.Add(c);
            }

            for (int i = 0; i < pruneBuffer.Count; i++)
                occupants.Remove(pruneBuffer[i]);

            return occupants.Count > 0;
        }

        private void ApplyPose()
        {
            if (doorPivot == null) return;

            if (mode == OpenMode.Rotate)
            {
                // 닫힘 자세에 열림 회전을 openAmount만큼 섞는다. Slerp라 각도가 커도 자연스럽다.
                Quaternion opened = closedLocalRotation * Quaternion.Euler(openRotation);
                doorPivot.localRotation = Quaternion.Slerp(closedLocalRotation, opened, openAmount);
            }
            else if (mode == OpenMode.Slide)
            {
                doorPivot.localPosition = closedLocalPosition + openOffset * openAmount;
            }
        }

#if UNITY_EDITOR
        // 인스펙터에서 크기를 만지면 즉시 콜라이더에 반영해 씬 뷰에서 범위를 확인할 수 있게 한다.
        private void OnValidate()
        {
            if (!TryGetComponent(out BoxCollider c)) return;

            c.isTrigger = true;
            c.size = detectSize;
        }

        private void OnDrawGizmos()
        {
            Vector3 center = TryGetComponent(out BoxCollider c) ? c.center : Vector3.zero;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Application.isPlaying && isOpen
                ? new Color(0.3f, 1f, 0.3f, 0.8f)
                : new Color(1f, 0.6f, 0.1f, 0.5f);
            Gizmos.DrawWireCube(center, detectSize);
        }
#endif
    }
}
