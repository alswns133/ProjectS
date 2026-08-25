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
    /// <remarks>
    /// <para>
    /// <b>강화창 성공률 게이지로 쓸 때는 값을 직접 꽂지 않고 이펙트 게이지를 따라간다.</b>
    /// 강화 게이지의 채움(<see cref="Image.fillAmount"/>)은 <c>EnhanceGaugeSweep</c> 하나가 소유한다
    /// (평상시엔 현 단계 성공률 자리, 강화 중엔 정점까지 쓸어올렸다 성공/실패로 분기).
    /// <c>RadialFlowGaugeFx</c>가 그 fillAmount를 매 프레임 읽어 흐름·담금질·불똥까지 몰고 가는데,
    /// 세그먼트 표시도 <b>같은 값을 따라야</b> 스윕 연출과 어긋나지 않는다. 그래서 성공률을 팝업에서
    /// 직접 <see cref="SetRatio"/>로 넣지 말고, <see cref="sourceFill"/>에 그 게이지 Image(GaugeF)를
    /// 물려 이 뷰가 매 프레임 fillAmount를 읽게 한다 — RadialFlowGaugeFx가 <c>_Fill</c>을 읽는 것과
    /// 똑같은 <b>비소유 미러 패턴</b>이다(값의 주인은 EnhanceGaugeSweep 하나여야 궤적이 안 튄다).
    /// </para>
    /// <para>
    /// <see cref="sourceFill"/>을 비워두면 예전처럼 <see cref="SetRatio"/>로 값을 직접 넣는
    /// 독립 위젯으로 동작한다(볼륨 바 등). 프레임워크 계층이라 특정 강화 컴포넌트를 참조하지 않고
    /// 일반 Image의 fillAmount만 미러링한다 — 강화창 배선은 씬에서 이뤄진다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Image))]
    public class SegmentGaugeView : MonoBehaviour
    {
        [SerializeField] private Image barImage;

        [Tooltip("연결하면 이 Image의 fillAmount를 매 프레임 그대로 따라간다(강화 게이지 이펙트와 동기). " +
                 "강화창에서는 GaugeF의 Image를 물린다. 비워두면 SetRatio로 값을 직접 넣는 독립 위젯이 된다.")]
        [SerializeField] private Image sourceFill;

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

        private void Update()
        {
            // 이펙트 게이지에 물려 있으면 그 채움을 그대로 미러링한다. 여기서 fillAmount를
            // 대신 세팅하지 않는다 — 값의 주인은 EnhanceGaugeSweep 하나이고, 이 뷰는 읽기만 한다
            // (RadialFlowGaugeFx가 같은 fillAmount를 읽어 흐름·열을 만드는 것과 같은 규칙).
            if (sourceFill != null) SetRatio(sourceFill.fillAmount);
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
