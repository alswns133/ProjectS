using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ProjectS.UI.Framework;
using ProjectS.Items;

namespace ProjectS.UI
{
    public class HUDPanel : BasePanel
    {
        [Header("HP")]
        [SerializeField] private FillGauge hp;
        [Header("SG")]
        [SerializeField] private FillGauge sg;
        [Header("EXP")]
        [SerializeField] private Image expBar;

        [Header("레벨")]
        [SerializeField] private TextMeshProUGUI levelText;

        // {0}에 레벨 숫자가 들어간다.
        private const string levelFormat = "{0}";

        // 스태미나는 FillGauge(Image/Text 참조)와 별개로 껐다 켤 루트 오브젝트가 필요하다.
        // FillGauge는 컴포넌트가 아닌 직렬화 클래스라 자기 GameObject를 모른다.
        [Header("스태미나")]
        [SerializeField] private FillGauge stamina;
        [SerializeField] private GameObject staminaRoot;

        // 인덱스 = 스킬 번호 - 1 (슬롯 0 = 스킬 1). 코드의 [0] 더미 규칙과 달리
        // 인스펙터에서 빈 첫 칸이 생기지 않게 UI 쪽은 실제 슬롯 수만큼만 둔다.
        [Header("스킬 쿨타임")]
        [SerializeField] private SkillCooldownSlot[] skillSlots = new SkillCooldownSlot[4];

        // [2026.07.13 태하] 피격/저체력 비네트 연출 추가. HP 변경을 받아 비네트에 비율을 전달한다.
        [Header("피격 효과")]
        [FormerlySerializedAs("_hpVignette")]
        [SerializeField] private HpVignette hpVignette;

        [SerializeField] private HpEcg hpEcg;

        [Header("직업 심볼")]
        [SerializeField] private Image classSymbol;

        protected override void OnInit()
        {
            hp.Init(this);
            sg.Init(this);
            stamina.Init(this);

            foreach (var slot in skillSlots)
                slot?.Init(this);

            // 시작 스태미나는 가득이므로 기본은 숨김. 첫 소모 이벤트가 오면 SetStamina가 켠다.
            staminaRoot.SetActive(false);

            hpEcg.SetMaterial(hp.Material);
        }

        public async void SetSymbol(int charId)
        {
            if (classSymbol == null) return;

            Sprite s = await ItemIconLoader.LoadAsync($"Char_Symbol_{charId}");

            if (this == null || classSymbol == null) return;
            classSymbol.sprite = s;
            classSymbol.enabled = s != null;
        }

        // [2026.07.13 태하] 피격/저체력 비네트 연출: HP 게이지 갱신 시 비네트에도 같은 비율을 전달.
        public void SetHp(float ratio)
        {
            // 낮은 HP에서 게이지가 안 보이는 문제는 HpEcgBar 셰이더의 _MinFill이 처리한다.
            // 여기서 또 보정하면 두 보정이 겹쳐 실제보다 많이 남은 것처럼 보인다.
            hp.SetRatio(ratio);
            hpVignette.SetHpRatio(ratio);
            hpEcg.SetHpRatio(ratio);
        }
        public void SetSg(float ratio) => sg.SetRatio(ratio);

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
        /// 레벨 표시 갱신. HUDPresenter가 OnLevelChanged 이벤트를 받아 호출한다.
        /// </summary>
        /// <param name="level">현재 레벨</param>
        public void SetLevel(int level)
        {
            // 레벨 표시는 HUD에 따라 없을 수도 있으므로(튜토리얼 HUD 등) 미할당을 허용한다.
            if (levelText == null) return;

            levelText.text = string.Format(levelFormat, level);
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
}
