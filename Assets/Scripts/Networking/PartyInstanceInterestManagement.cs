using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 파티 인스턴스(레이드·파티 던전) 가시성 규칙. <b>인스턴스 안의 오브젝트(보스·아바타)는 그 인스턴스에 들어가 있는 파티원에게만</b>
    /// 보이게 하고, 그 외는 모두에게 보인다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가(2026-09-17).</b> 관심 영역 관리가 없으면 Mirror는 스폰된 모든 오브젝트를 준비된 모든 접속에 보낸다.
    /// 그래서 마을에 있는 사람에게도 레이드 보스·파티원 아바타가 스폰되고, 레이드에서 먼저 나간 사람의 클라에도 보스 동기화가
    /// 계속 날아가 "대상을 찾지 못함" 경고가 매 프레임 쏟아졌다.
    /// </para>
    /// <para>
    /// <b>Mirror 기본 SceneInterestManagement를 쓰지 않는 이유.</b> 그건 접속자 본체(<see cref="PlayerPresence"/>·채팅·파티)까지
    /// 씬으로 가려, 레이드에 들어간 파티원이 마을 사람의 채팅·초대 목록에서 사라진다. 여기서는 접속자 본체는 어디 있든 모두에게 보이고,
    /// <b>인스턴스 안의 나머지 오브젝트만</b> 가린다.
    /// </para>
    /// <para>
    /// <b>인스턴스 판정.</b> 서버의 활성 씬(마을)이 아닌 추가 로드 씬이 인스턴스다. 파티원 판정은 그 접속자 본체가 어느 씬에 있는가로
    /// 한다 — 입장 시 서버가 본체를 인스턴스로 옮기고(<c>PartyManager</c>), 이탈 시 마을로 되돌린다. 본체가 씬을 옮기면 다음 프레임에
    /// 전체 가시성을 다시 계산한다.
    /// </para>
    /// <para>
    /// 씬 배치가 필요 없다 — <c>GameNetworkManager.Awake</c>가 붙인다.
    /// </para>
    /// </remarks>
    public class PartyInstanceInterestManagement : InterestManagement
    {
        // 오브젝트별 마지막으로 본 씬. 바뀌면(입장·이탈) 가시성을 다시 계산한다.
        private readonly Dictionary<NetworkIdentity, Scene> lastScenes = new();

        /// <summary>이 씬이 파티 인스턴스(서버 활성 씬·DDoL이 아닌 추가 로드 씬)인지.</summary>
        /// <param name="scene">판정할 씬.</param>
        public static bool IsInstanceScene(Scene scene)
            => scene.IsValid()
               && scene != SceneManager.GetActiveScene()
               && scene.buildIndex >= 0;   // DontDestroyOnLoad 씬은 buildIndex -1

        /// <inheritdoc/>
        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
            => IsVisible(identity, newObserver);

        /// <inheritdoc/>
        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
        {
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn != null && conn.isReady && IsVisible(identity, conn))
                    newObservers.Add(conn);
            }
        }

        /// <inheritdoc/>
        [ServerCallback]
        public override void OnDestroyed(NetworkIdentity identity) => lastScenes.Remove(identity);

        [ServerCallback]
        private void LateUpdate()
        {
            bool changed = false;

            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null) continue;

                Scene scene = identity.gameObject.scene;
                if (lastScenes.TryGetValue(identity, out Scene last) && last == scene) continue;

                lastScenes[identity] = scene;
                changed = true;
            }

            // 입장·이탈은 드물어 전체 재계산으로 충분하다(오브젝트·접속 수가 적다).
            if (changed) RebuildAll();
        }

        private static bool IsVisible(NetworkIdentity identity, NetworkConnectionToClient conn)
        {
            // 접속자 본체(프레즌스·채팅·파티)는 어디 있든 모두에게 보인다.
            if (identity.connectionToClient != null && identity.connectionToClient.identity == identity) return true;

            Scene objectScene = identity.gameObject.scene;
            if (!IsInstanceScene(objectScene)) return true;

            // 인스턴스 안의 오브젝트는 본체가 같은 인스턴스에 있는 접속에게만.
            return conn.identity != null && conn.identity.gameObject.scene == objectScene;
        }
    }
}
