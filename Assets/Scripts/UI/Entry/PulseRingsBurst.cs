using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// <c>ProjectS/UI Pulse Rings</c> 배경에서 링을 한 번 터뜨리는 트리거. 클래스를 고르는 순간
    /// <see cref="Burst"/>를 부르면 고른 카드 위치에서 링이 퍼져 나간다.
    ///
    /// 셰이더는 시작 시각(<c>_BurstStart</c>)과 중심(<c>_BurstCenter</c>)만 받고 나머지 연출(이징·메아리 링)은 직접 계산한다.
    /// 에셋 머티리얼에 값을 쓰면 에디터에서 저장되어 다음 실행 때 터진 채로 시작하므로, Awake에서 인스턴스를 만들어
    /// 그쪽에만 쓴다. 시작 시각은 셰이더의 <c>_Time.y</c>(= <see cref="Time.timeSinceLevelLoad"/>)와 같은 시계를 쓴다.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class PulseRingsBurst : MonoBehaviour
    {
        private static readonly int BurstStartId = Shader.PropertyToID("_BurstStart");
        private static readonly int BurstCenterId = Shader.PropertyToID("_BurstCenter");
        private static readonly int CenterId = Shader.PropertyToID("_Center");

        // 아직 터지지 않은 상태. 셰이더에서 age가 아주 커져 p가 범위 밖이 된다.
        private const float NeverBursted = -1000f;

        private Graphic graphic;
        private RectTransform rectTransform;
        private Material runtimeMaterial;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            rectTransform = (RectTransform)transform;

            // 머티리얼이 비어 있으면 Graphic은 기본 UI 머티리얼을 돌려준다 — 그러면 링 자체가 안 그려진다.
            if (graphic.material == null || graphic.material == graphic.defaultMaterial)
            {
                Debug.LogWarning("[PulseRingsBurst] 머티리얼이 'ProjectS/UI Pulse Rings' 셰이더로 만든 것이 아닙니다. 링이 그려지지 않습니다.", this);
                return;
            }

            runtimeMaterial = new Material(graphic.material);
            graphic.material = runtimeMaterial;
            runtimeMaterial.SetFloat(BurstStartId, NeverBursted);
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
        }

        /// <summary>
        /// 링을 한 번 터뜨린다. 이미 퍼지는 중이면 새로 시작한다.
        /// </summary>
        /// <param name="origin">터질 위치로 삼을 UI 요소(고른 카드). null이면 상시 링의 중심(<c>_Center</c>)에서 터진다</param>
        public void Burst(RectTransform origin = null)
        {
            if (runtimeMaterial == null) return;

            runtimeMaterial.SetVector(BurstCenterId, ResolveCenterUv(origin));
            runtimeMaterial.SetFloat(BurstStartId, Time.timeSinceLevelLoad);
        }

        // 요소의 중심을 이 이미지의 0~1 UV로 바꾼다. 카드와 이 이미지가 서로 다른 부모 아래 있어도 월드 좌표를 거쳐 맞는다.
        private Vector2 ResolveCenterUv(RectTransform origin)
        {
            if (origin == null)
            {
                Vector4 center = runtimeMaterial.GetVector(CenterId);
                return new Vector2(center.x, center.y);
            }

            Vector3 world = origin.TransformPoint(origin.rect.center);
            Vector2 local = rectTransform.InverseTransformPoint(world);
            return Rect.PointToNormalized(rectTransform.rect, local);
        }
    }
}
