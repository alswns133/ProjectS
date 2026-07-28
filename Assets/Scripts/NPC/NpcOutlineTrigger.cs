using System;
using UnityEngine;
using UnityEngine.Events;
using ProjectS.Players;

namespace ProjectS.NPCs
{
    /// <summary>
    /// 플레이어가 일정 거리 안에 들어오면 켜지고 벗어나면 꺼지는 근접 감지기.
    /// NPC 윤곽선(아웃라인) 표시에 쓰지만, 무엇을 켤지는 이 클래스가 모른다.
    ///
    /// 매 프레임 거리 계산 대신 트리거 콜라이더에 판정을 맡긴다 → NPC가 늘어도 Update 비용이 0이다.
    /// (Update에서 Vector3.Distance를 도는 방식은 NPC 수에 비례해 비싸진다.)
    ///
    /// <b>연출을 UnityEvent로 내보내는 이유</b>: 윤곽선 에셋(HighlightPlus)은
    /// ExternalAssets 아래에 있고 .gitignore로 깃에서 제외된다. 여기서 직접 참조하면
    /// 에셋이 없는 환경(CI, 에셋 미보유 팀원)에서 컴파일이 깨진다.
    /// 인스펙터에서 HighlightEffect.SetHighlighted를 연결하면 코드 의존 없이 같은 결과가 된다.
    /// 나중에 Assets/Shader/ToonOutline 같은 다른 방식으로 갈아탈 때도 코드를 안 고쳐도 된다.
    ///
    /// 붙이는 위치: NPC 루트가 아니라 <b>전용 자식 오브젝트</b>를 권장한다.
    /// 감지 범위만을 위한 콜라이더라 NPC 본체와 레이어를 분리해야 다른 물리 판정에 끼어들지 않는다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class NpcOutlineTrigger : MonoBehaviour
    {
        /// <summary>
        /// 인스펙터에 노출하기 위한 구체 이벤트 타입.
        /// UnityEvent&lt;T&gt;는 제네릭 그대로는 직렬화되지 않아 상속받은 클래스가 필요하다.
        /// </summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("감지 시 실행")]
        // 인스펙터에서 HighlightEffect의 SetHighlighted(bool)를 Dynamic bool로 연결한다.
        // 진입 시 true, 이탈 시 false가 그대로 전달된다.
        [SerializeField] private BoolEvent onPlayerNearChanged = new BoolEvent();

        [Header("감지 범위")]
        // 실제 판정은 SphereCollider가 하고, 이 값은 그 반지름을 인스펙터 한곳에서 관리하기 위한 것이다.
        // 주의: 콜라이더 반지름은 Transform 스케일의 영향을 받는다(3축 중 최댓값 기준).
        [SerializeField, Min(0f)] private float radius = 3f;

        private SphereCollider trigger;

        // 플레이어가 범위 안에 있는지. 기즈모 색에 쓰고, 상호작용(대화 시작 등)을
        // 붙일 때 그대로 게이트로 재사용할 수 있다.
        private bool isPlayerInside;

        /// <summary>플레이어가 감지 범위 안에 있는지 여부.</summary>
        public bool IsPlayerInside => isPlayerInside;

        /// <summary>
        /// 근접 진입(true)/이탈(false) 시 발행되는 C# 이벤트. 인스펙터 연결이 필요 없는
        /// 코드 구독용이다(예: QuestGiver가 Awake에서 이 트리거를 찾아 자동 구독한다).
        /// onPlayerNearChanged(UnityEvent)와 같은 시점에 발행되며, 서로 독립이라 둘 다 써도 된다.
        /// </summary>
        public event Action<bool> PlayerNearChanged;

        private void Awake()
        {
            trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;   // 물리 충돌이 아니라 감지 전용
            trigger.radius = radius;

            // 시작 상태는 항상 꺼짐. 프리팹에 켜진 채로 저장돼 있어도 여기서 정리된다.
            Notify(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other)) return;

            Notify(true);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other)) return;

            Notify(false);
        }

        // NPC가 비활성화되거나 씬이 정리될 때는 OnTriggerExit이 오지 않는다.
        // 그대로 두면 다시 켰을 때 윤곽선이 남은 채로 시작한다.
        private void OnDisable()
        {
            Notify(false);
        }

        // 플레이어 판정은 태그가 아니라 컴포넌트로 한다. 태그 문자열 오타는 컴파일러가 못 잡는다.
        // 무기·히트박스처럼 자식에 달린 콜라이더가 들어와도 잡히도록 부모까지 훑는다
        // (진입·이탈 순간에만 호출되므로 GetComponentInParent 비용은 문제되지 않는다).
        private static bool IsPlayer(Collider other)
        {
            return other.GetComponentInParent<Player>() != null;
        }

        private void Notify(bool value)
        {
            isPlayerInside = value;
            onPlayerNearChanged?.Invoke(value);   // 인스펙터 연결용(윤곽선 등)
            PlayerNearChanged?.Invoke(value);      // 코드 구독용(QuestGiver 등)
        }

    #if UNITY_EDITOR
        // 인스펙터에서 radius를 만지는 즉시 콜라이더에 반영해, 씬 뷰에서 범위를 바로 확인할 수 있게 한다.
        private void OnValidate()
        {
            if (!TryGetComponent(out SphereCollider c)) return;

            c.isTrigger = true;
            c.radius = radius;
        }

        // 항상 보이는 옅은 윤곽. 씬에 NPC를 여러 명 배치할 때 선택하지 않고도
        // 범위가 겹치는지(= 윤곽선이 동시에 여러 개 켜지는 구간인지) 한눈에 보인다.
        private void OnDrawGizmos()
        {
            DrawRange(new Color(0f, 1f, 1f, 0.25f), false);
        }

        // 선택 시에는 진하게 + 반투명 채움으로 강조한다.
        // 플레이 중이라면 플레이어가 범위 안에 있는지에 따라 색이 바뀐다(초록 = 감지 중).
        private void OnDrawGizmosSelected()
        {
            Color color = Application.isPlaying && isPlayerInside
                ? new Color(0.3f, 1f, 0.3f, 1f)
                : Color.cyan;

            DrawRange(color, true);
        }

        private void DrawRange(Color color, bool filled)
        {
            // 콜라이더의 center 오프셋과 스케일을 그대로 반영해야 실제 판정 범위와 일치한다.
            // 에디트 모드에서는 Awake를 거치지 않아 trigger 캐시가 비어 있으므로 매번 조회한다.
            Vector3 center = transform.position;
            if (TryGetComponent(out SphereCollider c))
                center = transform.TransformPoint(c.center);

            // SphereCollider의 실효 반지름은 스케일 3축 중 가장 큰 값을 따른다(Unity 물리 규칙).
            Vector3 scale = transform.lossyScale;
            float worldRadius = radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            Gizmos.color = color;
            Gizmos.DrawWireSphere(center, worldRadius);

            if (!filled) return;

            Gizmos.color = new Color(color.r, color.g, color.b, 0.1f);
            Gizmos.DrawSphere(center, worldRadius);
        }
    #endif
    }
}
