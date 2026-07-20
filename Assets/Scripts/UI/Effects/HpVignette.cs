using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// HP 상태를 화면 오버레이로 전달하는 View 컴포넌트. 두 이미지를 따로 제어한다:
    /// hpImage는 HP가 낮을수록 짙어지는 상시 오버레이, hitImage는 피격 순간
    /// 번쩍했다가 짧게 감쇠하는 플래시. 게임 이벤트를 직접 구독하지 않고
    /// HUDPanel이 SetHpRatio()를 호출하는 것으로만 동작한다
    /// (HUD 흐름: PlayerEvents.OnHpChanged -> HUDPresenter -> HUDPanel -> 여기).
    /// </summary>
    public class HpVignette : MonoBehaviour
    {
        // [2026.07.13 태하] 플래시와 상시 오버레이가 이미지 하나를 겸용하던 것을 분리.
        // 두 연출의 스프라이트·색을 따로 튜닝하기 위함. 기존 이미지는 hpImage로 승계된다.
        [FormerlySerializedAs("vignetteImage")]
        [SerializeField] private Image hpImage;    // HP가 낮을수록 짙어지는 상시 오버레이
        [SerializeField] private Image hitImage;   // 피격 순간 번쩍했다가 감쇠하는 플래시

        [Header("튜닝")]
        // HP 0일 때 hpImage의 알파(0~1). 255 기준 100 ≒ 0.39. 1에 가까우면 화면이
        // 완전히 가려져 플레이가 불가능해지므로 Range로 상한을 막아 둔다.
        [SerializeField, Range(0f, 0.8f)] private float maxAlpha = 0.25f;

        // 피격 순간 hitImage가 시작하는 알파. 0으로 두면 플래시 없이 상시 오버레이만 남는다.
        [SerializeField, Range(0f, 1f)] private float hitPunchAlpha = 0.25f;

        // hitImage가 0으로 가라앉는 시간.
        [SerializeField] private float punchFadeDuration = 0.25f;

        [Header("글리치 (DangerGlitchVignette 셰이더)")]
        // 이 HP 비율 아래부터 글리치가 시작된다. 비율이 여기서 0으로 떨어지는 동안
        // _Danger가 0 -> 1로 올라간다. 1로 두면 HP가 조금만 깎여도 글리치가 켜진다.
        [SerializeField, Range(0.05f, 1f)] private float glitchStartRatio = 0.4f;

        private static readonly int DangerId = Shader.PropertyToID("_Danger");

        private Material hpMaterial; // 인스턴스 복제본. 공유 머티리얼 오염 방지.
        private float punchTimer;
        private float lastRatio = 1f;

        private void Awake()
        {
            // 클릭/터치를 가로채면 안 되는 순수 연출용 오버레이.
            hpImage.raycastTarget = false;
            hitImage.raycastTarget = false;

            // UGUI의 Image.material은 공유 머티리얼이므로 복제해서 쓴다.
            // 그대로 SetFloat하면 같은 머티리얼을 쓰는 다른 UI까지 함께 글리치된다.
            if (hpImage.material != null)
            {
                hpMaterial = Instantiate(hpImage.material);
                hpImage.material = hpMaterial;
                hpMaterial.SetFloat(DangerId, 0f);
            }

            SetAlpha(hpImage, 0f);
            SetAlpha(hitImage, 0f);

            // 플래시 감쇠 중에만 Update가 돌면 되므로 평소에는 꺼 둔다.
            enabled = false;
        }

        private void OnDestroy()
        {
            if (hpMaterial != null)
                Destroy(hpMaterial);
        }

        /// <summary>
        /// HP 비율(0~1)을 반영한다. HP 변경 시마다 HUDPanel.SetHp()가 호출한다.
        /// 상시 오버레이는 항상 즉시 갱신되고, 플래시는 비율이 줄었을 때만 재생되므로
        /// 초기화·회복 시에는 번쩍임 없이 조용히 지나간다.
        /// </summary>
        /// <param name="ratio">현재/최대 HP 비율(0~1)</param>
        public void SetHpRatio(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);

            // 상시 오버레이는 플래시와 독립이라 즉시 반영해도 된다.
            SetAlpha(hpImage, maxAlpha * (1f - ratio));

            // 붉은 농도(선형)와 별개로, 글리치는 glitchStartRatio 아래에서만 올라온다.
            // 평상시엔 조용한 비네트, 위험 구간부터 모니터가 맛가기 시작하는 구조.
            if (hpMaterial != null)
            {
                float danger = Mathf.InverseLerp(glitchStartRatio, 0f, ratio);
                hpMaterial.SetFloat(DangerId, danger);
            }

            if (ratio < lastRatio)
            {
                punchTimer = punchFadeDuration;
                SetAlpha(hitImage, hitPunchAlpha);
                enabled = true;   // 감쇠가 끝나면 Update가 스스로 다시 끈다.
            }

            lastRatio = ratio;
        }

        private void Update()
        {
            punchTimer -= Time.deltaTime;

            if (punchTimer <= 0f)
            {
                SetAlpha(hitImage, 0f);
                enabled = false;
                return;
            }

            SetAlpha(hitImage, hitPunchAlpha * (punchTimer / punchFadeDuration));
        }

        private void SetAlpha(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }
    }
}
