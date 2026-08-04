using UnityEngine;

namespace ProjectS.FX
{
    /// <summary>
    /// 자식 오브젝트들을 빈틈없이 이어붙인 뒤, 한 방향으로 흘려보내며 무한 순환시킨다.
    /// 전광판에 글자가 끊김 없이 흐르는 연출용.
    ///
    /// 핵심은 "한 바퀴 거리 = 자식들의 실제 폭 합계" 로 두는 것이다.
    /// 이렇게 하면 마지막 간판의 오른쪽 끝과 첫 간판의 왼쪽 끝이 정확히 만나서
    /// 이음새가 생기지 않는다. 간격을 눈대중으로 맞출 필요가 없다.
    ///
    /// 사용법
    /// 1) 한 줄에 해당하는 간판들을 빈 게임오브젝트의 자식으로 모은다
    /// 2) 그 부모에 이 스크립트를 붙인다
    /// 3) Arrange Children을 켜두면 알아서 맞닿게 재배치한다
    /// </summary>

    public class ConveyorLoop : MonoBehaviour
    {
        [Header("이동")]
        [Tooltip("초당 이동 거리(로컬 단위). 음수면 반대 방향.")]
        [SerializeField] private float speed = 1.5f;

        [Tooltip("체크하면 speed의 부호를 뒤집는다. 줄마다 방향을 번갈아 줄 때 편하다.")]
        [SerializeField] private bool reverse;

        [Header("배치")]
        [Tooltip("메시 폭을 재서 자식들을 자동으로 맞닿게 재배치한다. " +
                 "끄면 현재 배치를 그대로 쓰되 폭 합계로만 순환 거리를 계산한다.")]
        [SerializeField] private bool arrangeChildren = true;

        [Tooltip("간판 사이에 일부러 띄울 간격. 0이면 완전히 붙는다.")]
        [SerializeField] private float gap = 0f;

        [Tooltip("메시 폭 계산이 부정확할 때 미세 조정. 1이 기본.")]
        [SerializeField] private float widthMultiplier = 1f;

        private Transform[] items;
        private float[] baseX;       // 순환 계산의 기준이 되는 배치 위치
        private float[] pivotToLeft; // 피벗에서 메시 왼쪽 끝까지의 거리 (순환 판정 기준)
        private float[] originalX;   // 스크립트를 끄면 되돌릴 원래 위치
        private float span;          // 한 바퀴 도는 거리 = 전체 띠 길이
        private float travelled;
        private float lastRealtime;
        private bool ready;

        private void OnEnable()
        {
            lastRealtime = Time.realtimeSinceStartup;
            Setup();
        }

        private void OnDisable()
        {
            RestoreOriginalPositions();
            ready = false;
        }

        private void Setup()
        {
            int count = transform.childCount;
            if (count < 1)
            {
                ready = false;
                return;
            }

            items = new Transform[count];
            baseX = new float[count];
            originalX = new float[count];
            pivotToLeft = new float[count];

            float[] widths = new float[count];

            for (int i = 0; i < count; i++)
            {
                items[i] = transform.GetChild(i);
                originalX[i] = items[i].localPosition.x;
                MeasureX(items[i], out widths[i], out pivotToLeft[i]);
            }

            WarnIfStatic();

            if (arrangeChildren)
            {
                // 왼쪽부터 차례로 붙여나간다.
                // 피벗이 메시 중앙이 아닐 수 있으므로, 피벗에서 왼쪽 끝까지의 거리를
                // 빼줘야 실제 가장자리가 정확히 맞닿는다.
                float cursor = FindLeftmostEdge();
                for (int i = 0; i < count; i++)
                {
                    baseX[i] = cursor - pivotToLeft[i];
                    cursor += widths[i] + gap;
                }
                span = cursor - FindLeftmostEdge();   // 전체 띠 길이 (마지막 gap 포함)
            }
            else
            {
                for (int i = 0; i < count; i++)
                    baseX[i] = originalX[i];

                float total = 0f;
                for (int i = 0; i < count; i++) total += widths[i] + gap;
                span = total;
            }

            if (span < 0.0001f)
            {
                Debug.LogWarning(
                    $"[ConveyorLoop] {name}: 폭 계산 결과가 0에 가깝습니다. " +
                    $"Width Multiplier로 조정하거나 Arrange Children을 꺼보세요.", this);
                ready = false;
                return;
            }

            travelled = 0f;
            ready = true;
            ApplyPositions();
        }

