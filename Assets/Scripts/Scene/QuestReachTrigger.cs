using UnityEngine;
using UnityEngine.Events;
using ProjectS.Core;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 플레이어가 이 영역에 들어오면 "여기 도착했다"를 퀘스트에 알리는 투명 트리거.
    /// <c>ObjectiveType.Reach</c> 목표를 가진 퀘스트가 진행 중이면 그 목표를 1 올린다.
    ///
    /// "특정 장소로 가라" 형태의 퀘스트 전부에 쓴다 — 던전 입구 안내, 마을 순찰, 던전 안 특정 구역 도달 등.
    /// 어느 씬에 놓아도 동작한다(QuestManager가 씬을 넘어 유지되므로).
    ///
    /// <b>퀘스트를 받기 전에 지나가면 아무 일도 일어나지 않는다.</b> 진행 중인 퀘스트만 검사하기 때문이다.
    /// 그래서 수락 전에 이미 그 자리를 밟았던 플레이어는 다시 와야 한다 — 영역을 넉넉히 크게 잡아
    /// "지나가다 저절로 찍히는" 일이 없도록, 반대로 목적지라면 놓치지 않도록 배치한다.
    ///
    /// 도착 시 문 개폐·연출을 함께 굴리려면 <see cref="onReached"/>에 연결한다(<see cref="QuestGateDoor"/> 참조).
    ///
    /// <b>나침반 웨이포인트로도 자기 등록한다</b>(<see cref="registerAsWaypoint"/>). 목적지와 조준점은 같은 자리이므로,
    /// <see cref="QuestObjectiveWaypoint"/>를 따로 붙여 같은 ID를 두 번 적는 것보다 어긋날 여지가 없다.
    /// 나침반이 가리키지 않아야 하는 숨은 지점이라면 끈다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class QuestReachTrigger : MonoBehaviour, IQuestWaypoint
    {
        [Header("도착 지점")]
        [Tooltip("이 지점의 ID. 퀘스트 목표(ObjectiveTargets.TargetId)와 같은 값을 넣는다. " +
                 "규칙은 [지역2][순번2] — 마을 101·102…, 던전1 지역 201… (docs/ID_NUMBERING.md). " +
                 "Reach 목표는 '도달할 레벨'에도 쓰이므로 레벨 최대치보다 큰 100 이상을 쓴다.")]
        [SerializeField, Min(1)] private int pointId = 101;

        [Header("발동")]
        [Tooltip("켜면 한 번 알린 뒤로는 다시 알리지 않는다. 끄면 들어올 때마다 알린다" +
                 "(같은 목표를 여러 번 밟아야 하는 퀘스트용).")]
        [SerializeField] private bool reportOnce = true;

        [Tooltip("도착한 순간 호출된다. 문 열기·연출·사운드를 연결한다. " +
                 "퀘스트와 무관하게 호출되므로(퀘스트가 없어도 발행) 연출 전용으로 써도 된다.")]
        [SerializeField] private UnityEvent onReached;

        [Header("네비게이션")]
        [Tooltip("켜면 나침반이 이 지점을 조준한다. 끄면 등록하지 않아 나침반이 가리키지 않는다" +
                 "(플레이어가 찾아야 하는 숨은 지점 등).")]
        [SerializeField] private bool registerAsWaypoint = true;

        [Header("기즈모")]
        [Tooltip("씬 뷰에 영역을 그릴 색. 투명 오브젝트라 이게 없으면 배치할 때 범위가 보이지 않는다.")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0.85f, 0.2f, 0.25f);

        private BoxCollider box;

        // 이미 알렸는지. reportOnce일 때 재발동을 막는다.
        private bool reported;

        /// <summary>이 지점의 ID. 문 같은 다른 컴포넌트가 같은 목표를 조회할 때 쓴다.</summary>
        public int PointId => pointId;

        /// <summary>이번 씬에서 이미 도착 보고를 했는지.</summary>
        public bool HasReported => reported;

        /// <inheritdoc/>
        public QuestWaypointKind Kind => QuestWaypointKind.Objective;

        /// <inheritdoc/>
        public int Key => pointId;

        // 조준점은 Transform이 아니라 콜라이더 중심이다. 영역을 Transform으로 옮기든 콜라이더 Center로
        // 옮기든 항상 실제 상자가 있는 자리를 가리키게 하기 위함이다(Center만 옮기면 나침반이 원점을 가리킨다).
        /// <inheritdoc/>
        public Vector3 Position
        {
            get
            {
                BoxCollider target = box != null ? box : GetComponent<BoxCollider>();
                return target != null ? transform.TransformPoint(target.center) : transform.position;
            }
        }

        /// <inheritdoc/>
        public bool IsActive => isActiveAndEnabled;

        // 씬에 나타날 때 나침반에 등록하고 사라질 때 해제한다(DungeonGate와 같은 방침).
        // 빠뜨리면 파괴된 지점을 나침반이 계속 조준한다.
        private void OnEnable()
        {
            if (registerAsWaypoint) QuestWaypointRegistry.Register(this);
        }

        private void OnDisable()
        {
            if (registerAsWaypoint) QuestWaypointRegistry.Unregister(this);
        }

        private void Awake()
        {
            box = GetComponent<BoxCollider>();

            // 트리거가 아니면 플레이어가 벽에 부딪히듯 막혀 버린다. 인스펙터에서 꺼 두더라도 여기서 강제한다.
            box.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (reportOnce && reported) return;
            if (other.GetComponentInParent<Player>() == null) return;   // 몬스터·투사체는 무시

            // 진행 중인 Reach 목표를 실제로 올렸을 때만 "한 번 다 썼다"로 잠근다. 퀘스트를 받기 전에
            // 스쳐 지나간 경우까지 여기서 잠가 버리면, 나중에 정식으로 퀘스트를 받아도 트리거가 다시는
            // 안 울려서 그 목표를 영영 못 채운다(2026-09-22 확인된 버그) — reportOnce의 취지("문 열림
            // 연출 중복 방지")는 유효한 진행이 있을 때만 적용한다.
            bool advanced = QuestManager.Instance != null && QuestManager.Instance.ReportReach(pointId);
            if (advanced) reported = true;

            onReached?.Invoke();
        }

        // 투명한 영역이라 기즈모가 없으면 씬 뷰에서 위치도 크기도 확인할 수 없다.
        // 선택하지 않아도 보이도록 OnDrawGizmos에 그린다(배치 중에는 늘 보여야 한다).
        private void OnDrawGizmos()
        {
            BoxCollider target = box != null ? box : GetComponent<BoxCollider>();
            if (target == null) return;

            Matrix4x4 saved = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = gizmoColor;
            Gizmos.DrawCube(target.center, target.size);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Gizmos.DrawWireCube(target.center, target.size);

            Gizmos.matrix = saved;
        }
    }
}
