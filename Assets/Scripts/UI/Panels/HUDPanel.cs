using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ProjectS.UI.Framework;
using ProjectS.Items;
using ProjectS.Managers;
using ProjectS.Players;

namespace ProjectS.UI
{
    [System.Serializable]
    public class LevelColor
    {
        public int charaterId;
        public Color decorColor = new(0, 0, 0, 0);
        public Color decorPatternColor = new(0, 0, 0, 0);
        public Color decorFrameColor = new(0, 0, 0, 0);
    }
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
        [SerializeField] private Image decor;
        [SerializeField] private Image decorPattern;
        [SerializeField] private Image decorFrame;
        [SerializeField] private LevelColor[] levelColor;

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

        // [2026.09.15 태하] 콤보 증가 연출(튕김·단계 색·유지 게이지·퇴장 페이드).
        // 숫자만 바뀌면 "올라갔다"는 게 눈에 안 띄어 타격감이 약하다는 피드백으로 추가.
        [Tooltip("콤보 유지 시간 게이지로 쓸 그래픽. 경고 깜빡임도 이 그래픽에 적용된다. 미할당이면 게이지 연출만 생략한다.\n" +
                 "· Image(Type = Filled)면 fillAmount로 줄인다.\n" +
                 "· COMBO 라벨을 게이지로 쓸 땐 밝은 라벨 텍스트를 넣고 아래 Timer Mask를 함께 지정한다.\n" +
                 "숫자 텍스트의 자식으로 두면 숨김·페이드가 같이 적용된다.")]
        [SerializeField] private Graphic hitComboTimerFill;

        // [2026.09.15 태하] 라벨 게이지. 어두운 COMBO 위에 겹친 밝은 COMBO를 RectMask2D로 오른쪽부터 잘라 남은 시간을 표현한다.
        // Image fillAmount는 텍스트에 못 쓰고 셰이더 수정은 TMP 머티리얼을 건드려야 해서, 코드만으로 되는 마스크 방식을 택했다.
        [Tooltip("라벨 게이지용 마스크. 밝은 라벨 텍스트의 부모에 붙인 RectMask2D. 남은 시간만큼 오른쪽 padding을 늘려 잘라낸다.\n" +
                 "경계를 부드럽게 하려면 RectMask2D의 Softness를 올린다.")]
        [SerializeField] private RectMask2D hitComboTimerMask;

        [Header("히트 콤보 - 튕김")]
        [Tooltip("튕길 대상. 비우면 콤보 숫자 텍스트 자체를 튕긴다(자식인 COMBO 라벨도 함께 커진다).")]
        [SerializeField] private RectTransform hitComboPunchTarget;
        [Tooltip("1타 적중 순간의 최대 스케일. 1이면 튕기지 않는다.")]
        [SerializeField, Min(1f)] private float hitComboPunchScale = 1.35f;
        [Tooltip("같은 프레임에 여러 타가 들어올 때(광역) 추가 1타당 최대 스케일에 더할 값.")]
        [SerializeField, Min(0f)] private float hitComboMultiHitBonusScale = 0.05f;
        [Tooltip("최대 스케일 상한. 광역 보너스·단계 달성 스케일이 겹쳐도 이 값을 넘지 않는다.")]
        [SerializeField, Min(1f)] private float hitComboMaxPunchScale = 1.8f;
        [Tooltip("최대 스케일에서 원래 크기로 돌아오는 시간(초). 히트스톱 중에도 돌도록 unscaled 시간으로 잰다.")]
        [SerializeField, Min(0.01f)] private float hitComboPunchDuration = 0.15f;
        [Tooltip("튕김 모양. 가로 = 진행도(0~1), 세로 = 튕김 비중(1 = 최대 스케일, 0 = 원래 크기).\n" +
                 "끝을 0 아래로 살짝 내리면 원래 크기보다 작아졌다 돌아오는 반동이 생긴다.")]
        [SerializeField] private AnimationCurve hitComboPunchCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [Tooltip("적중 순간 번쩍일 색. 튕김 시간 동안 현재 단계 색으로 돌아온다.")]
        [SerializeField] private Color hitComboFlashColor = Color.white;

