using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 페이지 전환용 사선 와이프. 화면을 우상단에서 좌하단으로 덮고(cover), 덮인 순간 호출부가 페이지를 바꾸게 한 뒤,
    /// 같은 방향으로 걷어 낸다(reveal). 덮인 동안 페이지·카메라가 바뀌므로 교체 순간이 보이지 않는다.
    ///
    /// 전체 화면 <see cref="Graphic"/>(Image/RawImage) 한 장에 부착하고, 그 머티리얼은 <c>ProjectS/UI Page Wipe</c> 셰이더로 만든다.
    /// 에셋 머티리얼을 직접 쓰면 <c>_Progress</c> 값이 에셋에 저장되므로 Awake에서 인스턴스를 만들어 그쪽에만 쓴다.
    ///
    /// 재생 중에는 이 오브젝트가 레이캐스트를 받아 아래 페이지의 버튼 입력을 막는다(와이프 도중 두 번 눌리는 것 방지).
    /// 시간은 unscaled로 돈다 — 로딩·일시정지 중 timeScale이 0이어도 멈추지 않게 하기 위함이다.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class PageWipeView : MonoBehaviour
    {
        [Header("시간")]
        [Tooltip("덮는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float coverSeconds = 0.35f;

        [Tooltip("완전히 덮인 채 유지하는 시간(초). 페이지가 켜지고 한 프레임 그려질 여유")]
        [SerializeField, Min(0f)] private float holdSeconds = 0.05f;

        [Tooltip("걷는 데 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float revealSeconds = 0.35f;

        [Header("그리기 순서")]
        [Tooltip("이 와이프가 전용 Canvas(와이프만 들어 있는 캔버스)에 있을 때 그 캔버스에 줄 Sorting Order. 팝업·로딩 화면(100 안팎)보다 커야 " +
                 "덮인 순간 켜지는 팝업 위로 와이프가 걷히는 모습이 보인다. 다른 UI와 캔버스를 같이 쓰면 건드리지 않는다.")]
        [SerializeField] private int nestedCanvasSortingOrder = 500;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private Graphic graphic;
        private Material runtimeMaterial;
        private Coroutine routine;
        private Action pendingCovered;
        private bool hasCovered;

        /// <summary>와이프가 재생 중인지(덮는 중·유지·걷는 중 모두 포함).</summary>
        public bool IsPlaying => routine != null;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            graphic.raycastTarget = true;

            EnsureOnTop();

            // 머티리얼이 비어 있으면 Graphic은 기본 UI 머티리얼을 돌려준다 — 그러면 와이프가 그냥 흰 사각형이 된다.
            if (graphic.material == null || graphic.material == graphic.defaultMaterial)
            {
                Debug.LogWarning("[PageWipeView] 머티리얼이 'ProjectS/UI Page Wipe' 셰이더로 만든 것이 아닙니다. 와이프가 제대로 그려지지 않습니다.", this);
            }
            else
            {
                runtimeMaterial = new Material(graphic.material);
                graphic.material = runtimeMaterial;
            }

            SetProgress(0f);
            graphic.enabled = false;
        }

        // Canvas끼리는 형제 순서가 아니라 Sorting Order로 그려지는 순서가 갈린다. 와이프가 자기 전용 캔버스에 들어 있으면
        // 그 안의 SetAsLastSibling은 소용이 없어, 캔버스 자체를 팝업·로딩 화면보다 위로 올려야 한다.
        // Bootstrap의 UIManager는 Canvas가 없는 일반 오브젝트라 그 자식 캔버스들이 전부 "루트" 캔버스다 —
        // 루트냐 중첩이냐로 가르면 정작 이 경우를 놓친다(실제로 놓쳐서 걷히는 연출이 팝업 뒤에 가려졌다).
        // 그래서 "이 캔버스가 와이프 전용인가"(와이프가 직계 자식이고 형제가 없음)로 가른다. 캐릭터 선택 씬처럼
        // 페이지들과 한 캔버스를 쓰는 경우는 그 캔버스 전체의 순서를 바꾸게 되므로 건드리지 않는다.
        // 레이캐스터가 없으면 클릭을 막지 못하므로(와이프 도중 아래 UI가 눌림) 함께 보장한다.
        private void EnsureOnTop()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            bool dedicated = transform.parent == canvas.transform && canvas.transform.childCount == 1;
            if (!dedicated) return;

            canvas.overrideSorting = true;
            canvas.sortingOrder = nestedCanvasSortingOrder;

            if (!canvas.TryGetComponent<GraphicRaycaster>(out _))
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        /// <summary>
        /// 와이프를 재생한다. 화면이 완전히 덮인 순간 <paramref name="onCovered"/>가 호출되므로 페이지·카메라 교체는 여기서 한다.
        /// 이미 재생 중이면 새로 시작하지 않는다: 아직 덮이기 전이면 콜백을 이어 붙여 같은 덮임 순간에 모두 실행하고(등록 순서대로,
        /// 마지막 요청이 최종 상태), 이미 걷는 중이면 즉시 실행한다.
        /// </summary>
        /// <param name="onCovered">화면이 완전히 덮인 순간 실행할 동작</param>
        public void Play(Action onCovered)
        {
            if (routine != null)
            {
                if (hasCovered) onCovered?.Invoke();
                else pendingCovered += onCovered;
                return;
            }

            // 씬에서 형제 순서가 어디에 있든 페이지·팝업 위에 그려지게 한다. 첫 자식으로 저장돼 있으면
            // 와이프가 모든 UI 뒤에서 재생돼 "아무 연출도 안 나오는" 것처럼 보인다.
            transform.SetAsLastSibling();

            UpdateAspect();

            pendingCovered = onCovered;
            routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            hasCovered = false;
            graphic.enabled = true;

            yield return Tween(0f, 1f, coverSeconds);

            hasCovered = true;
            Action callback = pendingCovered;
            pendingCovered = null;

            // 콜백이 예외를 던져도 와이프가 덮인 채로 멈추지 않게 한다. 코루틴 안의 예외는 코루틴을 그대로 끝내 버려
            // 걷기 단계에 영영 못 가고 화면이 가려진 채 남는다.
            try
            {
                callback?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }

            // 콜백에서 켠 페이지가 그려지고, 콜백이 만든 프레임 지연(팝업 초기화 등)이 지나간 뒤에 걷기 시작한다.
            // 두 프레임을 기다리는 이유: 콜백이 도는 프레임은 길어지고, 그 길이가 바로 다음 프레임의 deltaTime에 실린다.
            yield return null;
            yield return null;
            if (holdSeconds > 0f) yield return new WaitForSecondsRealtime(holdSeconds);

            yield return Tween(1f, 2f, revealSeconds);

            SetProgress(0f);
            graphic.enabled = false;
            routine = null;
        }

        // 프레임 하나가 이만큼(초)보다 길어도 이만큼만 진행으로 친다. 팝업 열기·페이지 교체처럼 무거운 작업이 한 프레임을 길게 만들면
        // deltaTime이 수백 ms가 되고, 그대로 더하면 걷기(0.35초)가 첫 프레임에 끝나 "덮였다가 바로 사라지는" 것처럼 보인다.
        private const float MaxStepSeconds = 1f / 30f;

        private IEnumerator Tween(float from, float to, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStepSeconds);
                SetProgress(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds)));
                yield return null;
            }

            SetProgress(to);
        }

        // 헥사곤 셀이 해상도와 무관하게 정육각형으로 보이도록 화면 비율을 셰이더에 알린다. 재생 때마다 갱신해
        // 창 크기·해상도가 바뀐 뒤에도 맞는다.
        private void UpdateAspect()
        {
            if (runtimeMaterial == null) return;

            Rect rect = ((RectTransform)transform).rect;
            if (rect.height > 0f) runtimeMaterial.SetFloat(AspectId, rect.width / rect.height);
        }

        private void SetProgress(float value)
        {
            if (runtimeMaterial != null) runtimeMaterial.SetFloat(ProgressId, value);
        }
    }
}
