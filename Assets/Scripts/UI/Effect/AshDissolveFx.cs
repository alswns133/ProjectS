using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// TMP 텍스트가 아래에서 위로 타들어가며 재가 되는 연출(<c>ProjectS/UI Ash Dissolve Text</c> 셰이더 구동).
    /// 보스 등장 텍스트가 물러날 때 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>파편을 뿌리지 않는다.</b> 조각을 날리면 "부서졌다"가 되지 "탔다"가 되지 않는다.
    /// 타는 것은 경계가 훑고 지나가며 그 자리가 없어지는 현상이라 셰이더로 픽셀을 지우는 편이 맞다.
    /// </para>
    /// <para>
    /// <b>여러 줄은 기본적으로 함께 탄다</b>(<see cref="burnTogether"/>). 묶음 전체를 하나의 경계선이
    /// 훑게 하면 그럴듯해 보이지만, 줄이 세로로 떨어져 있으면 아래 줄이 <b>전부 탄 뒤에</b> 위 줄이
    /// 타기 시작해 "한꺼번에"가 아니라 "차례대로"가 된다. 그래서 각 줄이 제 높이를 0~1로 쓰게 두고,
    /// 방향성은 <see cref="directionWeight"/>로 약하게만 준다.
    /// </para>
    /// <para>
    /// <b>머티리얼은 <see cref="TMP_Text.fontMaterial"/>(인스턴스)을 쓴다.</b> fontSharedMaterial을 만지면
    /// 같은 폰트를 쓰는 모든 텍스트가 함께 타 버린다. <see cref="GlitchTextFx"/>와 같은 방침이며,
    /// 에디터에서 만든 인스턴스에는 <see cref="HideFlags.DontSave"/>를 걸어 씬 diff를 만들지 않는다.
    /// </para>
    /// <para>시간은 unscaled로 센다. 연출 중 timeScale이 낮아져도 같은 속도로 타야 한다.</para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public class AshDissolveFx : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("태울 텍스트들. 비워두면 자식에서 모두 찾는다(비활성 포함).")]
        [SerializeField] private TMP_Text[] targets;

        [Tooltip("'ProjectS/UI Ash Dissolve Text' 셰이더. 폰트 머티리얼 인스턴스의 셰이더를 이걸로 갈아끼운다.")]
        [SerializeField] private Shader dissolveShader;

        [Header("타들어감")]
        [Tooltip("전부 타 없어지는 데 걸리는 시간(초).")]
        [SerializeField, Min(0.05f)] private float duration = 0.9f;

        [Tooltip("타는 진행 곡선. 값은 반드시 0에서 시작해 1로 끝나야 한다 " +
                 "— 시작값이 0이 아니면 첫 프레임부터 타 있는 상태라 과정 없이 사라진다. " +
                 "속도는 값이 아니라 각 키의 접선으로 조절한다.")]
        [SerializeField]
        private AnimationCurve burnCurve = new(
            new Keyframe(0f, 0f, 1.2f, 1.2f), new Keyframe(1f, 1f, 1.8f, 1.8f));

        [Header("불씨")]
        [Tooltip("경계에 남는 불씨 색.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color emberColor = new(1f, 0.45f, 0.12f, 1f);

        [Tooltip("불씨 띠의 두께.")]
        [SerializeField, Range(0f, 0.5f)] private float emberWidth = 0.07f;

        [Tooltip("경계 번짐. 크면 부드럽고 작으면 날카롭다.")]
        [SerializeField, Range(0.001f, 0.3f)] private float edgeSoftness = 0.03f;

        [Tooltip("경계가 일렁이는 굵기(px). 클수록 큰 물결로 탄다.")]
        [SerializeField, Min(1f)] private float noiseScale = 26f;

        [Tooltip("경계가 흔들리는 폭. 0이면 직선으로 잘려 셔터처럼 보인다.")]
        [SerializeField, Range(0f, 1f)] private float noiseAmount = 0.35f;

        [Tooltip("0이면 방향 없이 전체가 고루 삭고, 1이면 아래에서 위로 훑는 선이 된다. " +
                 "1에 가까우면 세로로 떨어진 줄들이 차례대로 타 보인다.")]
        [SerializeField, Range(0f, 1f)] private float directionWeight = 0.55f;

        [Tooltip("켜면 각 줄이 제 높이 안에서 동시에 탄다. 끄면 묶음 전체를 하나의 경계선이 훑는다 " +
                 "— 줄이 세로로 떨어져 있으면 아래 줄부터 차례대로 타므로 보통은 켜 두는 게 맞다.")]
        [SerializeField] private bool burnTogether = true;

        private RectTransform self;
        private readonly List<Material> materials = new();
        private Coroutine routine;
        private bool prepared;
        private bool curveWarned;

        private static readonly int DissolveID = Shader.PropertyToID("_Dissolve");
        private static readonly int EdgeColorID = Shader.PropertyToID("_EdgeColor");
        private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
        private static readonly int EdgeSoftID = Shader.PropertyToID("_EdgeSoft");
        private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseAmountID = Shader.PropertyToID("_NoiseAmount");
        private static readonly int DirectionWeightID = Shader.PropertyToID("_DirectionWeight");
        private static readonly int LocalMinYID = Shader.PropertyToID("_LocalMinY");
        private static readonly int LocalHeightID = Shader.PropertyToID("_LocalHeight");

        /// <summary>지금 타들어가는 중인지.</summary>
        public bool IsPlaying => routine != null;

        private void Awake()
        {
            self = (RectTransform)transform;
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsPlaying이 영영 true가 된다.
            routine = null;
        }

        /// <summary>
        /// 타들어감을 처음부터 재생한다. 호출부가 <c>yield return</c>으로 끝을 기다릴 수 있다.
        /// </summary>
        public IEnumerator Play()
        {
            ResetDissolve();

            if (!Prepare()) yield break;

            routine = StartCoroutine(PlayRoutine());
            yield return routine;
        }

        /// <summary>탄 상태를 원래대로 되돌린다. 재생 전에 반드시 거쳐야 다음 판이 정상으로 시작한다.</summary>
        public void ResetDissolve()
        {
            if (!Prepare()) return;

            foreach (Material material in materials) material.SetFloat(DissolveID, 0f);
        }

        private IEnumerator PlayRoutine()
        {
            WarnIfCurveMisconfigured();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                // 곡선이 잘못 잡혀도 범위를 벗어난 값이 셰이더로 새어 나가지 않게 막는다.
                float burn = Mathf.Clamp01(burnCurve.Evaluate(Mathf.Clamp01(elapsed / duration)));

                foreach (Material material in materials) material.SetFloat(DissolveID, burn);
                yield return null;
            }

            foreach (Material material in materials) material.SetFloat(DissolveID, 1f);
            routine = null;
        }

        /// <summary>
        /// 진행 곡선이 0에서 시작해 1로 끝나는지 확인한다.
        /// 시작값이 0이 아니면 첫 프레임부터 타 있는 상태라 <b>과정 없이 사라진 것처럼</b> 보이는데,
        /// 화면만 봐서는 곡선 탓인지 알기 어려워 원인을 짚어 준다(실제로 겪은 함정이다).
        /// </summary>
        private void WarnIfCurveMisconfigured()
        {
            if (curveWarned) return;

            float start = burnCurve.Evaluate(0f);
            float end = burnCurve.Evaluate(1f);
            if (start <= 0.01f && end >= 0.99f) return;

            curveWarned = true;
            Debug.LogWarning(
                $"{name}: burnCurve의 값이 0 → 1이 아닙니다(시작 {start:0.##}, 끝 {end:0.##}). " +
                "값은 0에서 시작해 1로 끝나야 하고, 속도는 값이 아니라 각 키의 접선으로 조절합니다. " +
                "필드를 우클릭 → Reset 하면 기본값으로 돌아갑니다.", this);
        }

        /// <summary>
        /// 대상 텍스트들의 폰트 머티리얼 인스턴스에 셰이더를 물리고 진행 범위·모양 값을 채운다.
        /// </summary>
        /// <returns>탈 준비가 된 머티리얼이 하나라도 있으면 true</returns>
        private bool Prepare()
        {
            if (prepared) return materials.Count > 0;
            prepared = true;

            if (self == null) self = (RectTransform)transform;
            if (targets == null || targets.Length == 0) targets = GetComponentsInChildren<TMP_Text>(true);

            if (dissolveShader == null)
            {
                Debug.LogWarning($"{name}: dissolveShader가 비어 있어 재 연출을 건너뜁니다. " +
                                 "'ProjectS/UI Ash Dissolve Text'를 넣으세요.", this);
                return false;
            }

            // 묶음 전체의 세로 범위. burnTogether가 꺼져 있을 때만 공통 기준으로 쓴다.
            Vector3[] corners = new Vector3[4];
            self.GetWorldCorners(corners);
            float groupMinY = corners[0].y;
            float groupMaxY = corners[1].y;

            foreach (TMP_Text label in targets)
            {
                if (label == null) continue;

                // 미리보기 인스턴스는 저장되지 않으므로, 미리보기를 켠 채 저장한 씬은 머티리얼 참조가
                // 비어 있는 채로 로드된다. TMP도 자기 Awake에서 복구하지만 순서가 보장되지 않아 먼저 되살린다.
                if (label.fontSharedMaterial == null && label.font != null)
                    label.fontSharedMaterial = label.font.material;

                Material material = label.fontMaterial;
                if (material == null) continue;

                material.shader = dissolveShader;

                if (!material.HasProperty(DissolveID))
                {
                    Debug.LogWarning($"{name}: {label.name}의 머티리얼에 _Dissolve가 없습니다. " +
                                     "셰이더 슬롯을 확인하세요.", this);
                    continue;
                }

                if (!Application.isPlaying) material.hideFlags = HideFlags.DontSave;

                // burnTogether면 각 라벨이 제 높이를 0~1로 쓰므로 모두 같은 시점에 타기 시작한다.
                // 끄면 묶음 범위를 라벨 로컬로 환산해, 하나의 경계선이 여러 줄을 이어서 훑는다.
                float localMinY, localHeight;
                if (burnTogether)
                {
                    Rect rect = label.rectTransform.rect;
                    localMinY = rect.yMin;
                    localHeight = rect.height;
                }
                else
                {
                    Transform t = label.transform;
                    localMinY = t.InverseTransformPoint(new Vector3(0f, groupMinY, 0f)).y;
                    localHeight = t.InverseTransformPoint(new Vector3(0f, groupMaxY, 0f)).y - localMinY;
                }

                material.SetFloat(LocalMinYID, localMinY);
                material.SetFloat(LocalHeightID, Mathf.Max(0.0001f, localHeight));

                material.SetColor(EdgeColorID, emberColor);
                material.SetFloat(EdgeWidthID, emberWidth);
                material.SetFloat(EdgeSoftID, edgeSoftness);
                material.SetFloat(NoiseScaleID, noiseScale);
                material.SetFloat(NoiseAmountID, noiseAmount);
                material.SetFloat(DirectionWeightID, directionWeight);
                material.SetFloat(DissolveID, 0f);

                materials.Add(material);
            }

            return materials.Count > 0;
        }
    }
}
