using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
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

        [Header("트랙 재바인딩")]
        [Tooltip("Timeline 트랙을 이름으로 정확히 지정해 보스의 어느 부분을 꽂을지 짝짓는다. " +
                 "같은 타입 트랙이 여러 개여도 이름으로 갈리므로 정확히 원하는 슬롯에 들어간다.")]
        [SerializeField] private TrackBinding[] trackBindings;

        [Tooltip("등장 연출 동안 UI(UIManager 루트)를 끌지. 시그널 대신 여기서 Play에 끄고 stop에 되살린다.")]
        [SerializeField] private bool hideUIDuringIntro = true;

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
        }

        // 등장 연출은 판당 1회다. 재진입은 씬 리로드로 새 인스턴스가 되어 자연히 초기화된다.
        private bool played;

        // 우리가 UI를 껐는지. 껐을 때만 되살려, 우리가 안 건드린 UI를 강제로 켜지 않게 한다(안전장치 판정 기준).
        private bool uiHidden;

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
            BossEvents.OnBossAppeared += OnBossAppeared;
        }

        private void OnDisable()
        {
            BossEvents.OnBossAppeared -= OnBossAppeared;
            if (director != null) director.stopped -= OnDirectorStopped;

            // 안전장치: 연출 도중 이 컴포넌트가 꺼지거나(씬 이탈 등) stopped를 놓쳐도 UI가 꺼진 채 남지 않게 한다.
            // 우리가 실제로 껐을 때(uiHidden)만 되살린다.
            if (uiHidden) SetUIHidden(false);
        }

        private void OnBossAppeared(Boss boss)
        {
            if (played || boss == null || director == null || director.playableAsset == null) return;
            played = true;

            // 한 번 재생했으면 다시는 트리거되지 않게 즉시 구독을 끊는다(다른 보스 등장·중복 발행에도 재생 안 됨).
            BossEvents.OnBossAppeared -= OnBossAppeared;

            RebindTo(boss);

            // 등장 연출 동안 UI를 끈다(시그널 대신). 되살리는 것은 연출 끝(OnDirectorStopped)에서 한다 —
            // 여기서 켜면 같은 프레임에 껐다 켜져 아무것도 안 숨겨진다.
            SetUIHidden(true);

            if (lockPlayerInput)
                PlayerManager.Instance?.Player?.BeginCutscene();

            // 중복 구독 방지 후 종료 콜백 연결(끝나면 입력·UI 되살림).
            director.stopped -= OnDirectorStopped;
            director.stopped += OnDirectorStopped;

            director.Play();
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

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (!TryGetPart(track.name, out BossPart part)) continue;   // 규칙에 없는 트랙은 건너뛴다
                director.SetGenericBinding(track, Resolve(part, boss));
            }
        }

        /// <summary>트랙 이름에 지정된 규칙이 있으면 그 part를 돌려준다.</summary>
        private bool TryGetPart(string trackName, out BossPart part)
        {
            for (int i = 0; i < trackBindings.Length; i++)
            {
                if (trackBindings[i].trackName == trackName)
                {
                    part = trackBindings[i].part;
                    return true;
                }
            }

            part = default;
            return false;
        }

        /// <summary>part에 대응하는 보스의 실제 컴포넌트/오브젝트를 돌려준다.</summary>
        private static UnityEngine.Object Resolve(BossPart part, Boss boss) => part switch
        {
            BossPart.Animator => boss.GetComponent<Animator>(),
            BossPart.Transform => boss.transform,
            BossPart.GameObject => boss.gameObject,
            _ => null,
        };

        private void OnDirectorStopped(PlayableDirector stopped)
        {
            if (stopped != director) return;

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
