using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 로그인 화면 중앙의 퍼포먼스 게이지 연출. 결과 화면의 <c>PerformanceGauge</c> 프리팹을 그대로 가져다
    /// <b>대기(조각 공전) → 인증 중(채움 상승·%) → 접속 완료(금고 다이얼 → 개방 → 진입)</b> 순서로 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 결과 화면의 잠금 클립(<see cref="PerformanceGaugeView.PlayLock"/>)을 재사용하지 않는가.</b>
    /// 1) 그 클립은 길이가 고정(약 4.6초)인데, 로그인은 Firebase 응답이 언제 올지 모른다.
    /// 2) 클립이 조각의 <c>m_Color</c>를 키로 잡고 있어(회색→흰색), 로그인 화면의 단일 파랑 배색을 매 프레임 덮어쓴다.
    /// 그래서 로그인에서는 Animator를 끄고, 채움·회전·색·배율을 이 스크립트가 직접 몬다. 프리팹 자체(결과 화면용)는 손대지 않는다.
    /// </para>
    /// <para>
    /// <b>진입 연출을 카메라가 아니라 게이지 배율로 하는 이유.</b> 로그인 캔버스는 Screen Space Overlay라
    /// 카메라를 움직여도 UI는 그대로다. 대신 링 중심을 화면 가운데로 끌어오며 게이지를 키우면, 링이 화면 밖으로
    /// 빠져나가면서 "구멍 안으로 들어가는" 것과 같은 그림이 되고 해상도와도 무관하다.
    /// </para>
    /// <para>
    /// <b>색은 단일 파랑 + 반투명.</b> 사이버펑크 세계관(인간성의 가치가 사라진 차가운 세계)과
    /// 불확실한 미래를 표현하기 위한 UI 원칙이라, 결과 화면의 주황·노랑을 로그인에서는 역할별로 파랑 계열로 다시 입힌다.
    /// </para>
    /// 배치·배선은 <c>Tools ▸ ProjectS ▸ Style Login Screen</c>이 한다.
    /// </remarks>
    [DisallowMultipleComponent]
    public class LoginGaugeView : MonoBehaviour
    {
        private enum Phase { Idle, Authenticating, Granted }

        [Header("게이지 조각")]
        [Tooltip("Filled · Radial360 채움 이미지(GaugeFill).")]
        [SerializeField] private Image fill;

        [Tooltip("인증 중 퍼센트 표기(GaugeNum).")]
        [SerializeField] private TMP_Text num;

        [Tooltip("가운데 큰 글자(GaugeRank). 대기 중엔 로고, 승인 시 승인 표기를 띄운다.")]
        [SerializeField] private TMP_Text rank;

        [Tooltip("가운데 아래 작은 상태 문구(선택).")]
        [SerializeField] private TMP_Text caption;

        [Tooltip("4조각을 함께 도는 부모(GaugeLockFx). 대기 공전과 다이얼 회전을 맡는다.")]
        [SerializeField] private RectTransform pieceRing;

        [Tooltip("조각 몸체(Piece00~03). 벌어졌다 부딪히는 이동은 각 조각의 anchoredPosition으로 한다.")]
        [SerializeField] private RectTransform[] pieces = new RectTransform[0];

        [Header("개방")]
        [Tooltip("잠금이 풀린 뒤 켜지는 바깥 링(GaugeTechRound). 대기 중에는 꺼 둔다.")]
        [SerializeField] private GameObject techRound;

        [Tooltip("개방과 동시에 꺼져 가운데를 비우는 오브젝트(원형 Background 등).")]
        [SerializeField] private GameObject[] hideOnReveal = new GameObject[0];

        [Header("진입")]
        [Tooltip("진입 연출 중 사라질 나머지 UI(로그인 폼, 배경 장식 등).")]
        [SerializeField] private CanvasGroup[] fadeOutOnDive = new CanvasGroup[0];

        [Header("승인 시 로그인 폼 숨김")]
        [Tooltip("로그인 성공으로 게이지 연출이 시작되는 순간 숨길 UI(로그인 폼, 게임 종료 버튼). " +
                 "연출 중 폼이 떠 있으면 시선이 게이지로 모이지 않고, 입력·종료 버튼을 다시 누를 수도 있다.")]
        [SerializeField] private CanvasGroup[] hideOnGranted = new CanvasGroup[0];

        [SerializeField, Min(0f)] private float formHideDuration = 0.12f;

        [Header("결과 화면 연출 끄기")]
        [Tooltip("프리팹의 잠금 Animator. 켜 두면 클립 기본값이 색·채움을 덮어써서 여기서 끈다.")]
        [SerializeField] private Animator lockAnimator;

        [Tooltip("결과 화면용 뷰. 같은 %를 두 곳에서 쓰지 않도록 여기서 끈다.")]
        [SerializeField] private PerformanceGaugeView resultView;

        [Header("색 (단일 파랑 · 반투명)")]
        [SerializeField] private Color fillColor = new Color32(90, 170, 255, 204);
        [SerializeField] private Color trackColor = new Color32(90, 140, 210, 51);
        [SerializeField] private Color baseColor = new Color32(20, 45, 90, 90);
        [SerializeField] private Color pieceColor = new Color32(30, 60, 110, 179);
        [SerializeField] private Color pieceEdgeColor = new Color32(150, 205, 255, 230);

        [Tooltip("조각이 부딪히는 순간 강조선이 번쩍이는 색.")]
        [SerializeField] private Color impactFlashColor = new Color32(220, 240, 255, 255);

        [SerializeField] private Color textColor = new Color32(214, 228, 250, 255);
        [SerializeField] private Color captionColor = new Color32(140, 175, 225, 179);

        [Tooltip("트랙(회색 링) 계열 이미지.")]
        [SerializeField] private Graphic[] trackGraphics = new Graphic[0];

        [Tooltip("원형 바탕·회로 무늬·바깥 링 계열 이미지.")]
        [SerializeField] private Graphic[] baseGraphics = new Graphic[0];

        [Tooltip("조각 몸체 이미지.")]
        [SerializeField] private Graphic[] pieceGraphics = new Graphic[0];

        [Tooltip("조각 가장자리 강조선(Gradient) 이미지.")]
        [SerializeField] private Graphic[] pieceEdgeGraphics = new Graphic[0];

        [Header("대기 · 인증")]
        [Tooltip("대기 중 조각 공전 속도(도/초). 음수 = 시계 방향.")]
        [SerializeField] private float idleSpinSpeed = -26f;

        [Tooltip("인증 중 조각 공전 속도(도/초). 대기보다 빠르게 돌아 '처리 중'임을 보인다.")]
        [SerializeField] private float authSpinSpeed = -90f;

        [Tooltip("응답 전까지 채움이 다가가는 상한. 1이면 응답을 기다리지 않고 한 번에 끝까지 찬다" +
                 "(1 미만이면 그 지점에서 응답을 기다리며 멈춰 보인다).")]
        [SerializeField, Range(0f, 1f)] private float authCreepTarget = 1f;

        [Tooltip("상한으로 다가가는 속도. 클수록 초반에 빨리 오르고 상한 근처에서 느려진다.")]
        [SerializeField, Min(0.01f)] private float authCreepRate = 14f;

        [Tooltip("실패 시 채움이 0으로 빠지는 속도(초당 비율).")]
        [SerializeField, Min(0.01f)] private float drainSpeed = 1.5f;

        [Header("① 승인 · 벌어짐")]
        [SerializeField, Min(0f)] private float grantFillDuration = 0.06f;

        [Tooltip("조각이 벌어지는 방향(각 피벗 기준). 피벗이 0/90/180/270°로 돌아 있어 한 방향으로 넷이 모두 바깥을 향한다. " +
                 "원래 잠금 클립과 같은 왼쪽 위 대각선.")]
        [SerializeField] private Vector2 spreadDirection = new Vector2(-0.7071f, 0.7071f);

        [Tooltip("벌어지는 거리(프리팹 원본 좌표, 게이지 지름 300 기준).")]
        [SerializeField] private float spreadDistance = 22f;

        [SerializeField, Min(0f)] private float spreadDuration = 0.28f;

        [Header("② 금고 다이얼")]
        [Tooltip("차례로 도는 각도(도). 부호가 바뀔 때마다 방향 전환. 양수 = 반시계. " +
                 "마지막 값은 방향·대략적인 크기만 쓰이고, 실제 각도는 링이 원래 자세(0°)에 서도록 자동 보정된다.")]
        [SerializeField] private float[] dialTurns = { 180f, -132f, 96f };

        [Tooltip("평균 회전 속도(도/초). 회전 시간 = 각도 ÷ 이 값이라 큰 회전도 늘어지지 않는다.")]
        [SerializeField, Min(1f)] private float dialSpeed = 900f;

        [Tooltip("아무리 작은 회전도 이 시간보다 짧아지지 않는다. 너무 짧으면 순간이동처럼 보인다.")]
        [SerializeField, Min(0.01f)] private float dialMinDuration = 0.1f;

        [Tooltip("회전 시간 중 가속 구간 비율. 짧을수록 확 튀어 나간다.")]
        [SerializeField, Range(0.01f, 0.5f)] private float dialAccelRatio = 0.12f;

        [Tooltip("회전 시간 중 감속(제동) 구간 비율. 짧을수록 딱 끊어 선다.")]
        [SerializeField, Range(0.01f, 0.5f)] private float dialBrakeRatio = 0.1f;

        [Tooltip("번호에 걸렸을 때 반대로 되튀는 각도.")]
        [SerializeField, Min(0f)] private float catchRecoil = 3f;

        [SerializeField, Min(0.01f)] private float catchDuration = 0.12f;

        [Tooltip("번호에 걸린 뒤 반대로 돌기 전까지 멈춰 있는 시간.")]
        [SerializeField, Min(0f)] private float dialPause = 0.16f;

        [Tooltip("돌기 직전 반대로 살짝 감는 각도. 무거운 다이얼에 힘을 싣는 예비 동작.")]
        [SerializeField, Min(0f)] private float windUpAngle = 4f;

        [SerializeField, Min(0f)] private float windUpDuration = 0.14f;

        [Tooltip("회전이 멈출 때마다 게이지 전체가 흔들리는 크기(캔버스 픽셀). 무게가 멈추며 전해지는 반동.")]
        [SerializeField, Min(0f)] private float stopShake = 6f;

        [SerializeField, Min(0.01f)] private float stopShakeDuration = 0.22f;

        [Header("③ 쾅 (다시 맞물림)")]
        [Tooltip("결합 직전 조각이 추가로 벌어지는 거리(spreadDistance에 더함). 반동을 모았다가 내리꽂는 예비 동작.")]
        [SerializeField, Min(0f)] private float preSlamExtraSpread = 10f;

        [Tooltip("추가로 벌어지는 시간.")]
        [SerializeField, Min(0.01f)] private float preSlamDuration = 0.12f;

        [Tooltip("추가로 벌어진 채 버티는 시간. 짧게 멈춰야 다음 결합이 더 세게 읽힌다.")]
        [SerializeField, Min(0f)] private float preSlamHold = 0.05f;

        [Tooltip("조각이 제자리로 날아드는 시간. 짧을수록 세게 부딪힌다.")]
        [SerializeField, Min(0.01f)] private float slamDuration = 0.11f;

        [Tooltip("부딪힌 순간 게이지 전체가 튀는 배율.")]
        [SerializeField, Min(1f)] private float impactPunchScale = 1.07f;

        [Tooltip("부딪힌 순간 흔들림 크기(캔버스 픽셀).")]
        [SerializeField, Min(0f)] private float impactShake = 10f;

        [SerializeField, Min(0.01f)] private float impactDuration = 0.3f;

        [Header("④ 개방")]
        [Tooltip("가운데가 빈 채로 머무는 시간. 빈 구멍을 읽을 틈을 준 뒤 들어간다.")]
        [SerializeField, Min(0f)] private float revealHold = 0.35f;

        [Tooltip("GaugeTechRound가 켜진 순간의 확대 속도(배율/초). 결합 순간부터 씬 전환까지 멈추지 않고 커진다.")]
        [SerializeField, Min(0f)] private float techGrowSpeed = 0.8f;

        [Tooltip("GaugeTechRound 확대 가속도(배율/초²). 클수록 시간이 지날수록 점점 빨리 커져 끌려들어가는 느낌이 강해진다.")]
        [SerializeField, Min(0f)] private float techGrowAccel = 2.5f;

        [Header("⑤ 진입")]
        [Tooltip("진입 끝에 게이지가 커지는 배율. 링이 화면을 완전히 벗어날 만큼 크게.")]
        [SerializeField, Min(1f)] private float diveScale = 14f;

        [SerializeField, Min(0.01f)] private float diveDuration = 0.9f;

        [Tooltip("진입 가속 정도. 클수록 처음엔 천천히, 끝에 확 빨려 들어간다.")]
        [SerializeField, Min(1f)] private float diveEasePower = 3f;

        [Header("문구")]
        [SerializeField] private string idleMark = "S";
        [SerializeField] private string idleCaption = "PROJECT";
        [SerializeField] private string authCaption = "AUTHENTICATING";
        [SerializeField] private string grantedMark = "OK";
        [SerializeField] private string grantedCaption = "ACCESS GRANTED";

        /// <summary>
        /// 조각이 원위치로 맞물리는 바로 그 프레임에 발생한다(쾅). 배경 육각 타일 웨이브처럼
        /// 결합 충격에 맞춰 반응해야 하는 연출이 구독한다.
        /// </summary>
        public event System.Action Engaged;

        /// <summary>
        /// 로그인 버튼을 누른 순간(인증 시작)부터 조각이 맞물리기 직전까지 true.
        /// 배경 웨이브가 이 동안 자동 발사를 멈춰, 게이지 연출과 겹쳐 시선이 흩어지지 않게 한다.
        /// 실패로 대기에 돌아가면 false가 되어 배경도 다시 돈다.
        /// </summary>
        public bool IsUnlocking { get; private set; }

        /// <summary>
        /// 로그인 성공으로 승인 연출(<see cref="PlayGrantedAsync"/>)이 시작되는 순간 발생한다.
        /// 장식용 홀로그램 정보창처럼 "로그인되면 걷혀야 하는" 연출이 구독한다.
        /// </summary>
        public event System.Action GrantStarted;

        private Phase phase = Phase.Idle;
        private int shownPercent = -1;

        // 연출 뒤 대기로 되돌릴 때 쓰는 원래 값들.
        // 링의 원래 각도. 조각이 안쪽 원형 틀과 맞물리는 각도이며 프리팹 원본값(0°)이다. 조각 원위치도 0(SetPieceOffset 참고).
        private const float RingRestAngle = 0f;

        // 마지막 회전의 최소 각도. 원래 자세까지 이보다 덜 남았으면 한 바퀴 더 돈다(거의 안 돌고 멈추면 다이얼로 안 읽힘).
        private const float MinFinalTurn = 20f;

        private RectTransform root;
        private Vector2 rootRestPivot;
        private Vector2 rootRestPosition;
        private Vector3 rootRestScale;
        private bool[] hideRestActive = new bool[0];

        // GaugeTechRound 확대: 결합 순간부터 켜져 씬 전환까지 계속 커진다.
        private Vector3 techRestScale = Vector3.one;
        private bool isTechGrowing;
        private float techGrowElapsed;

        private void Awake()
        {
            // 컨트롤러가 붙은 Animator는 State에 모션이 없어도 기본값을 다시 써서, 아래에서 입힌 색이 되돌아간다.
            if (lockAnimator != null) lockAnimator.enabled = false;
            if (resultView != null) resultView.enabled = false;

            CacheRest();
            ApplyPalette();
            ShowIdle();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 단계(Task)와 상관없이 매 프레임 커져야 해서 Update에서 돌린다 — 튀기(③)·진입(⑤)과 겹쳐도 끊기지 않는다.
            if (isTechGrowing) GrowTechRound(dt);

            switch (phase)
            {
                case Phase.Idle:
                    Spin(idleSpinSpeed, dt);
                    // 실패 후에는 채움이 한 번에 사라지지 않고 빠져나가게 한다(거부됐다는 걸 눈으로 읽게).
                    if (fill != null && fill.fillAmount > 0f)
                        fill.fillAmount = Mathf.MoveTowards(fill.fillAmount, 0f, drainSpeed * dt);
                    break;

                case Phase.Authenticating:
                    Spin(authSpinSpeed, dt);
                    if (fill != null)
                    {
                        // 지수 접근으로 상한(authCreepTarget)까지 빠르게 찬다. 상한이 1이면 응답 전에 끝까지 차고,
                        // 응답이 늦으면 가득 찬 채로 기다렸다가 승인 즉시 다이얼로 넘어간다.
                        float k = 1f - Mathf.Exp(-authCreepRate * dt);
                        fill.fillAmount = Mathf.Lerp(fill.fillAmount, authCreepTarget, k);
                    }
                    RefreshPercent();
                    break;
            }
        }

        /// <summary>
        /// 대기 표시로 되돌린다. 로그인 실패 시에도 부른다 — 채움은 즉시 0이 아니라 Update에서 빠져나간다.
        /// </summary>
        public void ShowIdle()
        {
            phase = Phase.Idle;
            IsUnlocking = false;
            RestoreRest();

            SetActive(num, false);
            SetText(rank, idleMark);
            SetActive(rank, true);
            SetActive(caption, true);
            SetText(caption, idleCaption);
        }

        /// <summary>
        /// 인증 요청 직전에 부른다. 조각이 빨라지고 채움이 상한까지 서서히 오르며 %를 표시한다.
        /// </summary>
        public void BeginAuth()
        {
            phase = Phase.Authenticating;
            IsUnlocking = true;
            RestoreRest();
            if (fill != null) fill.fillAmount = 0f;

            shownPercent = -1;
            SetActive(rank, false);
            SetActive(num, true);
            RefreshPercent();
            SetText(caption, authCaption);
        }

        /// <summary>
        /// 로그인 성공 시 부른다. 채움 완료 → 조각 벌어짐 → 금고 다이얼 → 쾅 맞물림 → 가운데 개방 → 구멍 안으로 진입.
        /// 호출부는 이 Task를 기다린 다음 씬을 전환한다(진입이 끝난 화면은 배경색만 남아 베일과 자연스럽게 이어진다).
        /// </summary>
        public async Task PlayGrantedAsync()
        {
            phase = Phase.Granted;
            GrantStarted?.Invoke();

            // 튀기(③)·진입(⑤) 배율이 링 중심 기준이 되도록 먼저 피벗을 가운데로 옮긴다.
            if (root != null) PivotToCenter(root);

            // 로그인 폼은 기다리지 않고 채움과 동시에 걷는다(연출 시작을 늦추지 않게).
            _ = HideFormAsync();

            await FillToFullAsync();
            if (this == null) return;

            SetActive(num, false);
            SetText(rank, grantedMark);
            SetActive(rank, true);
            SetText(caption, grantedCaption);

            await SpreadAsync();
            if (this == null) return;

            await DialAsync();
            if (this == null) return;

            await SlamAsync();
            if (this == null) return;

            await Wait(revealHold);
            if (this == null) return;

            await DiveAsync();
        }

        /// <summary>
        /// 역할별 이미지에 파랑 배색을 입힌다. 런타임 Awake와 에디터 배치 툴이 함께 쓴다
        /// (툴에서 미리 입혀 두어야 씬 뷰에서도 실제 화면과 같은 색으로 보인다).
        /// </summary>
        public void ApplyPalette()
        {
            if (fill != null) fill.color = fillColor;
            Tint(trackGraphics, trackColor);
            Tint(baseGraphics, baseColor);
            Tint(pieceGraphics, pieceColor);
            Tint(pieceEdgeGraphics, pieceEdgeColor);
            if (num != null) num.color = textColor;
            if (rank != null) rank.color = textColor;
            if (caption != null) caption.color = captionColor;
        }

        // ── 승인 단계 ────────────────────────────────────────

        // 입력부터 먼저 막고(클릭·키 입력이 곧바로 끊기게) 알파는 짧게 페이드한다.
        // SetActive(false)가 아니라 CanvasGroup을 쓰는 이유: 폼 아래에 LoginUI 같은 스크립트가 있으면
        // 오브젝트를 끄는 순간 OnDisable이 돌아 진행 중인 로그인 흐름이 꼬일 수 있다.
        private async Task HideFormAsync()
        {
            float[] from = new float[hideOnGranted.Length];
            for (int i = 0; i < hideOnGranted.Length; i++)
            {
                CanvasGroup group = hideOnGranted[i];
                if (group == null) continue;
                from[i] = group.alpha;
                group.interactable = false;
                group.blocksRaycasts = false;
            }

            await Tween(formHideDuration, t =>
            {
                for (int i = 0; i < hideOnGranted.Length; i++)
                    if (hideOnGranted[i] != null) hideOnGranted[i].alpha = from[i] * (1f - t);
            });
        }

        private async Task FillToFullAsync()
        {
            float from = fill != null ? fill.fillAmount : 1f;
            await Tween(grantFillDuration, t =>
            {
                if (fill != null) fill.fillAmount = Mathf.Lerp(from, 1f, EaseOutCubic(t));
                RefreshPercent();
            });
        }

        // ① 네 조각이 동시에 바깥으로 벌어진다. 넘침(되튐) 없이 멈춘다 — 되튀면 탄성이 느껴져 금속 잠금쇠가 아니라 장난감처럼 보인다.
        private async Task SpreadAsync()
        {
            Vector2 offset = spreadDirection.normalized * spreadDistance;
            await Tween(spreadDuration, t => SetPieceOffset(offset * EaseOutCubic(t)));
        }

        // ② 금고 다이얼. 반대로 살짝 감았다가(예비 동작) 한 번에 빠르게 돌고, 번호에 닿으면 딱 멈춰
        // 살짝 되튀며 게이지 전체가 흔들린다. 잠시 멈췄다가 반대로 돈다.
        // 회전은 사다리꼴 속도(짧은 가속 → 최고 속도 유지 → 짧은 제동)라, ease-in-out처럼 시작·끝이 늘어지지 않고 절도 있게 끊긴다.
        //
        // 마지막 회전은 링을 정확히 원래 각도(RingRestAngle)에 세운다. 쾅 단계에서 조각이 안쪽 원형 틀에
        // 딱 맞아떨어지려면 링이 0°(원래 자세)여야 하는데, 대기 공전 때문에 출발 각도가 매번 달라
        // 설정값대로만 돌면 엉뚱한 각도에서 끝난다. 그래서 마지막 한 번만 도착 각도에 맞춰 회전량을 보정한다.
        private async Task DialAsync()
        {
            if (pieceRing == null || dialTurns.Length == 0) return;

            // localEulerAngles는 0~360으로 감겨 누적 계산이 어긋나므로 각도를 직접 누적한다.
            float angle = pieceRing.localEulerAngles.z;

            for (int i = 0; i < dialTurns.Length; i++)
            {
                float turn = i == dialTurns.Length - 1 ? TurnToRest(angle, dialTurns[i]) : dialTurns[i];
                if (Mathf.Approximately(turn, 0f)) continue;

                await WindUpAsync(angle, Mathf.Sign(turn));
                if (this == null) return;

                float from = angle;
                float duration = Mathf.Max(dialMinDuration, Mathf.Abs(turn) / dialSpeed);
                await Tween(duration, t => SetRingAngle(from + turn * TrapezoidEase(t, dialAccelRatio, dialBrakeRatio)));
                if (this == null) return;

                angle = from + turn;
                SetRingAngle(angle);

                // 되튐과 흔들림을 동시에 — 멈추는 순간 무게가 다이얼에서 몸체로 전해지는 그림.
                await Task.WhenAll(CatchAsync(angle, Mathf.Sign(turn)), ShakeAsync(stopShake, stopShakeDuration));
                if (this == null) return;

                await Wait(dialPause);
                if (this == null) return;
            }

            // 누적 오차 없이 정확히 원래 각도로 못박는다.
            SetRingAngle(RingRestAngle);
        }

        // 현재 각도에서 원래 각도까지 가는 회전량 중, 설정한 방향(부호)이 같고 설정 크기에 가장 가까운 값을 고른다.
        // 예: 설정 +96°, 원래 각도까지 반시계로 30° 남음 → +30°(가장 가까운 값). 설정 +500°였다면 +390°.
        // 방향을 지키는 이유: 마지막 회전도 앞 회전과 번갈아 방향을 바꿔야 금고 다이얼로 읽힌다.
        private float TurnToRest(float fromAngle, float configured)
        {
            float delta = Mathf.DeltaAngle(fromAngle, RingRestAngle); // −180 ~ 180
            float sign = configured >= 0f ? 1f : -1f;

            // 같은 방향으로 가는 최소 회전량(0 이상 360 미만)을 구한 뒤, 설정 크기에 더 가까워지는 동안 한 바퀴씩 더한다.
            float sameDir = delta * sign;
            if (sameDir < 0f) sameDir += 360f;

            float target = Mathf.Abs(configured);
            while (sameDir + 180f < target) sameDir += 360f;

            // 남은 각도가 아주 작으면 돌지 않은 것처럼 보이므로 한 바퀴 더 돈다.
            if (sameDir < MinFinalTurn) sameDir += 360f;

            return sameDir * sign;
        }

        // 돌기 직전 반대 방향으로 살짝 감았다가 원위치 — 무거운 것을 돌리려 힘을 싣는 예비 동작.
        private async Task WindUpAsync(float angle, float direction)
        {
            if (windUpAngle <= 0f || windUpDuration <= 0f) return;

            await Tween(windUpDuration, t => SetRingAngle(angle - direction * windUpAngle * Mathf.Sin(t * Mathf.PI)));
            if (this == null) return;

            SetRingAngle(angle);
        }

        // 게이지 전체 흔들림. 감쇠하며 가라앉고, 끝나면 정확히 원래 위치로 돌려놓는다(흔들림이 누적돼 틀어지지 않게).
        private async Task ShakeAsync(float magnitude, float duration)
        {
            if (root == null || magnitude <= 0f) return;

            Vector2 position = root.anchoredPosition;
            await Tween(duration, t =>
            {
                float decay = (1f - t) * (1f - t);
                root.anchoredPosition = position + new Vector2(Mathf.Sin(t * 62f), Mathf.Cos(t * 47f)) * magnitude * decay;
            });
            if (this == null) return;

            root.anchoredPosition = position;
        }

        // 번호에 걸리는 순간: 진행 반대 방향으로 짧게 되튀었다 돌아오고, 강조선이 한 번 깜빡여 "걸렸다"를 보인다.
        private async Task CatchAsync(float angle, float direction)
        {
            await Tween(catchDuration, t =>
            {
                float kick = Mathf.Sin(t * Mathf.PI) * (1f - t);
                SetRingAngle(angle - direction * catchRecoil * kick);
                Tint(pieceEdgeGraphics, Color.Lerp(pieceEdgeColor, impactFlashColor, (1f - t) * 0.6f));
            });
            if (this == null) return;

            SetRingAngle(angle);
            Tint(pieceEdgeGraphics, pieceEdgeColor);
        }

        private void SetRingAngle(float angle)
        {
            if (pieceRing != null) pieceRing.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // ③ 조각이 한 번 더 벌어져(spreadDistance + preSlamExtraSpread) 반동을 모은 뒤, 가속하며 제자리로 날아들고(ease-in),
        // 닿는 순간 게이지 전체가 튀며 흔들리고 강조선이 번쩍인다.
        // 링은 다이얼 마지막 회전에서 이미 원래 각도에 서 있으므로 여기서는 조각만 반지름 방향으로 움직인다.
        // 끝에서 조각 위치·링 각도를 원래 값(0)으로 정확히 못박아, 안쪽 원형 틀과 픽셀 단위로 맞물리게 한다
        // (보간 끝값 t=1도 원래 값이지만, 부동소수 누적이나 프레임 끊김으로 끝값을 못 밟는 경우까지 막는다).
        private async Task SlamAsync()
        {
            Vector2 direction = spreadDirection.normalized;
            float from = spreadDistance;
            float wide = spreadDistance + preSlamExtraSpread;

            if (preSlamExtraSpread > 0f)
            {
                await Tween(preSlamDuration, t => SetPieceOffset(direction * Mathf.Lerp(from, wide, EaseOutCubic(t))));
                if (this == null) return;

                await Wait(preSlamHold);
                if (this == null) return;
            }

            await Tween(slamDuration, t => SetPieceOffset(direction * wide * (1f - t * t * t)));
            if (this == null) return;

            SetPieceOffset(Vector2.zero);
            SetRingAngle(RingRestAngle);

            // 맞물린 바로 그 프레임에 개방 — 결합의 충격으로 바깥 링이 튀어나오는 그림. 튀기·흔들림은 그 위에 겹친다.
            Reveal();
            IsUnlocking = false;
            Engaged?.Invoke();

            await ImpactAsync();
        }

        private async Task ImpactAsync()
        {
            if (root == null) return;

            Vector3 scale = root.localScale;
            Vector2 position = root.anchoredPosition;

            await Tween(impactDuration, t =>
            {
                // 감쇠: 부딪힌 직후가 가장 세고 금방 가라앉는다.
                float decay = (1f - t) * (1f - t);
                root.localScale = scale * Mathf.Lerp(1f, impactPunchScale, decay);

                float shake = impactShake * decay;
                root.anchoredPosition = position + new Vector2(Mathf.Sin(t * 70f), Mathf.Cos(t * 55f)) * shake;

                Color edge = Color.Lerp(pieceEdgeColor, impactFlashColor, decay);
                Tint(pieceEdgeGraphics, edge);
            });
            if (this == null) return;

            root.localScale = scale;
            root.anchoredPosition = position;
            Tint(pieceEdgeGraphics, pieceEdgeColor);
        }

        // ④ 바깥 링이 켜지고 동시에 원형 바탕이 꺼져, 가운데에 빈 구멍이 남는다. 가운데 글자도 함께 걷는다.
        // 바깥 링은 이때부터 계속 커진다(GrowTechRound).
        private void Reveal()
        {
            if (techRound != null)
            {
                techRound.transform.localScale = techRestScale;
                techRound.SetActive(true);
                techGrowElapsed = 0f;
                isTechGrowing = true;
            }
            foreach (GameObject go in hideOnReveal)
                if (go != null) go.SetActive(false);

            SetActive(rank, false);
            SetActive(num, false);
            SetActive(caption, false);
        }

        // 경과 시간의 2차식으로 배율을 올린다(속도 + 가속도). 처음엔 튀어나온 크기 근처에 머물다 점점 빨리 커져,
        // 게이지 전체 진입(⑤)과 겹치면 바깥 링이 먼저 화면 밖으로 빨려 나가 구멍으로 끌려들어가는 느낌이 된다.
        private void GrowTechRound(float dt)
        {
            if (techRound == null)
            {
                isTechGrowing = false;
                return;
            }

            techGrowElapsed += dt;
            float e = techGrowElapsed;
            techRound.transform.localScale = techRestScale * (1f + techGrowSpeed * e + techGrowAccel * e * e);
        }

        // ⑤ 링 중심을 화면 가운데로 끌어오며 게이지를 키운다. 링이 화면 밖으로 빠져나가면 구멍 안(배경)만 남는다.
        private async Task DiveAsync()
        {
            if (root == null) return;

            RectTransform parent = root.parent as RectTransform;
            Vector2 fromPos = root.anchoredPosition;
            Vector2 toPos = fromPos;
            if (parent != null)
            {
                Vector2 current = parent.InverseTransformPoint(root.position);
                toPos = fromPos + (parent.rect.center - current);
            }

            Vector3 fromScale = root.localScale;
            float[] fromAlpha = new float[fadeOutOnDive.Length];
            for (int i = 0; i < fadeOutOnDive.Length; i++)
                if (fadeOutOnDive[i] != null) fromAlpha[i] = fadeOutOnDive[i].alpha;

            await Tween(diveDuration, t =>
            {
                float e = Mathf.Pow(t, diveEasePower);
                root.localScale = fromScale * Mathf.Lerp(1f, diveScale, e);
                // 위치는 배율보다 먼저 가운데에 닿아야 "구멍 정면으로 들어가는" 그림이 된다.
                root.anchoredPosition = Vector2.Lerp(fromPos, toPos, EaseOutCubic(Mathf.Clamp01(t * 1.6f)));

                // 나머지 UI는 진입 전반부에 걷는다.
                float fade = 1f - Mathf.Clamp01(t * 2f);
                for (int i = 0; i < fadeOutOnDive.Length; i++)
                    if (fadeOutOnDive[i] != null) fadeOutOnDive[i].alpha = fromAlpha[i] * fade;
            });
        }

        // ── 보조 ─────────────────────────────────────────────

        private void Spin(float speed, float dt)
        {
            if (pieceRing != null) pieceRing.Rotate(0f, 0f, speed * dt);
        }

        private void RefreshPercent()
        {
            if (num == null || fill == null) return;

            int percent = Mathf.RoundToInt(fill.fillAmount * 100f);
            if (percent == shownPercent) return;

            shownPercent = percent;
            num.text = $"{percent}%";
        }

        // 각 조각은 자기 피벗 공간에서 같은 방향으로 움직인다. 피벗이 90°씩 돌아 있어 결과적으로 넷이 모두 바깥을 향한다.
        // 원위치는 0 고정: 조각(anchoredPosition)과 링(각도) 모두 프리팹 원본이 0일 때 안쪽 원형 틀과 맞물린다.
        // Awake 때 읽은 값을 원위치로 삼지 않는 이유 — 씬 인스턴스에 애니메이션 미리보기 등으로 어긋난 값이 저장돼 있으면
        // 그 어긋남이 그대로 "원위치"가 되어, 결합 때 틀과 미묘하게 안 맞는다.
        private void SetPieceOffset(Vector2 offset)
        {
            foreach (RectTransform piece in pieces)
                if (piece != null) piece.anchoredPosition = offset;
        }

        private void CacheRest()
        {
            root = transform as RectTransform;
            if (root != null)
            {
                rootRestPivot = root.pivot;
                rootRestPosition = root.anchoredPosition;
                rootRestScale = root.localScale;
            }

            if (techRound != null) techRestScale = techRound.transform.localScale;

            hideRestActive = new bool[hideOnReveal.Length];
            for (int i = 0; i < hideOnReveal.Length; i++)
                hideRestActive[i] = hideOnReveal[i] == null || hideOnReveal[i].activeSelf;
        }

        private void RestoreRest()
        {
            SetPieceOffset(Vector2.zero);

            if (root != null)
            {
                root.pivot = rootRestPivot;
                root.anchoredPosition = rootRestPosition;
                root.localScale = rootRestScale;
            }

            isTechGrowing = false;
            if (techRound != null)
            {
                techRound.transform.localScale = techRestScale;
                techRound.SetActive(false);
            }
            for (int i = 0; i < hideOnReveal.Length && i < hideRestActive.Length; i++)
                if (hideOnReveal[i] != null) hideOnReveal[i].SetActive(hideRestActive[i]);

            foreach (CanvasGroup group in fadeOutOnDive)
                if (group != null) group.alpha = 1f;

            foreach (CanvasGroup group in hideOnGranted)
            {
                if (group == null) continue;
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }

            Tint(pieceEdgeGraphics, pieceEdgeColor);
        }

        // 배율을 링 중심 기준으로 키우려면 피벗이 가운데여야 한다. 화면상 위치가 튀지 않게 피벗 이동만큼 위치를 보정한다.
        private static void PivotToCenter(RectTransform rect)
        {
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 delta = center - rect.pivot;
            if (delta == Vector2.zero) return;

            Vector2 size = rect.rect.size;
            Vector3 scale = rect.localScale;
            rect.pivot = center;
            rect.anchoredPosition += new Vector2(delta.x * size.x * scale.x, delta.y * size.y * scale.y);
        }

        // 로그인 흐름은 async/await로 짜여 있어(LoginUI·EntryVeil) 코루틴 대신 Task로 맞춘다.
        // 씬 전환으로 이 오브젝트가 사라지면 this == null로 빠져나간다.
        private async Task Tween(float duration, System.Action<float> step)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (this == null) return;
                step(elapsed / duration);
                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }
            if (this != null) step(1f);
        }

        private async Task Wait(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (this == null) return;
                await Task.Yield();
                elapsed += Time.unscaledDeltaTime;
            }
        }

        private static float EaseOutCubic(float t)
        {
            float x = 1f - t;
            return 1f - x * x * x;
        }

        // 사다리꼴 속도 곡선의 진행도(0~1). 가속 구간(accel)에서 등가속, 가운데는 최고 속도 등속, 제동 구간(brake)에서 등감속.
        // 넓이(=이동량)가 1이 되도록 최고 속도를 정하므로 끝값은 정확히 1이다.
        private static float TrapezoidEase(float t, float accel, float brake)
        {
            float peak = 1f / (1f - accel * 0.5f - brake * 0.5f);

            if (t < accel) return peak * t * t / (2f * accel);
            if (t <= 1f - brake) return peak * (accel * 0.5f + (t - accel));

            float r = 1f - t;
            return 1f - peak * r * r / (2f * brake);
        }

        private static void Tint(Graphic[] graphics, Color color)
        {
            foreach (Graphic g in graphics)
                if (g != null) g.color = color;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetActive(Component c, bool active)
        {
            if (c != null && c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
        }
    }
}
