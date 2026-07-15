using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HP바 심전도(HpEcgBar 셰이더) 연출 View. HP 비율을 _Alive(1=정상 박동, 0=플랫라인)에
/// 그대로 전달해 체력이 깎일수록 파형이 잦아들고, 0이 되면 플랫라인이 된다.
/// 게임 이벤트를 직접 구독하지 않고 HUDPanel이 SetHpRatio()를 호출하는 것으로만 동작한다
/// (HUD 흐름: PlayerEvents.OnHpChanged -> HUDPresenter -> HUDPanel -> 여기).
/// </summary>
public class HpEcg : MonoBehaviour
{
    [SerializeField] private Image ecgImage;

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
    /// </summary>
    /// <param name="ratio">현재/최대 HP 비율(0~1)</param>
    public void SetHpRatio(float ratio)
    {
        float alive = Mathf.Clamp01(ratio);
        _material.SetFloat(AliveId, alive);

        // Mask(스텐실) 아래에서 UGUI는 base 머티리얼이 아니라 '스텐실 패치 복사본'으로
        // 렌더링한다. 복사본은 생성 시점의 값 스냅샷이라 base에만 쓰면 화면이 안 바뀐다.
        // 실제 렌더링에 쓰이는 쪽에도 같이 써 준다(마스크가 없으면 같은 객체라 무해).
        Material rendering = ecgImage.materialForRendering;
        if (rendering != _material)
            rendering.SetFloat(AliveId, alive);
    }
}
