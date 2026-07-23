using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI.Framework
{
    /// <summary>
    /// SegmentVolumeBar 셰이더를 구동해 0~1 값을 세그먼트 바로 표시하는 뷰.
    /// 강화 성공률처럼 "낮을수록 위험"한 값을 그릴 때는 경고색 방향이 반대인 셰이더 변형이 필요하다
    /// (볼륨바는 값이 높을 때 hot color). 그 변형 머티리얼을 barImage에 물려주면 이 스크립트는 그대로 쓴다.
    /// (2026-07-23 TH)
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class SegmentGaugeView : MonoBehaviour
    {
        [SerializeField] private Image barImage;

        private Material instanced;
        private static readonly int ValueId = Shader.PropertyToID("_Value");

        private void Awake()
        {
            if (barImage == null) barImage = GetComponent<Image>();

            // 셰이더 프로퍼티(_Value)는 머티리얼 전역이라, 인스턴스를 뜨지 않으면
            // 같은 셰이더를 쓰는 다른 UI의 값까지 물든다. (설계 문서 주의점 — 머티리얼 인스턴스화)
            if (barImage != null && barImage.material != null)
            {
                instanced = new Material(barImage.material);
                barImage.material = instanced;
            }
        }

        /// <summary>
        /// 바 채움 비율을 설정한다.
        /// </summary>
        /// <param name="ratio">0~1 비율(범위를 벗어나면 잘라낸다)</param>
        public void SetRatio(float ratio)
        {
            if (instanced != null) instanced.SetFloat(ValueId, Mathf.Clamp01(ratio));
        }

        private void OnDestroy()
        {
            // Awake에서 뜬 인스턴스 머티리얼은 이 오브젝트만 쓰므로 함께 정리한다(누수 방지).
            if (instanced != null) Destroy(instanced);
        }
    }
}
