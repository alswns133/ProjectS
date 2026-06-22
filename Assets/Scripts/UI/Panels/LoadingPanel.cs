using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingPanel : BasePanel
{

    [SerializeField] private Slider loadingSlider;
    [SerializeField] private TMP_Text tip;

    protected override void OnInit() 
    {
        loadingSlider = GetComponentInChildren<Slider>();
        //tip = transform.Find("Im/BottomBarIm").GetComponentInChildren<TMP_Text>();
    }

    protected override void OnShow()
    {
        string s = $"<size=125%>TIP</size>。 <size=75%>전투지역에서는 <color=#30BAD4>장비 착용중 브로치 및 소켓 아이템 변경이 불가능</color> 합니다. 안전 지역에서 브로치 및 소켓 아이템을변경해주세요.</size>";
        SetTIPText(s);
    }

    public void SetProgress(float ratio)
        => loadingSlider.value = ratio;

    public void SetTIPText(string tipText) => tip.text = tipText;
}
