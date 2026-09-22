using UnityEngine;
using UnityEngine.Playables;
using ProjectS.Players;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 같은 오브젝트의 Timeline(<see cref="PlayableDirector"/>)이 도는 동안 플레이어 입력을 잠근다.
    /// 연출 도중 플레이어가 걸어 나가거나 공격을 지르는 것을 막기 위한 것으로,
    /// 마을 문 열림 연출(<see cref="QuestGateDoor"/>의 cutscene)처럼 짧은 컷신에 붙인다.
    ///
    /// <b>인스펙터로 Player를 직접 연결하지 않는 이유</b>: 플레이어는 씬에 배치되지 않고
    /// PlayerManager가 런타임에 만든다. 그래서 재생 시점에 <see cref="LocalPlayer.Current"/>로 찾는다.
    ///
    /// 잠금/해제를 Timeline의 Signal이 아니라 Director의 played/stopped로 잡는 이유는 두 가지다.
    /// 클립을 편집해도 배선이 깨지지 않고, <b>연출이 실제로 재생될 때만</b> 잠긴다
    /// (도착 트리거에 걸면 퀘스트 미수락으로 문이 안 열리는 경우에도 플레이어만 잠긴다).
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public class CutscenePlayerLock : MonoBehaviour
    {
        private PlayableDirector director;

        // 실제로 잠근 플레이어. LocalPlayer.Current는 멀티에서 도중에 바뀔 수 있어,
        // 잠글 때의 대상을 들고 있다가 그 대상에게 해제를 건다(BossIntroDirector와 같은 방침).
        private Player lockedPlayer;

        private void Awake()
        {
            director = GetComponent<PlayableDirector>();
        }

        private void OnEnable()
        {
            director.played += OnPlayed;
            director.stopped += OnStopped;
        }

        private void OnDisable()
        {
            director.played -= OnPlayed;
            director.stopped -= OnStopped;

            // 연출 도중 오브젝트가 꺼지면 stopped가 오지 않는다. 그대로 두면 입력이 안 돌아온다
            // (Player의 안전 타이머가 결국 풀어 주지만, 그때까지 몇 초를 묶인 채 보낸다).
            Release();
        }

        // 연출 시작. 남은 길이를 함께 넘겨, 종료 신호를 놓쳤을 때의 안전 타이머가 연출 길이에 맞게 돌게 한다.
        private void OnPlayed(PlayableDirector _)
        {
            Player player = LocalPlayer.Current;
            if (player == null) return;   // 씬 진입 직후 등 아직 플레이어가 없는 경우

            lockedPlayer = player;
            lockedPlayer.BeginCutscene((float)director.duration);
        }

        private void OnStopped(PlayableDirector _) => Release();

        // stopped와 OnDisable에서 겹쳐 불려도 안전하도록, 대상을 먼저 비우고 해제를 건다.
        private void Release()
        {
            if (lockedPlayer == null) return;

            Player player = lockedPlayer;
            lockedPlayer = null;
            player.EndCutscene();
        }
    }
}
