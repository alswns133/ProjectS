using System.Text;
using UnityEngine;
using UnityEngine.Playables;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 보스가 "안 보인다"는 증상을 원인별로 가르는 진단용 컴포넌트. 보스 오브젝트에 붙이고 실행한 뒤 콘솔의
    /// <c>[BossProbe]</c> 줄을 읽는다. 원인을 찾으면 떼어 낸다(빌드에서는 로그 호출이 제거된다).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "안 보인다"는 서로 다른 네 가지가 같은 모습으로 보인다. 이 컴포넌트는 그 넷을 따로 찍는다.
    ///   1) <b>꺼짐</b> — 오브젝트가 비활성화됐다. OnDisable에서 <b>호출 스택</b>을 남기므로 누가 껐는지
    ///      (예: Timeline의 ActivationMixerPlayable)가 그대로 나온다.
    ///   2) <b>이동</b> — 한 프레임에 크게 순간이동했다(애니메이션 트랙의 위치 오프셋 등).
    ///   3) <b>렌더러 꺼짐</b> — 오브젝트는 켜져 있는데 그려질 렌더러가 없다.
    ///   4) <b>화면 밖</b> — 다 멀쩡한데 메인 카메라 시야에 들어오지 않는다.
    /// </para>
    /// <para>
    /// 상태가 <b>바뀔 때만</b> 한 줄씩 남긴다. 매 프레임 찍으면 정작 바뀐 순간이 묻힌다.
    /// 함께 등장 연출 디렉터의 재생 시각을 붙여, 타임라인의 어느 지점에서 일어났는지 대조할 수 있게 한다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 인트로·히트스톱으로 timeScale이 바뀌어도 기록 간격이 흔들리지 않게 한다.</para>
    /// </remarks>
    public class BossVisibilityProbe : MonoBehaviour
    {
        [Tooltip("재생 시각을 함께 찍을 등장 연출 디렉터. 비우면 씬에서 BossIntroDirector가 붙은 디렉터를 찾는다.")]
        [SerializeField] private PlayableDirector introDirector;

        [Tooltip("한 프레임에 이 거리(m) 이상 움직이면 순간이동으로 기록한다.")]
        [SerializeField, Min(0.1f)] private float teleportDistance = 3f;

        [Tooltip("상태를 검사하는 간격(초). 순간이동 검사는 이와 무관하게 매 프레임 한다.")]
        [SerializeField, Min(0.02f)] private float checkInterval = 0.1f;

        private Renderer[] renderers;
        private Vector3 lastPosition;
        private float nextCheck;

        // 직전에 기록한 상태. 같으면 다시 찍지 않는다.
        private int lastEnabledRenderers = -1;
        private int lastInView = -1;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            lastPosition = transform.position;

            if (introDirector == null)
            {
                var intro = FindAnyObjectByType<ProjectS.Scenes.BossIntroDirector>(FindObjectsInactive.Include);
                if (intro != null) introDirector = intro.GetComponent<PlayableDirector>();
            }

            Log($"준비: 렌더러 {renderers.Length}개, 디렉터 {(introDirector != null ? introDirector.name : "없음")}, 위치 {transform.position}");
        }

        private void OnEnable()
        {
            Log($"켜짐 (위치 {transform.position})");
            lastPosition = transform.position;
        }

        private void OnDisable()
        {
            // 누가 껐는지가 핵심이다. Timeline이 껐다면 스택에 ActivationMixerPlayable이 보인다.
            Log($"꺼짐 (위치 {transform.position})\n호출 스택:\n{StackTraceUtility.ExtractStackTrace()}");
        }

        private void LateUpdate()
        {
            // 애니메이션·타임라인이 위치를 쓴 뒤에 봐야 이번 프레임의 최종 위치다.
            Vector3 position = transform.position;
            float moved = Vector3.Distance(position, lastPosition);
            if (moved >= teleportDistance)
                Log($"순간이동 {moved:0.0}m: {lastPosition} → {position}");
            lastPosition = position;

            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + checkInterval;

            CheckRenderers();
            CheckInView();
        }

        private void CheckRenderers()
        {
            int enabledCount = 0;
            foreach (Renderer r in renderers)
            {
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) enabledCount++;
            }

            if (enabledCount == lastEnabledRenderers) return;
            lastEnabledRenderers = enabledCount;

            Log($"그려질 렌더러 {enabledCount}/{renderers.Length}개");
        }

        /// <summary>
        /// 메인 카메라 시야 안에 있는지 본다. <c>Renderer.isVisible</c>은 Scene 뷰 카메라에 보여도 true라
        /// 에디터에서 믿을 수 없어, 메인 카메라의 절두체와 직접 비교한다.
        /// </summary>
        private void CheckInView()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                if (lastInView != -2) Log("메인 카메라 없음 (MainCamera 태그 확인)");
                lastInView = -2;
                return;
            }

            if (!TryGetBounds(out Bounds bounds)) return;

            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int inView = GeometryUtility.TestPlanesAABB(planes, bounds) ? 1 : 0;

            if (inView == lastInView) return;
            lastInView = inView;

            float distance = Vector3.Distance(cam.transform.position, bounds.center);
            Log(inView == 1
                ? $"카메라 시야 안 (카메라 '{cam.name}' {cam.transform.position}, 거리 {distance:0.0}m)"
                : $"카메라 시야 밖 (카메라 '{cam.name}' {cam.transform.position} 방향 {cam.transform.forward}, 보스 중심 {bounds.center}, 거리 {distance:0.0}m)");
        }

        private bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool has = false;

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return has;
        }

        private void Log(string message)
        {
            var sb = new StringBuilder("[BossProbe] ");
            sb.Append($"f{Time.frameCount} ");

            if (introDirector != null)
                sb.Append($"(인트로 {introDirector.state} {introDirector.time:0.00}s) ");

            sb.Append(message);
            DevLog.Log(sb.ToString(), this);
        }
    }
}
