using UnityEngine;
using ProjectS.Core;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 특정 던전으로 들어가는 게이트의 위치 표식. 목표가 다른 씬(던전)에 있을 때 나침반이 이 게이트를
    /// 조준하게 하려고, 자신이 여는 <see cref="dungeonId"/>로 <see cref="QuestWaypointRegistry"/>에 자기 등록한다.
    ///
    /// 배치: 마을의 던전 입구 오브젝트(던전 선택 팝업 트리거 근처)에 붙이고 dungeonId를 지정한다.
    /// 던전에서 마을로 돌아가는 '출구' 게이트는 dungeonId를 마을 값(0)으로 둔다 — 그러면 던전에서 완료한
    /// 퀘스트의 반납 NPC(마을에 있음)를 향해 나침반이 이 출구를 가리킨다(씬 밖 목표의 breadcrumb).
    /// </summary>
    public class QuestGate : MonoBehaviour, IQuestWaypoint
    {
        [Tooltip("이 게이트가 여는 던전 ID. 마을로 나가는 출구 게이트는 0.")]
        [SerializeField] private int dungeonId;

        /// <inheritdoc/>
        public QuestWaypointKind Kind => QuestWaypointKind.Gate;

        /// <inheritdoc/>
        public int Key => dungeonId;

        /// <inheritdoc/>
        public Vector3 Position => transform.position;

        /// <inheritdoc/>
        public bool IsActive => isActiveAndEnabled;

        private void OnEnable() => QuestWaypointRegistry.Register(this);

        private void OnDisable() => QuestWaypointRegistry.Unregister(this);
    }
}
