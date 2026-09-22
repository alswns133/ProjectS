using ProjectS.Cameras;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Networking;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 레이드 실패(파티 전멸)와 재시도의 <b>클라이언트 측 단일 진입점</b>. 화면(사망 팝업·실패 팝업)은
    /// 여기만 부르고, 싱글이냐 파티냐는 이 안에서 갈린다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 한곳에 모았나.</b> <see cref="PartyInstanceExit"/>과 같은 이유다 — 화면마다 싱글/멀티 분기를
    /// 따로 쓰면 한쪽만 고쳐지고 다른 쪽이 남는다. 실제로 마을 복귀가 그렇게 갈려 서버에 아바타가
    /// 남는 사고가 났었다(2026-09-17).
    /// </para>
    /// <para>
    /// <b>싱글도 같은 이벤트를 탄다.</b> 파티가 아니면 서버 세션 없이 이 클래스가 1/1로
    /// <see cref="RaidFailEvents"/>를 발행하므로, 실패 화면은 한 벌로 동작한다.
    /// </para>
    /// <para>
    /// <b>파티 판정 기준은 접속이 아니라 파티 소속</b>(<c>PlayerPresence.Local.PartyId</c>)이다.
    /// 에디터는 항상 Host로 떠서 접속 여부로 가르면 솔로 플레이까지 멀티로 오인한다
    /// (<c>RaidGather.SetupSpawning</c>이 같은 기준을 쓴다).
    /// </para>
    /// </remarks>
    public static class RaidFailFlow
    {
        /// <summary>지금 파티 레이드(서버가 진행을 쥐는 인스턴스)인지. 아니면 싱글로 처리한다.</summary>
        public static bool IsPartyRaid => PlayerPresence.Local != null && PlayerPresence.Local.PartyId != 0;

        /// <summary>
        /// 이번 판이 레이드인지. 사망·실패 처리가 일반 던전과 갈리는 기준이며, 사망 팝업도 이 값을 본다.
        /// </summary>
        /// <remarks>
        /// <b>왜 <see cref="DungeonContext.IsRaid"/>만으로 부족한가.</b> 그 값은 <c>RaidGather.Enter</c>가 채우는데,
        /// 그 진입 처리는 <b>클라가 자기 레이드 씬을 로드할 때</b> 돈다. 파티 입장에서 호스트는 서버 씬을
        /// 그대로 공유하므로 씬 로드 지시(SceneMessage)를 건너뛰고, 따라서 호스트에서는 <c>RaidGather.Enter</c>가
        /// 아예 돌지 않아 이 컨텍스트가 마을(0)로 남는다. 에디터는 항상 Host라 그 경로가 곧 기본 테스트 경로다.
        ///
        /// <para>그래서 파티가 향한 던전(<c>PlayerPresence.PartyDungeonId</c>)을 함께 본다. 서버가 채워
        /// SyncVar로 복제하는 값이라 호스트·원격 클라 어디서나 같다.</para>
        /// </remarks>
        public static bool IsRaidRun
        {
            get
            {
                if (DungeonContext.IsRaid) return true;

                PlayerPresence local = PlayerPresence.Local;
                return local != null
                       && local.PartyId != 0
                       && local.PartyDungeonId / 10 == DungeonContext.RaidDungeonNumber;
            }
        }

        /// <summary>
        /// "더 이상 일어설 수 없음"(사망 + 부활 기회 0) 상태를 서버에 보고한다. 부활하면 false로 되돌린다.
        /// 파티원 전원이 true가 되는 순간이 곧 레이드 실패다.
        /// </summary>
        /// <param name="down">다운됐으면 true, 부활해서 복귀했으면 false.</param>
        public static void ReportDown(bool down)
        {
            // 다운 상태의 단일 진입점이라 관전 카메라도 여기서 함께 켜고 끈다. 호출부가 둘을 따로 부르게
            // 하면 언젠가 한쪽만 불려 "죽었는데 내 시체만 계속 보고 있는" 상태가 된다.
            // 싱글은 볼 파티원이 없어 시작하지 않는다(해제는 상태와 무관하게 항상 안전하게 부른다).
            if (down && IsPartyRaid) SpectatorCamera.Begin();
            else if (!down) SpectatorCamera.End();

            if (IsPartyRaid)
            {
                if (PartyManager.Local != null) PartyManager.Local.ReportRaidDown(down);
                else Debug.LogWarning("[RaidFailFlow] PartyManager.Local이 없어 다운 상태를 서버에 알리지 못했습니다.");
                return;
            }

            // 싱글: 기다릴 파티원이 없으므로 다운이 곧 실패다. 투표도 혼자라 제한 시간을 두지 않는다.
            if (down) RaidFailEvents.FireFailed(1, 0f);
        }

        /// <summary>재시도 투표. 파티는 서버가 집계하고, 싱글은 곧바로 결정된다.</summary>
        /// <param name="agree">재시도에 동의하면 true, 마을로 돌아가려면 false.</param>
        public static void VoteRetry(bool agree)
        {
            if (IsPartyRaid)
            {
                if (PartyManager.Local != null) PartyManager.Local.VoteRaidRetry(agree);
                else Debug.LogWarning("[RaidFailFlow] PartyManager.Local이 없어 투표를 보내지 못했습니다.");
                return;
            }

            if (!agree)
            {
                RaidFailEvents.FireRetryCancelled("마을로 돌아갑니다.");
                return;
            }

            RaidFailEvents.FireRetryStarting();
            RestartSingle();
        }

        /// <summary>
        /// 레이드를 포기하고 마을로 돌아간다. 실패 화면의 "마을 복귀"와 재시도 무산이 모두 여기로 모인다.
        /// </summary>
        public static void ReturnToVillage()
        {
            // 관전 중이었다면 빌려 쓴 파티원 카메라를 돌려 놓는다(안 끄면 그 카메라가 켜진 채 남는다).
            SpectatorCamera.End();

            // 판이 끝났으므로 남은 기회를 정리한다(안 지우면 마을에서 죽었을 때 이전 판의 기회로 부활한다).
            ReviveBudget.Clear();

            // 살려 놓고 보내야 한다 — VillageGather의 RefillOnSceneEnter는 죽어 있으면 회복을 건너뛰어
            // 마을에 HP 0인 채로 도착한다. 멀티면 죽은 건 아바타지만 마을에서 조작할 건 지속 캐릭터라 둘 다 살린다.
            ReviveBoth();

            PartyInstanceExit.ReturnToVillage();
        }

        /// <summary>
        /// 싱글 레이드 재시도: 씬을 통째로 다시 연다. 보스·잡몹·기믹이 전부 초기 상태가 되고,
        /// 부활 기회도 <c>RaidGather.SetupPlayer</c>가 입장 처리에서 다시 채운다.
        /// </summary>
        /// <remarks>
        /// 파티 레이드에서는 이 경로를 쓰지 않는다 — 서버가 인스턴스를 다시 열고 파티원 전원에게
        /// 씬 로드를 지시한다(<c>PartyManager.ServerRestartRaidInstance</c>).
        /// </remarks>
        private static void RestartSingle()
        {
            SpectatorCamera.End();

            // 씬 진입 처리(회복·워프)가 죽은 캐릭터를 건너뛰지 않게 먼저 살린다.
            ReviveBoth();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<Raid>();
            else
                Debug.LogWarning("[RaidFailFlow] GameSceneManager가 없어 레이드를 다시 시작하지 못했습니다.");
        }

        // 지금 조작 중인 캐릭터와 지속 캐릭터를 모두 살린다. 멀티에서는 둘이 다른 오브젝트다
        // (조작 중인 건 네트워크 아바타, 지속 캐릭터는 OwnerGate가 숨겨 둔 마을 캐릭터).
        private static void ReviveBoth()
        {
            Player current = LocalPlayer.Current;
            current?.Revive();

            Player persistent = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (persistent != null && persistent != current) persistent.Revive();
        }
    }
}
