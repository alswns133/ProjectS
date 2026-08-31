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

        // [2026.08.12 태하] HP 수치 표기. 게이지만으로는 남은 양을 가늠하기 어려워 바 위에 숫자를 같이 띄운다.
        [SerializeField] private TextMeshProUGUI hpText;

        [Header("SG")]
        [SerializeField] private FillGauge sg;
        [SerializeField] private TextMeshProUGUI sgText;

        // {0}=현재 값, {1}=최대 값. HP/SG가 같은 표기 규칙을 쓰도록 하나로 둔다.
        private const string barValueFormat = "{0}/{1}";
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

        // [2026.08.12 태하] 저체력 구간 게이지 가독성 보정.
        // 잔여량이 적어지면 채워진 폭이 몇 픽셀 수준이라 "조금 남았는지 죽었는지" 구분이 안 된다.
        // (HP바 스프라이트가 기울어진 헥사곤이라 끝부분은 실제 폭보다 더 얇게 보인다.)
        [Header("저체력 게이지 최소 표시")]
        [Tooltip("이 비율 아래로 떨어지면 게이지를 minVisibleHpRatio로 고정한다.")]
        [SerializeField, Range(0f, 0.5f)] private float minVisibleHpThreshold = 0.02f;
        [Tooltip("고정 구간에서 그릴 게이지 비율. 0(사망)일 때는 이 값과 무관하게 완전히 비운다.")]
        [SerializeField, Range(0f, 0.2f)] private float minVisibleHpRatio = 0.02f;

        [Header("히트 콤보")]
        [Tooltip("연속 유효타 수를 그리는 텍스트. 콤보가 0일 땐 오브젝트째 숨긴다.")]
        [SerializeField] private TMP_Text hitCombo;

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

            // 시작 시엔 콤보가 0이므로 숨긴 채로 둔다. 첫 적중 이벤트(SetHitCombo)가 오면 켜진다.
            hitCombo.gameObject.SetActive(false);
        }

        public async void SetSymbol(int charId)
        {
            if (classSymbol == null) return;

            Sprite s = await ItemIconLoader.LoadAsync($"Char_Symbol_{charId}");

            if (this == null || classSymbol == null) return;
            classSymbol.sprite = s;
            classSymbol.enabled = s != null;
        }

        /// <summary>
        /// HP 게이지·비네트·심전도·수치 표기를 한 번에 갱신한다. HUDPresenter가 OnHpChanged를 받아 호출한다.
        /// 비율이 아니라 현재/최대 원본 값을 받는 이유는 "150/200" 수치 표기에 원본이 필요하기 때문이다.
        /// </summary>
        /// <param name="cur">현재 HP</param>
        /// <param name="max">최대 HP</param>
        public void SetHp(float cur, float max)
        {
            // [2026.07.13 태하] 피격/저체력 비네트 연출: HP 게이지 갱신 시 비네트에도 같은 비율을 전달.
            float ratio = max > 0f ? Mathf.Clamp01(cur / max) : 0f;

            // 게이지만 최소 표시 보정을 거친다. 비네트/심전도는 "얼마나 위험한가"를 보여주는
            // 연출이라 실제 비율을 그대로 받아야 저체력 연출이 제때 세진다.
            hp.SetRatio(GetHpDisplayRatio(ratio));
            hpVignette.SetHpRatio(ratio);
            hpEcg.SetHpRatio(ratio);

            SetBarValueText(hpText, cur, max);
        }

        /// <summary>
        /// 게이지 수치 표기 갱신("현재/최대"). 게이지의 최소 표시 보정과 달리 여기는 항상 실제 값을 보여준다.
        /// </summary>
        /// <param name="text">표기할 텍스트(미할당 허용)</param>
        /// <param name="cur">현재 값</param>
        /// <param name="max">최대 값</param>
        private void SetBarValueText(TextMeshProUGUI text, float cur, float max)
        {
            // 수치 표기가 없는 HUD(튜토리얼 등)도 있으므로 미할당을 허용한다.
            if (text == null) return;

            // 소수점 잔량이 0으로 표시되지 않게 올림한다. 실제로 다 떨어지면 정확히 0이라 0으로 나온다.
            int curDisplay = Mathf.CeilToInt(Mathf.Max(cur, 0f));
            int maxDisplay = Mathf.CeilToInt(Mathf.Max(max, 0f));

            text.text = string.Format(barValueFormat, curDisplay, maxDisplay);
        }

        /// <summary>
        /// 실제 HP 비율을 게이지에 그릴 표시 비율로 바꾼다.
        /// 0보다 크지만 임계치 아래인 구간은 minVisibleHpRatio에 고정해 "아직 남아 있다"를 계속 보이게 한다.
        /// 정확히 0(사망)일 때만 완전히 비운다 — 여기서 같이 고정하면 죽어도 게이지가 남아 보인다.
        /// </summary>
        /// <param name="ratio">현재/최대 HP 비율(0~1)</param>
        /// <returns>게이지에 넘길 표시 비율</returns>
        private float GetHpDisplayRatio(float ratio)
        {
            if (ratio <= 0f) return 0f;

            // 임계치가 고정값보다 낮으면 게이지가 오히려 줄어드는 역전이 생기므로 고정값을 하한으로 둔다.
            float threshold = Mathf.Max(minVisibleHpThreshold, minVisibleHpRatio);

            return ratio < threshold ? minVisibleHpRatio : ratio;
        }

        /// <summary>
        /// SG 게이지와 수치 표기 갱신. HUDPresenter가 OnSGChanged를 받아 호출한다.
        /// HP와 마찬가지로 "30/100" 표기에 원본이 필요해 비율이 아닌 현재/최대 값을 받는다.
        /// </summary>
        /// <param name="cur">현재 SG</param>
        /// <param name="max">최대 SG</param>
        public void SetSg(float cur, float max)
        {
            sg.SetRatio(max > 0f ? Mathf.Clamp01(cur / max) : 0f);
            SetBarValueText(sgText, cur, max);
        }

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

        /// <summary>
        /// 히트 콤보 표시를 갱신한다. 히트 수 계산·리셋은 PlayerHitCombo가 하고, 여기선 표시만 한다.
        /// 0이 오면(리셋) 텍스트를 지우는 대신 오브젝트째 숨겨 화면에서 완전히 치운다.
        /// </summary>
        /// <param name="hitCount">현재 누적 히트 수(0이면 콤보 없음)</param>
        public void SetHitCombo(int hitCount)
        {
            // 이미 숨겨져 있는데 또 0이 오면(중복 리셋 등) 할 일이 없으니 빠진다.
            if (hitCount == 0 && hitCombo.gameObject.activeSelf == false) return;

            hitCombo.gameObject.SetActive(true);
            hitCombo.SetText(hitCount.ToString());

            // 0은 "콤보 없음"이라 숫자를 남기지 않고 다시 숨긴다.
            if (hitCount == 0)
            {
                hitCombo.gameObject.SetActive(false);
            }
        }
    }
}
