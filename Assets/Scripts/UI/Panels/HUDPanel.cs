using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HUDPanel : BasePanel
{
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Slider mpSlider;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text goldText;

    protected override void OnInit()
    {
        if(hpSlider == null) hpSlider = transform.Find("HpSlider").GetComponent<Slider>();
        if(mpSlider == null) mpSlider = transform.Find("MpSlider").GetComponent<Slider>();
        if(levelText == null) levelText = transform.Find("LevelText").GetComponent<TMP_Text>();
        if(goldText == null) goldText = transform.Find("GoldText").GetComponent<TMP_Text>();
    }

    // Presenter가 호출하는 메서드들
    // 화면에 값 꽂아주는 것만 담당!
    public void SetHp(float ratio) => hpSlider.value = ratio;
    public void SetMp(float ratio) => mpSlider.value = ratio;
    public void SetLevel(int level) => levelText.text = $"Lv.{level}";
    public void SetGold(int gold) => goldText.text = $"{gold}G";
}
