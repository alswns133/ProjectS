using UnityEngine;

namespace ProjectS.Effects
{
    /// <summary>
    /// Emission을 랜덤 간격으로 켜고 끄다가, 가끔 짧고 빠른 연속 깜빡임(버스트)을 섞어 고장 난 조명 느낌을 냅니다.
    /// 머티리얼 복사본이 생기지 않도록 <see cref="MaterialPropertyBlock"/>으로 Emission 색만 바꿉니다.
    /// 대상 머티리얼은 Emission이 켜져 있어야 합니다(꺼져 있으면 빌드에서 셰이더 변형이 빠져 안 보일 수 있음).
    /// </summary>
    public class EmissionFlicker : MonoBehaviour
    {
        // 프로젝트 주력 셰이더(Synty Generic_Basic, FogControlLit)는 _Emission_Color를 쓰고, URP Lit/Standard는 _EmissionColor를 쓴다.
        // Synty 머티리얼에도 _EmissionColor 값이 잔재로 남아 있어, 셰이더가 실제로 읽는 _Emission_Color를 먼저 확인해야 한다.
        private static readonly int SyntyEmissionColorId = Shader.PropertyToID("_Emission_Color");
        private static readonly int SyntyEnableEmissionId = Shader.PropertyToID("_Enable_Emission");
        private static readonly int UnityEmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("대상")]
        [Tooltip("비워두면 자신과 자식의 Renderer 전체를 사용합니다.")]
        [SerializeField] private Renderer[] targetRenderers;
        [Tooltip("함께 깜빡일 Light. 비워두면 Emission만 깜빡입니다.")]
        [SerializeField] private Light[] syncLights;

        [Header("켜졌을 때 색")]
        [Tooltip("켜면 머티리얼에 설정된 원래 Emission 색을 쓰고, 끄면 아래 onColor를 씁니다.")]
        [SerializeField] private bool useMaterialColor = true;
        [SerializeField, ColorUsage(false, true)] private Color onColor = Color.white * 2f;
        [Tooltip("켜졌을 때 색에 곱하는 배율. Bloom 강도 조절용.")]
        [SerializeField] private float intensityMultiplier = 1f;

        [Header("일반 깜빡임 (초, 랜덤 범위)")]
        [SerializeField] private Vector2 onDuration = new Vector2(0.3f, 2.0f);
        [SerializeField] private Vector2 offDuration = new Vector2(0.05f, 0.3f);

        [Header("연속 빠른 깜빡임 (버스트)")]
        [Tooltip("일반 깜빡임 한 주기가 끝날 때 버스트로 넘어갈 확률.")]
        [SerializeField, Range(0f, 1f)] private float burstChance = 0.2f;
        [Tooltip("버스트 한 번에 On/Off를 반복할 횟수(랜덤 범위, 최대값 포함).")]
        [SerializeField] private Vector2Int burstCount = new Vector2Int(3, 7);
        [Tooltip("버스트 중 한 번 켜지거나 꺼져 있는 시간(초, 랜덤 범위).")]
        [SerializeField] private Vector2 burstStepDuration = new Vector2(0.02f, 0.07f);

        private MaterialPropertyBlock block;
        private Color[][] baseColors;
        private int[][] colorPropertyIds; // 머티리얼마다 셰이더가 달라 바꿀 프로퍼티도 다르다. -1이면 Emission이 없는 머티리얼.
        private float[] baseLightIntensities;

        private bool isOn = true;
        private float timer;
        private int burstStepsLeft;

        private void Awake()
        {
            if (targetRenderers == null || targetRenderers.Length == 0)
                targetRenderers = GetComponentsInChildren<Renderer>(true);

            block = new MaterialPropertyBlock();
            CacheBaseColors();
            CacheLightIntensities();
        }

        private void OnEnable()
        {
            isOn = true;
            burstStepsLeft = 0;
            timer = RandomRange(onDuration);
            Apply();
        }

