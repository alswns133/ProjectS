using ProjectS.Enemies;
using UnityEngine;

namespace ProjectS.Players
{
    /// <summary>
    /// 플레이어와 몬스터의 '수평 전용' 소프트 충돌. 물리 콜라이더로 막으면 CharacterController가
    /// 밀집 시 위로 밀려 올라타거나 끼이는 고질 문제가 생겨, 콜라이더 대신 매 프레임 주변 몬스터를 찾아
    /// 겹친 만큼 플레이어를 수평(XZ)으로만 밀어낸다. 수직 성분이 없어 올라타기·끼임이 원천적으로 없고,
    /// 몬스터 머리 위로 뛰어넘는 것은 그대로 허용된다(발이 머리보다 위면 밀어내지 않음).
    /// 기존 PlayerMovement 로직은 건드리지 않고 LateUpdate에서 CharacterController.Move 보정만 더한다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerEnemySeparation : MonoBehaviour
    {
        [Header("감지")]
        // 이 반경 안의 몬스터만 밀어내기 대상으로 본다. 몬스터 몸통 + 여유로 잡는다.
        [SerializeField, Min(0f)] private float detectRadius = 2f;
        // 몬스터가 있는 레이어(Enemy). 인스펙터에서 Enemy 레이어만 체크한다.
        [SerializeField] private LayerMask enemyMask;

        [Header("몬스터 몸통")]
        // 밀어내기 기준 몸통 반경. 플레이어 반경과 합친 거리보다 가까우면 밀어낸다.
        [SerializeField, Min(0f)] private float enemyBodyRadius = 0.5f;
        // 몬스터 키(발밑 기준). 플레이어 발이 이 높이보다 위면 '뛰어넘는 중'으로 보고 밀지 않는다.
        [SerializeField, Min(0f)] private float enemyBodyHeight = 1.8f;

        private CharacterController controller;

        private PlayerCombat combat;

        public Player player;

        // 매 프레임 할당을 없애는 NonAlloc 버퍼. 동시에 겹치는 몬스터 상한(넘치면 나머지는 다음 프레임에).
        private readonly Collider[] buffer = new Collider[16];

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
            player = GetComponent<Player>();
        }

        // 플레이어 이동과 몬스터(NavMeshAgent) 이동이 모두 끝난 뒤 겹침을 정리한다.
        private void LateUpdate()
        {
            // 구르기·공중대시·각성기 시전 중에는 몬스터를 통과한다(소프트 분리 정지).
            if (player != null && player.PassThroughEnemies) return;

            Vector3 center = transform.position + controller.center;
            int count = Physics.OverlapSphereNonAlloc(
                center, detectRadius, buffer, enemyMask, QueryTriggerInteraction.Collide);
            if (count == 0) return;

            float playerRadius = controller.radius;
            // 플레이어 발밑 y = 캡슐 중심 - 높이/2.
            float playerBottom = transform.position.y + controller.center.y - controller.height * 0.5f;
            float minDist = playerRadius + enemyBodyRadius;

            Vector3 push = Vector3.zero;

            for (int i = 0; i < count; i++)
            {
                Enemy enemy = buffer[i].GetComponentInParent<Enemy>();
                if (enemy == null) continue;
                if (enemy.Stats.IsDead) continue;   // 죽어가는 몬스터는 무시

                Vector3 enemyPos = enemy.transform.position;

                // 뛰어넘기 허용: 발이 몬스터 머리보다 위면 밀지 않는다 → 위로 통과 자연스러움.
                if (playerBottom >= enemyPos.y + enemyBodyHeight) continue;

                Vector3 delta = transform.position - enemyPos;
                delta.y = 0f;
                float dist = delta.magnitude;
                if (dist > 0.0001f && dist < minDist)
                    push += delta / dist * (minDist - dist);   // 겹친 깊이만큼 수평으로
            }

            if (push.sqrMagnitude > 0.0000001f)
                controller.Move(new Vector3(push.x, 0f, push.z));   // ★ 수평만, y=0
        }
    }
}
