using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// 자식 렌더러들의 컬링용 바운즈를 강제로 크게 잡아, 화면 가장자리에서 통째로 사라지는 것을 막는다.
    ///
    /// Unity는 렌더러를 감싸는 바운즈 상자가 카메라 시야를 벗어나면 그리기를 통째로 건너뛴다.
    /// 그런데 셰이더가 정점을 늘려 그리는 이펙트(빔, 스트레치 파티클 등)는 실제로 보이는 범위가
    /// 원본 메시의 바운즈보다 훨씬 크다. 그러면 눈에는 아직 화면에 걸쳐 보이는데도
    /// "상자가 화면 밖"이라는 이유로 사라진다. 각도만 살짝 틀어도 툭 없어지는 증상이 이것이다.
    ///
    /// 하늘에 고정된 배경 연출처럼 "거의 항상 보여야 하는" 오브젝트에 붙인다.
    /// 컬링을 사실상 포기하는 셈이지만, 어차피 계속 화면에 있어야 하는 대상이라 손해가 없다.
    /// 반대로 자주 화면 밖으로 나가는 오브젝트에 붙이면 헛되이 그리게 되므로 쓰지 않는다.
    /// </summary>
    public class RendererBoundsOverride : MonoBehaviour
    {
        [Header("바운즈 크기")]
        [Tooltip("바운즈 한 변의 길이. 이펙트가 실제로 뻗어나가는 범위보다 넉넉하게 잡는다.")]
        [SerializeField] private float size = 1000f;

        [Tooltip("바운즈 중심. 이 오브젝트 기준의 로컬 좌표다. 보통 0으로 두면 된다.")]
        [SerializeField] private Vector3 center = Vector3.zero;

        [Header("적용 방식")]
        [Tooltip("매 프레임 다시 적용한다. 파티클 렌더러는 Unity가 매 프레임 바운즈를 다시 계산하므로 " +
                 "한 번만 넣으면 곧 덮어써진다. 메시만 있다면 꺼도 된다.")]
        [SerializeField] private bool applyEveryFrame = true;

        private Renderer[] targets;

        private void Awake()
        {
            // 비활성 자식까지 포함해서 찾는다. 이펙트는 일부가 꺼진 채로 시작하는 경우가 있다.
            targets = GetComponentsInChildren<Renderer>(true);

            if (targets.Length == 0)
            {
                Debug.LogWarning($"{name}: 바운즈를 넓힐 Renderer가 없다.", this);
                enabled = false;
                return;
            }

            Apply();
        }

        // 컬링 판정은 렌더링 직전에 일어나므로, LateUpdate에서 덮어써야 이번 프레임에 반영된다.
        private void LateUpdate()
        {
            if (applyEveryFrame) Apply();
        }

        private void OnDisable()
        {
            // 원래 계산값으로 되돌린다. 컴포넌트를 껐는데 넓힌 바운즈가 남아 있으면
            // 왜 컬링이 안 되는지 나중에 찾기 어려워진다.
            if (targets == null) return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].ResetLocalBounds();
            }
        }

        /// <summary>
        /// 지금 설정한 크기로 자식 렌더러들의 바운즈를 다시 적용한다.
        /// 인스펙터에서 값을 바꾼 뒤 플레이 중에 바로 확인하고 싶을 때 쓴다.
        /// </summary>
        [ContextMenu("지금 적용")]
        public void Apply()
        {
            if (targets == null) targets = GetComponentsInChildren<Renderer>(true);

            Bounds bounds = new Bounds(center, Vector3.one * Mathf.Max(size, 0.01f));

            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null) targets[i].localBounds = bounds;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(center, Vector3.one * size);
        }
    }
}
