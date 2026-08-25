using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Players;
using System;
using UnityEngine;
using UnityEngine.Events;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 플레이어가 이 트리거 안에 들어오고 나가는 것만 알리는 범용 감지기. 무엇을 할지는 모른다.
    /// 튜토리얼의 '시작 지점'과 '목적지' 양쪽에 같은 것을 쓴다.
    ///
    /// NpcOutlineTrigger와 하는 일이 비슷하지만 따로 둔다. 그쪽은 SphereCollider 전용이고
    /// (반지름을 인스펙터 값으로 강제) NPC 윤곽선이라는 의미가 이름에 박혀 있다. 목적지 구간은
    /// 보통 길목을 가로지르는 납작한 박스라 모양 제약이 없어야 한다 → 여기서는 어떤 Collider든 받는다.
    ///
    /// 붙이는 곳: 빈 오브젝트에 BoxCollider(또는 SphereCollider)를 붙이고 이 스크립트를 추가한다.
    /// isTrigger는 Awake에서 강제로 켜므로 체크를 깜빡해도 동작한다.
    /// </summary>
    public class PlayerZoneTrigger : MonoBehaviour
    {
        /// <summary>인스펙터에 노출하기 위한 구체 이벤트 타입(UnityEvent&lt;T&gt;는 제네릭 그대로는 직렬화되지 않는다).</summary>
        [Serializable]
        public class BoolEvent : UnityEvent<bool> { }

        [Header("감지 시 실행")]
        [Tooltip("진입 시 true, 이탈 시 false가 전달된다. 켜고 끄는 대상(윤곽선 등)을 연결할 때 쓴다.")]
        [SerializeField] private BoolEvent onPlayerInsideChanged = new BoolEvent();

        // 아래 둘은 인자가 없어서 "매개변수 없는 메서드"를 인스펙터에서 바로 물릴 수 있다.
        // onPlayerInsideChanged에 그런 메서드를 물리면 진입할 때와 나갈 때 '둘 다' 호출돼
        // "들어오면 문을 잠근다" 같은 한쪽 방향 동작을 표현할 수 없다.
        [Tooltip("진입 순간 1회 호출. 예: 방에 들어서면 문을 영구히 잠근다(AutoDoor.Lock).")]
        [SerializeField] private UnityEvent onPlayerEntered = new UnityEvent();

        [Tooltip("이탈 순간 1회 호출.")]
        [SerializeField] private UnityEvent onPlayerExited = new UnityEvent();

        [Header("기즈모")]
        [SerializeField] private Color gizmoColor = new Color(0.2f, 0.9f, 1f, 0.35f);

        private bool isPlayerInside;

        /// <summary>플레이어가 지금 범위 안에 있는지.</summary>
        public bool IsPlayerInside => isPlayerInside;

        /// <summary>
        /// 진입(true)/이탈(false) 시 발행되는 코드 구독용 이벤트.
        /// onPlayerInsideChanged(UnityEvent)와 같은 시점에 발행되며 서로 독립이라 둘 다 써도 된다.
        /// </summary>
        public event Action<bool> PlayerInsideChanged;

        private void Awake()
        {
            // 콜라이더가 없으면 아무 일도 일어나지 않는데 원인을 찾기 어렵다. 여기서 바로 알린다.
            if (!TryGetComponent(out Collider zone))
            {
                Debug.LogWarning($"[PlayerZoneTrigger] {name}: Collider가 없어 감지가 동작하지 않습니다. " +
                                 "BoxCollider 등을 추가하세요.", this);
                return;
            }

            zone.isTrigger = true;   // 물리 충돌이 아니라 감지 전용
        }

        // 오브젝트가 꺼지거나 씬이 정리될 때는 OnTriggerExit이 오지 않는다.
        // 그대로 두면 다시 켰을 때 '안에 있다'는 상태로 시작한다.
        private void OnDisable()
        {
            Notify(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsPlayer(other) || isPlayerInside) return;

            Notify(true);
            //Boss boss = FindAnyObjectByType<Boss>();
            //BossEvents.FireBossAppeared(boss);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsPlayer(other) || !isPlayerInside) return;

            Notify(false);
        }

        // 플레이어 판정은 태그가 아니라 컴포넌트로 한다(오타를 컴파일러가 못 잡는 태그 회피).
        // 무기·히트박스처럼 자식 콜라이더가 들어와도 잡히도록 부모까지 훑는다.
        private static bool IsPlayer(Collider other) => other.GetComponentInParent<Player>() != null;

        private void Notify(bool value)
        {
            if (isPlayerInside == value) return;

            isPlayerInside = value;

            onPlayerInsideChanged?.Invoke(value);

            if (value) onPlayerEntered?.Invoke();
            else onPlayerExited?.Invoke();

            PlayerInsideChanged?.Invoke(value);
        }

#if UNITY_EDITOR
        // 씬 뷰에서 감지 범위를 눈으로 확인할 수 있게 실제 콜라이더 모양을 그대로 그린다.
        private void OnDrawGizmos()
        {
            Gizmos.color = Application.isPlaying && isPlayerInside
                ? new Color(0.3f, 1f, 0.3f, 0.5f)
                : gizmoColor;

            Gizmos.matrix = transform.localToWorldMatrix;

            if (TryGetComponent(out BoxCollider box))
            {
                Gizmos.DrawWireCube(box.center, box.size);
                return;
            }

            if (TryGetComponent(out SphereCollider sphere))
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
#endif
    }
}
