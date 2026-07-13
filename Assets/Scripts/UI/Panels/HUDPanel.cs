using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HUDPanel : BasePanel
{
    [Header("HP")]
    [SerializeField] private FillGauge hp;
    [Header("SG")]
    [SerializeField] private FillGauge sg;
    [Header("EXP")]
    [SerializeField] private Image expBar;

    // 스태미나는 FillGauge(Image/Text 참조)와 별개로 껐다 켤 루트 오브젝트가 필요하다.
    // FillGauge는 컴포넌트가 아닌 직렬화 클래스라 자기 GameObject를 모른다.
    [Header("Stamina")]
    [SerializeField] private FillGauge stamina;
    [SerializeField] private GameObject staminaRoot;

    // 인덱스 = 스킬 번호 - 1 (슬롯 0 = 스킬 1). 코드의 [0] 더미 규칙과 달리
    // 인스펙터에서 빈 첫 칸이 생기지 않게 UI 쪽은 실제 슬롯 수만큼만 둔다.
    [Header("Skill Cooldown")]
    [SerializeField] private SkillCooldownSlot[] skillSlots = new SkillCooldownSlot[4];

    // [2026.07.13 태하] 피격/저체력 비네트 연출 추가. HP 변경을 받아 비네트에 비율을 전달한다.
    [Header("Hit Effect")]
    [SerializeField] private HpVignette _hpVignette;

    protected override void OnInit()
    {
        hp.Init(this);
        sg.Init(this);
        stamina.Init(this);

        foreach (var slot in skillSlots)
            slot?.Init(this);

        // 시작 스태미나는 가득이므로 기본은 숨김. 첫 소모 이벤트가 오면 SetStamina가 켠다.
        staminaRoot.SetActive(false);
    }

    // [2026.07.13 태하] 피격/저체력 비네트 연출: HP 게이지 갱신 시 비네트에도 같은 비율을 전달.
    public void SetHp(float ratio)
    {
        _hp.SetRatio(ratio);
        _hpVignette.SetHpRatio(ratio);
    }
    public void SetSg(float ratio) => _sg.SetRatio(ratio);

    //public void SetHp(float ratio) => hp.SetRatio(ratio);
    //public void SetSg(float ratio) => sg.SetRatio(ratio);

    /// <summary>
    /// 스태미나 게이지 갱신. 가득 차면 게이지를 숨기고, 소모 중일 때만 보여준다(기획).
    /// 회복이 끝나 비율이 1이 되는 순간 자동으로 사라진다.
    /// </summary>
    /// <param name="ratio">현재/최대 스태미나 비율(0~1)</param>
    public void SetStamina(float ratio)
    {
        staminaRoot.SetActive(ratio < 1f);
        stamina.SetRatio(ratio);
    }

    public void SetExp(float ratio)
    {
        expBar.fillAmount = ratio ;
    }

    /// <summary>
    /// 스킬 쿨타임 카운트다운 시작. HUDPresenter가 OnSkillUsed 이벤트를 받아 호출한다.
    /// </summary>
    /// <param name="skillNumber">사용한 스킬 번호(1~)</param>
    /// <param name="duration">쿨타임 길이(초)</param>
    public void StartSkillCooldown(int skillNumber, float duration)
    {
        int index = skillNumber - 1;

        // 슬롯 수를 벗어난 스킬 번호는 UI만 조용히 건너뛴다(게임 로직은 이미 발동된 상태).
        if (index < 0 || index >= skillSlots.Length) return;

        skillSlots[index]?.StartCooldown(duration);
    }
}
