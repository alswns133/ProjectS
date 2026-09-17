using System.Collections;
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
    /// <b>보스는 어떻게 받나</b>: <see cref="Boss"/>가 등장하며 <see cref="BossEvents.OnBossAppeared"/>를
    /// 발행한다(Boss.Start). 이 신호를 구독해 재바인딩 → 재생한다. 스폰 순서와 무관하게 항상 등장 순간에 맞물린다.
    /// </para>
    /// <para>
    /// <b>입력 잠금</b>: 등장 연출 동안 플레이어 조작을 막는다. Timeline 시작에 <see cref="Player.BeginCutscene"/>,
    /// 끝(<see cref="PlayableDirector.stopped"/>)에 <see cref="Player.EndCutscene"/>를 코드로 부른다 —
    /// Signal 에셋을 따로 두지 않고 여기서 묶는다(놓쳐도 Player 쪽 안전 타이머가 입력을 되살린다).
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
        [Tooltip("등장 연출 Timeline을 재생할 디렉터. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("등장 연출 동안 플레이어 입력을 잠글지. 컷신형 등장이면 켠다.")]
        [SerializeField] private bool lockPlayerInput = true;

        [Tooltip("컷신 안전 해제 시간을 타임라인 길이보다 이만큼 더 길게 잡는 여유(초). " +
                 "종료 신호(stopped)를 놓쳤을 때만 쓰이는 백스톱이라, 연출을 항상 조금 넘기도록 둔다.")]
        [SerializeField, Min(0f)] private float cutsceneSafetyMargin = 3f;

        [Header("트랙 재바인딩")]
        [Tooltip("Timeline 트랙을 이름으로 정확히 지정해 보스의 어느 부분을 꽂을지 짝짓는다. " +
                 "같은 타입 트랙이 여러 개여도 이름으로 갈리므로 정확히 원하는 슬롯에 들어간다.")]
        [SerializeField] private TrackBinding[] trackBindings;

        [Tooltip("등장 연출 동안 UI(UIManager 루트)를 끌지. 시그널 대신 여기서 Play에 끄고 stop에 되살린다.")]
        [SerializeField] private bool hideUIDuringIntro = true;

        [Header("싱글(비파티) 플레이")]
        [Tooltip("파티에 속하지 않고 혼자 들어온 경우, 서버 지시를 기다리지 않고 보스 등장 + 내 캐릭터 준비가 " +
                 "끝나는 즉시 스스로 연출을 시작한다. 파티 레이드에서는 서버(PartyManager)가 전원 스폰 완료 후 " +
                 "지시하므로 이 경로를 타지 않는다.")]
        [SerializeField] private bool autoStartWhenSolo = true;

        [Tooltip("싱글일 때 내 캐릭터가 준비되기를 기다리는 최대 시간(초). 넘으면 포기하고 경고만 남긴다.")]
        [SerializeField, Min(1f)] private float soloWaitTimeout = 20f;

        /// <summary>보스에서 트랙에 꽂을 부분. 트랙이 요구하는 타입에 맞춰 고른다.</summary>
        private enum BossPart
        {
            Animator,     // Animator 요구 트랙(Animation Track)
            Transform,    // Transform 요구 트랙(위치 연출)
            GameObject,   // GameObject 요구 트랙(Activation 등)
        }

        /// <summary>"이 이름의 트랙에는 보스의 이 부분을 꽂는다"는 한 줄의 규칙.</summary>
        [System.Serializable]
        private struct TrackBinding
        {
            [Tooltip("Timeline 트랙 헤더에 보이는 트랙 이름. 바인딩이 비어도 유지되는 정확한 주소다.")]
            public string trackName;

            [Tooltip("그 트랙에 꽂을 보스의 부분.")]
            public BossPart part;

            [Tooltip("보스 루트 아래에서 꽂을 자식. 슬래시가 없으면 그 \"이름\"으로 자손 전체를 뒤져 찾고(예: \"AppearAura\"), " +
                     "슬래시가 있으면 그 \"경로\"를 그대로 따라간다(예: \"Weapon_R/Blade/Fx\"). 비우면 보스 루트 자신에 꽂는다. " +
                     "이름으로 쓸 땐 그 이름이 보스 안에서 유일해야 한다(중복이면 경고 후 첫 매치 사용).")]
            public string childPath;
        }

        // 등장 연출은 판당 1회다. 재진입은 씬 리로드로 새 인스턴스가 되어 자연히 초기화된다.
        private bool played;

        // 우리가 UI를 껐는지. 껐을 때만 되살려, 우리가 안 건드린 UI를 강제로 켜지 않게 한다(안전장치 판정 기준).
        private bool uiHidden;

        // 이번 연출로 재운 보스. stopped 콜백은 보스를 인자로 받지 않으므로 여기 보관해 ResumeAI에 쓴다.
        // null이 아니면 "아직 깨우지 않은 재운 보스가 있다"는 뜻이라, 안전장치(OnDisable)의 판정 기준도 된다.
        private Boss suspendedBoss;

        // 등장 신호로 받아 둔 보스. 연출 "시작"과 분리해 보관한다 — 트리거(PlayerZoneTrigger 등)가 올 때
        // 재바인딩 대상이 필요한데, 보스 스폰은 그보다 한참 먼저(인스턴스 씬 생성 직후) 끝나기 때문이다.
        private Boss appearedBoss;

        // 보스보다 트리거가 먼저 온 경우의 예약. 보스가 등장하는 순간 곧바로 시작한다.
        private bool pendingPlay;

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

            Debug.Log($"[진단][BossIntro] Awake — 오브젝트='{name}', 씬='{gameObject.scene.name}', " +
                      $"director={(director != null ? "있음" : "★없음")}, " +
                      $"playableAsset={(director != null && director.playableAsset != null ? director.playableAsset.name : "★없음")}", this);
        }

        private void OnEnable()
        {
            BossEvents.OnBossAppeared += OnBossAppeared;
            Debug.Log($"[진단][BossIntro] OnEnable — 보스 등장 신호 구독 시작(씬='{gameObject.scene.name}')", this);
        }

        private void OnDisable()
        {
            BossEvents.OnBossAppeared -= OnBossAppeared;
            if (director != null) director.stopped -= OnDirectorStopped;

            // 안전장치: 연출 도중 이 컴포넌트가 꺼지거나(씬 이탈 등) stopped를 놓쳐도 뒷정리가 남지 않게 한다.
            // 우리가 실제로 껐을 때(uiHidden)만 UI를 되살리고, 아직 못 깨운 재운 보스가 있으면 깨운다
            // (보스 AI 재우기는 Player 컷신처럼 자체 안전 타이머가 없어, 여기서 반드시 되돌린다).
            if (uiHidden) SetUIHidden(false);
            if (suspendedBoss != null)
            {
                suspendedBoss.ResumeAI();
                suspendedBoss = null;
            }
        }

        // 등장 신호는 "연출을 시작하라"가 아니라 "재바인딩할 보스가 준비됐다"는 뜻으로만 받는다.
        // 레이드 보스는 인스턴스 씬이 생기는 순간 스폰되므로(PartyManager), 여기서 바로 재생하면
        // 플레이어가 보스방 근처에도 가기 전에 컷신이 터진다. 시작 시점은 PlayNow()가 정한다.
        private void OnBossAppeared(Boss boss)
        {
            Debug.Log($"[진단][BossIntro] 보스 등장 신호 수신 — boss={(boss != null ? boss.name : "null")}, " +
                      $"보스씬='{(boss != null ? boss.gameObject.scene.name : "-")}', 내씬='{gameObject.scene.name}', played={played}", this);

            if (played || boss == null) return;

            // 다른 파티의 인스턴스 씬에서 온 보스는 무시한다 — BossEvents는 전역 static이라
            // additive로 레이드 인스턴스가 여러 개 떠 있으면 남의 보스 신호까지 들어온다.
            if (boss.gameObject.scene != gameObject.scene)
            {
                Debug.LogWarning($"[진단][BossIntro] ★씬이 달라 무시 — 보스='{boss.gameObject.scene.name}' vs 나='{gameObject.scene.name}'. " +
                                 "이게 잘못된 거라면 이 씬 검사를 빼야 합니다.", this);
                return;
            }

            appearedBoss = boss;
            Debug.Log($"[진단][BossIntro] 보스 확보 완료 — '{boss.name}'. 이제 PlayNow() 호출을 기다립니다(pendingPlay={pendingPlay})", this);

            // 시작 지시가 먼저 와 있었으면(스폰 완료 지시가 보스보다 빨랐던 경우) 여기서 시작한다.
            if (pendingPlay)
            {
                BeginIntro();
                return;
            }

            // 싱글(비파티)에는 "전원 스폰 완료"를 알려 줄 서버가 없다. 내 캐릭터가 준비되면 스스로 시작한다.
            if (autoStartWhenSolo && IsSoloPlay())
                StartCoroutine(PlayWhenLocalPlayerReady());
        }

        /// <summary>
        /// 파티 없이 혼자 플레이 중인지. 파티 레이드에서는 <see cref="PartyManager"/>가 전원 스폰 완료 후
        /// 연출 시작을 지시하므로, 그 지시를 기다려야 할지 스스로 시작해도 되는지를 여기서 가른다.
        /// </summary>
        /// <remarks>
        /// 네트워크를 아예 안 쓰는 경우(오프라인 던전)와, 접속은 했지만 파티에 속하지 않은 경우를 모두 싱글로 본다.
        /// </remarks>
        private static bool IsSoloPlay()
        {
            if (!NetworkClient.active) return true;
            if (PartyManager.Local == null) return true;

            return !PartyManager.Local.TryGetComponent(out PlayerPresence me) || me.PartyId == 0;
        }

        // 싱글 경로: 보스는 떴지만 내 캐릭터가 아직 인스턴스 씬에 자리잡지 않았을 수 있다
        // (PlayerManager가 워프 후 활성화한다). 준비될 때까지 기다렸다 시작한다.
        private IEnumerator PlayWhenLocalPlayerReady()
        {
            Debug.Log("[진단][BossIntro] 싱글 플레이로 판단 — 내 캐릭터 스폰을 기다렸다 연출을 시작합니다.", this);

            float deadline = Time.time + soloWaitTimeout;

            while (Time.time < deadline)
            {
                if (played) yield break;   // 그 사이 서버 지시 등으로 이미 시작됐다

                Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
                if (player != null && player.gameObject.activeInHierarchy)
                {
                    BeginIntro();
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"[진단][BossIntro] {soloWaitTimeout:0}초 안에 내 캐릭터가 준비되지 않아 연출을 시작하지 못했습니다 " +
                             "— PlayerManager가 이 씬에 캐릭터를 배치했는지 확인하세요.", this);
        }

        /// <summary>
        /// 보스 등장 연출을 시작한다. 연출을 시작시키는 <b>유일한 공개 진입점</b>이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>누가 부르나</b>: 파티 레이드는 서버(<see cref="PartyManager"/>)가 <b>파티원 아바타와 보스를
        /// 전부 스폰한 뒤</b> 각 클라에 지시해 부른다 — 시작 조건이 "보스방 진입"이 아니라 "전원 스폰 완료"라서다
        /// (2026-09-16 확정). 싱글은 서버 지시가 없으므로 이 컴포넌트가 스스로 부른다
        /// (<see cref="autoStartWhenSolo"/>).
        /// </para>
        /// <para>
        /// ★ <b>존 트리거를 <see cref="PlayableDirector.Play"/>에 직접 물리지 않는다.</b> 그러면 타임라인만 돌고
        /// 보스 트랙 재바인딩·AI 재우기·입력 잠금·종료 복구가 전부 생략된다. 보스 트랙들의 씬 바인딩이
        /// 비어 있는 것은 런타임에 <see cref="RebindTo"/>가 채우는 전제이기 때문이라, 직통 Play는
        /// <b>보스가 연출대로 하나도 움직이지 않는</b> 증상으로 나타난다(2026-09-16 실제 사고).
        /// 보스방 진입형 연출이 필요한 다른 던전이라면 트리거를 이 메서드에 물린다.
        /// </para>
        /// <para>
        /// 보스가 아직 안 떴으면 재생을 예약만 하고, 등장하는 순간 시작한다.
        /// </para>
        /// </remarks>
        public void PlayNow()
        {
            Debug.Log($"[진단][BossIntro] ★PlayNow() 호출됨 — played={played}, 보스={(appearedBoss != null ? appearedBoss.name : "아직 없음")}", this);

            if (played) return;

            if (appearedBoss == null)
            {
                pendingPlay = true;
                Debug.LogWarning("[BossIntroDirector] 보스가 아직 등장하지 않아 연출을 예약합니다 — 스폰 직후 시작합니다. " +
                                 "계속 시작되지 않으면 보스 스폰 자체를 확인하세요(PartyManager의 raidBossPrefab).", this);
                return;
            }

            BeginIntro();
        }

        /// <summary>보스가 확정된 뒤의 실제 시작. 재바인딩 → AI 재우기 → 입력·UI 잠금 → 재생 순서를 지킨다.</summary>
        private void BeginIntro()
        {
            if (director == null || director.playableAsset == null)
            {
                Debug.LogError("[BossIntroDirector] 재생할 Timeline이 없습니다 — PlayableDirector 또는 Playable Asset이 비었습니다.", this);
                return;
            }

            played = true;
            pendingPlay = false;

            Boss boss = appearedBoss;

            // 한 번 재생했으면 다시는 트리거되지 않게 즉시 구독을 끊는다(페이즈 2 등장·중복 발행에도 재생 안 됨).
            BossEvents.OnBossAppeared -= OnBossAppeared;

            RebindTo(boss);

            // 보스 AI를 재운다. 이걸 안 하면 Timeline이 Animator 포즈만 덮을 뿐, 보스의 상태 머신·NavMeshAgent가
            // 그대로 돌아 플레이어를 추격하고 연출대로 움직이지 않는다. 연출이 끝나면 stopped에서 깨운다.
            boss.SuspendAI();
            suspendedBoss = boss;

            // 등장 연출 동안 UI를 끈다(시그널 대신). 되살리는 것은 연출 끝(OnDirectorStopped)에서 한다 —
            // 여기서 켜면 같은 프레임에 껐다 켜져 아무것도 안 숨겨진다.
            SetUIHidden(true);

            // 안전 해제 시간을 타임라인 실제 길이에 맞춘다. 고정값(Player.maxCutsceneDuration=12초)은 20~30초
            // 연출보다 짧아 도중에 입력이 풀리고 경고가 떴다 — director.duration + 여유로 넘겨 연출을 다 덮는다.
            if (lockPlayerInput)
                PlayerManager.Instance?.Player?.BeginCutscene((float)director.duration + cutsceneSafetyMargin);

            // 중복 구독 방지 후 종료 콜백 연결(끝나면 입력·UI 되살림).
            director.stopped -= OnDirectorStopped;
            director.stopped += OnDirectorStopped;

            director.Play();

            Debug.Log($"[진단][BossIntro] ★재생 시작 — timeline='{director.playableAsset.name}', 길이={director.duration:0.00}초, " +
                      $"보스='{boss.name}', state={director.state}", this);
        }

        /// <summary>
        /// <see cref="trackBindings"/>에 지정한 <b>트랙 이름</b>에 정확히 대응하는 슬롯만 보스로 채운다.
        /// </summary>
        /// <remarks>
        /// 타입으로 짐작하지 않고 이름으로 딱 집는다 — 같은 타입 트랙이 여러 개여도, 트랙 순서가 바뀌어도
        /// 정확히 원하는 슬롯에만 꽂힌다. 규칙에 없는 트랙(카메라·다른 오브젝트 "4" 등)은 손대지 않는다.
        /// 지정한 부분(<see cref="BossPart"/>)이 그 트랙이 요구하는 타입과 어긋나면 연출이 조용히 죽으므로,
        /// 트랙이 요구하는 타입에 맞는 part를 고른다(Animator 트랙엔 Animator 등).
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
                director.SetGenericBinding(track, Resolve(in binding, boss));
            }

            for (int i = 0; i < matched.Length; i++)
            {
                if (matched[i]) continue;

                Debug.LogWarning($"[BossIntroDirector] 트랙 이름 '{trackBindings[i].trackName}'이 Timeline '{timeline.name}'에 없습니다. " +
                                 "이 규칙은 무시됩니다 — 트랙을 지웠거나 이름을 바꿨는지 확인하세요.", this);
            }
        }

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
        private static UnityEngine.Object Resolve(in TrackBinding binding, Boss boss)
        {
            UnityEngine.Object resolved = ResolveCore(in binding, boss);

            // part가 트랙이 요구하는 타입과 어긋나거나(Animator 없는 오브젝트에 Animator 지정 등) 자식을 못 찾으면
            // 그 트랙만 빈 채로 재생된다 — 화면으론 "그 부분만 안 움직인다"로 보여 원인을 찾기 어렵다.
            if (resolved == null)
            {
                string where = string.IsNullOrEmpty(binding.childPath) ? "보스 루트" : $"자식 '{binding.childPath}'";
                Debug.LogWarning($"[BossIntroDirector] '{binding.trackName}' 트랙에 꽂을 {binding.part}를 {where}에서 찾지 못해 비운 채 진행합니다. " +
                                 "자식 이름/경로와, 트랙이 요구하는 타입에 part가 맞는지 확인하세요.", boss);
            }

            return resolved;
        }

        private static UnityEngine.Object ResolveCore(in TrackBinding binding, Boss boss)
        {
            // 자식 지정이 있으면 그 자식을, 없으면 보스 루트를 기준으로 삼는다.
            Transform target = boss.transform;
            if (!string.IsNullOrEmpty(binding.childPath))
            {
                target = FindChild(boss.transform, binding.childPath);
                if (target == null) return null;   // 경고는 호출부(Resolve) 한 곳에서 낸다
            }

            return binding.part switch
            {
                BossPart.Animator => target.GetComponent<Animator>(),
                BossPart.Transform => target,
                BossPart.GameObject => target.gameObject,
                _ => null,
            };
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

        // 진단용: 우리를 거치지 않고 타임라인이 재생되는 경우를 잡아낸다. PlayerZoneTrigger가 아직
        // PlayableDirector.Play()에 직접 물려 있으면 여기 걸린다 — 그 경로는 보스 트랙 재바인딩이
        // 통째로 생략돼 "컷신은 도는데 보스가 안 움직인다"가 된다.
        private bool externalPlayWarned;

        private void Update()
        {
            if (played || externalPlayWarned || director == null) return;
            if (director.state != PlayState.Playing) return;

            externalPlayWarned = true;
            Debug.LogError("[진단][BossIntro] ★★ BossIntroDirector를 거치지 않고 Timeline이 재생되고 있습니다 — " +
                           "보스 트랙 재바인딩이 통째로 생략돼 보스가 연출대로 움직이지 않습니다. " +
                           "PlayerZoneTrigger의 On Player Entered에 남아 있는 PlayableDirector.Play() 연결을 지우세요.", this);
        }

        private void OnDirectorStopped(PlayableDirector stopped)
        {
            Debug.Log($"[진단][BossIntro] 재생 종료 신호 — stopped={(stopped != null ? stopped.name : "null")}, 내director={(director != null ? director.name : "null")}", this);

            if (stopped != director) return;

            // 재웠던 보스를 깨운다(에이전트 NavMesh 복귀 + 교전 흐름 진입). 한 번만 하도록 참조를 비운다.
            if (suspendedBoss != null)
            {
                suspendedBoss.ResumeAI();
                suspendedBoss = null;
            }

            if (lockPlayerInput)
                PlayerManager.Instance?.Player?.EndCutscene();

            // 시작에서 껐던 UI를 되살린다. 디렉터 오브젝트는 안 꺼지므로 이 콜백이 확실히 돈다
            // (UI 오브젝트 위에 붙은 시그널 Receiver가 함께 꺼져 못 켜지던 함정을 피하는 이유).
            SetUIHidden(false);
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