        private void OnDisable()
        {
            // 꺼진 채로 비활성화되면 다시 켤 때까지 어둡게 남으므로 원래 상태로 되돌린다.
            isOn = true;
            Apply();
        }

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;

            if (burstStepsLeft > 0)
            {
                burstStepsLeft--;
                isOn = !isOn;

                // 버스트 마지막 스텝은 반드시 켜진 상태로 끝내 일반 주기로 자연스럽게 이어지게 한다.
                if (burstStepsLeft == 0 && !isOn)
                    burstStepsLeft = 1;

                timer = RandomRange(burstStepDuration);
            }
            else if (isOn && Random.value < burstChance)
            {
                // 짝수 스텝이어야 켜진 상태로 끝나므로 On/Off 한 쌍 단위로 센다.
                burstStepsLeft = Random.Range(burstCount.x, burstCount.y + 1) * 2;
                timer = 0f;
                return;
            }
            else
            {
                isOn = !isOn;
                timer = isOn ? RandomRange(onDuration) : RandomRange(offDuration);
            }

            Apply();
        }

        private void CacheBaseColors()
        {
            baseColors = new Color[targetRenderers.Length][];
            colorPropertyIds = new int[targetRenderers.Length][];
            for (int r = 0; r < targetRenderers.Length; r++)
            {
                Material[] materials = targetRenderers[r] != null ? targetRenderers[r].sharedMaterials : new Material[0];
                baseColors[r] = new Color[materials.Length];
                colorPropertyIds[r] = new int[materials.Length];
                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];
                    int propertyId = FindEmissionColorId(material);
                    colorPropertyIds[r][m] = propertyId;
                    if (propertyId == -1) continue;

                    if (propertyId == SyntyEmissionColorId && material.HasProperty(SyntyEnableEmissionId)
                        && material.GetFloat(SyntyEnableEmissionId) < 0.5f)
                    {
                        // Synty 계열은 색에 _Enable_Emission을 곱하므로 토글이 꺼져 있으면 색을 바꿔도 보이지 않는다.
                        Debug.LogWarning($"[EmissionFlicker] '{material.name}'의 Enable Emission이 꺼져 있어 깜빡임이 보이지 않습니다.", this);
                    }

                    Color color = useMaterialColor ? material.GetColor(propertyId) : onColor;
                    baseColors[r][m] = color * intensityMultiplier;
                }
            }
        }

        private static int FindEmissionColorId(Material material)
        {
            if (material == null) return -1;
            if (material.HasProperty(SyntyEmissionColorId)) return SyntyEmissionColorId;
            if (material.HasProperty(UnityEmissionColorId)) return UnityEmissionColorId;
            return -1;
        }

        private void CacheLightIntensities()
        {
            if (syncLights == null) return;

            baseLightIntensities = new float[syncLights.Length];
            for (int i = 0; i < syncLights.Length; i++)
                baseLightIntensities[i] = syncLights[i] != null ? syncLights[i].intensity : 0f;
        }

        private void Apply()
        {
            if (block == null) return;

            for (int r = 0; r < targetRenderers.Length; r++)
            {
                Renderer targetRenderer = targetRenderers[r];
                if (targetRenderer == null) continue;

                for (int m = 0; m < baseColors[r].Length; m++)
                {
                    int propertyId = colorPropertyIds[r][m];
                    if (propertyId == -1) continue;

                    targetRenderer.GetPropertyBlock(block, m);
                    block.SetColor(propertyId, isOn ? baseColors[r][m] : Color.black);
                    targetRenderer.SetPropertyBlock(block, m);
                }
            }

            if (syncLights == null) return;

            for (int i = 0; i < syncLights.Length; i++)
            {
                if (syncLights[i] != null)
                    syncLights[i].intensity = isOn ? baseLightIntensities[i] : 0f;
            }
        }

        private static float RandomRange(Vector2 range)
        {
            return Random.Range(range.x, range.y);
        }
    }
}
