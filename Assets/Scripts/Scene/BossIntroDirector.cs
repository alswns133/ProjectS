using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Networking;
using ProjectS.Players;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 보스 등장 연출 Timeline을, <b>런타임에 스폰된 보스</b>에 다시 물려 재생하는 디렉터.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가</b>: Timeline의 트랙 바인딩(어느 씬 오브젝트를 움직일지)은 Timeline 에셋이 아니라
    /// 씬의 <see cref="PlayableDirector"/>에 저장된다. 그래서 보스를 씬에 미리 박아 두면 그 인스턴스가 바인딩에
    /// 저장되지만, 보스를 프리팹으로 런타임 스폰하면 그 바인딩이 비어(Missing) 연출이 보스를 못 움직인다.
    /// 이 컴포넌트가 스폰된 보스를 받아 트랙을 그 인스턴스로 갈아끼운 뒤 재생한다.
    /// </para>
    /// <para>
    /// <b>언제 시작하나(2026-09-17 확정 구조)</b>:
    /// <list type="bullet">
    ///   <item><b>파티 레이드</b> — 서버(<see cref="RaidIntroSession"/>)가 정한다. 각 클라는 "로딩이 끝났고 내 캐릭터와
    ///     보스가 도착했다"를 보고만 하고(대기 화면 n/총원), 전원 준비 또는 시간 초과 시 서버가 <b>서버 시계 기준 시작 시각</b>을
    ///     알린다. 모두가 그 시각에 맞춰 재생(<see cref="PlaySynced"/>)하므로 신호가 늦게 닿은 클라도 같은 순간에 끝난다.</item>
    ///   <item><b>싱글</b> — 같은 흐름을 "1/1, 서버 없음"으로 스스로 돈다. 대기 화면은 짧은 지연 뒤에만 띄워,
    ///     곧바로 통과할 때 검은 화면이 번쩍이지 않게 한다.</item>
    ///   <item><b>보스방 진입형(일반 던전)</b> — 존 트리거가 <see cref="PlayNow"/>를 부른다.
    ///     <see cref="autoStartWhenSolo"/>를 끈다.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>재생 주체와 화면 연출은 분리돼 있다.</b> 서버는 보스를 실제로 연출대로 움직이려고 재생하되(관찰자는 그 위치를
    /// 동기화로 받는다) UI·입력·카메라 같은 화면 연출은 하지 않는다. 화면 연출(<see cref="Present"/>)은 그 연출을
    /// "보는" 파티원 프로세스에서만 붙는다 — 호스트가 이 파티원이 아니면 호스트 화면이 남의 컷신에 잠기지 않게 하기 위함이다.
    /// </para>
    /// <para>
    /// 재바인딩 대상은 <b>트랙 이름으로</b> 지정한다(<see cref="trackBindings"/>). 이름을 주소로 쓰므로
    /// 같은 타입 트랙이 여러 개여도 정확히 원하는 슬롯에만 꽂힌다. 클립이 보스를 ExposedReference로
    /// 참조하는 구성이라면 <see cref="RebindTo"/>에 SetReferenceValue를 더한다.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayableDirector))]
    public class BossIntroDirector : MonoBehaviour
    {
        // 파티원이 준비를 보고한 뒤 시작 지시를 기다리는 클라 쪽 상한. 서버 대기 상한(30초)보다 넉넉히 길게 둬,
        // 서버가 살아 있으면 항상 서버가 먼저 결론을 낸다. 서버가 죽었을 때만 입력 잠금을 스스로 푸는 백스톱이다.
        private const float MemberStartSafety = 45f;

        // 파티원 클라가 "화면 준비"를 기다리는 상한(로딩·캐릭터·보스 도착). 넘으면 보고를 포기하고 서버 시간 초과에 맡긴다.
        private const float MemberReadyTimeout = 60f;

        // 시작 시각을 받았는데 보스가 아직 도착하지 않은 경우 기다리는 상한.
        private const float BossArrivalTimeout = 10f;

        [Tooltip("등장 연출 Timeline을 재생할 디렉터. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("등장 연출 동안 플레이어 입력을 잠글지. 컷신형 등장이면 켠다.")]
        [SerializeField] private bool lockPlayerInput = true;

        [Tooltip("연출 동안 플레이어 캐릭터를 감출지. 보스만 비추는 컷신이면 켠다. " +
                 "오브젝트를 끄지 않고 메시 렌더러만 꺼(PlayerBodyVisibility) 카메라·입력·네트워크 동기화는 그대로 둔다.")]
        [SerializeField] private bool hidePlayerDuringCutscene;

        // 전환 연출 무적의 안전 만료: 연출이 끝났어야 할 시점에 종료(Finish)를 못 잡았으면 그로부터 이만큼 뒤에 풀린다.
        // (입력 잠금도 Player 쪽에서 같은 기준 — 예정 종료 + 5초 — 으로 스스로 풀린다.)
        private const float CutsceneEndGrace = 5f;

        [Header("트랙 재바인딩")]
        [Tooltip("Timeline 트랙을 이름으로 정확히 지정해 보스의 어느 부분을 꽂을지 짝짓는다. " +
                 "같은 타입 트랙이 여러 개여도 이름으로 갈리므로 정확히 원하는 슬롯에 들어간다.")]
        [SerializeField] private TrackBinding[] trackBindings;

        [Tooltip("등장 연출 동안 UI(UIManager 루트)를 끌지. 시그널 대신 여기서 Play에 끄고 stop에 되살린다.")]
        [SerializeField] private bool hideUIDuringIntro = true;

        [Header("싱글(비파티) 플레이")]
        [Tooltip("혼자 들어온 경우(보스가 로컬 스폰), 보스 등장 + 로딩 종료 + 내 캐릭터 준비가 끝나면 스스로 연출을 시작한다. " +
                 "파티 레이드는 서버가 시작을 정하므로 이 값과 무관하다. 보스방 진입 트리거로 시작하는 던전이면 끈다.")]
        [SerializeField] private bool autoStartWhenSolo = true;

        [Tooltip("싱글일 때 내 캐릭터가 준비되기를 기다리는 최대 시간(초). 넘으면 연출 없이 보스를 깨우고 경고만 남긴다.")]
        [SerializeField, Min(1f)] private float soloWaitTimeout = 20f;

        [Tooltip("싱글에서 대기 화면을 띄우기까지의 지연(초). 보통 곧바로 준비되므로, 그 전에 끝나면 화면을 아예 띄우지 않는다.")]
        [SerializeField, Min(0f)] private float soloWaitShowDelay = 0.3f;

        [Tooltip("연출이 시작될 때 보스가 있어야 할 위치·방향. Timeline 애니메이션 트랙의 Track Offset과 같은 값으로 맞춘다. " +
         "비우면 보스가 연출 시작 순간 서 있던 자리(1페이즈가 HP 임계에 닿은 임의의 지점)에서 그대로 재생된다.")]
        [SerializeField] private GameObject startPoint;


        [Tooltip("연출 끝나고 보스가 있어야할 위치")]
        [SerializeField] private GameObject endPoint;

        [Header("역할")]
        [Tooltip("Intro = 보스 등장 연출(등장 신호·파티원 준비 대기로 시작). " +
                 "PhaseTransition = 페이즈 전환 연출(BossPhaseTransition이 1페이즈 HP 임계에서 시작시키고, 끝나면 실제 전환).")]
        [SerializeField] private DirectorRole role = DirectorRole.Intro;

        /// <summary>이 디렉터가 맡은 연출 종류.</summary>
        public enum DirectorRole
        {
            /// <summary>보스 등장 연출.</summary>
            Intro,

            /// <summary>페이즈 전환 연출. 현재 페이즈와 다음 페이즈 보스를 함께 꽂는다.</summary>
            PhaseTransition,
        }

        /// <summary>트랙에 꽂을 보스. 등장 연출은 항상 <see cref="Boss"/>, 전환 연출은 1페이즈/2페이즈를 고른다.</summary>
        private enum BindTarget
        {
            Boss,             // 등장 연출의 보스 / 전환 연출의 현재(나가는) 페이즈
            NextPhaseBoss,    // 전환 연출에서 새로 등장하는 다음 페이즈
        }

        /// <summary>"이 이름의 트랙에는 이 보스(의 이 자식)를 꽂는다"는 한 줄의 규칙.</summary>
        /// <remarks>
        /// 무엇을 꽂을지(Animator/GameObject/Transform 등)는 <b>트랙이 요구하는 타입으로 자동 판별</b>한다(2026-09-17).
        /// 예전엔 규칙마다 직접 골랐는데, 같은 이름의 활성화 트랙과 애니메이션 트랙이 섞인 타임라인(페이즈 전환의
        /// Boss_Raid_Hammer 3개)을 한 규칙으로 맞출 수 없었다.
        /// </remarks>
        [System.Serializable]
        private struct TrackBinding
        {
            [Tooltip("Timeline 트랙 헤더에 보이는 트랙 이름. 같은 이름의 트랙이 여러 개면 전부 이 규칙을 따른다.")]
            public string trackName;

            [Tooltip("꽂을 보스. 등장 연출은 Boss만 쓴다. 전환 연출은 나가는 페이즈=Boss, 새로 등장하는 페이즈=NextPhaseBoss.")]
            public BindTarget target;

            [Tooltip("보스 루트 아래에서 꽂을 자식. 슬래시가 없으면 그 \"이름\"으로 자손 전체를 뒤져 찾고(예: \"AppearAura\"), " +
                     "슬래시가 있으면 그 \"경로\"를 그대로 따라간다(예: \"Weapon_R/Blade/Fx\"). 비우면 보스 루트 자신에 꽂는다. " +
                     "이름으로 쓸 땐 그 이름이 보스 안에서 유일해야 한다(중복이면 경고 후 첫 매치 사용).")]
            public string childPath;
        }

        /// <summary>이 디렉터의 역할.</summary>
        public DirectorRole Role => role;


        /// <summary>
        /// 연출 시작 위치·회전. 판정 권한이 있는 쪽(서버·싱글)이 연출 직전 보스를 여기로 놓는다. 미지정이면 null.
        /// </summary>
        /// <remarks>
        /// ★ <b>왜 Timeline의 Track Offset으로 안 되는가</b>: 보스 루트에 Animator와 함께 붙은
        /// <c>EnemyMovement</c>가 <c>OnAnimatorMove</c>를 구현해 루트모션을 가로채고
        /// (<c>transform.position += animator.deltaPosition</c> — 현재 위치 기준 상대 누적),
        /// <c>deltaRotation</c>은 아예 쓰지 않는다. 그래서 트랙 오프셋의 절대 위치·회전이 transform에
        /// 도달하지 못한다. <b>트랙 오프셋 값을 바꾸면 이 지점도 같이 맞춰야 한다.</b>
        /// </remarks>
        public Transform StartPoint => startPoint != null ? startPoint.transform : null;

        /// <summary>
        /// 연출이 끝나고 뒷정리까지 마친 순간 1회 발행(자연 종료·강제 종료·중단 모두). 페이즈 전환이 "연출 뒤 실제 전환"을 여기서 한다.
        /// </summary>
        public event System.Action Finished;

        // 전환 연출에서 새로 등장하는 다음 페이즈 보스. 등장 연출에서는 null.
        private Boss nextPhaseBoss;

        // ── 진행 상태(판당 1회. 재진입은 씬 리로드로 새 인스턴스가 되어 자연히 초기화된다) ──
        private bool scheduled;       // 이 프로세스에서 시작이 확정됐다(중복 시작 방지)
        private bool graphStarted;    // 타임라인을 실제로 Play했다
        private bool finished;        // 뒷정리까지 끝났다
        private bool wantPresentation; // 이 프로세스가 화면 연출을 붙여야 한다(파티원·싱글)
        private bool presented;       // 화면 연출(UI 끄기·입력 잠금)을 실제로 붙였다

        // 등장 신호로 받아 둔 보스. 연출 "시작"과 분리해 보관한다 — 보스 스폰은 시작 지시보다 한참 먼저 끝난다.
        private Boss appearedBoss;

        // 우리가 UI를 껐는지. 껐을 때만 되살려, 우리가 안 건드린 UI를 강제로 켜지 않게 한다(안전장치 판정 기준).
        private bool uiHidden;

        // 이번 연출로 재운 보스. stopped 콜백은 보스를 인자로 받지 않으므로 여기 보관해 ResumeAI에 쓴다.
        // null이 아니면 "아직 깨우지 않은 재운 보스가 있다"는 뜻이라, 안전장치(OnDisable)의 판정 기준도 된다.
        private Boss suspendedBoss;

        // 컷신으로 입력을 잠근 플레이어. 연출 도중 LocalPlayer.Current가 바뀌어도 같은 대상을 풀기 위해 보관한다.
        private Player cutscenePlayer;

        // 페이즈 전환 연출로 무적을 건 플레이어. 입력 잠금(lockPlayerInput)과 독립이라 따로 보관해 Finish에서 푼다.
        private Player invinciblePlayer;

        // 이번 연출로 감춘 플레이어 본체들. 끝나면 정확히 이들만 되돌린다(우리가 안 감춘 캐릭터는 건드리지 않기 위함).
        private readonly List<PlayerBodyVisibility> hiddenBodies = new();

        // 대기 화면 동안 입력을 잠근 플레이어(owner=this). 대기 뒤에서도 게임은 돌고 있어 먼저 준비된 사람이 움직이지 않게 한다.
        private Player waitLockedPlayer;

        // ── 네트워크 관찰자 클라 전용 ──
        // 보스 위치는 서버가 연출대로 옮긴 것을 NetworkTransform으로 받는다(중간부터 재생해도 위치가 맞도록 루트모션을
        // 로컬로 쌓지 않는다). 포즈는 로컬 타임라인이 그리므로, 서버 애니메이터 상태를 덮어쓰는 NetworkAnimator만 멈춘다.
        private readonly List<Behaviour> pausedSync = new();

        // 서버 쪽에서 연출 동안 애니메이터 컬링을 끈 대상. 보스 프리팹이 Cull Update Transforms라, 렌더링 카메라가 없는
        // 전용 서버에서는 루트모션 이동 자체가 생략돼 보스가 제자리에 선다. 연출 동안만 Always Animate로 바꾼다.
        // 전환 연출은 두 보스(나가는/등장하는 페이즈)를 함께 돌려 여러 개를 기억한다.
        private readonly List<(Animator animator, AnimatorCullingMode saved)> cullingOverrides = new();

        /// <summary>연결된 Timeline의 길이(초). 서버 세션이 끝 시각을 계산할 때 쓴다.</summary>
        public double Duration => director != null ? director.duration : 0.0;

        private void Awake()
        {
            if (director == null) director = GetComponent<PlayableDirector>();

            if (director != null)
            {
                // 빈 바인딩으로 자동 재생되지 않게 한다 — 반드시 보스에 재바인딩한 뒤 우리가 Play한다.
                director.playOnAwake = false;

                // ★ 등장 연출이 여러 번 반복되던 원인 차단: Wrap Mode가 Loop면 Play가 끝없이 되풀이된다.
                //   한 번 재생하고 멈추도록 None으로 고정한다(끝나면 stopped가 발화해 입력·UI도 복구된다).
                director.extrapolationMode = DirectorWrapMode.None;
            }
        }

        private void OnEnable()
        {
            // 등장 신호·파티원 준비 보고는 등장 연출만의 시작 경로다. 전환 연출은 BossPhaseTransition이 직접 시작시킨다.
            if (role != DirectorRole.Intro) return;

            BossEvents.OnBossAppeared += OnBossAppeared;

            // 파티원: 화면이 준비되면 서버에 보고한다. 대기 화면은 서버 지시로 이미 떠 있다.
            if (IsPartyMember()) StartCoroutine(ReportReadyWhenScreenReady());
        }

        /// <summary>
        /// 이 씬에서 지정 역할의 디렉터를 찾는다. 등장·전환 디렉터가 한 씬에 함께 있어 역할로 가려야 한다.
        /// </summary>
        /// <param name="scene">찾을 씬. 유효하지 않으면 로드된 전체에서 찾는다(클라는 인스턴스가 하나뿐).</param>
        /// <param name="wanted">찾을 역할.</param>
        /// <returns>찾은 디렉터. 없으면 null.</returns>
        public static BossIntroDirector Find(UnityEngine.SceneManagement.Scene scene, DirectorRole wanted)
        {
            foreach (BossIntroDirector candidate in FindObjectsByType<BossIntroDirector>(FindObjectsSortMode.None))
            {
                if (candidate.role != wanted) continue;
                if (scene.IsValid() && candidate.gameObject.scene != scene) continue;
                return candidate;
            }

            return null;
        }

        /// <summary>
        /// 페이즈 전환 연출을 재생한다. 나가는 페이즈(<paramref name="current"/>)와 새로 등장할 페이즈(<paramref name="next"/>)를
        /// 트랙에 꽂고, 정해진 시각에 맞춰 재생한다(<see cref="PlaySynced"/>와 같은 규칙).
        /// </summary>
        /// <remarks>
        /// 연출이 끝나면 <see cref="Finished"/>가 발행되고, 판정 권한이 있는 쪽(서버·싱글)의 BossPhaseTransition이 1페이즈를 걷어내며
        /// 실제 전환을 마무리한다. 2페이즈의 AI는 이 디렉터가 연출 동안 재웠다가 끝나면 깨운다.
        /// </remarks>
        /// <param name="current">나가는 페이즈 보스.</param>
        /// <param name="next">새로 등장하는 페이즈 보스.</param>
        /// <param name="startTime">시작 시각(NaN이면 즉시).</param>
        /// <param name="presentLocally">이 화면에 연출을 붙일지(UI 끄기·입력 잠금). 서버 쪽 재생은 false.</param>
        public void PlayPhaseTransition(Boss current, Boss next, double startTime, bool presentLocally)
        {
            if (next != null && nextPhaseBoss == null) nextPhaseBoss = next;
            PlaySynced(current, startTime, presentLocally);
        }

        private void OnDisable()
        {
            BossEvents.OnBossAppeared -= OnBossAppeared;
            if (director != null) director.stopped -= OnDirectorStopped;

            // 안전장치: 연출 도중 이 컴포넌트가 꺼지거나(씬 이탈 등) stopped를 놓쳐도 뒷정리가 남지 않게 한다.
            // 보스 AI 재우기는 Player 컷신처럼 자체 안전 타이머가 없어, 여기서 반드시 되돌린다.
            Finish();
        }

        // 등장 신호는 "연출을 시작하라"가 아니라 "재바인딩할 보스가 준비됐다"는 뜻으로만 받는다.
        private void OnBossAppeared(Boss boss)
        {
            if (finished || graphStarted || boss == null || !AcceptsBoss(boss)) return;

            if (appearedBoss == null) appearedBoss = boss;

            // 로컬 스폰 보스(netId=0) = 싱글. 서버가 없으니 대기·시작을 스스로 돈다.
            if (autoStartWhenSolo && !scheduled && !IsNetworkBoss(boss))
                StartCoroutine(SoloWaitThenPlay(boss));
        }

        /// <summary>
        /// 연출을 <b>지금</b> 시작한다(보스방 진입 트리거용). 보스가 아직 안 떴으면 등장하는 순간 시작한다.
        /// </summary>
        /// <remarks>
        /// ★ <b>존 트리거를 <see cref="PlayableDirector.Play"/>에 직접 물리지 않는다.</b> 그러면 타임라인만 돌고
        /// 보스 트랙 재바인딩·AI 재우기·입력 잠금·종료 복구가 전부 생략된다. 보스 트랙들의 씬 바인딩이
        /// 비어 있는 것은 런타임에 <see cref="RebindTo"/>가 채우는 전제이기 때문이라, 직통 Play는
        /// <b>보스가 연출대로 하나도 움직이지 않는</b> 증상으로 나타난다(2026-09-16 실제 사고).
        /// 레이드에서는 쓰지 않는다 — 레이드는 전원 준비 후 서버가 시작한다.
        /// </remarks>
        public void PlayNow() => PlaySynced(null, double.NaN, true);

        /// <summary>
        /// 정해진 시각에 맞춰 연출을 재생한다. 이미 지난 시각이면 그만큼 건너뛰어, 모두가 같은 순간에 끝나게 한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 같은 프로세스에서 여러 번 불려도 타임라인은 한 번만 시작한다. 호스트가 파티원이면 서버 재생(화면 연출 없음)과
        /// 자기 클라 지시(화면 연출 있음)가 모두 오는데, 뒤에 온 호출은 이미 도는 연출에 화면 연출만 붙인다.
        /// </para>
        /// <para>
        /// 시각은 네트워크 중이면 <see cref="NetworkTime.time"/>(서버 시계), 아니면 로컬 시계 기준이다.
        /// </para>
        /// </remarks>
        /// <param name="boss">재바인딩할 보스. null이면 등장 신호로 받아 둔 보스(없으면 씬에서 찾는다)를 쓴다.</param>
        /// <param name="startTime">시작 시각. <see cref="double.NaN"/>이면 "보스가 준비되는 즉시".</param>
        /// <param name="presentLocally">이 프로세스 화면에 연출을 붙일지(UI 끄기·입력 잠금). 서버 쪽 재생은 false.</param>
        public void PlaySynced(Boss boss, double startTime, bool presentLocally)
        {
            if (boss != null && appearedBoss == null && AcceptsBoss(boss)) appearedBoss = boss;

            if (finished)
            {
                if (presentLocally) EndWait();
                return;
            }

            if (presentLocally) wantPresentation = true;

            // 이미 도는 연출에 화면 연출만 붙는 경우(호스트 파티원).
            if (graphStarted)
            {
                if (wantPresentation && !presented) Present(director.duration - director.time);
                return;
            }

            if (scheduled) return;   // 시작 대기 중 — 화면 연출 여부는 위에서 기록했으니 시작할 때 붙는다
            scheduled = true;

            StartCoroutine(StartAt(startTime));
        }

        /// <summary>
        /// 연출을 즉시 끝내고 뒷정리한다(보스 깨우기·입력/UI/동기화 복구). 서버의 끝 시각 도달 알림에서 부른다.
        /// 이미 끝났으면 아무 일도 하지 않는다.
        /// </summary>
        public void ForceEnd()
        {
            if (finished) return;

            // Stop이 stopped를 발화시켜 OnDirectorStopped → Finish로 이어진다. 재생 중이 아니었어도 아래 Finish가 정리한다.
            if (graphStarted && director != null && director.state == PlayState.Playing) director.Stop();
            Finish();
        }

        // ── 시작 ────────────────────────────────────────────────

        private IEnumerator StartAt(double startTime)
        {
            // 보스가 아직 이 클라에 도착하지 않았을 수 있다(스폰 메시지가 시작 지시보다 늦게 옴).
            float bossDeadline = Time.time + BossArrivalTimeout;
            while (appearedBoss == null)
            {
                appearedBoss = FindBossInScene();
                if (appearedBoss != null) break;

                if (Time.time > bossDeadline && !double.IsNaN(startTime))
                {
                    Debug.LogWarning($"{LogTag} 시작 시각이 됐지만 보스가 도착하지 않아 연출을 건너뜁니다.", this);
                    Finish();
                    yield break;
                }

                yield return null;
            }

            if (double.IsNaN(startTime)) startTime = Clock();

            // 관찰자는 보스 위치를 보간 버퍼만큼 늦게 받는다. 포즈도 그만큼 늦춰 발과 위치가 어긋나지 않게 한다.
            double localStart = startTime + (IsNetworkObserver(appearedBoss) ? NetworkClient.bufferTime : 0.0);

            while (!finished && Clock() < localStart) yield return null;
            if (finished) yield break;

            StartGraph(Clock() - localStart);
        }

        private void StartGraph(double elapsed)
        {
            if (director == null || director.playableAsset == null)
            {
                Debug.LogError("[BossIntroDirector] 재생할 Timeline이 없습니다 — PlayableDirector 또는 Playable Asset이 비었습니다.", this);
                Finish();
                return;
            }

            double duration = director.duration;
            if (elapsed >= duration)
            {
                // 너무 늦게 도착했다(시간 초과로 먼저 시작한 뒤 합류 등). 볼 연출이 없으니 정리만 한다.
                Debug.Log($"{LogTag} 연출이 이미 끝난 시각({elapsed:0.00}/{duration:0.00}초)이라 건너뜁니다.", this);
                Finish();
                return;
            }

            graphStarted = true;
            Boss boss = appearedBoss;

            // 한 번 재생했으면 다시는 트리거되지 않게 구독을 끊는다(페이즈 2 등장·중복 발행에도 재생 안 됨).
            BossEvents.OnBossAppeared -= OnBossAppeared;

            RebindTo(boss);

            if (IsNetworkObserver(boss))
            {
                // 관찰자 클라: AI는 서버 소유라 재우지도 깨우지도 않는다(ResumeAI가 에이전트를 켜 서버와 싸운다).
                PauseAnimatorSync(boss);
                if (nextPhaseBoss != null) PauseAnimatorSync(nextPhaseBoss);
            }
            else
            {
                // 서버(호스트·전용 서버)·싱글: 보스 AI를 재운다. 이걸 안 하면 Timeline이 Animator 포즈만 덮을 뿐,
                // 상태 머신·NavMeshAgent가 그대로 돌아 플레이어를 추격하고 연출대로 움직이지 않는다.
                // 서버에서 보스가 실제로 연출대로 움직여야 관찰자가 그 위치를 받는다.
                boss.SuspendAI();
                OverrideCulling(boss);

                if (nextPhaseBoss != null)
                {
                    // 전환 연출: 나가는 페이즈는 끝나면 걷어내므로 깨우지 않고, 연출 뒤 계속 싸울 다음 페이즈를 깨운다.
                    nextPhaseBoss.SuspendAI();
                    OverrideCulling(nextPhaseBoss);
                    suspendedBoss = nextPhaseBoss;
                }
                else
                {
                    suspendedBoss = boss;
                }
            }

            // 중복 구독 방지 후 종료 콜백 연결(끝나면 입력·UI 되살림).
            director.stopped -= OnDirectorStopped;
            director.stopped += OnDirectorStopped;

            director.Play();
            if (elapsed > 0.0) director.time = elapsed;   // 늦게 시작한 만큼 건너뛴다(끝 시각을 모두와 맞춘다)

            if (wantPresentation) Present(duration - elapsed);

            // 진단: 연출 시작 순간의 위치·상태를 한 줄 남긴다 — 종료 후 기록과 비교해 연출 중 실제로 이동했는지 본다.
            ProjectS.Debugging.BossAnimatorProbe.Attach(boss, 0.1f, "연출 시작");

            Debug.Log($"{LogTag} 재생 시작 — 보스='{boss.name}', 다음페이즈={(nextPhaseBoss != null ? nextPhaseBoss.name : "-")}, " +
                      $"{elapsed:0.00}초 지점부터, 화면연출={wantPresentation}, 관찰자={IsNetworkObserver(boss)}", this);
        }

        // 진단 로그 머리말: 어떤 연출(등장/전환)이고 어느 컴퓨터(서버/호스트/클라)인지. 클라 로그도 서버 콘솔로 모이므로 구분이 필요하다.
        private string LogTag
        {
            get
            {
                string net = NetworkServer.active ? (NetworkClient.active ? "Host" : "Server") : (NetworkClient.active ? "Client" : "Solo");
                return $"[진단][{(role == DirectorRole.Intro ? "BossIntro" : "Phase")}][{net}]";
            }
        }

        // 이 프로세스 화면에 연출을 붙인다: 대기 화면을 내리고, UI를 끄고, 조작 중인 캐릭터의 입력을 컷신으로 잠근다.
        private void Present(double remaining)
        {
            presented = true;

            RaidIntroEvents.FireWaitEnded();

            // 등장 연출 동안 UI를 끈다(시그널 대신). 되살리는 것은 연출 끝(Finish)에서 한다 —
            // 여기서 켜면 같은 프레임에 껐다 켜져 아무것도 안 숨겨진다.
            SetUIHidden(true);

            // 남은 연출 길이를 예정 종료 시점으로 넘긴다. 종료 신호를 놓치면 Player가 그 시점 + 5초에 스스로 입력을 푼다.
            // (고정값 Player.maxCutsceneDuration=12초는 20~30초 연출보다 짧아 도중에 입력이 풀리던 문제가 있었다.)
            // ★ 멀티에선 PlayerManager.Player가 숨겨진 마을 캐릭터라, 조작 중인 아바타(LocalPlayer.Current)에 건다.
            if (lockPlayerInput)
            {
                cutscenePlayer = LocalPlayer.Current;
                if (cutscenePlayer != null) cutscenePlayer.BeginCutscene((float)remaining);
            }

            // 페이즈 전환 연출은 조작이 잠긴 채 보스 옆에 서 있으므로 무적을 건다(등장 연출에는 걸지 않는다).
            // 입력 잠금 여부와 무관하게 이 화면의 조작 캐릭터에 건다. 푸는 것은 Finish, 못 잡으면 예정 종료 + 5초에 만료.
            if (role == DirectorRole.PhaseTransition)
            {
                invinciblePlayer = LocalPlayer.Current;
                if (invinciblePlayer != null && invinciblePlayer.Stats != null)
                    invinciblePlayer.Stats.SetCutsceneInvincible((float)remaining + CutsceneEndGrace);
            }

            // 보스만 비추는 컷신에서 캐릭터가 화면에 남지 않게 감춘다. 종료 신호를 놓쳐도
            // PlayerBodyVisibility의 안전 타이머가 예정 종료 + 5초에 스스로 되돌린다.
            if (hidePlayerDuringCutscene) HidePlayers((float)remaining + CutsceneEndGrace);

            // 컷신 잠금이 걸린 뒤에 대기 잠금을 푼다 — 순서가 반대면 그 사이 한 프레임 조작이 샌다.
            ReleaseWaitLock();
        }

        /// <summary>
        /// 이 화면에 보이는 플레이어 본체를 감춘다(오브젝트는 끄지 않고 메시 렌더러만).
        /// </summary>
        /// <remarks>
        /// ★ 조작 캐릭터는 씬과 무관하게 무조건 포함한다 — 싱글은 PlayerManager가 DontDestroyOnLoad로 들고 있어
        /// 보스 씬에 속하지 않는다. 씬 필터만 쓰면 멀티 아바타만 감춰지고 싱글은 그대로 보이는 함정이 생긴다.
        /// 파티 레이드는 같은 인스턴스 씬의 다른 파티원 아바타도 함께 감춘다 — 남의 캐릭터가 서 있어도 똑같이 어색하므로.
        /// </remarks>
        private void HidePlayers(float safetySeconds)
        {
            HideBody(LocalPlayer.Current, safetySeconds);

            foreach (Player other in FindObjectsByType<Player>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (other != null && other.gameObject.scene == gameObject.scene) HideBody(other, safetySeconds);
        }

        // 한 캐릭터를 감추고 목록에 남긴다.
        // ★ PlayerBodyVisibility가 없으면 감출 수단이 없다 — 조용히 넘기면 "왜 이 캐릭터만 보이지"를 한참 찾게 되므로 경고를 남긴다
        //   (실제로 Erwin 프리팹에 컴포넌트가 없어 연출 중 캐릭터가 그대로 보였다, 2026-09-25).
        private void HideBody(Player player, float safetySeconds)
        {
            if (player == null) return;

            if (!player.TryGetComponent(out PlayerBodyVisibility body))
            {
                Debug.LogWarning($"{LogTag} '{player.name}'에 PlayerBodyVisibility가 없어 연출 중에도 그대로 보입니다. " +
                                 "캐릭터 프리팹 루트(Animator와 같은 오브젝트)에 그 컴포넌트를 추가하세요.", player);
                return;
            }

            if (hiddenBodies.Contains(body)) return;   // 조작 캐릭터가 씬 검색에도 걸리는 중복 방지

            body.BeginCutsceneHide(safetySeconds);
            hiddenBodies.Add(body);
        }

        // 감췄던 본체를 되돌린다. 모든 종료 경로(Finish)가 거친다.
        private void ShowPlayers()
        {
            foreach (PlayerBodyVisibility body in hiddenBodies)
                if (body != null) body.EndCutsceneHide();

            hiddenBodies.Clear();
        }

        // ── 준비 대기 ────────────────────────────────────────────

        // 파티원: 로딩이 끝나고 내 캐릭터와 보스가 도착하면 서버에 준비를 보고하고, 시작 지시를 기다린다.
        private IEnumerator ReportReadyWhenScreenReady()
        {
            float deadline = Time.time + MemberReadyTimeout;
            Player player = null;

            while (!finished && !graphStarted)
            {
                if (appearedBoss == null) appearedBoss = FindBossInScene();
                player = LocalPlayer.Current;

                if (appearedBoss != null && !IsScreenLoading() && player != null && player.gameObject.activeInHierarchy)
                    break;

                if (Time.time > deadline)
                {
                    Debug.LogWarning($"[BossIntroDirector] {MemberReadyTimeout:0}초 안에 화면이 준비되지 않아 준비 보고를 포기합니다 " +
                                     $"(보스={appearedBoss != null}, 로딩중={IsScreenLoading()}, 캐릭터={player != null}). 서버 시간 초과에 맡깁니다.", this);
                    yield break;
                }

                yield return null;
            }

            if (finished || graphStarted) yield break;

            LockWaitInput(player);

            if (PartyManager.Local != null) PartyManager.Local.ReportRaidIntroReady();
            Debug.Log("[진단][BossIntro] 준비 완료 보고 — 서버의 시작 지시를 기다립니다.", this);

            // 백스톱: 서버가 끝내 지시하지 않으면(서버 이상) 대기 잠금을 스스로 푼다.
            float safety = Time.time + MemberStartSafety;
            while (!finished && !presented && Time.time < safety) yield return null;

            if (!finished && !presented)
            {
                Debug.LogWarning($"[BossIntroDirector] 준비 보고 후 {MemberStartSafety:0}초 동안 시작 지시가 없어 대기를 풉니다.", this);
                EndWait();
            }
        }

        // 싱글: 서버가 없으므로 같은 대기 흐름을 1/1로 스스로 돈다.
        private IEnumerator SoloWaitThenPlay(Boss boss)
        {
            scheduled = true;   // 이 경로가 시작을 맡는다(트리거·중복 신호가 따로 시작하지 않게)

            // 대기 뒤에서도 게임은 돈다. 보스가 대기 중에 먼저 움직이거나 공격하지 않게 등장 즉시 재운다.
            boss.SuspendAI();
            suspendedBoss = boss;

            RaidIntroEvents.FireWaitStarted(soloWaitShowDelay);
            RaidIntroEvents.FireReadyCountChanged(0, 1);

            float deadline = Time.time + soloWaitTimeout;
            Player player = null;

            while (!finished)
            {
                player = LocalPlayer.Current;
                bool playerReady = player != null && player.gameObject.activeInHierarchy;

                if (playerReady && waitLockedPlayer == null) LockWaitInput(player);
                if (playerReady && !IsScreenLoading()) break;

                // 에디터에서 레이드 씬을 바로 튼 테스트면 캐릭터가 영영 안 온다(PlayerManager 없음). 기다리지 않고 바로 재생한다.
                if (IsEditorDirectPlay()) break;

                if (Time.time > deadline)
                {
                    Debug.LogWarning($"[BossIntroDirector] {soloWaitTimeout:0}초 안에 내 캐릭터가 준비되지 않아 연출 없이 보스를 깨웁니다 " +
                                     "— PlayerManager가 이 씬에 캐릭터를 배치했는지 확인하세요.", this);
                    Finish();
                    yield break;
                }

                yield return null;
            }

            if (finished) yield break;

            RaidIntroEvents.FireReadyCountChanged(1, 1);

            // scheduled를 잠시 풀어 PlaySynced가 시작을 확정하게 한다(싱글은 "지금"이 곧 시작 시각).
            scheduled = false;
            PlaySynced(boss, double.NaN, true);
        }

        private void LockWaitInput(Player player)
        {
            if (!lockPlayerInput || player == null || waitLockedPlayer != null) return;

            waitLockedPlayer = player;
            waitLockedPlayer.Input.SetInputSuspended(true, this);
        }

        private void ReleaseWaitLock()
        {
            if (waitLockedPlayer == null) return;

            if (waitLockedPlayer.Input != null) waitLockedPlayer.Input.SetInputSuspended(false, this);
            waitLockedPlayer = null;
        }

        // 대기를 끝낸다(화면 내리기 + 대기 잠금 해제). 연출 없이 끝나는 모든 경로가 거친다.
        private void EndWait()
        {
            RaidIntroEvents.FireWaitEnded();
            ReleaseWaitLock();
        }

        // ── 종료 ────────────────────────────────────────────────

        private void OnDirectorStopped(PlayableDirector stopped)
        {
            if (stopped != director) return;
            Finish();
        }

        // 모든 종료 경로(자연 종료·강제 종료·비활성화·시간 초과)가 거치는 뒷정리. 한 번만 돈다.
        private void Finish()
        {
            if (finished) return;
            finished = true;

            if (director != null) director.stopped -= OnDirectorStopped;

            // 진단: 연출 뒤 보스 애니메이터가 서버·클라에서 제대로 이동 모션으로 넘어가는지 6초간 기록한다.
            // (비활성화 경로에선 오브젝트가 곧 사라지므로 붙이지 않는다.)
            if (graphStarted && isActiveAndEnabled) ProjectS.Debugging.BossAnimatorProbe.Attach(appearedBoss, 6f, "연출 종료 후");

            // 전환 연출: 다음 페이즈는 타임라인 활성화 트랙이 켜고 끈다. 트랙의 재생 후 상태가 "꺼짐"이면 연출이 끝난 뒤
            // 보이지 않은 채 남으므로 모든 화면에서 확실히 켠다(AI를 깨우기 전에 — 꺼진 채 에이전트를 켜면 실패한다).
            if (nextPhaseBoss != null && !nextPhaseBoss.gameObject.activeSelf) nextPhaseBoss.gameObject.SetActive(true);

            // 재웠던 보스를 깨운다(에이전트 NavMesh 복귀 + 교전 흐름 진입).
            if (suspendedBoss != null)
            {
                // ResumeAI의 착지(NavMesh Warp)가 현재 위치를 기준으로 하므로, 반드시 그 전에 스폰 자리로 돌려놓는다.
                // Finish는 시간 초과·씬 이탈 경로에서도 돌므로, endPoint가 비었다고 여기서 예외가 나면 보스가 영영 안 깨어난다.
                // 비어 있으면 옮기지 않고 연출이 끝난 자리에서 깨운다.
                if (endPoint != null)
                    suspendedBoss.transform.SetPositionAndRotation(endPoint.transform.position, endPoint.transform.rotation);

                suspendedBoss.ResumeAI();
                suspendedBoss = null;
            }

            RestoreCulling();
            ResumeAnimatorSync();

            if (cutscenePlayer != null)
            {
                cutscenePlayer.EndCutscene();
                cutscenePlayer = null;
            }

            if (invinciblePlayer != null)
            {
                if (invinciblePlayer.Stats != null) invinciblePlayer.Stats.ClearCutsceneInvincible();
                invinciblePlayer = null;
            }

            ShowPlayers();

            // 시작에서 껐던 UI를 되살린다. 디렉터 오브젝트는 안 꺼지므로 이 경로가 확실히 돈다
            // (UI 오브젝트 위에 붙은 시그널 Receiver가 함께 꺼져 못 켜지던 함정을 피하는 이유).
            if (uiHidden) SetUIHidden(false);

            EndWait();
            presented = false;

            // 마지막에 알린다 — 구독자(BossPhaseTransition)가 1페이즈를 걷어내기 전에 위 뒷정리(2페이즈 깨우기)가 끝나 있어야 한다.
            System.Action finishedHandlers = Finished;
            Finished = null;
            finishedHandlers?.Invoke();
        }

        // ── 판정 도우미 ──────────────────────────────────────────

        // 네트워크 중이면 서버 시계, 아니면 로컬 시계. 시작 시각과 같은 시계로 비교해야 건너뛸 양이 맞는다.
        private static double Clock()
            => NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.timeAsDouble;

        // 이 클라 화면이 아직 로딩 화면인가. 로딩 뒤에서 연출이 먼저 시작되면 앞부분을 못 본다.
        private static bool IsScreenLoading()
            => GameSceneManager.Instance != null && GameSceneManager.Instance.IsLoading;

        // 에디터에서 부트스트랩을 거치지 않고 씬을 바로 튼 테스트인가(DebugRaidDirectPlay 참고).
        // 이때는 캐릭터를 만들어 줄 PlayerManager가 없어, 캐릭터 대기를 그대로 두면 soloWaitTimeout 뒤 연출이 통째로 생략된다.
        // 빌드는 항상 부트스트랩을 거치므로 false로 고정한다.
        private static bool IsEditorDirectPlay()
        {
#if UNITY_EDITOR
            return PlayerManager.Instance == null && GameSceneManager.Instance == null;
#else
            return false;
#endif
        }

        // 이 프로세스의 로컬 플레이어가 파티에 속해 있는가(= 서버가 시작을 정하는 레이드).
        private static bool IsPartyMember()
            => PartyManager.Local != null
               && PartyManager.Local.TryGetComponent(out PlayerPresence me)
               && me.PartyId != 0;

        private static bool IsNetworkBoss(Boss boss)
            => boss.TryGetComponent(out NetworkIdentity identity) && identity.netId != 0;

        /// <summary>
        /// 이 클라가 보스의 <b>네트워크 관찰자</b>(서버가 아닌 순수 클라)인지. 이때만 AI 대신 애니메이터 동기화를 멈춘다.
        /// </summary>
        /// <remarks>
        /// 싱글(로컬 스폰, netId=0)과 서버(호스트 포함, isServer)는 보스를 직접 소유하므로 AI를 재운다.
        /// </remarks>
        private static bool IsNetworkObserver(Boss boss)
            => boss != null && boss.TryGetComponent(out NetworkIdentity identity) && identity.netId != 0 && !identity.isServer;

        // 이 디렉터가 받아도 되는 보스인가. 서버 프로세스는 여러 파티 인스턴스를 동시에 들고 있어 같은 씬의 보스만 받는다.
        // 순수 클라는 인스턴스가 하나뿐이고, 네트워크 스폰 오브젝트가 활성 씬에 놓여 디렉터와 씬이 다를 수 있어 검사하지 않는다.
        private bool AcceptsBoss(Boss boss)
            => !NetworkServer.active || boss.gameObject.scene == gameObject.scene;

        // 등장 신호를 놓쳤을 때(구독 전에 스폰됨)의 보강. 받아도 되는 보스 중 첫 번째를 쓴다.
        private Boss FindBossInScene()
        {
            foreach (Boss candidate in FindObjectsByType<Boss>(FindObjectsSortMode.None))
                if (candidate != null && AcceptsBoss(candidate)) return candidate;

            return null;
        }

        // ── 관찰자: 애니메이터 동기화 일시정지 ─────────────────────────

        private void PauseAnimatorSync(Boss boss)
        {
            pausedSync.Clear();

            // 켜져 있던 것만 끄고 기억한다 — 원래 꺼져 있던 컴포넌트를 연출 뒤 멋대로 켜지 않기 위함.
            // NetworkTransform은 끄지 않는다: 위치는 서버가 연출대로 옮긴 값을 받는 게 정답이다.
            foreach (NetworkAnimator animatorSync in boss.GetComponentsInChildren<NetworkAnimator>())
            {
                if (!animatorSync.enabled) continue;
                animatorSync.enabled = false;
                pausedSync.Add(animatorSync);
            }
        }

        // ★ 즉시 켜면 안 된다(2026-09-17 "연출 뒤 보스가 대기 애니메이션만 재생" 원인).
        //   NetworkAnimator는 OnEnable에서 animator.parameters로 파라미터 목록을 다시 만드는데, 타임라인이 막 멈춘
        //   그 프레임엔 애니메이터가 아직 컨트롤러로 돌아오지 않아 목록이 비어 있다(0개). 그러면 이후 서버가 보내는
        //   파라미터·상태 동기화가 전부 "parameter count 불일치"로 버려져, 보스가 위치만 따라 미끄러지고 애니메이션은
        //   대기 상태로 굳는다. 그래서 애니메이터가 컨트롤러 파라미터를 다시 내놓을 때까지 기다렸다 켠다.
        private void ResumeAnimatorSync()
        {
            if (pausedSync.Count == 0) return;

            List<Behaviour> toResume = new(pausedSync);
            pausedSync.Clear();

            // 비활성화 중(씬 이탈 등)엔 코루틴을 못 돌린다. 그땐 오브젝트도 곧 사라지므로 즉시 켜는 것으로 충분하다.
            if (isActiveAndEnabled) StartCoroutine(EnableWhenAnimatorReady(toResume));
            else EnableAll(toResume);
        }

        private static IEnumerator EnableWhenAnimatorReady(List<Behaviour> syncs)
        {
            const float timeout = 2f;
            float deadline = Time.unscaledTime + timeout;

            // 최소 한 프레임은 넘긴다 — 타임라인 그래프 해제가 같은 프레임 안에서 끝나지 않는다.
            yield return null;

            while (Time.unscaledTime < deadline && !AnimatorsReady(syncs))
                yield return null;

            EnableAll(syncs);
        }

        // 동기화 대상 애니메이터가 전부 컨트롤러 파라미터를 다시 내놓는가.
        private static bool AnimatorsReady(List<Behaviour> syncs)
        {
            foreach (Behaviour sync in syncs)
            {
                if (sync is not NetworkAnimator networkAnimator || networkAnimator.animator == null) continue;

                Animator animator = networkAnimator.animator;
                bool controllerHasParameters = animator.runtimeAnimatorController != null && animator.parameterCount > 0;
                if (animator.isActiveAndEnabled && !controllerHasParameters) return false;
            }

            return true;
        }

        private static void EnableAll(List<Behaviour> syncs)
        {
            foreach (Behaviour sync in syncs)
                if (sync != null) sync.enabled = true;
        }

        // ── 서버: 애니메이터 컬링 ───────────────────────────────────

        private void OverrideCulling(Boss boss)
        {
            Animator animator = boss.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.cullingMode == AnimatorCullingMode.AlwaysAnimate) return;

            cullingOverrides.Add((animator, animator.cullingMode));
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void RestoreCulling()
        {
            foreach ((Animator animator, AnimatorCullingMode saved) in cullingOverrides)
                if (animator != null) animator.cullingMode = saved;

            cullingOverrides.Clear();
        }

        // ── 재바인딩 ────────────────────────────────────────────

        /// <summary>
        /// <see cref="trackBindings"/>에 지정한 <b>트랙 이름</b>에 정확히 대응하는 슬롯만 보스로 채운다.
        /// </summary>
        /// <remarks>
        /// 어느 트랙인지는 이름으로 딱 집는다 — 같은 타입 트랙이 여러 개여도, 트랙 순서가 바뀌어도
        /// 정확히 원하는 슬롯에만 꽂힌다. 규칙에 없는 트랙(카메라·다른 오브젝트 "4" 등)은 손대지 않는다.
        /// 무엇을 꽂을지(Animator·GameObject 등)는 트랙이 요구하는 타입으로 자동 판별하고, 이미 씬 오브젝트가 꽂힌 트랙은 건너뛴다.
        /// </remarks>
        private void RebindTo(Boss boss)
        {
            if (director.playableAsset is not TimelineAsset timeline) return;
            if (trackBindings == null) return;

            // 어느 규칙이 실제로 트랙을 만났는지 표시해 둔다. 이름이 어긋난 규칙(오타·트랙 리네임)은
            // 그냥 지나가면 아무 일도 안 일어나 "연출이 조용히 반만 도는" 증상이 된다 — 아래에서 짚어 준다.
            bool[] matched = new bool[trackBindings.Length];

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!TryGetBinding(track.name, out TrackBinding binding, out int index)) continue;   // 규칙에 없는 트랙은 건너뛴다

                matched[index] = true;

                // 씬에서 이미 무언가 꽂혀 있는 트랙은 건드리지 않는다. 런타임 스폰 보스를 꽂을 트랙은 편집 시점에 비어 있고,
                // 같은 이름이라도 씬 오브젝트가 꽂힌 트랙(전환 연출의 Steam1 등)은 그대로 두어야 한다.
                Object current = director.GetGenericBinding(track);
                if (current != null) continue;

                Boss targetBoss = binding.target == BindTarget.NextPhaseBoss ? nextPhaseBoss : boss;
                director.SetGenericBinding(track, Resolve(in binding, targetBoss, RequiredBindingType(track)));
            }

            for (int i = 0; i < matched.Length; i++)
            {
                if (matched[i]) continue;

                Debug.LogWarning($"[BossIntroDirector] 트랙 이름 '{trackBindings[i].trackName}'이 Timeline '{timeline.name}'에 없습니다. " +
                                 "이 규칙은 무시됩니다 — 트랙을 지웠거나 이름을 바꿨는지 확인하세요.", this);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 에디터 전용(플레이 X): 미리보기용 보스를 트랙에 꽂는다. Timeline 창에서 바로 스크럽해 연출을 확인하기 위함이다.
        /// 런타임과 같은 규칙(<see cref="RebindTo"/>)을 그대로 써서, 미리보기에서 맞게 꽂히면 런타임에서도 맞게 꽂힌다.
        /// </summary>
        /// <remarks>BossIntroDirectorEditor의 [트랙 자동 할당] 버튼이 부른다. 해제는 <see cref="EditorUnbindWhere"/>.</remarks>
        /// <param name="boss">등장 연출의 보스 / 전환 연출의 나가는 페이즈.</param>
        /// <param name="next">전환 연출의 새로 등장하는 페이즈. 등장 연출이면 null.</param>
        public void EditorBindPreview(Boss boss, Boss next)
        {
            if (director == null) director = GetComponent<PlayableDirector>();

            // nextPhaseBoss는 런타임 진행 상태라 미리보기가 끝나면 원래대로 돌려 둔다.
            Boss savedNext = nextPhaseBoss;
            nextPhaseBoss = next;
            RebindTo(boss);
            nextPhaseBoss = savedNext;
        }

        /// <summary>
        /// 에디터 전용: 규칙(<see cref="trackBindings"/>)에 걸린 트랙 중 <paramref name="isPreview"/>가 true인 대상이 꽂힌 트랙을 비운다.
        /// 씬 오브젝트가 꽂힌 트랙(전환 연출의 Steam1 등)은 미리보기 대상이 아니므로 건드리지 않는다.
        /// </summary>
        /// <param name="isPreview">꽂힌 대상이 미리보기 보스(의 일부)인지 판정.</param>
        public void EditorUnbindWhere(System.Func<Object, bool> isPreview)
        {
            if (director == null) director = GetComponent<PlayableDirector>();
            if (director == null || director.playableAsset is not TimelineAsset timeline || trackBindings == null) return;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!TryGetBinding(track.name, out _, out _)) continue;

                Object current = director.GetGenericBinding(track);
                if (current != null && isPreview(current)) director.SetGenericBinding(track, null);
            }
        }
#endif

        /// <summary>트랙 이름에 지정된 규칙이 있으면 그 바인딩을 돌려준다.</summary>
        /// <param name="index">찾은 규칙의 인덱스. 어느 규칙이 쓰였는지 표시해 미사용 규칙을 경고하는 데 쓴다.</param>
        private bool TryGetBinding(string trackName, out TrackBinding binding, out int index)
        {
            for (int i = 0; i < trackBindings.Length; i++)
            {
                if (trackBindings[i].trackName == trackName)
                {
                    binding = trackBindings[i];
                    index = i;
                    return true;
                }
            }

            binding = default;
            index = -1;
            return false;
        }

        /// <summary>
        /// 바인딩 규칙에 대응하는 보스의 실제 컴포넌트/오브젝트를 돌려준다.
        /// <see cref="TrackBinding.childPath"/>가 있으면 보스 루트가 아니라 그 자식(무기·이펙트 프리팹 등)을 기준으로 삼는다.
        /// </summary>
        /// <remarks>
        /// 경로가 있는데 그 자식을 못 찾으면(오타·프리팹 구조 변경) 그 트랙만 빈 채 두고 경고만 남긴다 —
        /// 연출 전체가 조용히 죽지 않게 하기 위함이다. part가 트랙이 요구하는 타입과 어긋나도 마찬가지로 그 슬롯만 빈다.
        /// </remarks>
        private static Object Resolve(in TrackBinding binding, Boss boss, System.Type requiredType)
        {
            if (boss == null)
            {
                Debug.LogWarning($"[BossIntroDirector] '{binding.trackName}' 트랙의 대상 보스({binding.target})가 없어 비운 채 진행합니다. " +
                                 "등장 연출에 NextPhaseBoss를 지정했거나, 전환 연출에 다음 페이즈가 넘어오지 않았는지 확인하세요.");
                return null;
            }

            Object resolved = ResolveCore(in binding, boss, requiredType);

            // 자식을 못 찾거나 그 자식에 트랙이 요구하는 컴포넌트가 없으면 그 트랙만 빈 채로 재생된다 —
            // 화면으론 "그 부분만 안 움직인다"로 보여 원인을 찾기 어렵다.
            if (resolved == null)
            {
                string where = string.IsNullOrEmpty(binding.childPath) ? "보스 루트" : $"자식 '{binding.childPath}'";
                Debug.LogWarning($"[BossIntroDirector] '{binding.trackName}' 트랙에 꽂을 {requiredType?.Name ?? "대상"}을(를) {where}에서 찾지 못해 " +
                                 "비운 채 진행합니다. 자식 이름/경로를 확인하세요.", boss);
            }

            return resolved;
        }

        private static Object ResolveCore(in TrackBinding binding, Boss boss, System.Type requiredType)
        {
            // 자식 지정이 있으면 그 자식을, 없으면 보스 루트를 기준으로 삼는다.
            Transform target = boss.transform;
            if (!string.IsNullOrEmpty(binding.childPath))
            {
                target = FindChild(boss.transform, binding.childPath);
                if (target == null) return null;   // 경고는 호출부(Resolve) 한 곳에서 낸다
            }

            // 트랙이 요구하는 타입으로 꽂는다: GameObject(활성화 트랙) / 컴포넌트(Animator 등). 타입을 모르면 GameObject.
            if (requiredType == null || requiredType == typeof(GameObject)) return target.gameObject;
            if (requiredType == typeof(Transform)) return target;
            if (typeof(Component).IsAssignableFrom(requiredType)) return target.GetComponent(requiredType);
            return target.gameObject;
        }

        // 트랙이 바인딩으로 요구하는 타입(활성화 트랙=GameObject, 애니메이션 트랙=Animator 등).
        private static System.Type RequiredBindingType(TrackAsset track)
        {
            foreach (PlayableBinding output in track.outputs)
                return output.outputTargetType;

            return null;
        }

        /// <summary>
        /// 보스 루트 아래에서 자식을 찾는다. <paramref name="query"/>에 슬래시가 있으면 <b>경로</b>로
        /// 그대로 따라가고(<see cref="Transform.Find(string)"/>), 없으면 <b>이름</b>으로 자손 전체를 뒤진다.
        /// </summary>
        /// <remarks>
        /// 이름 검색은 전체 경로를 손으로 적는 부담을 없애기 위함이다. 대신 그 이름이 보스 안에서 유일해야 한다 —
        /// 같은 이름이 여러 개면 어느 것을 잡을지 보장되지 않으므로 경고를 남기고 처음 만난 것을 쓴다.
        /// 비활성 자식(등장 전 꺼둔 이펙트 등)도 찾도록 <c>includeInactive</c>로 훑는다.
        /// </remarks>
        private static Transform FindChild(Transform root, string query)
        {
            // 슬래시가 있으면 정확한 경로로 간다(같은 이름이 여럿일 때 딱 집고 싶을 때의 탈출구).
            if (query.IndexOf('/') >= 0)
                return root.Find(query);

            Transform match = null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);   // 0번은 root 자신
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == root || all[i].name != query) continue;

                if (match != null)
                {
                    Debug.LogWarning($"[BossIntroDirector] 이름 '{query}'가 보스 안에 여러 개 있습니다. 처음 찾은 것을 씁니다 — 정확히 집으려면 경로(부모/자식)로 지정하세요.", root);
                    break;   // 첫 매치 유지
                }
                match = all[i];
            }

            return match;
        }

        // ── 진단 / UI ───────────────────────────────────────────

        // 진단용: 우리를 거치지 않고 타임라인이 재생되는 경우를 잡아낸다. PlayerZoneTrigger가 아직
        // PlayableDirector.Play()에 직접 물려 있으면 여기 걸린다 — 그 경로는 보스 트랙 재바인딩이
        // 통째로 생략돼 "컷신은 도는데 보스가 안 움직인다"가 된다.
        private bool externalPlayWarned;

        private void Update()
        {
            if (graphStarted || externalPlayWarned || director == null) return;
            if (director.state != PlayState.Playing) return;

            externalPlayWarned = true;
            Debug.LogError("[진단][BossIntro] ★★ BossIntroDirector를 거치지 않고 Timeline이 재생되고 있습니다 — " +
                           "보스 트랙 재바인딩이 통째로 생략돼 보스가 연출대로 움직이지 않습니다. " +
                           "PlayerZoneTrigger의 On Player Entered에 남아 있는 PlayableDirector.Play() 연결을 지우세요.", this);
        }

        /// <summary>등장 연출용 UI 끄기/켜기를 한곳에서 처리한다. 실제로 끈 경우만 <see cref="uiHidden"/>로 기록해 안전장치가 켤 수 있게 한다.</summary>
        /// <param name="hide">true면 UI를 끄고, false면 되살린다.</param>
        private void SetUIHidden(bool hide)
        {
            if (!hideUIDuringIntro) return;
            if (UIManager.Instance == null) return;

            UIManager.Instance.gameObject.SetActive(!hide);
            uiHidden = hide;
        }
    }
}
