using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// 미니맵 위젯(플레이어 중심 · North-up). HUDPanel 아래에 상시 떠 있는 요소로, 별도 Panel이 아니다.
    /// 플레이어가 항상 중앙에 고정되고, 배경과 다른 마커들이 플레이어 기준 상대 위치로 움직인다.
    /// 미니맵 자체는 회전하지 않는다(위쪽 = 월드 북쪽 +Z 고정). 방향은 마커 아이콘 회전으로만 나타낸다.
    /// <para>
    /// 역할 분리: "무엇이 있나"(등록/해제)는 MinimapPresenter가 이벤트로 넣고,
    /// "어디 있나"(매 프레임 위치)는 이 View가 플레이어 기준으로 계산한다.
    /// </para>
    /// <para>
    /// 배경은 맵 스냅샷 한 장을 markerArea 안에서 스크롤한다. 스냅샷을 안 넣으면 placeholder가 그대로 스크롤된다.
    /// 배경이 미니맵 밖으로 삐져나오지 않으려면 markerArea에 Mask 또는 RectMask2D가 있어야 한다.
    /// </para>
    /// </summary>
    public class MinimapView : MonoBehaviour
    {
        // 마커 종류별 아이콘 프리팹. 아이콘은 이미 있으므로 드래그로 연결만 한다.
        [Serializable]
        private class MarkerBinding
        {
            public MinimapMarkerType type;

            // 마커 아이콘 프리팹(UI RectTransform). 플레이어=삼각형, 적=육각형 등.
            public RectTransform prefab;

            // 대상의 바라보는 방향(월드 Y 회전)에 맞춰 마커를 회전시킬지.
            // 미니맵이 회전하지 않으므로, 방향 표시는 마커별 이 옵션으로만 한다(플레이어 삼각형=켜기).
            public bool rotateWithEntity;
        }

        // 마커·배경이 놓이는 영역. 플레이어(중심)는 이 영역의 한가운데(0,0)에 온다.
        // 배경 스크롤이 밖으로 넘치지 않게 이 오브젝트에 Mask/RectMask2D를 둔다.
        [Header("영역")]
        [SerializeField] private RectTransform markerArea;

        // 미니맵 배경 그림. placeholder 사각형이나 맵 스냅샷 Image. 플레이어 위치에 맞춰 스크롤된다.
        [SerializeField] private Image background;

        // 플레이어를 중심으로 보여줄 반경(월드 미터). 이 거리가 미니맵 가장자리에 닿는다 = 줌 정도.
        // 작을수록 확대(주변만 크게), 클수록 축소(넓게). 30이면 반경 30m가 화면 반절에 들어온다.
        [Header("플레이어 중심")]
        [SerializeField, Min(1f)] private float viewRadius = 30f;

        // 반경 밖 대상을 가장자리에 붙일지(켜기) 아니면 숨길지(끄기).
        // 켜면 멀리 있는 적이 테두리에 머물러 방향을 알 수 있다.
        [SerializeField] private bool clampToEdge = true;

        // 플레이어 마커를 캐릭터가 바라보는 방향이 아니라 '마우스(카메라 조준)' 방향으로 돌릴지.
        // 이 게임은 커서 잠금 상태에서 마우스로 카메라를 돌려 조준하므로(공격·스킬이 모두
        // SnapToCameraForward = cam.forward 기준), 조준 방향 = 카메라 forward의 yaw다.
        // 끄면 기존처럼 캐릭터 transform의 실제 바라보는 방향을 따른다.
        [SerializeField] private bool playerFacesCameraAim = true;

        // 배경 스냅샷이 담는 맵 전체 범위(XZ). 배경을 얼마나 크게·어디에 둘지 계산에 쓴다.
        // SetBackground(씬 진입)로 스테이지마다 갱신한다. 스냅샷을 안 쓰면 배경 스크롤 기준으로만 쓰인다.
        [Header("맵 범위 (배경 스냅샷 기준, XZ)")]
        [SerializeField] private Vector2 worldCenter = Vector2.zero;
        [SerializeField] private Vector2 worldSize = new Vector2(100f, 100f);

        // 종류별 마커 프리팹.
        [SerializeField] private MarkerBinding[] markerBindings;

        // 살아 있는 마커 하나. type을 들고 있어야 제거 시 같은 종류 풀로 돌려보낼 수 있다.
        private class Marker
        {
            public RectTransform rect;
            public MinimapMarkerType type;
            public bool rotate;
        }

        private readonly Dictionary<MinimapMarkerType, MarkerBinding> bindingMap
            = new Dictionary<MinimapMarkerType, MarkerBinding>();

        private readonly Dictionary<Transform, Marker> markers = new Dictionary<Transform, Marker>();

        private readonly Dictionary<MinimapMarkerType, Queue<RectTransform>> pools
            = new Dictionary<MinimapMarkerType, Queue<RectTransform>>();

        private readonly List<Transform> deadTargets = new List<Transform>();

        // 미니맵의 중심 기준점(플레이어). Player 타입 마커가 등록될 때 잡고, 해제되면 놓는다.
        // 이게 없으면(플레이어 마커 없음) 맵 중심을 기준으로 폴백한다.
        private Transform playerTarget;

        // 플레이어 마커의 조준 방향 계산에 쓰는 메인 카메라. Camera.main은 매 프레임 조회 비용이 있어 캐싱하고,
        // 씬 전환 등으로 파괴되면(캐시 무효) 다음 프레임에 다시 잡는다.
        private Transform aimCamera;

        private void Awake()
        {
            if (markerBindings == null) return;

            foreach (MarkerBinding binding in markerBindings)
            {
                if (binding == null || binding.prefab == null) continue;
                bindingMap[binding.type] = binding;
            }
        }

        /// <summary>
        /// 씬별 미니맵 배경과 맵 범위를 한 번에 교체한다. 씬 진입 시 호출한다.
        /// sprite가 null이면 그림은 그대로 두고 범위만 바꾼다(placeholder 유지).
        /// </summary>
        public void SetBackground(Sprite sprite, Vector2 worldCenter, Vector2 worldSize)
        {
            if (sprite != null && background != null) background.sprite = sprite;

            this.worldCenter = worldCenter;
            this.worldSize = worldSize;
        }

        /// <summary>대상 하나의 마커를 만든다. 프리팹이 없는 종류는 조용히 건너뛴다.</summary>
        public void AddMarker(Transform target, MinimapMarkerType type)
        {
            if (target == null || markers.ContainsKey(target)) return;
            if (!bindingMap.TryGetValue(type, out MarkerBinding binding)) return;

            RectTransform rect = GetFromPool(type, binding.prefab);
            markers.Add(target, new Marker { rect = rect, type = type, rotate = binding.rotateWithEntity });

            // 플레이어는 미니맵의 중심 기준점이 된다. 등록되는 순간 기준으로 잡는다.
            if (type == MinimapMarkerType.Player) playerTarget = target;
        }

        /// <summary>대상의 마커를 제거하고 풀로 돌려보낸다.</summary>
        public void RemoveMarker(Transform target)
        {
            if (target == null || !markers.TryGetValue(target, out Marker marker)) return;

            markers.Remove(target);
            ReturnToPool(marker);

            if (target == playerTarget) playerTarget = null;
        }

        /// <summary>모든 마커를 정리한다. 미니맵이 꺼질 때 호출한다.</summary>
        public void ClearAll()
        {
            foreach (Marker marker in markers.Values)
                ReturnToPool(marker);

            markers.Clear();
            playerTarget = null;
        }

        private void Update()
        {
            if (markerArea == null) return;

            // 중심 기준점: 플레이어가 있으면 그 위치, 없으면 맵 중심으로 폴백.
            Vector3 center = playerTarget != null
                ? playerTarget.position
                : new Vector3(worldCenter.x, 0f, worldCenter.y);

            Vector2 area = markerArea.rect.size;
            Vector2 areaHalf = area * 0.5f;

            // 월드 미터 → 미니맵 픽셀 배율. viewRadius가 미니맵 절반 크기(중심→가장자리)에 대응한다.
            // 짧은 축 기준으로 잡아 가로세로가 달라도 원형 거리 감각이 일정하게 유지된다.
            float scale = Mathf.Min(areaHalf.x, areaHalf.y) / Mathf.Max(1f, viewRadius);

            UpdateBackground(center, scale);

            foreach (KeyValuePair<Transform, Marker> pair in markers)
            {
                if (pair.Key == null)
                {
                    deadTargets.Add(pair.Key);
                    continue;
                }

                UpdateMarker(pair.Key, pair.Value, center, scale, areaHalf);
            }

            if (deadTargets.Count > 0)
            {
                foreach (Transform dead in deadTargets)
                {
                    if (markers.TryGetValue(dead, out Marker marker))
                    {
                        markers.Remove(dead);
                        ReturnToPool(marker);
                    }
                }

                deadTargets.Clear();
            }
        }

        // 대상의 위치를 플레이어(center) 기준 상대 픽셀로 옮긴다. 플레이어 자신은 offset 0이라 정중앙에 온다.
        private void UpdateMarker(Transform target, Marker marker, Vector3 center, float scale, Vector2 areaHalf)
        {
            Vector3 offset = target.position - center;
            Vector2 pos = new Vector2(offset.x * scale, offset.z * scale);

            // 반경 밖: 가장자리에 붙이거나(clampToEdge) 숨긴다.
            bool inside = Mathf.Abs(pos.x) <= areaHalf.x && Mathf.Abs(pos.y) <= areaHalf.y;
            if (!inside)
            {
                if (!clampToEdge)
                {
                    if (marker.rect.gameObject.activeSelf) marker.rect.gameObject.SetActive(false);
                    return;
                }

                pos.x = Mathf.Clamp(pos.x, -areaHalf.x, areaHalf.x);
                pos.y = Mathf.Clamp(pos.y, -areaHalf.y, areaHalf.y);
            }

            if (!marker.rect.gameObject.activeSelf) marker.rect.gameObject.SetActive(true);
            marker.rect.anchoredPosition = pos;

            // 미니맵은 회전하지 않으므로, 방향 표시는 마커 자체를 돌려서만 한다.
            // 월드 +Z(정면)가 미니맵 위(+Y)를 향하도록, 시계방향 월드 yaw를 부호 반전해 UI 회전에 맞춘다.
            if (marker.rotate)
                marker.rect.localEulerAngles = new Vector3(0f, 0f, -GetMarkerYaw(target, marker));
        }

        // 마커가 나타낼 방향(월드 yaw, 도 단위)을 고른다.
        // 플레이어 마커는 옵션에 따라 캐릭터가 실제 바라보는 방향 대신 카메라 조준(마우스) 방향을 쓴다.
        // 카메라의 pitch는 무시되고 yaw만 뽑히므로, 미니맵 평면 위 조준 방향이 그대로 나온다.
        private float GetMarkerYaw(Transform target, Marker marker)
        {
            if (marker.type == MinimapMarkerType.Player && playerFacesCameraAim && TryCacheAimCamera())
                return aimCamera.eulerAngles.y;

            return target.eulerAngles.y;
        }

        // 메인 카메라 Transform을 캐싱한다. 파괴/미존재면 false. PlayerMovement.TryCacheMainCamera와 같은 패턴.
        private bool TryCacheAimCamera()
        {
            if (aimCamera != null) return true;
            if (Camera.main == null) return false;

            aimCamera = Camera.main.transform;
            return true;
        }

        // 배경 스냅샷을 플레이어 위치의 반대로 밀어, 플레이어가 선 맵 지점이 미니맵 중앙에 오게 한다.
        private void UpdateBackground(Vector3 center, float scale)
        {
            if (background == null) return;

            RectTransform bg = background.rectTransform;

            // 맵 전체를 현재 배율로 픽셀 크기로 환산한다(스냅샷 한 장이 맵 전체를 덮는다는 전제).
            bg.sizeDelta = new Vector2(worldSize.x, worldSize.y) * scale;

            // 맵 중심과 플레이어의 월드 차이만큼, 반대 방향으로 배경을 민다.
            Vector3 mapCenter = new Vector3(worldCenter.x, 0f, worldCenter.y);
            Vector3 off = mapCenter - center;
            bg.anchoredPosition = new Vector2(off.x * scale, off.z * scale);
        }

        private RectTransform GetFromPool(MinimapMarkerType type, RectTransform prefab)
        {
            if (pools.TryGetValue(type, out Queue<RectTransform> pool) && pool.Count > 0)
            {
                RectTransform reused = pool.Dequeue();
                reused.gameObject.SetActive(true);
                return reused;
            }

            RectTransform rect = Instantiate(prefab, markerArea);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private void ReturnToPool(Marker marker)
        {
            if (marker.rect == null) return;

            marker.rect.gameObject.SetActive(false);

            if (!pools.TryGetValue(marker.type, out Queue<RectTransform> pool))
            {
                pool = new Queue<RectTransform>();
                pools.Add(marker.type, pool);
            }

            pool.Enqueue(marker.rect);
        }
    }
}
