using Mirror;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Networking;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 던전·레이드에서 마을로 돌아가는 단일 진입점. 싱글이면 씬 전환만, 파티 인스턴스(멀티)면 서버에 이탈을 알리고 돌아간다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 한곳에 모았나(2026-09-17).</b> 사망 팝업·던전 퇴장 선택창이 각자 <c>RequestSceneChange&lt;VillageGather&gt;</c>만
    /// 불러, 멀티에서는 클라 화면만 마을로 바뀌고 서버에는 아바타·파티원 자리가 레이드에 그대로 남았다.
    /// </para>
    /// <para>
    /// <b>호스트는 씬을 다시 불러오지 않는다.</b> 씬 전환 매니저는 마을을 단독(Single) 모드로 불러오는데, 서버를 겸하는 호스트에서
    /// 그러면 마을 위에 올려 둔 파티 인스턴스(다른 파티 것 포함)까지 함께 내려간다. 호스트의 마을 씬은 레이드 동안에도 계속
    /// 떠 있었으므로, 지속 캐릭터를 마을 스폰 지점으로 되돌리고 마을 모드로 바꾸는 것으로 충분하다.
    /// </para>
    /// </remarks>
    public static class PartyInstanceExit
    {
        /// <summary>지금 파티 인스턴스(서버가 스폰한 내 아바타로 플레이 중)에 들어가 있는지.</summary>
        public static bool IsInNetworkInstance => OwnerGate.LocalAvatar != null;

        /// <summary>마을로 돌아간다. 부활 기회 정리 등 호출측의 판 정리는 이 호출 전에 끝낸다.</summary>
        public static void ReturnToVillage()
        {
            if (IsInNetworkInstance)
            {
                bool isHost = NetworkServer.active;

                if (PartyManager.Local != null) PartyManager.Local.RequestLeaveInstance();
                else Debug.LogWarning("[PartyInstanceExit] PartyManager.Local이 없어 서버에 인스턴스 이탈을 알리지 못했습니다.");

                if (isHost)
                {
                    HostReturnToVillage();
                    return;
                }
            }

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
            else
                Debug.LogWarning("[PartyInstanceExit] GameSceneManager가 없어 마을로 돌아가지 못했습니다.");
        }

        // 호스트 전용: 씬 로드 없이 VillageGather.Enter의 플레이어 복귀 부분만 한다(UI 패널·접속은 이미 마을 상태 그대로).
        private static void HostReturnToVillage()
        {
            DungeonContext.ClearDungeon();

            Player player = PlayerManager.Instance != null ? PlayerManager.Instance.Player : null;
            if (player == null) return;

            PlayerManager.Instance.WarpToSpawn();   // 활성 씬(마을)의 스폰 지점으로 옮기고 다시 켠다
            player.EnterVillage();
            player.Stats.RefillOnSceneEnter();
            PlayerEvents.FireStatsRefreshRequested();
        }
    }
}
