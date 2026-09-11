using UnityEngine;

namespace ProjectS.FX
{
    /// <summary>
    /// 홀로그램 간판에 간헐적 글리치(순간 명멸)를 입힌다.
    /// 난이도가 높은 구역의 경고판이 "고장 직전"처럼 보이게 하기 위한 연출.
    ///
    /// 이 컴포넌트와 그 하위의 모든 Renderer를 한 덩어리로 묶어 동시에 글리치시킨다.
    /// 줄(Row) 부모에 붙이면 줄 단위로, 간판 하나에 붙이면 그 간판만 깜빡인다.
    /// 간판마다 따로 붙여 제각각 깜빡이게 하면 "경고판"이 아니라 "깨진 픽셀"로 보이므로,
    /// 줄 부모에 붙이고 줄끼리 Phase Offset을 다르게 주는 쪽을 권한다.
    ///
    /// 값은 MaterialPropertyBlock으로 넣는다. 머티리얼을 직접 건드리면 그것을 공유하는
    /// 다른 간판이 전부 같이 변한다(현재 WARNING 간판 17개가 머티리얼 하나를 공유).
    ///
    /// 치지직거리는 느낌의 핵심은 밝기가 아니라 `_Scroll_Speed`다. 이 셰이더는
    /// Panner로 홀로그램 스캔라인(_Holo_Lines)을 흘리는데, 글리치 순간 속도를 확 올리면
    /// 라인이 쏟아지듯 흐른다. 게다가 패너 오프셋이 '시간 x 속도'라 속도를 급변시키면
    /// 라인 위치가 순간 툭 튀어, 신호가 끊긴 것 같은 스냅이 공짜로 딸려온다.
    /// 밝기(_Emission_Power)만 흔들면 '빠른 깜빡임'이지 '치지직'이 아니다.
    ///
    /// ★ SyntyStudios_Hologram_01 셰이더 전용이다. `_Emission_Power`/`_Opacity`/
    ///   `_Scroll_Speed`를 노출하는 머티리얼에만 동작하고, 없으면 경고 후 비활성된다.
    ///
    /// ★ 글자(_Albedo_Map / _Emission_Mask)는 이 셰이더가 흘려주지 않는다.
    ///   글자 자체를 흐르게 하려면 ConveyorLoop가 따로 필요하다.
    ///
    /// ★ 플레이 모드에서만 돈다. [ExecuteAlways]를 일부러 붙이지 않았다 —
    ///   에디터에서 상시 재생하면 씬 뷰가 계속 다시 그려져 작업에 방해가 된다.
    ///   (ConveyorLoop는 반대로 에디터 실행을 전제로 쓰였으면서 어트리뷰트가 빠져 있었다.)
    /// </summary>
    public class HologramGlitch : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("비워두면 이 오브젝트와 하위의 모든 Renderer를 자동으로 찾는다.")]
        [SerializeField] private Renderer[] targets;

        [Header("글리치 간격(초)")]
        [Tooltip("글리치와 글리치 사이 대기 시간. 이 범위 안에서 매번 새로 뽑는다 — "
               + "간격이 일정하면 '고장'이 아니라 '신호'로 읽혀 효과가 죽는다.")]
        [SerializeField] private float minInterval = 3f;
        [SerializeField] private float maxInterval = 6f;

        [Header("글리치 지속(초)")]
        [SerializeField] private float minDuration = 0.06f;
        [SerializeField] private float maxDuration = 0.18f;

        [Header("글리치 세기")]
        [Tooltip("글리치 중 발광 배율 범위. 0에 가까울수록 꺼진 것처럼 보인다.")]
        [SerializeField] private float minPowerScale = 0.05f;
        [SerializeField] private float maxPowerScale = 0.45f;

        [Tooltip("글리치 중 배율을 다시 뽑는 간격(초). 짧을수록 지직거린다. "
               + "한 번만 꺼졌다 켜지면 '깜빡임'이지 '글리치'가 아니다.")]
        [SerializeField] private float stutterInterval = 0.03f;

        [Header("불투명도")]
        [Tooltip("글리치 중 반투명해져 신호가 끊긴 느낌을 준다.")]
        [SerializeField] private bool affectOpacity = true;

        [Range(0f, 1f)]
        [Tooltip("글리치가 가장 강할 때의 불투명도.")]
        [SerializeField] private float glitchOpacity = 0.35f;

        [Header("스캔라인 폭주 (치지직의 핵심)")]
        [Tooltip("글리치 중 _Scroll_Speed를 확 올려 홀로그램 라인을 쏟아지게 한다. "
               + "이걸 끄면 밝기만 흔들려 '빠른 깜빡임'으로 보인다.")]
        [SerializeField] private bool affectScroll = true;

        [Tooltip("기준 스크롤 속도 대비 배율 범위. 20~60배쯤이 지직거리는 느낌이 난다.")]
        [SerializeField] private float minScrollMultiplier = 20f;
        [SerializeField] private float maxScrollMultiplier = 60f;

        [Header("맥박 (선택 · 기본 꺼짐)")]
        [Tooltip("글리치만으로 밋밋할 때 켠다. 글리치가 이미 충분하면 끈 채로 두는 게 낫다.")]
        [SerializeField] private bool usePulse;

        [SerializeField] private float pulsePeriod = 1.2f;

        [Tooltip("기준 발광 대비 진폭. 0.1이면 ±10%. 0.2를 넘기면 촌스러워진다.")]
        [SerializeField] private float pulseAmount = 0.1f;

        [Header("위상")]
        [Tooltip("줄마다 다른 값을 주면 동시에 깜빡이지 않아 깊이가 생긴다.")]
        [SerializeField] private float phaseOffset;

        // 셰이더그래프가 실제로 읽는 이름. 머티리얼에는 _BaseColour 같은 구형 이름도
        // 남아 있지만 셰이더는 언더바 버전만 참조한다(무시되는 쪽을 만지면 반영이 안 된다).
        private static readonly int EmissionPowerId = Shader.PropertyToID("_Emission_Power");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int ScrollSpeedId = Shader.PropertyToID("_Scroll_Speed");

        private MaterialPropertyBlock block;
        private float basePower;
        private float baseOpacity;
        private float baseScroll;

        private float nextGlitchTime;
        private float glitchEndTime;
        private float nextStutterTime;
        private float powerScale = 1f;
        private float scrollMultiplier = 1f;
        private bool ready;

        private void Awake()
        {
            if (targets == null || targets.Length == 0)
                targets = GetComponentsInChildren<Renderer>(true);

            if (targets.Length == 0)
            {
                Debug.LogWarning($"[HologramGlitch] {name}: Renderer가 없어 비활성됩니다.", this);
                return;
            }

            // 기준값은 머티리얼에서 읽는다. 코드에 6 같은 숫자를 박아두면 아트가 머티리얼을
            // 조정했을 때 연출만 옛 값을 기준으로 흔들려 밝기가 어긋난다.
            Material source = targets[0].sharedMaterial;

            if (source == null || !source.HasProperty(EmissionPowerId))
            {
                Debug.LogWarning(
                    $"[HologramGlitch] {name}: 머티리얼에 '_Emission_Power'가 없습니다. " +
                    "SyntyStudios_Hologram_01 셰이더를 쓰는 간판에만 동작합니다.", this);
                return;
            }

            basePower = source.GetFloat(EmissionPowerId);
            baseOpacity = source.HasProperty(OpacityId) ? source.GetFloat(OpacityId) : 1f;
            baseScroll = source.HasProperty(ScrollSpeedId) ? source.GetFloat(ScrollSpeedId) : 0f;

            // 기준 속도가 0이면 배율을 곱해도 계속 0이라 스캔라인이 아예 안 움직인다.
            if (affectScroll && Mathf.Abs(baseScroll) < 0.0001f)
            {
                Debug.LogWarning(
                    $"[HologramGlitch] {name}: '_Scroll_Speed'가 0이라 스캔라인 폭주가 보이지 않습니다. " +
                    "머티리얼에서 0이 아닌 값을 주거나 Affect Scroll을 끄세요.", this);
            }

            block = new MaterialPropertyBlock();
            ready = true;
        }

        private void OnEnable()
        {
            if (!ready) return;

            float now = Time.time + phaseOffset;

            glitchEndTime = now;   // 지금은 글리치 중이 아니다
            nextGlitchTime = now + Random.Range(minInterval, maxInterval);
            powerScale = 1f;
            scrollMultiplier = 1f;
        }

        private void OnDisable()
        {
            if (!ready) return;

            // 글리치 도중에 꺼지면 어두워진 채로 씬에 남는다. 기준값으로 되돌린다.
            powerScale = 1f;
            scrollMultiplier = 1f;
            Apply(basePower, baseOpacity, baseScroll);
        }

        private void Update()
        {
            if (!ready) return;

            float now = Time.time + phaseOffset;

            if (now >= glitchEndTime)
            {
                if (now >= nextGlitchTime) BeginGlitch(now);
                else
                {
                    powerScale = 1f;
                    scrollMultiplier = 1f;
                }
            }

            // 글리치 중에는 짧은 간격으로 배율을 다시 뽑아 지직거리게 만든다.
            if (now < glitchEndTime && now >= nextStutterTime)
            {
                powerScale = Random.Range(minPowerScale, maxPowerScale);

                // 매 스터터마다 새로 뽑는다. 속도가 바뀔 때마다 패너 오프셋이 튀므로,
                // 값이 바뀌는 순간 자체가 라인이 어긋나는 연출이 된다.
                scrollMultiplier = Random.Range(minScrollMultiplier, maxScrollMultiplier);

                nextStutterTime = now + Mathf.Max(0.01f, stutterInterval);
            }

            float power = basePower * powerScale;

            // 맥박은 글리치가 없는 동안에만 얹는다. 겹치면 두 주기가 간섭해
            // "가끔 이상하다" 싶은 순간이 생기는데, 원인 찾기가 매우 어렵다.
            if (usePulse && powerScale >= 1f)
            {
                float wave = Mathf.Sin(now / Mathf.Max(0.01f, pulsePeriod) * Mathf.PI * 2f);
                power = basePower * (1f + wave * pulseAmount);
            }

            float opacity = baseOpacity;
            if (affectOpacity && powerScale < 1f)
                opacity = Mathf.Lerp(glitchOpacity, baseOpacity, powerScale);

            Apply(power, opacity, baseScroll * scrollMultiplier);
        }

        private void BeginGlitch(float now)
        {
            glitchEndTime = now + Random.Range(minDuration, maxDuration);
            nextStutterTime = now;   // 첫 프레임부터 바로 튄다

            // 다음 글리치는 이번 글리치가 끝난 시점부터 센다. now 기준으로 잡으면
            // 지속시간이 길 때 간격이 그만큼 먹혀 들어간다.
            nextGlitchTime = glitchEndTime + Random.Range(minInterval, maxInterval);
        }

        private void Apply(float power, float opacity, float scroll)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;

                // 기존 블록을 먼저 읽어와야 다른 스크립트가 넣어둔 값을 덮어쓰지 않는다.
                targets[i].GetPropertyBlock(block);

                block.SetFloat(EmissionPowerId, power);
                if (affectOpacity) block.SetFloat(OpacityId, opacity);
                if (affectScroll) block.SetFloat(ScrollSpeedId, scroll);

                targets[i].SetPropertyBlock(block);
            }
        }

        private void OnValidate()
        {
            // 최소가 최대보다 큰 상태로 Random.Range에 들어가면 값이 뒤집혀
            // 의도와 반대로 동작한다(에러는 나지 않아 알아채기 어렵다).
            if (maxInterval < minInterval) maxInterval = minInterval;
            if (maxDuration < minDuration) maxDuration = minDuration;
            if (maxPowerScale < minPowerScale) maxPowerScale = minPowerScale;
            if (maxScrollMultiplier < minScrollMultiplier) maxScrollMultiplier = minScrollMultiplier;
        }
    }
}
