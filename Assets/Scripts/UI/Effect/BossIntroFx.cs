using ProjectS.Managers;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Timeline;
using UnityEngine.UI;

namespace ProjectS.UI
{
    /// <summary>
    /// 보스 등장 연출(HUD 기획서 6절 · UI_DG_BossText · UI_DG_Warning).
    /// 경고 배너가 흐르며 위험 표시가 깜박이다가, 경고가 위아래로 걷히는 동안 위험 표시가 지지직거리며 커지고,
    /// 한순간 일그러진 뒤 "BOSS" 텍스트로 바뀐다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>흐름 → 깜박임 → 걷힘(과 겹쳐) 고조 → 변형 → 텍스트</b> 순서다. 사망 팝업의 글리치 연출(<see cref="GlitchTextFx"/>)을
    /// 통째로 재사용하지 않는 것은 그 시그니처가 뚜렷해서 플레이어가 "죽었을 때 그것"으로 읽기 때문이다.
    /// 사망은 실패, 보스 등장은 긴장·기대라 감정 방향이 반대다.
    /// (위험 표시의 지지직은 <see cref="GlitchImageFx"/>로 결이 다른 지속 상태를 쓴다.)
    /// </para>
    /// <para>
    /// <b>슬램(위에서 내려찍기)을 쓰지 않는다</b>(2026-09-15 TH). 앞 연출이 불꽃 커튼·플레어·지지직으로 이어지는
    /// "신호가 터져 들어오는" 결인데, 물리적으로 내려찍는 동작만 결이 달라 따로 논다. 그래서 위험 표시 자체가
    /// 일그러져 텍스트로 <b>변하는</b> 흐름으로 바꿨다. 아이콘이 가로로 찢어진 모양과 텍스트가 나타나는 첫 모양을
    /// 같은 값(<see cref="warpStretch"/>·<see cref="warpSquash"/>)으로 맞춰, 두 오브젝트가 교체되는 순간이 이어져 보이게 한다.
    /// </para>
    /// <para>
    /// <b>텍스트의 지지직은 셰이더가 아니라 글자 정점으로 낸다.</b> BOSS 텍스트는 이미 <see cref="AshDissolveFx"/>가
    /// 폰트 셰이더를 재 셰이더로 갈아끼워 쓰므로 글리치 셰이더를 함께 얹을 수 없다. 정점 이동은 셰이더와 무관하게 먹는다.
    /// </para>
    /// <para>
    /// <b>걷힘과 변형은 겹쳐서 재생한다.</b> 띠가 다 빠져나간 뒤에 바꾸면 두 동작이
    /// 순서대로 재생되는 별개 연출로 보인다. 아직 걷히는 중에 바뀌어야 하나의 흐름으로 읽힌다.
    /// </para>
    /// <para>
    /// <b>표시 참조는 전부 선택 사항이다.</b> 비어 있는 단계는 건너뛰고 나머지만 재생한다 —
    /// 아트가 붙기 전에도 타이밍을 검증할 수 있게 했다(<c>DeathPopup</c>·<c>LevelUpNotice</c>와 같은 방침).
    /// </para>
    /// <para>
    /// <b>재생 경로는 둘이고, 화면은 하나의 함수가 그린다.</b> 레이드 등장 컷신에서는 Timeline의
    /// <see cref="BossIntroTrack"/>에 꽂아 클립 시간으로 구동하고(불 가림막과 같은 방식),
    /// 테스트·단독 호출은 <see cref="Play"/>가 코루틴으로 구동한다. 두 경로 모두 "시각 t의 화면"을
    /// <see cref="Sample"/>로 그리므로 타이밍이 어긋나지 않는다. 보스 오브젝트를 참조하지 않는다 —
    /// 이름은 클립(또는 호출부)이 넘기므로 보스 스폰·등장 신호와 무관하게 돈다.
    /// </para>
    /// <para>
    /// <b>왜 코루틴이 아니라 시각 샘플링인가.</b> 코루틴은 제 시간으로 흘러가 타임라인을 스크럽·되감아도
    /// 화면이 따라오지 않는다. 등장 컷신은 카메라·보스 모션과 프레임 단위로 맞춰야 해서, 헤드를 끌면
    /// 연출이 같이 움직여야 한다(<see cref="FireCurtainClip"/>과 같은 이유).
    /// </para>
    /// <para>
    /// 코루틴 경로의 시간은 unscaled로 센다. 보스 등장은 히트스톱·컷신으로 timeScale이 낮아진 순간에 겹치기 쉽다.
    /// 타임라인 경로는 디렉터의 시계를 따른다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class BossIntroFx : MonoBehaviour
    {
        [Header("경고 단계")]
        [Tooltip("배너·위험 표시를 담은 묶음. 아이콘이 텍스트로 바뀌는 순간 통째로 숨긴다.")]
        [SerializeField] private GameObject warningRoot;

        [Tooltip("위아래 경고 띠. 흐르는 방향은 각 ScrollingBand의 speed 부호로 정한다.")]
        [SerializeField] private ScrollingBand[] bands;

        [Tooltip("가운데 위험 표시. 깜박일 대상.")]
        [SerializeField] private Graphic warningIcon;

        [Tooltip("메인 플레어 전에 주위에서 하나씩 반짝이는 작은 빛들. 배열 순서가 곧 터지는 순서다. " +
                 "몇 개를 터뜨릴지는 보스의 강함에 맞춰 클립의 Spark Count(또는 Play 인자)로 정한다 — 모바일 게임의 별 등급처럼. " +
                 "★ 메인 플레어와 마찬가지로 경고 묶음(Warning Root) 밖에 둔다.")]
        [SerializeField] private Graphic[] sparkFlares;

        [Tooltip("주위 빛이 하나씩 터지는 간격(초). 마지막 빛이 터지고 이만큼 뒤에 메인 플레어가 터진다.")]
        [SerializeField, Min(0.01f)] private float sparkInterval = 0.12f;

        [Tooltip("주위 빛 하나가 켜져 있는 시간(초). 크기 변화는 메인 플레어의 Start/End Scale·곡선을 함께 쓴다.")]
        [SerializeField, Min(0f)] private float sparkSeconds = 0.08f;

        [Tooltip("주위 빛이 켜지는 순간의 회전(도)을 빛마다 ±이 범위에서 랜덤하게 준다. 0이면 배치한 각도 그대로. " +
                 "재생할 때마다 새로 뽑고, 에디터에서 타임라인을 스크럽할 때는 같은 각도로 고정된다.")]
        [SerializeField, Range(0f, 180f)] private float sparkRandomAngle = 15f;

        [Tooltip("주위 빛이 켜져 있는 동안 도는 각도(도). 음수면 반대 방향. 0이면 돌지 않는다. " +
                 "크기와 같은 곡선을 따라 처음에 빠르게 돌고 끝에서 멎는다 — 일정하게 돌면 반짝임이 아니라 회전하는 물체로 보인다.")]
        [SerializeField] private float sparkSpinDegrees = 45f;

        [Tooltip("경고가 뜨기 직전 번쩍이는 플레어(Image). 비우면 플레어 없이 곧바로 경고가 나타난다. " +
                 "★ 경고 묶음(Warning Root) 밖, 이 오브젝트 바로 아래에 둔다 — 플레어 동안 경고 묶음은 꺼져 있어 안에 두면 안 보인다.")]
        [SerializeField] private Graphic warningFlare;

        [Tooltip("플레어가 켜져 있는 시간(초). 경고가 언제 나타나는지는 아래 Warning Delay After Flare가 정한다. " +
                 "한 프레임(60fps 약 0.017초)보다 짧아도 플레이 중에는 최소 한 프레임은 보인다.")]
        [SerializeField, Min(0f)] private float flareSeconds = 0.01f;

        [Tooltip("플레어가 켜진 뒤 경고(아이콘·띠)가 나타나기까지의 시간(초). " +
                 "Flare Seconds보다 작으면 플레어가 번쩍이는 도중에 경고가 페이드 없이 한 번에 나타난다 — 빛에 가려 튀어나오는 순간이 안 보여 '빛 속에서 등장'으로 읽힌다. " +
                 "Flare Seconds 이상이면 플레어가 꺼진 뒤 Warning Fade In만큼 서서히 나타난다.")]
        [SerializeField, Min(0f)] private float warningDelayAfterFlare = 0.02f;

        [Tooltip("플레어가 켜지는 순간의 크기 배율(플레어 오브젝트에 설정한 크기 기준).")]
        [SerializeField, Min(0f)] private float flareStartScale = 0.8f;

        [Tooltip("플레어가 꺼지기 직전의 크기 배율. 시작보다 커야 빛이 터져 나오는 것으로 읽힌다.")]
        [SerializeField, Min(0f)] private float flareEndScale = 1f;

        [Tooltip("플레어가 커지는 진행 곡선. 앞이 가파를수록 한 번에 터져 나온다 — 선형이면 부풀어 오르는 것으로 보인다.")]
        [SerializeField]
        private AnimationCurve flareScaleCurve = new(
            new Keyframe(0f, 0f, 2.6f, 2.6f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("경고가 나타나는 시간(초).")]
        [SerializeField, Min(0f)] private float warningFadeIn = 0.2f;

        [Tooltip("위험 표시가 빠르게 깜박이는 횟수. 이 뒤에 한 번 켜진 채로 머문다.")]
        [SerializeField, Min(0)] private int fastBlinkCount = 2;

        [Tooltip("빠른 깜박임에서 켜져 있는 시간(초).")]
        [SerializeField, Min(0f)] private float fastBlinkOnSeconds = 0.07f;

        [Tooltip("빠른 깜박임에서 꺼져 있는 시간(초).")]
        [SerializeField, Min(0f)] private float fastBlinkOffSeconds = 0.07f;

        [Tooltip("깜박임이 끝난 뒤 위험 표시가 켜진 채로 머무는 시간(초).")]
        [SerializeField, Min(0f)] private float iconHoldSeconds = 0.5f;

        [Tooltip("위험 표시의 글리치. 비워두면 아이콘에서 찾는다. 깜박여 켜질 때마다 크게 튀게 한다.")]
        [SerializeField] private GlitchImageFx iconGlitch;

        [Header("경고 걷힘(커튼)")]
        [Tooltip("경고 띠가 화면 밖으로 쓸려 나가는 시간(초).")]
        [SerializeField, Min(0.02f)] private float curtainDuration = 0.34f;

        [Tooltip("띠가 밀려나는 거리(px). 화면 밖까지 완전히 빠져나갈 만큼 줘야 한다.")]
        [SerializeField, Min(0f)] private float curtainDistance = 640f;

        [Tooltip("밀려나는 진행 곡선. 뒤로 갈수록 가팔라야 붙잡혔다 뜯겨 나가는 느낌이 난다.")]
        [SerializeField]
        private AnimationCurve curtainCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.6f, 2.6f));

