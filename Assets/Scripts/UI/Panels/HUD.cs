using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUD : BasePanel
{
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Slider sgSlider;

    protected override void OnInit()
    {
        if (hpSlider == null) hpSlider = transform.Find("TopLeft/HP").GetComponentInChildren<Slider>();
        if (sgSlider == null) sgSlider = transform.Find("TopLeft/SG").GetComponentInChildren<Slider>();
    }

    public void SetHP(float value) => hpSlider.value = value;
    public void SetSG(float value) => sgSlider.value = value;
}
