using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 제한시간 표시. 값을 받아 그리기만 하는 뷰다 —
    /// 카운트다운과 성공/실패 판정은 <see cref="TutorialRunTrial"/>이 한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>꺼진 채 저장해도 동작한다(2026-09-18 TH).</b> 예전에는 Awake에서 무조건 숨겼는데, Unity는 꺼진 오브젝트의
    /// Awake를 처음 켜질 때 실행하므로 "Show → 켜짐 → Awake → Hide"로 타이머가 에러 없이 안 보였다.
    /// 이제 Awake는 아직 Show가 불리지 않았을 때만 숨긴다.
    /// </para>
    /// <para>
    /// <b>촉박하면 글자와 게이지를 함께 바꾼다.</b> 한쪽만 바뀌면 강조가 아니라 오류처럼 보인다.
    /// 게이지는 글자와 바탕색이 다른 경우가 많아 색을 따로 둔다.
    /// </para>
    /// <para>
    /// <b>서서히 사라지는 것은 보이던 타이머를 치울 때만이다.</b> 시작 시 숨김(TutorialRunTrial.Awake의 Hide)까지
    /// 페이드하면 씬이 열리자마자 빈 타이머가 0.5초 보였다 사라진다.
    /// </para>
    /// <para>
    /// 시간은 unscaled로 흐리게 한다. 페이드는 연출이라 timeScale과 무관하게 같은 속도여야 한다.
    /// 카운트다운 자체의 시간 기준은 값을 넣는 쪽(TutorialRunTrial)이 정한다.
    /// </para>
    /// </remarks>
    public class TutorialTimerView : MonoBehaviour
    {
        [Tooltip("켜고 끌 대상. 비우면 이 오브젝트 자신을 켜고 끈다. " +
                 "스크립트는 항상 켜져 있는 부모에 두고 여기에 타이머 묶음을 연결하는 것을 권장한다.")]
        [SerializeField] private GameObject root;

        [SerializeField] private TMP_Text timeText;

        [Tooltip("선택. 게이지 바(Image Type을 Filled로). 남은 비율만큼 채운다.")]
        [SerializeField] private Image fillImage;

        [Header("숫자")]
        [Tooltip("남은 시간 표기 형식. \"0.0\"이면 6.3처럼, \"0\"이면 6처럼 나온다.")]
        [SerializeField] private string numberFormat = "0.0";

        [Header("색")]
        [Tooltip("남은 시간이 여유 있을 때 글자 색.")]
        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("남은 시간이 촉박할 때 글자 색. 긴장감을 주려는 연출값이다.")]
        [SerializeField] private Color urgentColor = new Color(1f, 0.35f, 0.3f);

        [Tooltip("여유 있을 때 게이지 색.")]
        [SerializeField] private Color gaugeNormalColor = new Color32(0x4F, 0xD8, 0xE8, 0xFF);

        [Tooltip("촉박할 때 게이지 색.")]
        [SerializeField] private Color gaugeUrgentColor = new Color(1f, 0.35f, 0.3f);

        [Tooltip("이 시간(초) 이하로 남으면 촉박한 색으로 바뀐다.")]
        [SerializeField, Min(0f)] private float urgentThreshold = 3f;

        [Header("사라짐")]
        [Tooltip("Hide 시 서서히 사라지는 시간(초). 0이면 즉시 사라진다.")]
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;

        // 페이드용 투명도 그룹과 원래 투명도. 사라진 뒤 다음 표시를 위해 원래 값으로 되돌린다.
        private CanvasGroup rootGroup;
        private float rootAlpha = 1f;
        private bool isInitialized;

        // Show가 한 번이라도 불렸는지. 꺼진 채 저장된 오브젝트는 Show로 켜지는 순간 Awake가 도는데,
        // 그때 숨기면 안 되므로 이 값으로 가른다.
        private bool showRequested;

        // 지금 화면에 보이는 상태인지(Show 이후 Hide 전). 보이던 것을 치울 때만 페이드한다.
        private bool isShown;

        private Coroutine fadeRoutine;

        private void Awake()
        {
            EnsureInitialized();

            // 씬에 켜둔 채 저장돼 있으면 시작은 숨김이다. 도전 전에는 화면에 타이머가 떠 있으면 안 된다.
            // 단 Show가 이 오브젝트를 켜면서 Awake가 도는 경우는 건드리지 않는다(위 showRequested 설명).
            if (!showRequested) HideImmediate();
        }

        private void OnDisable()
        {
            // 페이드 도중 꺼지면 코루틴이 멈춰 반투명으로 굳는다. 다음에 켜질 때 흐리게 뜨지 않게 되돌린다.
            fadeRoutine = null;
            if (rootGroup != null) rootGroup.alpha = rootAlpha;
        }

        /// <summary>타이머를 화면에 띄운다. 도전 시작 시 호출된다. 사라지는 중이었으면 페이드를 끊고 다시 보인다.</summary>
        public void Show()
        {
            EnsureInitialized();
            showRequested = true;

            StopFade();
            if (rootGroup != null) rootGroup.alpha = rootAlpha;

            if (root != null) root.SetActive(true);
            isShown = true;
        }

        /// <summary>
        /// 타이머를 숨긴다. 성공·실패로 도전이 끝날 때 호출된다. 보이던 중이면 서서히 사라진다.
        /// </summary>
        public void Hide()
        {
            EnsureInitialized();

            bool canFade = isShown
                        && fadeOutDuration > 0f
                        && isActiveAndEnabled
                        && root != null && root.activeInHierarchy;

            if (!canFade)
            {
                HideImmediate();
                return;
            }

            isShown = false;
            StopFade();
            fadeRoutine = StartCoroutine(FadeOutRoutine());
        }

        /// <summary>페이드 없이 즉시 숨긴다.</summary>
        public void HideImmediate()
        {
            EnsureInitialized();

            StopFade();
            isShown = false;

            if (root != null) root.SetActive(false);
            if (rootGroup != null) rootGroup.alpha = rootAlpha;
        }

        /// <summary>남은 시간을 갱신한다. 매 프레임 불러도 된다.</summary>
        /// <param name="remaining">남은 초. 음수는 0으로 보정된다.</param>
        /// <param name="duration">전체 제한시간(게이지 비율 계산용). 0 이하면 게이지는 갱신하지 않는다.</param>
        public void SetRemaining(float remaining, float duration)
        {
            remaining = Mathf.Max(0f, remaining);
            bool urgent = remaining <= urgentThreshold;

            if (timeText != null)
            {
                timeText.text = remaining.ToString(numberFormat);
                timeText.color = urgent ? urgentColor : normalColor;
            }

            // 게이지는 선택 사항이라 없으면 조용히 건너뛴다.
            if (fillImage != null)
            {
                if (duration > 0f) fillImage.fillAmount = Mathf.Clamp01(remaining / duration);
                fillImage.color = urgent ? gaugeUrgentColor : gaugeNormalColor;
            }
        }

        // Show/Hide가 Awake보다 먼저 불릴 수 있어(꺼진 오브젝트) 필요한 곳마다 한 번만 준비한다.
        private void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;

            if (root == null) root = gameObject;

            // 없으면 붙인다 — 타이머 묶음마다 CanvasGroup을 챙겨 달지 않아도 페이드가 동작하게.
            if (!root.TryGetComponent(out rootGroup)) rootGroup = root.AddComponent<CanvasGroup>();
            rootAlpha = rootGroup.alpha;

            if (fillImage != null && fillImage.type != Image.Type.Filled)
            {
                Debug.LogWarning($"[TutorialTimerView] {name}: fillImage의 Image Type이 Filled가 아니라 게이지가 줄지 않는다.", this);
            }
        }

        private void StopFade()
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        private IEnumerator FadeOutRoutine()
        {
            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                rootGroup.alpha = Mathf.Lerp(rootAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            fadeRoutine = null;
            root.SetActive(false);
            rootGroup.alpha = rootAlpha;
        }
    }
}
