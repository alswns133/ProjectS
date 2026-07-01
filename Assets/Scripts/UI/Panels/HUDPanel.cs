using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HUDPanel : BasePanel
{
    [SerializeField] private FillGauge _hp;
    [SerializeField] private FillGauge _sg;
    [SerializeField] private TMP_Text expText;
    [SerializeField] private Image expBar;


    protected override void OnInit()
    {
        _hp.Init(this);
        _sg.Init(this);
    }

    public void SetHp(float ratio) => _hp.SetRatio(ratio);
    public void SetSg(float ratio) => _sg.SetRatio(ratio);

    public void SetExp(float ratio)
    { 
        expText.text = $"{ratio * 100}%";
        expBar.fillAmount = ratio ;
    }
}
