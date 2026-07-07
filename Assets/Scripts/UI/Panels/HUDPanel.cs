using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HUDPanel : BasePanel
{
    [SerializeField] private FillGauge _hp;
    [SerializeField] private FillGauge _sg;
    [SerializeField] private Image expBar;

    // 스태미나는 FillGauge(Image/Text 참조)와 별개로 껐다 켤 루트 오브젝트가 필요하다.
    // FillGauge는 컴포넌트가 아닌 직렬화 클래스라 자기 GameObject를 모른다.
    [Header("Stamina")]
    [SerializeField] private FillGauge _stamina;
    [SerializeField] private GameObject _staminaRoot;

    protected override void OnInit()
    {
        _hp.Init(this);
        _sg.Init(this);
        _stamina.Init(this);

        // 시작 스태미나는 가득이므로 기본은 숨김. 첫 소모 이벤트가 오면 SetStamina가 켠다.
        _staminaRoot.SetActive(false);
    }

    public void SetHp(float ratio) => _hp.SetRatio(ratio);
    public void SetSg(float ratio) => _sg.SetRatio(ratio);

    /// <summary>
    /// 스태미나 게이지 갱신. 가득 차면 게이지를 숨기고, 소모 중일 때만 보여준다(기획).
    /// 회복이 끝나 비율이 1이 되는 순간 자동으로 사라진다.
    /// </summary>
    /// <param name="ratio">현재/최대 스태미나 비율(0~1)</param>
    public void SetStamina(float ratio)
    {
        _staminaRoot.SetActive(ratio < 1f);
        _stamina.SetRatio(ratio);
    }

    public void SetExp(float ratio)
    {
        expBar.fillAmount = ratio ;
    }
}