        [Header("히트 콤보 - 단계")]
        [Tooltip("콤보 수 단계별 색·달성 강조. minCount 오름차순으로 적는다.\n" +
                 "첫 단계 미만이면 텍스트에 원래 지정된 색을 쓴다.")]
        [SerializeField] private HitComboTier[] hitComboTiers =
        {
            new HitComboTier { minCount = 10, color = new Color(1f, 0.62f, 0.2f), milestonePunchScale = 1.6f },
            new HitComboTier { minCount = 30, color = new Color(1f, 0.38f, 0.18f), milestonePunchScale = 1.7f },
            new HitComboTier { minCount = 50, color = new Color(1f, 0.2f, 0.25f), milestonePunchScale = 1.8f },
        };

        [Header("히트 콤보 - 유지 게이지·퇴장")]
        [Tooltip("남은 유지 시간이 이 비율 이하가 되면 게이지를 깜빡여 곧 끊긴다고 알린다.")]
        [SerializeField, Range(0f, 1f)] private float hitComboWarningRatio = 0.2f;
        [Tooltip("경고 깜빡임 속도(라디안/초).")]
        [SerializeField, Min(0f)] private float hitComboWarningBlinkSpeed = 18f;
        [Tooltip("콤보가 끊길 때 사라지는 시간(초). 0이면 즉시 숨긴다.")]
        [SerializeField, Min(0f)] private float hitComboFadeOutDuration = 0.2f;

        [System.Serializable]
        private class HitComboTier
        {
            [Tooltip("이 콤보 수 이상이면 이 단계.")]
            public int minCount;
            [Tooltip("이 단계의 숫자 색.")]
            public Color color = Color.white;
            [Tooltip("이 단계에 처음 올라선 순간의 최대 스케일. 일반 튕김보다 크게 두어 달성감을 준다.")]
            [Min(1f)] public float milestonePunchScale = 1.6f;
        }

        // 숨김/페이드를 COMBO 라벨(자식)까지 한 번에 적용하려고 숫자 오브젝트에 붙은(없으면 추가한) CanvasGroup을 쓴다.
        private CanvasGroup hitComboGroup;
        private Color hitComboBaseColor;
        private Color hitComboTierColor;
        private Color hitComboTimerFillBaseColor;

        // 표시에 반영된 콤보 수 / 튕김까지 반영된 콤보 수.
        // 둘을 나눠 LateUpdate에서 차이만큼 한 번 튕긴다 → 광역으로 한 프레임에 여러 번 올라도 튕김이 매번 처음부터 다시 시작되지 않는다.
        private int shownHitCount;
        private int punchedHitCount;

        private bool isHitComboPunching;
        private float hitComboPunchElapsed;
        private float hitComboPunchPeak = 1f;

        private bool isHitComboFadingOut;
        private float hitComboFadeElapsed;

        private float hitComboTimerRatio;

        // OnInit에서 원래 색을 저장하기 전에는 콤보 연출을 돌리지 않는다.
        // 패널이 씬에 활성으로 배치돼 있으면 UIManager가 Show(→OnInit)를 부르기 전에 LateUpdate가 먼저 돈다.
        // 그때 기본값 색(0,0,0,0)을 그래픽에 써 버리면, 뒤늦게 OnInit이 그 검은색을 "원래 색"으로 저장해 영구히 안 보이게 된다.
        private bool isHitComboReady;

        private RectTransform HitComboPunchTarget
            => hitComboPunchTarget != null ? hitComboPunchTarget : hitCombo.rectTransform;

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

            // 단계 색은 이 원래 색을 기준으로 바뀌므로, 코드가 색을 건드리기 전에 한 번 저장한다.
            hitComboBaseColor = hitCombo.color;
            hitComboTierColor = hitComboBaseColor;