        [Tooltip("커튼이 이만큼 진행했을 때 위험 표시가 일그러지기 시작한다(0~1). " +
                 "띠는 아이콘이 텍스트로 바뀌는 순간 밀려난 자리에서 한 번에 사라진다 — " +
                 "0에 가까우면 거의 안 밀린 채 뚝 끊기고, 1이면 다 걷힌 뒤에 일그러진다.")]
        [FormerlySerializedAs("slamTriggerRatio")]
        [SerializeField, Range(0f, 1f)] private float morphTriggerRatio = 0.5f;

        [Tooltip("커튼이 Morph Trigger Ratio에 도달한 뒤 일그러지기까지 더 기다릴 시간(초). " +
                 "0이면 그 지점에서 곧바로 일그러진다.")]
        [FormerlySerializedAs("slamExtraDelay")]
        [FormerlySerializedAs("shatterToSlamDelay")]
        [SerializeField, Min(0f)] private float morphExtraDelay;

        [Header("위험 표시 고조 (등장 → 변형)")]
        [Tooltip("아이콘이 나타난 순간부터 일그러지기 직전까지 커지는 최종 배율. 1이면 커지지 않는다. " +
                 "일그러짐은 이 크기에서 시작한다.")]
        [SerializeField, Min(0f)] private float iconBuildUpScale = 1.3f;

        [Tooltip("아이콘이 나타날 때의 떨림 폭(px). 0이면 처음엔 가만히 있다가 점점 떨기 시작한다.")]
        [SerializeField, Min(0f)] private float iconShakeStart;

        [Tooltip("일그러지기 직전의 떨림 폭(px). 커질수록 변형 직전의 긴장이 세진다.")]
        [SerializeField, Min(0f)] private float iconShakeEnd = 8f;

        [Tooltip("일그러지기 직전의 글리치 세기(0~1). 아이콘이 커지는 동안 같은 곡선으로 지지직이 거세진다. " +
                 "0이면 Glitch Image Fx의 평상시 값·깜박임 튐만 남는다. 플레이 중에만 보인다(에디터 스크럽에서는 안 보임).")]
        [SerializeField, Range(0f, 1f)] private float iconBuildUpGlitch = 0.45f;