        /// <summary>
        /// Batching Static으로 표시된 자식은 정적 배칭 때문에 메시가 월드 좌표에 구워진다.
        /// 그러면 Transform은 정상적으로 움직이는데 화면에는 전혀 반영되지 않아서,
        /// "스크립트가 아무 일도 안 하는 것"처럼 보인다. 원인을 바로 알 수 있게 로딩 시점에 알린다.
        /// </summary>
        private void WarnIfStatic()
        {
            if (!Application.isPlaying) return;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null || !items[i].gameObject.isStatic) continue;

                Debug.LogWarning(
                    $"[ConveyorLoop] {name}: 자식 '{items[i].name}'이(가) Static으로 표시돼 있습니다. " +
                    "정적 배칭으로 메시가 고정돼 움직여도 화면에 보이지 않습니다. " +
                    "자식들의 Static(최소 Batching Static, Contribute GI)을 해제하세요.", items[i]);
                return;   // 한 줄에 간판이 여럿이므로 첫 건만 알린다. 콘솔 도배 방지.
            }
        }

        /// <summary>재배치 시작점. 원래 배치의 가장 왼쪽을 유지해서 줄 위치가 안 튀게 한다.</summary>
        private float FindLeftmostEdge()
        {
            float min = float.MaxValue;
            for (int i = 0; i < originalX.Length; i++)
                if (originalX[i] < min) min = originalX[i];
            return min == float.MaxValue ? 0f : min;
        }

        /// <summary>
        /// 자식의 로컬 X축 방향 폭과, 피벗에서 왼쪽 끝까지의 거리를 구한다.
        /// 자기 자신뿐 아니라 하위 렌더러까지 포함해서 잰다.
        /// </summary>
        private void MeasureX(Transform child, out float width, out float pivotToLeft)
        {
            width = 0f;
            pivotToLeft = 0f;

            // 비활성 자식도 포함한다. 빠뜨리면 폭이 0으로 잡혀 그 칸만 겹쳐 붙는다.
            var renderers = child.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            // 부모(this) 기준 로컬 공간에서의 최소/최대 X를 구한다.
            float min = float.MaxValue;
            float max = float.MinValue;

            foreach (var r in renderers)
            {
                Bounds b = r.bounds;   // 월드 공간 AABB
                Vector3 c = b.center;
                Vector3 e = b.extents;

                // 월드 AABB의 8개 꼭짓점을 부모 로컬로 옮겨서 X 범위를 잰다.
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            Vector3 corner = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);
                            float lx = transform.InverseTransformPoint(corner).x;
                            if (lx < min) min = lx;
                            if (lx > max) max = lx;
                        }
            }

            if (min == float.MaxValue) return;

            width = (max - min) * widthMultiplier;
            pivotToLeft = min - child.localPosition.x;
        }

        private void RestoreOriginalPositions()
        {
            if (items == null || originalX == null) return;

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                Vector3 p = items[i].localPosition;
                p.x = originalX[i];
                items[i].localPosition = p;
            }
        }

        private void ApplyPositions()
        {
            float origin = FindLeftmostEdge();

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;

                // 기준 위치에서 travelled 만큼 밀되, span을 넘으면 반대쪽으로 감는다.
                // Repeat을 쓰면 "끝에 닿으면 앞으로 보내기" 같은 분기 없이 순환한다.
                //
                // 감는 기준은 피벗이 아니라 반드시 "메시 왼쪽 끝"이어야 한다.
                // 배치를 왼쪽 끝 기준으로 맞닿게 해놨으므로, 순환도 같은 기준이어야
                // 빠져나간 자리에 다음 간판이 정확히 들어온다. 피벗 기준으로 감으면
                // (피벗이 메시 중앙일 때) 오른쪽 절반이 아직 띠 안에 있는데도 통째로
                // 튀어버려서, 간판 폭만큼의 빈칸이 주기적으로 생겼다 사라진다.
                float edge = Mathf.Repeat(baseX[i] + pivotToLeft[i] - origin + travelled, span);

                Vector3 p = items[i].localPosition;
                p.x = origin + edge - pivotToLeft[i];
                items[i].localPosition = p;
            }
        }

        private void Update()
        {
            if (!ready) return;

            float dt;
            if (Application.isPlaying)
            {
                dt = Time.deltaTime;
            }
            else
            {
                float now = Time.realtimeSinceStartup;
                dt = Mathf.Clamp(now - lastRealtime, 0f, 0.1f);
                lastRealtime = now;
            }

            float dir = reverse ? -speed : speed;
            travelled = Mathf.Repeat(travelled + dir * dt, span);

            ApplyPositions();
        }
    }
}
