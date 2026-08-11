using UnityEngine;
using ProjectS.Core;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 씬에 배치하는 퀘스트 목표 지점(주로 '위치 도달' Reach 목표). 목표 대상 ID(<see cref="targetId"/>)로
    /// <see cref="QuestWaypointRegistry"/>에 자기 등록해, 나침반이 현재 씬에서 이 지점을 직접 조준하게 한다.
    ///
    /// Reach 목표에 이 웨이포인트가 있으면 '위치 도달'로 보고 조준하고, 없으면 '레벨 도달'로 보고 나침반을
    /// 숨긴다 — 즉 이 컴포넌트의 배치 여부 자체가 판별자다(별도 데이터 플래그를 두지 않기 위함).
    /// </summary>
    public class QuestObjectiveWaypoint : MonoBehaviour, IQuestWaypoint
    {
        [Tooltip("이 지점이 만족시키는 목표 대상 ID(ObjectiveTarget.TargetId). Reach 위치 도달의 지역/지점 ID.")]
        [SerializeField] private int targetId;

        /// <inheritdoc/>
        public QuestWaypointKind Kind => QuestWaypointKind.Objective;

        /// <inheritdoc/>
        public int Key => targetId;

        /// <inheritdoc/>
        public Vector3 Position => transform.position;

        /// <inheritdoc/>
        public bool IsActive => isActiveAndEnabled;

        private void OnEnable() => QuestWaypointRegistry.Register(this);

        private void OnDisable() => QuestWaypointRegistry.Unregister(this);
    }
}