        [Tooltip("크기·떨림·글리치가 고조되는 진행 곡선. 뒤로 갈수록 가팔라야 '점점 차오르다 터진다'가 된다 — " +
                 "선형이면 처음부터 같은 속도로 커져 긴장이 쌓이는 느낌이 약하다.")]
        [SerializeField]
        private AnimationCurve iconBuildUpCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2.2f, 2.2f));

        [Header("변형 (아이콘 → 텍스트)")]
        [Tooltip("아이콘이 일그러지는 시간(초). '한순간'이어야 한다 — 0.1초를 넘기면 찢어지는 게 아니라 늘어나는 것으로 보인다. " +
                 "이 시간이 끝나는 프레임에 아이콘이 사라지고 텍스트가 나타난다.")]
        [SerializeField, Min(0.01f)] private float warpSeconds = 0.07f;

        [Tooltip("일그러진 모양의 가로 배율. 텍스트는 이 모양에서 나타나 제 모양으로 잡힌다(두 오브젝트가 이어져 보이게 공유).")]
        [SerializeField, Min(0f)] private float warpStretch = 2.2f;

        [Tooltip("일그러진 모양의 세로 배율. 작을수록 납작하게 찢어진다. 가로와 반대로 줘야 '신호가 가로로 찢어졌다'로 읽힌다.")]
        [SerializeField, Min(0f)] private float warpSquash = 0.25f;

        [Tooltip("일그러지는 순간 좌우로 튀는 폭(px). 텍스트가 잡히는 동안 이 폭에서 0으로 줄어든다.")]
        [SerializeField, Min(0f)] private float warpJitter = 24f;

        [Tooltip("일그러지는 진행 곡선. 끝이 가파를수록 버티다 한 번에 찢어진다.")]
        [SerializeField]
        private AnimationCurve warpCurve = new(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 3f, 3f));

        [Tooltip("일그러지는 동안의 아이콘 글리치 세기(0~1). 플레이 중에만 보인다.")]
        [SerializeField, Range(0f, 1f)] private float warpGlitch = 1f;

        [Tooltip("아이콘이 텍스트로 바뀌는 순간 튀는 전기 스파크. 비워두면 자식에서 찾는다. 없으면 스파크 없이 바뀐다. " +
                 "양·속도·색은 SparkBurstFx 쪽에서 조절한다.")]
        [SerializeField] private SparkBurstFx sparkBurst;

        [Header("텍스트 등장")]
        [Tooltip("BOSS 텍스트 묶음. 일그러진 모양에서 제 모양으로 잡힌다.")]
        [SerializeField] private RectTransform bossRoot;

        [Tooltip("보스 이름 라벨. 비워두면 이름을 표시하지 않는다.")]
        [SerializeField] private TMP_Text bossNameText;

        [Tooltip("텍스트가 일그러진 모양에서 제 모양으로 잡히는 시간(초).")]
        [SerializeField, Min(0f)] private float revealSeconds = 0.22f;

        [Tooltip("텍스트가 잡히는 진행 곡선. 앞이 가파를수록 '탁' 잡히고 끝에 남는 떨림만 잦아든다.")]
        [SerializeField]
        private AnimationCurve revealCurve = new(
            new Keyframe(0f, 0f, 3f, 3f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("텍스트가 잡히는 동안 글자마다 따로 좌우로 튀는 폭(px). 0이면 글자는 묶음째로만 움직인다. " +
                 "글자가 서로 어긋나야 '신호가 아직 안 맞았다'로 읽힌다.")]
        [SerializeField, Min(0f)] private float textCharJitter = 18f;

        [Header("텍스트 글리치 (셰이더)")]
        [Tooltip("텍스트가 나타나는 순간의 셰이더 글리치 세기(0~1). 아이콘이 찢어진 기세를 이어받는다. " +
                 "★ Ash Dissolve Fx의 Dissolve Shader 슬롯에 'ProjectS/UI Glitch Ash Text'가 들어 있어야 보인다.")]
        [SerializeField, Range(0f, 1f)] private float textGlitchPeak = 1f;

        [Tooltip("글리치가 잦아든 뒤 남는 세기. 0이면 완전히 멈추고, 0.1쯤이면 머무는 동안에도 미세하게 지지직거린다.")]
        [SerializeField, Range(0f, 1f)] private float textGlitchSettled = 0.1f;

        [Tooltip("글리치가 Peak에서 Settled로 잦아드는 시간(초).")]
        [SerializeField, Min(0.01f)] private float textGlitchSettleSeconds = 0.6f;

        [Tooltip("잦아드는 진행 곡선(0 → 1). 앞이 가파를수록 빨리 잡히고 끝에 남는 지지직만 오래 간다.")]
        [SerializeField]
        private AnimationCurve textGlitchCurve = new(
            new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("글리치 모양(파편 크기·슬라이스·고스트 등)을 복사해 올 머티리얼. 'ProjectS/UI Glitch Ash Text' 셰이더로 만든 " +
                 "BossTextGlitchLook.mat을 넣고 그 머티리얼 값을 조절한다. 비우면 셰이더 기본값을 쓴다. " +
                 "매 프레임 복사하므로 타임라인을 스크럽하거나 플레이 중에 바꾼 값이 바로 보인다.")]
        [SerializeField] private Material textGlitchLook;

        [Header("보스 텍스트 고조 (등장 → 사라짐)")]
        [Tooltip("BOSS 글자 라벨. 이름 라벨과 함께 떨면서 커진다. 비워두면 BOSS 텍스트 묶음 아래에서 이름 라벨이 아닌 첫 텍스트를 찾는다. " +
                 "★ BOSS 텍스트 묶음(Boss Root) 자신이 아니라 그 자식이어야 한다 — 묶음의 크기는 텍스트 등장 변형이 쓴다.")]
        [SerializeField] private TMP_Text bossLabelText;

        [Tooltip("텍스트가 나타난 순간부터 다 타 사라질 때까지 커지는 최종 배율. BOSS 라벨·이름 라벨에 같이 쓴다. 1이면 커지지 않는다.")]
        [FormerlySerializedAs("nameGrowScale")]
        [SerializeField, Min(0f)] private float textGrowScale = 1.15f;

        [Tooltip("텍스트가 나타날 때의 떨림 폭(px).")]
        [FormerlySerializedAs("nameShakeStart")]
        [SerializeField, Min(0f)] private float textShakeStart = 1.5f;

        [Tooltip("다 타 사라지기 직전의 떨림 폭(px).")]
        [FormerlySerializedAs("nameShakeEnd")]
        [SerializeField, Min(0f)] private float textShakeEnd = 4f;

        [Tooltip("크기·떨림이 커지는 진행 곡선. 기본은 일정한 속도 — 터지는 끝이 없어 아이콘처럼 끝을 가파르게 할 이유가 없다.")]
        [FormerlySerializedAs("nameGrowCurve")]
        [SerializeField] private AnimationCurve textGrowCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("마무리")]
        [Tooltip("텍스트가 제 모양으로 잡힌 뒤 화면에 머무는 시간(초).")]
        [SerializeField, Min(0f)] private float holdSeconds = 1.4f;

        [Tooltip("BOSS 텍스트가 재처럼 타들어가며 사라지는 연출. 비워두면 자식에서 찾는다. " +
                 "없으면 아래 시간만큼 그냥 페이드아웃한다.")]
        [SerializeField] private AshDissolveFx ashDissolve;

        [Tooltip("사라지는 데 걸리는 시간(초). 재 연출을 쓰면 그쪽 duration이 대신 쓰인다.")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.35f;

        private CanvasGroup group;
        private Coroutine routine;

        // 커튼은 띠를, 텍스트 등장은 BOSS를 제자리에서 옮긴다. 원래 자리를 기억해 두지 않으면
        // 두 번째 재생부터 옮겨진 자리를 기준으로 삼아 조금씩 밀려난다.
        private Vector2[] bandHome;
        private Vector2 bossHome;
        private Vector2 iconHome;   // 고조 단계의 떨림이 아이콘을 옮기므로 제자리를 기억한다

        // ── 샘플링 세션(코루틴 재생 1회 또는 타임라인 클립 1회) ──
        // 세션 시작 시점의 상태를 쥐고 있다가 끝날 때 그대로 되돌린다. 하드코딩한 "기본 상태"로 돌리면
        // 에디터 미리보기가 씬에 저장된 값을 바꿔 놓아 씬이 더러워진다.
        private bool sampling;
        private float lastSampleTime;
        private bool ashTouched;

        // 이번 세션에 플레어를 한 프레임이라도 그렸는지. 플레어가 한 프레임보다 짧으면 샘플 시각이 그 구간을
        // 건너뛰어 아예 안 보이는데, 그걸 막으려고 "아직 못 그렸으면 이번 프레임에 한 번 그린다"의 기준으로 쓴다.
        private bool flareShown;

        // 주위 빛마다 한 프레임이라도 그렸는지(비트 i = sparkFlares[i]). 용도는 flareShown과 같다.
        private int sparksShownMask;

        // 이번 세션의 주위 빛 시작 각도를 뽑는 씨앗. 빛마다 이 값과 인덱스로 각도를 결정적으로 뽑는다(BeginSampling에서 정함).
        private int sparkAngleSeed;

        // 이번 재생에서 터뜨릴 주위 빛 개수. -1이면 배열 전부. 클립/Play가 재생 시작에 정한다(SetSparkCount).
        private int sparkCountOverride = -1;
        private RestState rest;

        // 텍스트 등장 중 글자 정점을 흔들 대상(bossRoot 아래 전부). 흔든 뒤에는 메시를 다시 만들어 되돌려야 한다.
        private TMP_Text[] bossTexts;
        private bool textWarped;

        // 이번 세션에 글리치를 넣을 폰트 머티리얼 인스턴스들(재 연출이 만든 것). BeginSampling에서 모은다.
        private readonly List<Material> textGlitchMaterials = new();
        private bool textGlitchWarned;
        private Vector2 nameHome;
        private Vector2 labelHome;

        private static readonly int GlitchId = Shader.PropertyToID("_Glitch");

        // Text Glitch Look 머티리얼에서 복사할 모양 값. 재 연출 값(_Dissolve 등)과 글자 색은 복사하지 않는다 — 그쪽 주인은 따로 있다.
        private static readonly int[] GlitchLookFloatIds =
        {
            Shader.PropertyToID("_CellSize"), Shader.PropertyToID("_CellAspect"), Shader.PropertyToID("_RowWidthJitter"),
            Shader.PropertyToID("_Scatter"), Shader.PropertyToID("_CellOffset"), Shader.PropertyToID("_RgbSplit"),
            Shader.PropertyToID("_FlickerSpeed"), Shader.PropertyToID("_Scanline"), Shader.PropertyToID("_Softness"),
            Shader.PropertyToID("_SliceHeight"), Shader.PropertyToID("_SliceAmount"), Shader.PropertyToID("_SliceOffset"),
            Shader.PropertyToID("_GhostOffset"), Shader.PropertyToID("_GhostJitter"), Shader.PropertyToID("_GhostIdle"),
        };

        private static readonly int[] GlitchLookColorIds =
        {
            Shader.PropertyToID("_GhostColorL"), Shader.PropertyToID("_GhostColorR"),
        };

        // 떨림을 뽑는 박자(Hz). 매 프레임 난수 대신 시각에서 결정적으로 뽑아,
        // 스크럽해도 같은 모양이 나오게 한다.
        private const float ShakeRate = 60f;

        /// <summary>세션 시작 시점의 표시 상태. <see cref="EndSampling"/>이 이 값으로 되돌린다.</summary>
        private struct RestState
        {
            public bool RootActive;
            public float Alpha;
            public bool WarningActive;
            public bool IconActive;
            public Color IconColor;
            public Vector3 IconScale;
            public bool FlareActive;
            public Vector3 FlareScale;
            public bool[] SparkActive;
            public Vector3[] SparkScale;
            public Quaternion[] SparkRotation;
            public bool BossActive;
            public Vector3 BossScale;
            public Vector3 NameScale;
            public Vector3 LabelScale;
        }

        /// <summary>연출 안의 각 단계가 시작·끝나는 시각(초). 인스펙터 값에서 매번 계산한다.</summary>
        private struct Timings
        {
            public int SparkCount;
            public float FlareStart;
            public float FlareEnd;
            public float WarningStart;
            public float FadeIn;
            public float BlinkStart;
            public float HoldStart;
            public float CurtainStart;
            public float CurtainEnd;
            public float Morph;       // 아이콘이 일그러지기 시작. 고조의 끝
            public float Swap;        // 아이콘이 사라지고 텍스트가 나타남
            public float RevealEnd;   // 텍스트가 제 모양으로 잡힘
            public float OutStart;
            public float OutDuration;
            public float End;
        }

        /// <summary>지금 코루틴으로 재생 중인지. 타임라인 구동은 포함하지 않는다.</summary>
        public bool IsPlaying => routine != null;

        /// <summary>
        /// 연출 전체 길이(초). 경고 등장부터 BOSS가 다 타 사라질 때까지다.
        /// 타임라인 클립은 이 길이 이상이어야 끝까지 재생된다.
        /// </summary>
        public float Duration
        {
            get
            {
                ResolveOptionalRefs();
                return BuildTimings().End;
            }
        }

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();

            // 연출은 입력을 받지 않는다. 켜 두면 화면을 덮는 동안 아래 HUD 클릭을 삼킨다.
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;

            CaptureBandHome();
            if (bossRoot != null) bossHome = bossRoot.anchoredPosition;
        }

        private void OnDisable()
        {
            // 코루틴은 비활성화와 함께 죽는데 참조가 남으면 IsPlaying이 영영 true가 된다.
            routine = null;
        }

        /// <summary>
        /// 보스 등장 연출을 처음부터 재생한다(코루틴 경로). 이미 재생 중이면 중단하고 다시 시작한다.
        /// 레이드 컷신은 이 메서드가 아니라 <see cref="BossIntroTrack"/>으로 재생한다.
        /// </summary>
        /// <param name="bossName">BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다.</param>
        public void Play(string bossName) => Play(bossName, -1);

        /// <summary>
        /// 보스 등장 연출을 처음부터 재생한다(코루틴 경로). 주위 빛 개수(보스 강함)를 함께 지정한다.
        /// </summary>
        /// <param name="bossName">BOSS 아래에 표시할 보스 이름. 비우면 이름 줄을 숨긴다.</param>
        /// <param name="sparkCount">메인 플레어 전에 터뜨릴 주위 빛 개수. -1이면 배열 전부.</param>
        public void Play(string bossName, int sparkCount)
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;

            // 끊긴 지난 재생의 흔적을 먼저 되돌린다. 오브젝트를 켠 뒤에 되돌리면, 지난 세션이 꺼진 상태에서
            // 시작했을 때 여기서 다시 꺼져 방금 시작한 코루틴이 죽는다.
            EndSampling();

            SetBossName(bossName);
            SetSparkCount(sparkCount);

            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning($"{name}: 컴포넌트가 비활성이라 보스 등장 연출을 재생하지 못했다.", this);
                return;
            }

            // TODO(sound): 보스 등장 연출음 — 등장 스팅/전용 BGM 전환. SoundManager.Instance.PlaySFX(<보스 등장 SFX>) 또는 PlayBgm(<보스 BGM>);
            //   아이콘이 텍스트로 바뀌는 순간(Timings.Swap)에 맞춰 임팩트음을 Sample의 교체 교차 지점에서 따로 낼 수도 있다.
            routine = StartCoroutine(PlayRoutine());
        }

        /// <summary>재생 중인 연출을 즉시 끝낸다. 씬 전환·보스 즉사처럼 화면이 통째로 바뀔 때 호출한다.</summary>
        public void Dismiss()
        {
            StopAllCoroutines();
            routine = null;

            EndSampling();
            if (group != null) group.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// BOSS 아래 이름 줄을 채운다. 비우면 줄을 숨긴다. 타임라인 클립은 재생 시작에 한 번 부른다.
        /// </summary>
        /// <param name="bossName">표시할 이름.</param>
        public void SetBossName(string bossName)
        {
            if (bossNameText == null) return;

            string text = bossName ?? string.Empty;
            if (bossNameText.text != text) bossNameText.text = text;
            SetActive(bossNameText.gameObject, !string.IsNullOrWhiteSpace(text));
        }

        /// <summary>
        /// 메인 플레어 전에 터뜨릴 주위 빛 개수를 정한다. 보스의 강함을 별 등급처럼 보여 주는 손잡이다.
        /// 뒤 단계 시각이 모두 이 개수만큼 밀리므로 <see cref="Sample"/> 전에 부른다(타임라인 클립은 재생 시작에 한 번).
        /// </summary>
        /// <param name="count">터뜨릴 개수. 음수면 배열 전부, 배열보다 크면 배열 길이로 자른다.</param>
        public void SetSparkCount(int count)
        {
            sparkCountOverride = count;
        }

        // 실제로 터뜨릴 주위 빛 개수. 비트마스크로 한 프레임 보장을 세므로 31개로 묶는다.
        private int ActiveSparkCount
        {
            get
            {
                int length = sparkFlares != null ? Mathf.Min(sparkFlares.Length, 31) : 0;
                return sparkCountOverride < 0 ? length : Mathf.Min(sparkCountOverride, length);
            }
        }

        /// <summary>
        /// 연출 시작으로부터 <paramref name="time"/>초 지점의 화면을 그린다. 앞뒤 어느 시각이든 바로 그 모습이 된다.
        /// </summary>
        /// <param name="time">연출 시작 기준 시각(초). 0 미만이나 <see cref="Duration"/> 이상이면 숨긴다.</param>
        /// <remarks>
        /// 첫 호출이 샘플링 세션을 열고 그 순간의 상태를 기억한다. 세션은 <see cref="EndSampling"/>으로 닫아야
        /// 원래 상태로 돌아간다. 한 순간에 튀는 효과(위험 표시 글리치·교체 스파크)는 되감을 수 없으므로
        /// <b>플레이 모드에서 시간이 앞으로 흘러 그 시각을 지날 때만</b> 터뜨린다.
        /// </remarks>
        public void Sample(float time)
        {
            if (!sampling) BeginSampling();

            float prev = lastSampleTime;
            lastSampleTime = time;
            bool emitEvents = Application.isPlaying && time > prev;

            Timings tm = BuildTimings();

            if (time < 0f || time >= tm.End)
            {
                if (group != null) group.alpha = 0f;
                SetActive(gameObject, false);
                return;
            }

            // 먼저 켜야 한다. 플레이 모드에서 처음 켜질 때 Awake가 알파를 0으로 덮으므로, 알파는 그 뒤에 쓴다.
            SetActive(gameObject, true);
            if (group == null) group = GetComponent<CanvasGroup>();

            bool sparkOn = SampleSparks(time, prev, emitEvents, in tm);
            bool flareOn = SampleFlare(time, prev, emitEvents, in tm);
            if (flareOn && time >= tm.FlareEnd)
            {
                // 한 프레임보다 짧은 플레어를 이번 프레임에 억지로 그렸다. 지나쳐 버린 뒤쪽 시각(깜박임·글리치 등)은
                // 다음 프레임이 교차로 잡도록 기준 시각을 플레어 끝 직전으로 되돌린다.
                lastSampleTime = Mathf.Min(time, tm.FlareEnd - 0.0001f);
            }

            SampleGroupAlpha(time, flareOn || sparkOn, in tm);
            // 경고가 아직 안 나올 때. 플레어 뒤에 나오는 설정에서 한 프레임짜리 플레어를 억지로 그리는 프레임도 포함한다
            // (그 프레임에 경고까지 같이 뜨면 "플레어 → 경고" 순서가 무너진다).
            bool beforeWarning = time < tm.WarningStart || (flareOn && tm.WarningStart >= tm.FlareEnd);

            SampleWarning(time, beforeWarning, in tm);
            SampleIcon(time, prev, emitEvents, beforeWarning, in tm);
            SampleBoss(time, in tm);
            SampleTextGrow(time, in tm);
            SampleTextGlitch(time, in tm);
            SampleAsh(time, in tm);

            // 교체 프레임. 아이콘이 텍스트로 바뀌는 바로 그 순간에 스파크가 튀어야 둘이 한 사건으로 읽힌다.
            if (emitEvents && sparkBurst != null && warningIcon != null && Crossed(prev, time, tm.Swap))
                sparkBurst.Play(warningIcon.rectTransform.position);
        }

        /// <summary>
        /// 샘플링 세션을 닫고 세션 시작 시점의 상태로 되돌린다. 열린 세션이 없으면 아무것도 하지 않는다.
        /// 타임라인 클립이 범위를 벗어나거나 그래프가 사라질 때 부른다 — 빠지면 연출이 중간에 잘렸을 때
        /// 경고 띠가 밀려난 채, 일그러진 BOSS가 떠 있는 채 화면에 남는다.
        /// </summary>
        public void EndSampling()
        {
            if (!sampling) return;
            sampling = false;

            MoveBands(0f);

            // 흔든 글자 정점은 씬에 저장되지 않지만, 남겨 두면 다음 등장까지 어긋난 글자가 그대로 보인다.
            if (textWarped) RestoreTextMesh();
            if (iconGlitch != null) iconGlitch.SetDrive(0f);

            // 글리치를 0으로 둬야 다음 등장 전까지 멀쩡한 글자로 보인다(인스턴스라 씬에는 남지 않는다).
            foreach (Material material in textGlitchMaterials)
            {
                if (material != null) material.SetFloat(GlitchId, 0f);
            }

            textGlitchMaterials.Clear();

            if (bossNameText != null)
            {
                bossNameText.rectTransform.localScale = rest.NameScale;
                bossNameText.rectTransform.anchoredPosition = nameHome;
            }

            if (GrowableLabel != null)
            {
                GrowableLabel.rectTransform.localScale = rest.LabelScale;
                GrowableLabel.rectTransform.anchoredPosition = labelHome;
            }

            if (bossRoot != null)
            {
                bossRoot.anchoredPosition = bossHome;
                bossRoot.localScale = rest.BossScale;
                SetActive(bossRoot.gameObject, rest.BossActive);
            }

            if (warningIcon != null)
            {
                warningIcon.rectTransform.localScale = rest.IconScale;
                warningIcon.rectTransform.anchoredPosition = iconHome;
                warningIcon.color = rest.IconColor;
                SetActive(warningIcon.gameObject, rest.IconActive);
            }

            SetActive(warningRoot, rest.WarningActive);
            if (warningFlare != null)
            {
                warningFlare.rectTransform.localScale = rest.FlareScale;
                SetActive(warningFlare.gameObject, rest.FlareActive);
            }

            if (sparkFlares != null && rest.SparkActive != null)
            {
                for (int i = 0; i < sparkFlares.Length && i < rest.SparkActive.Length; i++)
                {
                    if (sparkFlares[i] == null) continue;
                    sparkFlares[i].rectTransform.localScale = rest.SparkScale[i];
                    sparkFlares[i].rectTransform.localRotation = rest.SparkRotation[i];
                    SetActive(sparkFlares[i].gameObject, rest.SparkActive[i]);
                }
            }

            if (ashDissolve != null && ashTouched) ashDissolve.ResetDissolve();
            ashTouched = false;

            if (group != null) group.alpha = rest.Alpha;
            SetActive(gameObject, rest.RootActive);
        }

        /// <summary>
        /// 타임라인 에디터 미리보기가 건드리는 속성을 등록한다. <see cref="BossIntroTrack"/>이 부른다.
        /// </summary>
        /// <param name="driver">미리보기가 끝나면 등록된 속성을 원래 값으로 되돌리는 수집기.</param>
        /// <remarks>
        /// 여기에 없는 속성을 <see cref="Sample"/>이 바꾸면, 헤드를 끌어 본 것만으로 그 값이 씬에 저장된다
        /// (띠가 밀려난 위치, 꺼진 오브젝트가 그대로 커밋되는 사고). 그리는 대상을 늘리면 여기도 함께 늘린다.
        /// </remarks>
        public void CollectDrivenProperties(IPropertyCollector driver)
        {
            if (driver == null) return;

            driver.AddFromName(gameObject, "m_IsActive");
            AddComponent(driver, GetComponent<CanvasGroup>());

            if (warningRoot != null) driver.AddFromName(warningRoot, "m_IsActive");
            if (warningFlare != null)
            {
                driver.AddFromName(warningFlare.gameObject, "m_IsActive");
                AddComponent(driver, warningFlare.rectTransform);
            }

            if (sparkFlares != null)
            {
                foreach (Graphic spark in sparkFlares)
                {
                    if (spark == null) continue;
                    driver.AddFromName(spark.gameObject, "m_IsActive");
                    AddComponent(driver, spark.rectTransform);
                }
            }

            if (bands != null)
            {
                foreach (ScrollingBand band in bands)
                {
                    if (band != null) AddComponent(driver, band.transform);
                }
            }

            if (warningIcon != null)
            {
                driver.AddFromName(warningIcon.gameObject, "m_IsActive");
                AddComponent(driver, warningIcon.rectTransform);
                AddComponent(driver, warningIcon);
            }

            if (bossRoot != null)
            {
                driver.AddFromName(bossRoot.gameObject, "m_IsActive");
                AddComponent(driver, bossRoot);
            }

            if (bossNameText != null)
            {
                driver.AddFromName(bossNameText.gameObject, "m_IsActive");
                AddComponent(driver, bossNameText);
                AddComponent(driver, bossNameText.rectTransform);
            }

            ResolveOptionalRefs();
            if (GrowableLabel != null) AddComponent(driver, GrowableLabel.rectTransform);
        }

        private IEnumerator PlayRoutine()
        {
            float end = Duration;
            float elapsed = 0f;

            while (elapsed < end)
            {
                Sample(elapsed);
                yield return null;
                elapsed += Delta;
            }

            // 다 탄 뒤의 모습은 "아무것도 없음"이다. 상태를 되돌린 뒤 그룹째 내린다.
            routine = null;
            EndSampling();
            group.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void BeginSampling()
        {
            sampling = true;
            lastSampleTime = -1f;
            ashTouched = false;
            flareShown = false;
            sparksShownMask = 0;

            // 플레이 중엔 재생마다 각도를 새로 뽑고, 에디터 스크럽 중엔 고정해 헤드를 끌 때마다 각도가 튀지 않게 한다.
            sparkAngleSeed = Application.isPlaying ? Random.Range(1, int.MaxValue) : 0;

            if (group == null) group = GetComponent<CanvasGroup>();
            ResolveOptionalRefs();
            WarnIfFlareInsideWarning();

            // 세션마다 제자리를 다시 뜬다. 지난 세션은 EndSampling이 제자리로 돌려 두었으므로 항상 같은 값이다.
            CaptureBandHome();
            if (bossRoot != null) bossHome = bossRoot.anchoredPosition;
            if (warningIcon != null) iconHome = warningIcon.rectTransform.anchoredPosition;
            if (bossNameText != null) nameHome = bossNameText.rectTransform.anchoredPosition;
            if (GrowableLabel != null) labelHome = GrowableLabel.rectTransform.anchoredPosition;

            rest = new RestState
            {
                RootActive = gameObject.activeSelf,
                Alpha = group != null ? group.alpha : 0f,
                WarningActive = warningRoot != null && warningRoot.activeSelf,
                IconActive = warningIcon != null && warningIcon.gameObject.activeSelf,
                IconColor = warningIcon != null ? warningIcon.color : Color.white,
                IconScale = warningIcon != null ? warningIcon.rectTransform.localScale : Vector3.one,
                FlareActive = warningFlare != null && warningFlare.gameObject.activeSelf,
                FlareScale = warningFlare != null ? warningFlare.rectTransform.localScale : Vector3.one,
                SparkActive = CaptureSparkActive(),
                SparkScale = CaptureSparkScale(),
                SparkRotation = CaptureSparkRotation(),
                BossActive = bossRoot != null && bossRoot.gameObject.activeSelf,
                BossScale = bossRoot != null ? bossRoot.localScale : Vector3.one,
                NameScale = bossNameText != null ? bossNameText.rectTransform.localScale : Vector3.one,
                LabelScale = GrowableLabel != null ? GrowableLabel.rectTransform.localScale : Vector3.one,
            };

            PrepareTextGlitch();
        }

        /// <summary>
        /// 이번 세션에 글리치를 넣을 BOSS 텍스트 머티리얼을 모은다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 머티리얼을 직접 만들거나 셰이더를 바꾸지 않는다. 폰트 머티리얼 인스턴스와 셰이더 교체는 재 연출(<see cref="AshDissolveFx"/>)이
        /// 이미 맡고 있어서, 여기서도 바꾸면 두 주인이 서로 덮어쓴다. 재 연출의 셰이더 슬롯에 병합 셰이더를 넣어 두면
        /// 같은 인스턴스에 <c>_Glitch</c>가 생기고, 이쪽은 그 값만 넣는다.
        /// </para>
        /// <para>
        /// <see cref="TMP_Text.fontMaterial"/>이 아니라 <see cref="TMP_Text.fontSharedMaterial"/>을 읽는다. fontMaterial은 읽을 때마다
        /// 메시를 다시 만들라고 표시하고, 인스턴스가 없으면 새로 만든다. 재 연출이 만든 뒤라면 shared가 곧 그 인스턴스다.
        /// <c>_Glitch</c> 유무로 거르므로 폰트 원본 에셋을 건드릴 일은 없다.
        /// </para>
        /// </remarks>
        private void PrepareTextGlitch()
        {
            textGlitchMaterials.Clear();
            if (bossTexts == null) return;

            // 재 연출이 셰이더를 갈아끼운 뒤여야 _Glitch가 보인다. 준비만 시키고 탄 정도는 0으로 둔다(세션 시작이라 원래 0이다).
            if (ashDissolve != null) ashDissolve.ResetDissolve();

            foreach (TMP_Text label in bossTexts)
            {
                Material material = label != null ? label.fontSharedMaterial : null;
                if (material != null && material.HasProperty(GlitchId) && !textGlitchMaterials.Contains(material))
                    textGlitchMaterials.Add(material);
            }

            // 슬롯을 안 바꾸면 화면만 봐서는 "글리치가 안 나온다"로만 보여 원인을 찾기 어렵다.
            if (textGlitchMaterials.Count > 0 || textGlitchPeak <= 0f || textGlitchWarned) return;

            textGlitchWarned = true;
            Debug.LogWarning($"{name}: BOSS 텍스트 머티리얼에 _Glitch가 없어 텍스트 글리치를 건너뜁니다. " +
                             "Ash Dissolve Fx의 Dissolve Shader 슬롯에 'ProjectS/UI Glitch Ash Text'를 넣으세요.", this);
        }

        // 플레어를 경고 묶음 안에 두면, 플레어가 켜지는 동안 묶음이 꺼져 있어 아무것도 안 보인다.
        // 화면만 봐서는 "플레어가 안 나온다"로만 보여 원인을 찾기 어려우니 짚어 준다.
        private void WarnIfFlareInsideWarning()
        {
            if (warningRoot == null) return;

            WarnIfInsideWarning(warningFlare, "Warning Flare");
            if (sparkFlares == null) return;
            foreach (Graphic spark in sparkFlares) WarnIfInsideWarning(spark, "Spark Flares");
        }

        private void WarnIfInsideWarning(Graphic flare, string slot)
        {
            if (flare == null || !flare.transform.IsChildOf(warningRoot.transform)) return;

            Debug.LogWarning($"{name}: {slot} '{flare.name}'가 경고 묶음 '{warningRoot.name}' 안에 있어 " +
                             "플레어 동안 함께 꺼져 보이지 않습니다. 경고 묶음 밖(이 오브젝트 바로 아래)으로 옮기세요.", flare);
        }

        private bool[] CaptureSparkActive()
        {
            if (sparkFlares == null) return null;

            var active = new bool[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                active[i] = sparkFlares[i] != null && sparkFlares[i].gameObject.activeSelf;
            return active;
        }

        private Quaternion[] CaptureSparkRotation()
        {
            if (sparkFlares == null) return null;

            var rotation = new Quaternion[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                rotation[i] = sparkFlares[i] != null ? sparkFlares[i].rectTransform.localRotation : Quaternion.identity;
            return rotation;
        }

        private Vector3[] CaptureSparkScale()
        {
            if (sparkFlares == null) return null;

            var scale = new Vector3[sparkFlares.Length];
            for (int i = 0; i < sparkFlares.Length; i++)
                scale[i] = sparkFlares[i] != null ? sparkFlares[i].rectTransform.localScale : Vector3.one;
            return scale;
        }

        private void ResolveOptionalRefs()
        {
            if (iconGlitch == null && warningIcon != null) iconGlitch = warningIcon.GetComponent<GlitchImageFx>();
            if (sparkBurst == null) sparkBurst = GetComponentInChildren<SparkBurstFx>(true);
            if (ashDissolve == null) ashDissolve = GetComponentInChildren<AshDissolveFx>(true);
            if (bossTexts == null && bossRoot != null) bossTexts = bossRoot.GetComponentsInChildren<TMP_Text>(true);

            if (bossLabelText == null && bossTexts != null)
            {
                foreach (TMP_Text label in bossTexts)
                {
                    if (label == null || label == bossNameText || label.transform == bossRoot) continue;
                    bossLabelText = label;
                    break;
                }
            }
        }

        // 떨림·확대를 넣을 BOSS 라벨. 묶음 자신이면 제외한다 — 묶음 크기·위치는 SampleBoss가 쓰므로 둘이 덮어써 튄다.
        private TMP_Text GrowableLabel =>
            bossLabelText != null && bossLabelText.transform != bossRoot && bossLabelText != bossNameText ? bossLabelText : null;

        /// <summary>
        /// 인스펙터 값에서 단계별 시각을 계산한다. 순서는 <b>(플레어) → 흐름 → 깜박임 → 걷힘(과 겹쳐) 변형 → 텍스트 → 머묾 → 타들어감</b>이다.
        /// </summary>
        /// <remarks>
        /// 변형 시작은 커튼 끝이 아니라 커튼 진행 도중(<see cref="morphTriggerRatio"/>)에 둔다.
        /// 다 걷힌 뒤에 바꾸면 두 동작이 순서대로 재생되는 별개 연출로 보인다.
        /// </remarks>
        private Timings BuildTimings()
        {
            Timings t = default;
            // 플레어가 없으면 둘 다 0 — 이하 시각이 플레어 추가 전과 같다.
            // 주위 빛이 하나씩 터지고, 마지막 빛에서 한 간격 뒤에 메인 플레어가 터진다. 주위 빛이 없으면 0.
            t.SparkCount = ActiveSparkCount;
            t.FlareStart = t.SparkCount * sparkInterval;
            t.FlareEnd = t.FlareStart + (warningFlare != null ? flareSeconds : 0f);
            t.WarningStart = t.FlareStart + (warningFlare != null ? warningDelayAfterFlare : 0f);

            // 플레어가 번쩍이는 도중에 나타나면 페이드를 건너뛴다. 빛이 등장 순간을 가려 주므로 서서히 밝아질 이유가 없고,
            // 페이드를 그대로 두면 같은 그룹의 플레어까지 흐려진다.
            t.FadeIn = t.WarningStart < t.FlareEnd ? 0f : warningFadeIn;
            t.BlinkStart = t.WarningStart + t.FadeIn;
            t.HoldStart = t.BlinkStart + fastBlinkCount * (fastBlinkOnSeconds + fastBlinkOffSeconds);
            t.CurtainStart = t.HoldStart + iconHoldSeconds;
            t.CurtainEnd = t.CurtainStart + curtainDuration;
            t.Morph = t.CurtainStart + curtainDuration * morphTriggerRatio + morphExtraDelay;
            t.Swap = t.Morph + warpSeconds;
            t.RevealEnd = t.Swap + revealSeconds;
            t.OutStart = t.RevealEnd + holdSeconds;
            t.OutDuration = ashDissolve != null ? ashDissolve.Duration : fadeOutSeconds;
            t.End = t.OutStart + t.OutDuration;
            return t;
        }

        /// <summary>
        /// 경고 직전의 플레어. 켜져 있어야 하는 프레임이면 true를 돌려주고, 그동안 경고·아이콘은 숨긴다.
        /// </summary>
        /// <remarks>
        /// 페이드 없이 한 번에 켜졌다 꺼진다. 서서히 밝아지면 "반짝"이 아니라 "켜졌다"로 읽혀,
        /// 경고가 터져 들어오는 인상이 약해진다. 대신 켜져 있는 동안 작게 시작해 커진다 — 크기가 그대로면
        /// 켜졌다 꺼지는 깜빡임으로만 보이고, 커져야 빛이 터져 나오는 것으로 읽힌다.
        /// </remarks>
        private bool SampleFlare(float time, float prev, bool emitEvents, in Timings tm)
        {
            if (warningFlare == null) return false;

            bool shown = flareShown;
            // 메인 플레어는 돌리지 않는다(가로로 긴 모양이라 돌면 화면을 가르는 선이 기울어 보인다).
            bool on = SampleBurst(warningFlare, rest.FlareScale, tm.FlareStart, tm.FlareEnd, time, prev, emitEvents, ref shown);
            flareShown = shown;
            return on;
        }

        /// <summary>
        /// 메인 플레어 전에 주위 빛을 배열 순서대로 하나씩 터뜨린다. 하나라도 켜져 있으면 true.
        /// </summary>
        /// <remarks>
        /// 한꺼번에 켜지 않고 간격을 두고 차례로 터뜨린다 — 모바일 게임의 별 등급처럼 "하나, 둘, 셋…" 세어지는 박자가
        /// 곧 보스의 강함으로 읽힌다. 개수(<see cref="SetSparkCount"/>)를 넘는 빛은 이번 재생에서 꺼 둔다.
        /// </remarks>
        private bool SampleSparks(float time, float prev, bool emitEvents, in Timings tm)
        {
            if (sparkFlares == null) return false;

            bool anyOn = false;
            for (int i = 0; i < sparkFlares.Length; i++)
            {
                Graphic spark = sparkFlares[i];
                if (spark == null) continue;

                if (i >= tm.SparkCount)
                {
                    SetActive(spark.gameObject, false);
                    continue;
                }

                float start = i * sparkInterval;
                int bit = 1 << i;
                bool shown = (sparksShownMask & bit) != 0;
                Vector3 baseScale = rest.SparkScale != null && i < rest.SparkScale.Length ? rest.SparkScale[i] : Vector3.one;
                Quaternion baseRotation = rest.SparkRotation != null && i < rest.SparkRotation.Length ? rest.SparkRotation[i] : Quaternion.identity;

                // 빛마다 다른 시작 각도. 씨앗+인덱스로 뽑아 한 재생 안에서는 프레임마다 같은 값이 나온다.
                float startAngle = (Hash01(sparkAngleSeed + i * 7919) * 2f - 1f) * sparkRandomAngle;

                if (SampleBurst(spark, baseScale, start, start + sparkSeconds, time, prev, emitEvents, ref shown))
                {
                    anyOn = true;
                    ApplySparkRotation(spark, baseRotation, startAngle, start, start + sparkSeconds, time);
                }

                if (shown) sparksShownMask |= bit;
            }

            return anyOn;
        }

        /// <summary>
        /// 켜져 있는 주위 빛을 시작 각도에서 <see cref="sparkSpinDegrees"/>만큼 돌린다. 크기와 같은 곡선을 따른다.
        /// </summary>
        /// <remarks>
        /// 회전은 배치해 둔 각도(세션 시작 값)에 더한다. 레이아웃에서 빛마다 기울여 둔 각도를 덮어쓰지 않게 하기 위함이다.
        /// </remarks>
        private void ApplySparkRotation(Graphic spark, Quaternion baseRotation, float startAngle, float start, float end, float time)
        {
            float length = end - start;
            float k = length > 0f ? Mathf.Clamp01((time - start) / length) : 1f;
            float e = flareScaleCurve != null && flareScaleCurve.length > 0 ? flareScaleCurve.Evaluate(k) : k;

            spark.rectTransform.localRotation = baseRotation * Quaternion.Euler(0f, 0f, startAngle + sparkSpinDegrees * e);
        }

        /// <summary>
        /// 빛 하나를 [<paramref name="start"/>, <paramref name="end"/>) 동안 켜고, 그사이 작게 시작해 커지게 한다.
        /// 메인 플레어와 주위 빛이 같은 규칙으로 터진다.
        /// </summary>
        /// <param name="shown">이번 세션에 한 프레임이라도 그렸는지. 한 프레임보다 짧은 빛을 건너뛰지 않기 위한 기준.</param>
        /// <returns>이번 프레임에 켜져 있으면 true.</returns>
        private bool SampleBurst(Graphic burst, Vector3 baseScale, float start, float end,
                                 float time, float prev, bool emitEvents, ref bool shown)
        {
            bool on = time >= start && time < end;

            // 빛이 한 프레임보다 짧으면 두 샘플 사이에 끼어 한 번도 안 그려질 수 있다.
            // 플레이 중 그 구간을 방금 건너뛰었다면 이번 프레임에 한 번은 그린다(스크럽 중에는 보이는 그대로 둔다).
            if (!on && emitEvents && !shown && time >= start && prev < end) on = true;
            if (on) shown = true;

            if (on)
            {
                // 크기는 오브젝트에 잡아 둔 크기(세션 시작 값)에 곱한다. 레이아웃에서 맞춘 비율을 덮어쓰지 않게.
                // 억지로 그린 한 프레임(구간을 이미 지난 시각)은 끝 크기로 보인다.
                float length = end - start;
                float k = length > 0f ? Mathf.Clamp01((time - start) / length) : 1f;
                float e = flareScaleCurve != null && flareScaleCurve.length > 0 ? flareScaleCurve.Evaluate(k) : k;
                burst.rectTransform.localScale = baseScale * Mathf.LerpUnclamped(flareStartScale, flareEndScale, e);
            }

            SetActive(burst.gameObject, on);
            return on;
        }

        /// <summary>그룹 알파. 경고가 나타나는 페이드인과, 재 연출이 없을 때의 페이드아웃만 담당한다.</summary>
        private void SampleGroupAlpha(float time, bool flareOn, in Timings tm)
        {
            // 플레어는 같은 그룹 안에 있으므로 번쩍이는 동안은 1이어야 한다. 경고의 페이드인은 경고가 나타나는 시각부터 센다
            // (플레어 도중에 나타나는 설정이면 FadeIn이 0이라 페이드 자체가 없다).
            float alpha = 1f;
            if (!flareOn && tm.FadeIn > 0f && time < tm.WarningStart + tm.FadeIn)
                alpha = Mathf.Clamp01((time - tm.WarningStart) / tm.FadeIn);

            // 재 연출을 쓰면 텍스트가 직접 타들어가므로 그룹은 1로 둔다.
            // 둘을 겹치면 재가 뜨자마자 같이 흐려져 날리는 게 안 보인다.
            if (ashDissolve == null && time >= tm.OutStart && tm.OutDuration > 0f)
                alpha = 1f - Mathf.Clamp01((time - tm.OutStart) / tm.OutDuration);

            group.alpha = alpha;
        }

        /// <summary>
        /// 경고 띠가 위아래로 갈라지며 화면 밖으로 쓸려 나가다가, 아이콘이 텍스트로 바뀌는 순간 경고 묶음째 끊긴다.
        /// </summary>
        /// <remarks>
        /// 슬램 시절에는 다 빠져나간 뒤 껐다(툭 없어지면 다음 단계와 이어지지 않아서). 변형 연출에서는 반대로
        /// <b>교체 프레임에 한 번에 끊는 것</b>이 신호가 바뀌는 순간으로 읽혀 앞 단계와 이어진다(2026-09-15 TH).
        /// 밀려나는 정도는 <see cref="morphTriggerRatio"/>가 정한다.
        /// </remarks>
        private void SampleWarning(float time, bool beforeWarning, in Timings tm)
        {
            // 아이콘이 텍스트로 바뀌는 순간 띠도 함께 끊는다. 교체와 한 프레임에 겹쳐야 "신호가 바뀌었다"는 한 사건으로 읽힌다
            // (다 빠져나갈 때까지 기다리면 텍스트가 뜬 뒤에도 띠가 흘러가 시선이 갈린다).
            // 위험 표시가 이 묶음 안에 있어도 일그러지는 동안(Morph~Swap)은 보인다.
            SetActive(warningRoot, !beforeWarning && time < tm.Swap);

            float progress = time < tm.CurtainStart
                ? 0f
                : curtainCurve.Evaluate(Mathf.Clamp01((time - tm.CurtainStart) / Mathf.Max(0.0001f, curtainDuration)));
            MoveBands(progress);
        }

        /// <summary>
        /// 위험 표시: 빠르게 두어 번 깜박인 뒤 켜진 채 머물면서 점점 커지고 거세게 지지직거리다가, 한순간 가로로 찢어지듯 일그러지고 사라진다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 같은 간격으로 계속 깜박이면 신호등처럼 읽힌다. 짧게 튄 뒤 멎어야 "경고가 들어왔다"가 되고,
        /// 머무는 동안 긴장이 쌓인다. 경고가 길어지면 가만히 있는 아이콘이 정적으로 보이므로, 변형까지 크기·떨림·글리치를
        /// 함께 키워 "차오르다 터진다"로 만든다.
        /// </para>
        /// <para>
        /// <b>일그러짐은 가로로 늘고 세로로 납작해진다.</b> 고르게 부풀리면 "커졌다"일 뿐이고, 가로로 찢어져야
        /// 앞 연출의 지지직(신호 손상)과 같은 언어로 읽힌다. 텍스트가 같은 모양에서 시작하므로 교체가 끊겨 보이지 않는다.
        /// </para>
        /// </remarks>
        private void SampleIcon(float time, float prev, bool emitEvents, bool beforeWarning, in Timings tm)
        {
            if (warningIcon == null) return;

            // 고조 진행도: 아이콘이 나타난 순간 0 → 일그러지기 시작하는 순간 1. 곡선을 거쳐 뒤로 갈수록 빠르게 차오른다.
            float buildSpan = tm.Morph - tm.WarningStart;
            float buildRaw = buildSpan > 0f ? Mathf.Clamp01((time - tm.WarningStart) / buildSpan) : 1f;
            float build = Evaluate(iconBuildUpCurve, buildRaw);
            float grown = Mathf.LerpUnclamped(1f, iconBuildUpScale, build);

            bool visible;
            Vector3 scale = Vector3.one * grown;
            Vector2 shake = Vector2.zero;
            float glitch = 0f;
            int step = Mathf.FloorToInt(time * ShakeRate);

            if (beforeWarning)
            {
                // 플레어가 먼저 번쩍이고, 아이콘은 Warning Delay After Flare 뒤에 나타난다.
                visible = false;
            }
            else if (time < tm.Morph)
            {
                visible = time >= tm.HoldStart || IsBlinkOn(time, in tm);

                // 떨림도 크기와 같은 진행도로 거세진다. 시각에서 결정적으로 뽑아 스크럽해도 같은 모양이 나온다.
                float amp = Mathf.LerpUnclamped(iconShakeStart, iconShakeEnd, build);
                if (amp > 0f)
                {
                    shake.x = (Hash01(step * 2 + 101) * 2f - 1f) * amp;
                    shake.y = (Hash01(step * 2 + 102) * 2f - 1f) * amp;
                }

                glitch = Mathf.LerpUnclamped(0f, iconBuildUpGlitch, build);
            }
            else if (time < tm.Swap)
            {
                // 일그러짐은 다 커진 크기에서 시작한다. 1에서 다시 시작하면 변형 순간 아이콘이 툭 줄어들어 보인다.
                float w = Evaluate(warpCurve, warpSeconds > 0f ? Mathf.Clamp01((time - tm.Morph) / warpSeconds) : 1f);
                visible = true;
                scale = new Vector3(grown * Mathf.LerpUnclamped(1f, warpStretch, w),
                                    grown * Mathf.LerpUnclamped(1f, warpSquash, w), 1f);

                // 가로로는 크게 튀고 세로 떨림은 죽인다 — 세로까지 흔들리면 찢어진 게 아니라 흔들린 것으로 보인다.
                shake.x = (Hash01(step * 2 + 101) * 2f - 1f) * Mathf.LerpUnclamped(iconShakeEnd, warpJitter, w);
                shake.y = (Hash01(step * 2 + 102) * 2f - 1f) * iconShakeEnd * (1f - w);

                glitch = warpGlitch;
            }
            else
            {
                visible = false;
            }

            warningIcon.rectTransform.localScale = scale;
            warningIcon.rectTransform.anchoredPosition = iconHome + shake;

            Color c = warningIcon.color;
            c.a = 1f;
            warningIcon.color = c;

            SetActive(warningIcon.gameObject, visible);

            // 글리치 세기의 바닥값을 준다. 평상시 떨림·깜박임 튐은 GlitchImageFx가 계속 굴리고, 이 값보다 약할 때만 밀려난다.
            if (iconGlitch != null) iconGlitch.SetDrive(glitch);

            // 켜지는 순간마다 글리치를 크게 튀긴다. 깜박임과 지지직이 같은 박자로 맞아야
            // 신호가 들어오면서 화면이 흔들리는 것처럼 읽힌다.
            if (visible && emitEvents && iconGlitch != null && CrossedIconOnEdge(prev, time, in tm))
                iconGlitch.Pulse();
        }

        /// <summary>
        /// BOSS 텍스트가 아이콘이 일그러진 모양에서 나타나 제 모양으로 잡힌다. 잡히는 동안 묶음과 글자가 좌우로 어긋나다 맞아 들어간다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 텍스트는 아이콘과 <b>같은 가로·세로 배율</b>(<see cref="warpStretch"/>·<see cref="warpSquash"/>)에서 시작한다.
        /// 교체 프레임에 모양이 이어져야 "아이콘이 사라지고 글자가 떴다"가 아니라 "아이콘이 글자로 변했다"가 된다.
        /// </para>
        /// <para>
        /// 묶음 전체의 흔들림만으로는 글자가 한 덩어리로 움직여 "흔들리는 간판"으로 보인다. 글자마다 따로 어긋나야
        /// 신호가 아직 맞춰지는 중으로 읽혀서 <see cref="textCharJitter"/>를 함께 준다.
        /// </para>
        /// </remarks>
        private void SampleBoss(float time, in Timings tm)
        {
            if (bossRoot == null) return;

            bool active = time >= tm.Swap;
            SetActive(bossRoot.gameObject, active);

            Vector3 scale = Vector3.one;
            Vector2 offset = Vector2.zero;
            float charJitter = 0f;

            if (active && time < tm.RevealEnd)
            {
                float e = Evaluate(revealCurve, revealSeconds > 0f ? Mathf.Clamp01((time - tm.Swap) / revealSeconds) : 1f);
                float remain = 1f - e;

                scale = new Vector3(Mathf.LerpUnclamped(warpStretch, 1f, e), Mathf.LerpUnclamped(warpSquash, 1f, e), 1f);

                int step = Mathf.FloorToInt(time * ShakeRate);
                offset.x = (Hash01(step * 2 + 301) * 2f - 1f) * warpJitter * remain;

                charJitter = textCharJitter * remain;
            }

            bossRoot.localScale = scale;
            bossRoot.anchoredPosition = bossHome + offset;

            SampleTextJitter(time, charJitter);
        }

        /// <summary>
        /// BOSS 묶음 안의 글자들을 글자마다 다른 폭으로 좌우로 어긋낸다. 폭이 0이면 흔들었던 메시를 되돌린다.
        /// </summary>
        /// <remarks>
        /// 셰이더가 아니라 TMP 정점을 직접 옮기는 이유는 재 연출(<see cref="AshDissolveFx"/>)이 폰트 셰이더를 이미 쓰고 있어서다.
        /// 매 샘플마다 메시를 새로 만든 뒤 옮기므로 이동이 누적되지 않는다. 등장 구간(수백 ms)에만 돌아 비용은 무시할 만하다.
        /// </remarks>
        private void SampleTextJitter(float time, float amplitude)
        {
            if (bossTexts == null) return;

            if (amplitude <= 0f)
            {
                if (textWarped) RestoreTextMesh();
                return;
            }

            textWarped = true;
            int step = Mathf.FloorToInt(time * ShakeRate);

            foreach (TMP_Text label in bossTexts)
            {
                if (label == null || !label.isActiveAndEnabled) continue;

                label.ForceMeshUpdate();
                TMP_TextInfo info = label.textInfo;

                for (int i = 0; i < info.characterCount; i++)
                {
                    TMP_CharacterInfo ch = info.characterInfo[i];
                    if (!ch.isVisible) continue;

                    // 가로가 주가 되어야 신호가 어긋난 것으로 읽힌다. 세로까지 크면 글자가 흩어진 것으로 보인다.
                    var delta = new Vector3(
                        (Hash01(step * 131 + i * 7919 + 17) * 2f - 1f) * amplitude,
                        (Hash01(step * 131 + i * 7919 + 53) * 2f - 1f) * amplitude * 0.2f,
                        0f);

                    Vector3[] vertices = info.meshInfo[ch.materialReferenceIndex].vertices;
                    int v = ch.vertexIndex;
                    vertices[v] += delta;
                    vertices[v + 1] += delta;
                    vertices[v + 2] += delta;
                    vertices[v + 3] += delta;
                }

                label.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            }
        }

        /// <summary>흔들었던 글자 메시를 원래 배치로 다시 만든다. 꺼져 있는 라벨은 켜질 때 TMP가 스스로 다시 만든다.</summary>
        private void RestoreTextMesh()
        {
            textWarped = false;
            if (bossTexts == null) return;

            foreach (TMP_Text label in bossTexts)
            {
                if (label != null && label.isActiveAndEnabled) label.ForceMeshUpdate();
            }
        }

        /// <summary>
        /// BOSS 라벨과 보스 이름이 나타난 순간부터 다 타 사라질 때까지 계속 떨면서 커진다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 텍스트가 잡힌 뒤 가만히 머물면 경고 아이콘에서 이어진 긴장이 끊겨 정지 화면이 된다. 아이콘의 고조와 같은 언어(떨림 + 확대)를
        /// 텍스트에 이어 붙여 "아직 끝나지 않았다"를 남긴다. BOSS 묶음(<see cref="SampleBoss"/>)의 변형 위에 곱해진다.
        /// </para>
        /// <para>
        /// 묶음이 아니라 라벨마다 따로 움직인다. 크기는 같은 값으로 커지지만 떨림은 라벨마다 다른 난수를 써서,
        /// 두 줄이 한 덩어리로 흔들리지 않고 각자 불안하게 떨린다.
        /// </para>
        /// </remarks>
        private void SampleTextGrow(float time, in Timings tm)
        {
            float g = 0f;
            bool on = time >= tm.Swap;
            if (on)
            {
                float span = tm.End - tm.Swap;
                g = Evaluate(textGrowCurve, span > 0f ? Mathf.Clamp01((time - tm.Swap) / span) : 1f);
            }

            if (GrowableLabel != null)
                ApplyGrowShake(GrowableLabel.rectTransform, rest.LabelScale, labelHome, on, g, time, 501);

            if (bossNameText != null)
                ApplyGrowShake(bossNameText.rectTransform, rest.NameScale, nameHome, on, g, time, 401);
        }

        private void ApplyGrowShake(RectTransform target, Vector3 baseScale, Vector2 home, bool on, float g, float time, int seed)
        {
            Vector3 scale = baseScale;
            Vector2 shake = Vector2.zero;

            if (on)
            {
                scale = baseScale * Mathf.LerpUnclamped(1f, textGrowScale, g);

                float amp = Mathf.LerpUnclamped(textShakeStart, textShakeEnd, g);
                if (amp > 0f)
                {
                    int step = Mathf.FloorToInt(time * ShakeRate);
                    shake.x = (Hash01(step * 2 + seed) * 2f - 1f) * amp;
                    shake.y = (Hash01(step * 2 + seed + 1) * 2f - 1f) * amp;
                }
            }

            target.localScale = scale;
            target.anchoredPosition = home + shake;
        }

        /// <summary>
        /// BOSS 텍스트의 셰이더 글리치. 나타나는 순간 <see cref="textGlitchPeak"/>에서 시작해 <see cref="textGlitchSettled"/>로 잦아든다.
        /// </summary>
        /// <remarks>
        /// 모양 값은 매 프레임 <see cref="textGlitchLook"/>에서 복사한다. 인스턴스 머티리얼은 재생 중에만 있어 직접 고칠 방법이 없는데,
        /// 복사를 세션 시작에 한 번만 하면 프리셋을 고쳐도 다시 재생하기 전까지 반영되지 않아 값 맞추기가 번거롭다.
        /// </remarks>
        private void SampleTextGlitch(float time, in Timings tm)
        {
            if (textGlitchMaterials.Count == 0) return;

            float glitch = textGlitchPeak;
            if (time >= tm.Swap)
            {
                float k = Mathf.Clamp01((time - tm.Swap) / textGlitchSettleSeconds);
                glitch = Mathf.LerpUnclamped(textGlitchPeak, textGlitchSettled, Evaluate(textGlitchCurve, k));
            }

            foreach (Material material in textGlitchMaterials)
            {
                if (material == null) continue;

                CopyGlitchLook(material);
                material.SetFloat(GlitchId, Mathf.Clamp01(glitch));
            }
        }

        private void CopyGlitchLook(Material target)
        {
            if (textGlitchLook == null) return;

            foreach (int id in GlitchLookFloatIds)
            {
                if (textGlitchLook.HasProperty(id)) target.SetFloat(id, textGlitchLook.GetFloat(id));
            }

            foreach (int id in GlitchLookColorIds)
            {
                if (textGlitchLook.HasProperty(id)) target.SetColor(id, textGlitchLook.GetColor(id));
            }
        }

        /// <summary>
        /// 머묾이 끝나면 BOSS 텍스트가 재처럼 타들어간다. 이 시점에 화면에 남은 것은 BOSS 텍스트뿐이다.
        /// </summary>
        private void SampleAsh(float time, in Timings tm)
        {
            if (ashDissolve == null) return;

            if (time >= tm.OutStart)
            {
                ashDissolve.SetProgress(tm.OutDuration > 0f ? (time - tm.OutStart) / tm.OutDuration : 1f);
                ashTouched = true;
            }
            else if (ashTouched)
            {
                // 되감아 머묾 이전으로 돌아왔다 — 탄 자국을 한 번만 지운다.
                ashDissolve.ResetDissolve();
                ashTouched = false;
            }
        }

        private bool IsBlinkOn(float time, in Timings tm)
        {
            float local = time - tm.BlinkStart;
            if (local < 0f) return true;   // 페이드인 동안은 켜진 채로 나타난다

            float period = fastBlinkOnSeconds + fastBlinkOffSeconds;
            if (period <= 0f) return true;

            return local % period < fastBlinkOnSeconds;
        }

        // 위험 표시가 "켜지는" 시각들: 경고 등장, 빠른 깜박임의 매 켜짐, 머묾 시작.
        private bool CrossedIconOnEdge(float prev, float now, in Timings tm)
        {
            if (Crossed(prev, now, tm.WarningStart) || Crossed(prev, now, tm.HoldStart)) return true;

            float period = fastBlinkOnSeconds + fastBlinkOffSeconds;
            for (int i = 0; i < fastBlinkCount; i++)
            {
                if (Crossed(prev, now, tm.BlinkStart + i * period)) return true;
            }

            return false;
        }

        private static bool Crossed(float prev, float now, float edge) => prev < edge && edge <= now;

        /// <summary>띠를 각자 화면 바깥 방향으로 <paramref name="progress"/>만큼 밀어낸다.</summary>
        private void MoveBands(float progress)
        {
            if (bands == null || bandHome == null) return;

            for (int i = 0; i < bands.Length && i < bandHome.Length; i++)
            {
                if (bands[i] == null) continue;

                // 화면 중앙보다 위에 있으면 위로, 아래면 아래로 나간다.
                float outward = bandHome[i].y >= 0f ? 1f : -1f;

                RectTransform rt = (RectTransform)bands[i].transform;
                rt.anchoredPosition = bandHome[i] + new Vector2(0f, outward * curtainDistance * progress);
            }
        }

        private void CaptureBandHome()
        {
            if (bands == null)
            {
                bandHome = null;
                return;
            }

            bandHome = new Vector2[bands.Length];
            for (int i = 0; i < bands.Length; i++)
            {
                if (bands[i] != null) bandHome[i] = ((RectTransform)bands[i].transform).anchoredPosition;
            }
        }

        // 상태가 실제로 바뀔 때만 토글한다. 같은 값으로 매 프레임 SetActive를 부르면 하위 OnEnable/OnDisable이 튄다.
        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static void AddComponent(IPropertyCollector driver, Component component)
        {
            if (component != null) driver.AddFromComponent(component.gameObject, component);
        }

        /// <summary>곡선이 비어 있어도 선형으로 동작하게 한다. 인스펙터에서 곡선을 지우면 키가 0개가 된다.</summary>
        private static float Evaluate(AnimationCurve curve, float t)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
        }

        /// <summary>정수 하나에서 0~1 의사난수를 뽑는다(PCG 해시). 같은 시각이면 항상 같은 흔들림이 나온다.</summary>
        private static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x = (x >> 22) ^ x;
                return x / (float)uint.MaxValue;
            }
        }

        private float Delta => Time.unscaledDeltaTime;
    }
}
