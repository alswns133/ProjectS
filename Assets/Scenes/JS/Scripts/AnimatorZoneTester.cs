using UnityEngine;
using ProjectS.Combat;
using ProjectS.Movement;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 플레이어 발밑의 <see cref="AnimatorZone"/>을 감지해 애니메이터 컨트롤러를 바꿔주는 테스트 하네스.
    /// 한 씬에 던전용·마을용 발판을 나란히 두고 걸어서 오가며 두 애니메이터를 즉시 비교하기 위한 것이다.
    ///
    /// ★ 테스트 전용. 실제 빌드에서는 마을/던전이 씬으로 분리되므로 이 컴포넌트와
    ///   <see cref="AnimatorZone"/>을 함께 제거한다.
    ///
    /// 주의: 컨트롤러 대입은 애니메이터를 재초기화해 현재 State와 파라미터를 기본값으로 되돌린다.
    /// 그래서 "구역이 실제로 바뀐 프레임"에만 교체한다. 매 프레임 대입하면 계속 리빌드되어
    /// 캐릭터가 영원히 기본 State(Idle)에서 벗어나지 못한다.
    /// 리셋된 파라미터는 FreeMoveController가 매 프레임 다시 써주므로 다음 프레임에 복구된다.
    /// </summary>
    public class AnimatorZoneTester : MonoBehaviour
    {
        [Header("참조 (비우면 같은 오브젝트에서 자동 탐색)")]
        [SerializeField] private Animator animator;
        [SerializeField] private FreeCombatController combatController;
        [SerializeField] private FreeMoveController moveController;

        [Header("발밑 감지")]
        [Tooltip("발판 콜라이더가 속한 레이어. FreeMoveController의 groundLayer와 같은 값(Floor)을 넣는다.")]
        [SerializeField] private LayerMask groundLayer;

        [Tooltip("레이 시작 높이(발 기준 위쪽 오프셋). 캡슐 안에서 시작해야 바닥을 놓치지 않는다.")]
        [SerializeField] private float probeUpOffset = 1f;

        [Tooltip("아래로 훑는 거리. 점프 중에는 닿지 않아도 되며, 그때는 직전 구역을 유지한다.")]
        [SerializeField] private float probeDistance = 5f;

        [Header("디버그")]
        [SerializeField] private bool logZoneChange = true;

        // 마지막으로 적용한 구역. 같은 구역이면 교체를 건너뛰는 판정 기준이다.
        private AnimatorZone currentZone;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (combatController == null) combatController = GetComponent<FreeCombatController>();
            if (moveController == null) moveController = GetComponent<FreeMoveController>();
        }

        private void Update()
        {
            AnimatorZone zone = ProbeZone();

            // 공중이거나 구역 밖이면 null이 온다. 이때는 직전 구역을 그대로 유지한다
            // (점프 중 컨트롤러가 튀는 것을 막기 위함).
            if (zone == null || zone == currentZone) return;

            Apply(zone);
        }

        /// <summary>발밑으로 레이를 쏴 현재 서 있는 구역을 찾는다. 못 찾으면 null.</summary>
        private AnimatorZone ProbeZone()
        {
            Vector3 origin = transform.position + Vector3.up * probeUpOffset;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeDistance, groundLayer))
                return null;

            // 콜라이더가 자식에 있어도 잡히도록 부모까지 훑는다.
            return hit.collider.GetComponentInParent<AnimatorZone>();
        }

        /// <summary>구역 설정을 실제로 적용한다. 구역이 바뀐 프레임에만 호출된다.</summary>
        private void Apply(AnimatorZone zone)
        {
            currentZone = zone;

            if (zone.Controller != null && animator != null)
                animator.runtimeAnimatorController = zone.Controller;

            // 마을 구역에서 전투가 살아 있으면, 재생할 State가 없는 채로 시전 상태만 남아
            // 이후 입력이 먹지 않을 수 있다. 구역 규칙에 맞춰 함께 켜고 끈다.
            if (combatController != null)
                combatController.enabled = zone.CombatEnabled;

            // ★ 잠금 해제가 반드시 필요하다. 공격 중(ActionLocked=true)에 전투 조율자를 끄면
            //   매 프레임 잠금을 갱신하던 주체가 사라져 true로 굳고, 캐릭터가 영구히 못 움직인다.
            if (!zone.CombatEnabled && moveController != null)
            {
                moveController.ActionLocked = false;
                moveController.JumpBlocked = false;
            }

            if (logZoneChange)
            {
                string ctrl = zone.Controller != null ? zone.Controller.name : "(없음)";
                Debug.Log($"[AnimatorZoneTester] 구역 진입: {zone.Label} → 컨트롤러 {ctrl}, 전투 {(zone.CombatEnabled ? "ON" : "OFF")}", this);
            }
        }
    }
}
