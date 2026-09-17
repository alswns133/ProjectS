using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// <see cref="PartyManager"/>의 파티 인스턴스 이탈부. 입장(<c>ServerLoadInstanceAndMove</c>)의 반대 방향이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가(2026-09-17).</b> 예전의 "마을로 돌아가기"는 클라가 마을 씬만 로드했다. 서버에는 그 사람의 아바타가 레이드에
    /// 남아 보스가 계속 노렸고(어그로 후보), 본체(프레즌스)도 인스턴스 씬에 남아 마지막 사람이 나가도 인스턴스가 내려가지 않았다.
    /// </para>
    /// <para>
    /// 이탈 순서: ① 그 접속이 소유한 인스턴스 안 오브젝트(아바타) 파괴 → ② 본체를 서버 활성 씬(마을)으로 복귀
    /// (<see cref="PartyInstanceInterestManagement"/>가 인스턴스 오브젝트를 그 사람에게서 가린다) → ③ 인스턴스에 아무도 없으면
    /// 남은 네트워크 오브젝트(보스 등)를 파괴하고 씬을 내린다. 파티 소속은 유지한다.
    /// </para>
    /// </remarks>
    public partial class PartyManager
    {
        /// <summary>파티 인스턴스에서 나가겠다고 서버에 알린다. <c>PartyInstanceExit.ReturnToVillage</c>가 부른다.</summary>
        public void RequestLeaveInstance() => CmdLeaveInstance();

        [Command]
        private void CmdLeaveInstance() => ServerLeaveInstance(connectionToClient);

        /// <summary>
        /// 서버: 이 접속을 지금 들어가 있는 파티 인스턴스에서 빼낸다. 인스턴스에 있지 않으면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="conn">나가는 접속.</param>
        public static void ServerLeaveInstance(NetworkConnectionToClient conn)
        {
            if (!NetworkServer.active || conn == null || conn.identity == null) return;

            Scene instance = conn.identity.gameObject.scene;
            if (!PartyInstanceInterestManagement.IsInstanceScene(instance)) return;

            // ① 이 접속이 소유한, 인스턴스 안의 오브젝트(레이드 아바타) 파괴. 본체는 남긴다.
            foreach (NetworkIdentity owned in new List<NetworkIdentity>(conn.owned))
            {
                if (owned == null || owned == conn.identity) continue;
                if (owned.gameObject.scene == instance) NetworkServer.Destroy(owned.gameObject);
            }

            // ② 본체를 마을(서버 활성 씬)로 되돌린다.
            SceneManager.MoveGameObjectToScene(conn.identity.gameObject, SceneManager.GetActiveScene());

            Debug.Log($"[진단][Instance] conn={conn.connectionId} 인스턴스 '{instance.name}' 이탈", conn.identity);

            // ③ 비었으면 인스턴스 정리.
            ServerUnloadInstanceIfEmpty(instance);
        }

        /// <summary>
        /// 서버: 인스턴스에 남은 파티원이 없으면 남은 네트워크 오브젝트를 파괴하고 씬을 내린다.
        /// 이탈 직후와 접속 종료(<c>GameNetworkManager.OnServerDisconnect</c>) 직후에 부른다.
        /// </summary>
        /// <param name="instance">검사할 인스턴스 씬.</param>
        public static void ServerUnloadInstanceIfEmpty(Scene instance)
        {
            if (!NetworkServer.active || !instance.IsValid() || !instance.isLoaded) return;
            if (!PartyInstanceInterestManagement.IsInstanceScene(instance)) return;

            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn != null && conn.identity != null && conn.identity.gameObject.scene == instance) return;   // 아직 누가 있다
            }

            // 씬을 내리기 전에 네트워크 오브젝트를 서버 경로로 파괴한다 — 씬 언로드로 조용히 사라지면 클라에 파괴가 안 간다.
            foreach (NetworkIdentity identity in new List<NetworkIdentity>(NetworkServer.spawned.Values))
            {
                if (identity != null && identity.gameObject.scene == instance) NetworkServer.Destroy(identity.gameObject);
            }

            Debug.Log($"[진단][Instance] 인스턴스 '{instance.name}'가 비어 내립니다.");
            SceneManager.UnloadSceneAsync(instance);
        }
    }
}
