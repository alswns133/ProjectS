using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 달궈진 게이지 끝점에서 용접 불똥이 <b>계속</b> 튀게 한다.
    /// 방출량은 <see cref="RadialFlowGaugeFx.Heat"/>에 비례하므로, 게이지가 12시에 다가갈수록
    /// 거세지고 실패해서 되돌아가면 저절로 잦아든다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>불꽃을 그리는 건 이 클래스가 아니다.</b> 생성·중력·꼬리·냉각·풀링은 전부
    /// <see cref="SparkBurstFx"/>가 하고, 이쪽은 <b>언제 얼마나 어느 방향으로</b>만 정한다.
    /// 그래서 불꽃 모양을 바꾸려면 SparkBurstFx의 인스펙터를, 리듬을 바꾸려면 이쪽을 만진다.
    /// </para>
    /// <para>
    /// <b>SparkBurstFx가 붙은 오브젝트는 RectMask2D 바깥에 있어야 한다.</b> 강화창 Body 밑에 두면
    /// 불똥이 창 테두리를 넘는 순간 마스크에 잘려 사라진다. 팝업 최상위나 FX 오버레이 레이어에 둔다.
    /// 좌표는 <see cref="SparkBurstFx.ToLocal"/>로 옮기므로 부모가 달라도 상관없다.
    /// </para>
    /// <para>
    /// <b>방출은 일부러 고르지 않다.</b> 초당 개수를 그대로 흘리지 않고 짧은 뭉치로 끊어 낸다.
    /// 용접 불똥은 일정한 분수가 아니라 불규칙하게 터지는 것이라, 고르게 내면 스프링클러로 보인다.
    /// </para>
    /// <para>시간은 unscaled로 센다(마을 timeScale 0 대응).</para>
    /// (2026-08-21 TH)
    /// </remarks>
    public class GaugeHeatSparks : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("열과 끝점 위치를 가져올 게이지(GaugeF).")]
        [SerializeField] private RadialFlowGaugeFx gauge;

        [Tooltip("불꽃을 그릴 레이어. 반드시 RectMask2D 바깥에 있어야 한다.")]
        [SerializeField] private SparkBurstFx sparks;

        [Header("방출량")]
        [Tooltip("열이 최대일 때 초당 불똥 개수.")]
        [SerializeField, Min(0f)] private float rateAtFullHeat = 55f;

        [Tooltip("이 열 미만이면 아예 튀지 않는다. 미지근할 때 찔끔거리는 것을 막는다.")]
        [SerializeField, Range(0f, 1f)] private float heatThreshold = 0.12f;

        [Tooltip("한 번에 뭉쳐 나오는 개수의 최소·최대.")]
        [SerializeField, Min(1)] private int burstMin = 1;
        [SerializeField, Min(1)] private int burstMax = 4;

        [Header("방향")]
        [Tooltip("링 바깥 방향 기준 좌우 흔들림(도).")]
        [SerializeField, Range(0f, 180f)] private float spreadAngle = 38f;

        [Tooltip("게이지가 나아가는 쪽으로 기울이는 정도. 방향이 반대로 보이면 부호를 뒤집는다 " +
                 "(게이지의 회전 방향에 따라 달라진다).")]
        [SerializeField, Range(-2f, 2f)] private float tangentBias = 0.45f;

        [Header("안쪽 불똥")]
        [Tooltip("링 안쪽(중심 방향)으로 튀는 비율. 0이면 바깥으로만 튄다.")]
        [SerializeField, Range(0f, 1f)] private float inwardRatio = 0.35f;

        [Tooltip("안쪽 불똥의 속도 배율. 꼬리 길이가 속도에서 나오므로 이 값이 곧 크기가 된다. " +
                 "바깥보다 작아야 링이 바깥으로 뿜는 것처럼 읽힌다.")]
        [SerializeField, Range(0.1f, 1f)] private float inwardScale = 0.45f;

        [Tooltip("안쪽 불똥의 좌우 흔들림(도). 튕겨 나가는 파편이라 바깥보다 넓게 흩어져도 된다.")]
        [SerializeField, Range(0f, 180f)] private float inwardSpread = 60f;

        [Header("도달 순간 버스트")]
        [Tooltip("게이지가 끝(12시)에 닿는 순간 한꺼번에 터뜨릴 개수.")]
        [SerializeField, Min(0)] private int burstCount = 40;

        [Tooltip("버스트 불똥의 속도 배율. 평소 방출보다 확실히 커야 '한 방'으로 읽힌다.")]
        [SerializeField, Range(0.5f, 3f)] private float burstScale = 1.8f;

        [Tooltip("버스트의 좌우 흔들림(도). 평소보다 넓게 퍼뜨린다.")]
        [SerializeField, Range(0f, 180f)] private float burstSpread = 70f;

        [Header("디버그")]
        [Tooltip("체크하면 아래 간격마다 버스트를 반복한다. 인스펙터를 건드리는 순간 에디터가 멈칫해서 " +
                 "정작 터지는 걸 놓치기 때문에, 켜두고 손을 뗀 채로 보라고 있는 것이다.")]
        [SerializeField] private bool debugRepeatBurst;

        [Tooltip("반복 버스트 간격(초).")]
        [SerializeField, Min(0.2f)] private float debugBurstInterval = 1.5f;

        // 한 프레임에 인정할 최대 시간(초). 에디터 멈칫·로딩 히치 뒤에는 unscaledDeltaTime이
        // 통째로 튀어 들어오는데, 그대로 쓰면 밀린 방출량이 한꺼번에 터진다.
        private const float MaxStep = 0.05f;

        // 1개 미만으로 쌓인 방출량 이월분.
        private float emitDebt;

        private float debugTimer;

        // 도달 순간을 알려줄 스윕. 게이지와 같은 오브젝트에 있으므로 따로 배선하지 않는다.
        private EnhanceGaugeSweep sweep;

        private void Awake()
        {
            if (gauge == null || sparks == null)
            {
                Debug.LogWarning($"{name}: 게이지 또는 SparkBurstFx가 비어 있습니다. 불똥이 튀지 않습니다.", this);
                enabled = false;
                return;
            }

            // 스윕은 게이지와 같은 오브젝트에 붙는다. 게이지 참조가 이미 있으니 배선이 더 필요 없다.
            sweep = gauge.GetComponent<EnhanceGaugeSweep>();
            if (sweep == null)
            {
                Debug.LogWarning($"{name}: 게이지에 EnhanceGaugeSweep이 없습니다. " +
                                 "연속 방출은 되지만 도달 순간 버스트는 터지지 않습니다.", this);
            }
        }

        private void OnEnable()
        {
            emitDebt = 0f;
            debugTimer = 0f;
            debugRepeatBurst = false;   // 실험용 값이 실제 플레이로 새어 들어가지 않게
            if (sweep != null) sweep.OnReachedEnd += Burst;
        }

        private void OnDisable()
        {
            if (sweep != null) sweep.OnReachedEnd -= Burst;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);

            if (debugRepeatBurst)
            {
                debugTimer += dt;
                if (debugTimer >= debugBurstInterval)
                {
                    debugTimer = 0f;
                    Burst();
                }
            }

            float heat = gauge.Heat;

            if (heat < heatThreshold)
            {
                // 식었으면 이월분도 버린다. 남겨두면 다시 달아오르는 순간 밀린 양이 한꺼번에 터진다.
                emitDebt = 0f;
                return;
            }

            emitDebt += heat * rateAtFullHeat * dt;
            if (emitDebt < 1f) return;

            int amount = Mathf.Min(Random.Range(burstMin, burstMax + 1), Mathf.FloorToInt(emitDebt));
            if (amount <= 0) return;

            emitDebt -= amount;
            Emit(amount, heat);
        }

        /// <summary>
        /// 게이지가 끝에 닿는 순간의 한 방. 인스펙터의 버스트 설정으로 크게 터뜨린다.
        /// <see cref="EnhanceGaugeSweep"/>이 성공해서 1.0에 도달한 시점에 호출한다.
        /// </summary>
        /// <remarks>
        /// 평소 방출과 달리 열을 보지 않는다. 도달 순간은 이미 최대로 달아오른 시점이고,
        /// 무엇보다 <b>이건 연속이 아니라 사건</b>이라 세기를 따로 잡아야 한 방으로 읽힌다.
        /// </remarks>
        [ContextMenu("버스트 테스트")]
        public void Burst()
        {
            if (enabled) Emit(burstCount, burstScale, burstSpread, burstSpread);
        }

        private void Emit(int amount, float heat)
            => Emit(amount, Mathf.Lerp(0.55f, 1f, heat), spreadAngle, inwardSpread);

        private void Emit(int amount, float scale, float outSpread, float inSpread)
        {
            // 끝 선의 양 끝(안쪽 가장자리 ~ 바깥 가장자리)을 불꽃 레이어 좌표계로 옮긴다.
            // 게이지와 불꽃 레이어는 부모가 다르므로(마스크 회피) 이 변환을 건너뛸 수 없다.
            Vector2 inner = sparks.ToLocal(gauge.GetTipWorldPosition(0f));
            Vector2 outer = sparks.ToLocal(gauge.GetTipWorldPosition(1f));

            // 바깥 방향은 끝 선 자체에서 나온다(안쪽 → 바깥). 링 중심을 따로 잴 필요가 없다.
            Vector2 outward = outer - inner;
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector2.up;

            // 접선(바깥 방향을 90도 돌린 것)을 섞어 게이지가 나아가는 쪽으로 흩뿌린다.
            Vector2 tangent = new Vector2(-outward.y, outward.x);

            int inwardCount = Mathf.RoundToInt(amount * inwardRatio);

            for (int i = 0; i < amount; i++)
            {
                // 한 점이 아니라 끝 선 위 임의의 지점에서 태어난다.
                // 쇠를 내려칠 때 불똥은 점이 아니라 맞은 면 전체에서 튄다.
                Vector2 position = Vector2.Lerp(inner, outer, Random.value);

                bool goInward = i < inwardCount;
                Vector2 baseDir = goInward ? -outward : outward;
                Vector2 dir = (baseDir + tangent * tangentBias).normalized;

                sparks.EmitLocal(
                    position,
                    1,
                    dir,
                    goInward ? inSpread : outSpread,
                    goInward ? scale * inwardScale : scale);
            }
        }
    }
}
