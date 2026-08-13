using UnityEngine;
using UnityEngine.UI;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// HP바 심전도(HpEcgBar 셰이더) 연출 View. HP 비율을 _Alive(1=정상 박동, 0=플랫라인)에
    /// 그대로 전달해 체력이 깎일수록 파형이 잦아들고, 0이 되면 플랫라인이 된다.
    /// 게임 이벤트를 직접 구독하지 않고 HUDPanel이 SetHpRatio()를 호출하는 것으로만 동작한다
    /// (HUD 흐름: PlayerEvents.OnHpChanged -> HUDPresenter -> HUDPanel -> 여기).
    /// </summary>
    public class HpEcg : MonoBehaviour
    {
        [SerializeField] private Image ecgImage;

        // [2026.08.12 태하] 저체력에서 _Alive가 0에 가까워지면 파형이 사실상 평평해져
        // 게이지가 남아 있어도 이미 죽은 것처럼 보인다. HP 구간 전체를 minAlive~1로 리매핑해
        // 박동이 계속 눈에 띄면서도 체력이 깎일수록 약해지는 변화는 유지한다.
        // 진짜 플랫라인은 사망(비율 0)일 때만.
        [SerializeField, Range(0f, 1f)] private float minAlive = 0.12f;

        private static readonly int AliveId = Shader.PropertyToID("_Alive");

        // 인스턴스 복제본. 에셋 머티리얼에 직접 SetFloat하면 에디터에서 .mat 파일이
        // 영구 수정되고, 같은 머티리얼을 쓰는 다른 UI에도 값이 번진다.
        private Material _material;

        //private void Awake()
        //{
        //    _material = Instantiate(ecgImage.material);
        //    ecgImage.material = _material;
        //    _material.SetFloat(AliveId, 1f);
        //}

        //private void OnDestroy()
        //{
        //    if (_material != null)
        //        Destroy(_material);
        //}

        public void SetMaterial(Material material)
        {
            _material = material;
        }

        /// <summary>
        /// HP 비율(0~1)을 _Alive에 반영한다. HP 변경 시마다 HUDPanel.SetHp()가 호출한다.
        /// 살아 있는 동안(비율 &gt; 0)은 minAlive~1 구간으로 리매핑되고, 사망 시에만 0(플랫라인)이 된다.
        /// </summary>
        /// <param name="ratio">현재/최대 HP 비율(0~1)</param>
        public void SetHpRatio(float ratio)
        {
            // HP가 줄면 파형도 같이 약해지되, 하한이 minAlive라 0 직전까지 박동이 보인다.
            float alive = ratio <= 0f ? 0f : Mathf.Lerp(minAlive, 1f, Mathf.Clamp01(ratio));
            _material.SetFloat(AliveId, alive);

            // Mask(스텐실) 아래에서 UGUI는 base 머티리얼이 아니라 '스텐실 패치 복사본'으로
            // 렌더링한다. 복사본은 생성 시점의 값 스냅샷이라 base에만 쓰면 화면이 안 바뀐다.
            // 실제 렌더링에 쓰이는 쪽에도 같이 써 준다(마스크가 없으면 같은 객체라 무해).
            Material rendering = ecgImage.materialForRendering;
            if (rendering != _material)
                rendering.SetFloat(AliveId, alive);
        }
    }
}