            if (!hitCombo.TryGetComponent(out hitComboGroup))
                hitComboGroup = hitCombo.gameObject.AddComponent<CanvasGroup>();

            if (hitComboTimerFill != null)
                hitComboTimerFillBaseColor = hitComboTimerFill.color;

            // 시작 시엔 콤보가 0이므로 숨긴 채로 둔다. 첫 적중 이벤트(SetHitCombo)가 오면 켜진다.
            hitCombo.gameObject.SetActive(false);
            isHitComboReady = true;
        }

        private void LateUpdate()
        {
            if (!isHitComboReady || !hitCombo.gameObject.activeSelf) return;

            // 이번 프레임에 오른 만큼을 한 번의 튕김으로 합친다(광역 다중 적중 대비).
            if (shownHitCount > punchedHitCount)
            {
                StartHitComboPunch(punchedHitCount, shownHitCount);
                punchedHitCount = shownHitCount;
            }

            // HUD 연출은 히트스톱(timeScale 저하) 중에도 멈추면 안 되므로 unscaled 시간을 쓴다.
            float dt = Time.unscaledDeltaTime;
            UpdateHitComboPunch(dt);
            UpdateHitComboTimerFill();
            UpdateHitComboFadeOut(dt);
        }

        private void OnDisable()
        {
            // 페이드·튕김 도중 HUD가 꺼지면 LateUpdate가 멈춰 반쯤 투명/커진 채로 남는다. 최종 상태로 즉시 정리한다.
            if (!isHitComboReady) return;

            if (isHitComboFadingOut)
                HideHitComboImmediate();

            EndHitComboPunch();
        }

        /// <summary>
        /// 직업 심볼을 캐릭터 타입에 맞춰 바꾼다. HudPresenter가 씬 진입·스탯 리프레시 때 호출한다.
        /// 그림은 <see cref="PlayerManager.Roster"/>에서 꺼낸다 — 상주 UI라 어드레서블 비동기 로드가 필요 없다.
        /// </summary>
        /// <param name="charId">캐릭터 타입(1=검사, 2=거너 …)</param>
        public void SetSymbol(int charId)
        {
            if (classSymbol == null) return;

            CharacterRoster roster = PlayerManager.Instance != null ? PlayerManager.Instance.Roster : null;
            Sprite s = roster != null ? roster.GetSymbol(charId) : null;

            classSymbol.sprite = s;
            classSymbol.enabled = s != null;
        }

        public void SetLevelColor(int charId)
        {
            if (decor == null || decorFrame == null|| decorPattern == null) return;

            if (levelColor == null) return;

            foreach(var v in levelColor)
            {
                if(v.charaterId == charId)
                {
                    decor.color = v.decorColor;
                    decorFrame.color = v.decorFrameColor;
                    decorPattern.color = v.decorPatternColor;
                    return;
                }
            }
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
            expBar.fillAmount = ratio;
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
        /// 모든 스킬 슬롯의 쿨타임 표시를 즉시 걷어낸다. HUDPresenter가 OnSkillCooldownsReset을 받아 호출한다.
        /// </summary>
        public void ClearSkillCooldowns()
        {
            foreach (SkillCooldownSlot slot in skillSlots)
                slot?.Clear();
        }

        /// <summary>
        /// 히트 콤보 표시를 갱신한다. 히트 수 계산·리셋은 PlayerHitCombo가 하고, 여기선 표시만 한다.
        /// 증가는 LateUpdate에서 튕김으로, 0(리셋)은 페이드 아웃 후 오브젝트째 숨기는 것으로 표현한다.
        /// </summary>
        /// <param name="hitCount">현재 누적 히트 수(0이면 콤보 없음)</param>
        public void SetHitCombo(int hitCount)
        {
            // OnInit 전(CanvasGroup·원래 색 준비 전)에 온 갱신은 무시한다. OnInit이 어차피 콤보를 숨긴 상태로 시작한다.
            if (!isHitComboReady) return;

            if (hitCount <= 0)
            {
                BeginHitComboFadeOut();
                return;
            }

            // 페이드 도중 다시 적중하면 페이드를 취소하고 새 콤보로 이어 보여준다.
            ShowHitCombo();
            hitCombo.SetText(hitCount.ToString());
            shownHitCount = hitCount;
        }

        /// <summary>
        /// 콤보 유지 게이지 비율을 전달한다. HUDPresenter가 매 프레임 PlayerHitCombo.RemainingRatio를 읽어 호출한다.
        /// 실제 그리기는 LateUpdate에서 경고 깜빡임과 함께 처리한다.
        /// </summary>
        /// <param name="ratio">남은 유지 시간 비율(1 = 방금 적중, 0 = 곧 끊김)</param>
        public void SetHitComboTimer(float ratio)
        {
            hitComboTimerRatio = Mathf.Clamp01(ratio);
        }

        public void SetHitComboVisible(bool visible)
        {
            if (!isHitComboReady) return;

            if (visible)
                ShowHitCombo();
            else
                HideHitComboImmediate();
        }

        private void ShowHitCombo()
        {
            isHitComboFadingOut = false;
            hitComboGroup.alpha = 1f;
            hitCombo.gameObject.SetActive(true);
        }

        private void BeginHitComboFadeOut()
        {
            // 이미 숨겨져 있거나 페이드 중이면(중복 리셋 등) 할 일이 없다.
            if (!hitCombo.gameObject.activeSelf || isHitComboFadingOut) return;

            // 페이드를 쓰지 않거나 HUD가 꺼져 LateUpdate가 돌 수 없으면 바로 숨긴다(반투명으로 남는 것 방지).
            if (hitComboFadeOutDuration <= 0f || !isActiveAndEnabled)
            {
                HideHitComboImmediate();
                return;
            }

            isHitComboFadingOut = true;
            hitComboFadeElapsed = 0f;
        }

        private void HideHitComboImmediate()
        {
            isHitComboFadingOut = false;
            EndHitComboPunch();
            hitComboGroup.alpha = 1f;
            hitCombo.gameObject.SetActive(false);

            // 다음 콤보는 1부터 다시 튕기고, 단계 색도 처음부터 다시 오른다.
            shownHitCount = 0;
            punchedHitCount = 0;
            hitComboTierColor = hitComboBaseColor;
            hitCombo.color = hitComboBaseColor;
        }

        private void UpdateHitComboFadeOut(float dt)
        {
            if (!isHitComboFadingOut) return;

            hitComboFadeElapsed += dt;
            float t = Mathf.Clamp01(hitComboFadeElapsed / hitComboFadeOutDuration);
            hitComboGroup.alpha = 1f - t;

            if (t >= 1f)
                HideHitComboImmediate();
        }

        /// <summary>
        /// from → to로 오른 만큼 튕김을 시작한다. 광역으로 한 번에 많이 오를수록, 새 단계에 올라설수록 크게 튕긴다.
        /// </summary>
        private void StartHitComboPunch(int from, int to)
        {
            int gain = to - from;
            float peak = hitComboPunchScale + (gain - 1) * hitComboMultiHitBonusScale;

            int fromTier = GetHitComboTierIndex(from);
            int toTier = GetHitComboTierIndex(to);

            // 단계를 새로 달성한 순간만 달성 스케일로 키운다. 같은 단계 안에서는 일반 튕김.
            if (toTier > fromTier)
                peak = Mathf.Max(peak, hitComboTiers[toTier].milestonePunchScale);

            hitComboPunchPeak = Mathf.Max(1f, Mathf.Min(peak, hitComboMaxPunchScale));
            hitComboTierColor = toTier >= 0 ? hitComboTiers[toTier].color : hitComboBaseColor;

            hitComboPunchElapsed = 0f;
            isHitComboPunching = true;
        }

        private void UpdateHitComboPunch(float dt)
        {
            if (!isHitComboPunching) return;

            hitComboPunchElapsed += dt;
            float t = Mathf.Clamp01(hitComboPunchElapsed / hitComboPunchDuration);
            float weight = hitComboPunchCurve.Evaluate(t);

            // Unclamped: 곡선을 0 아래/1 위로 그려 반동을 줄 수 있게 한다.
            HitComboPunchTarget.localScale = Vector3.one * Mathf.LerpUnclamped(1f, hitComboPunchPeak, weight);
            hitCombo.color = Color.Lerp(hitComboTierColor, hitComboFlashColor, weight);

            // 곡선 끝값이 0이 아니어도 원래 크기·단계 색으로 확실히 돌려놓는다.
            if (t >= 1f)
                EndHitComboPunch();
        }

        private void EndHitComboPunch()
        {
            isHitComboPunching = false;
            HitComboPunchTarget.localScale = Vector3.one;
            hitCombo.color = hitComboTierColor;
        }

        private void UpdateHitComboTimerFill()
        {
            if (hitComboTimerMask != null)
                UpdateHitComboTimerMask();

            if (hitComboTimerFill == null) return;

            if (hitComboTimerFill is Image image && image.type == Image.Type.Filled)
                image.fillAmount = hitComboTimerRatio;

            Color color = hitComboTimerFillBaseColor;

            // 곧 끊기는 구간에서만 깜빡인다. 0(이미 리셋)은 페이드 중이라 깜빡일 필요가 없다.
            if (hitComboTimerRatio > 0f && hitComboTimerRatio <= hitComboWarningRatio)
            {
                float blink = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * hitComboWarningBlinkSpeed);
                color.a *= Mathf.Lerp(0.25f, 1f, blink);
            }

            hitComboTimerFill.color = color;
        }

        /// <summary>
        /// 라벨 게이지 마스크의 오른쪽 padding을 남은 시간에 맞춰 조절한다. 왼쪽을 기준으로 남기고 오른쪽부터 줄어든다.
        /// </summary>
        private void UpdateHitComboTimerMask()
        {
            RectTransform maskRect = hitComboTimerMask.rectTransform;
            Rect rect = maskRect.rect;

            // 기본은 마스크 전체 폭을 게이지 구간으로 쓴다.
            float gaugeStart = 0f;
            float gaugeEnd = rect.width;

            // 텍스트는 가운데 정렬이라 글자가 마스크 폭의 일부만 차지한다. 마스크 폭 기준으로 자르면
            // 앞부분 시간 동안은 빈 여백만 잘려 변화가 안 보이고, 끝나기 한참 전에 글자가 다 사라진다.
            // 그래서 실제 글자가 그려진 범위를 게이지 구간으로 삼는다.
            if (hitComboTimerFill is TMP_Text text && text.textInfo != null && text.textInfo.characterCount > 0)
            {
                Bounds bounds = text.textBounds;
                Vector3 min = maskRect.InverseTransformPoint(text.rectTransform.TransformPoint(bounds.min));
                Vector3 max = maskRect.InverseTransformPoint(text.rectTransform.TransformPoint(bounds.max));

                gaugeStart = Mathf.Clamp(min.x - rect.xMin, 0f, rect.width);
                gaugeEnd = Mathf.Clamp(max.x - rect.xMin, gaugeStart, rect.width);
            }

            float edge = Mathf.Lerp(gaugeStart, gaugeEnd, hitComboTimerRatio);

            // padding = (left, bottom, right, top)
            hitComboTimerMask.padding = new Vector4(0f, 0f, rect.width - edge, 0f);
        }

        /// <summary>해당 콤보 수가 속한 단계 인덱스. 첫 단계 미만이면 -1.</summary>
        private int GetHitComboTierIndex(int hitCount)
        {
            int index = -1;
            if (hitComboTiers == null) return index;

            // minCount 오름차순 전제. 조건을 만족하는 마지막 단계가 현재 단계다.
            for (int i = 0; i < hitComboTiers.Length; i++)
            {
                if (hitCount >= hitComboTiers[i].minCount)
                    index = i;
            }

            return index;
        }
    }
}
