using Mirror;
using ProjectS.Core;
using ProjectS.Data;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 채팅 네트워크 릴레이. 플레이어 오브젝트(NetworkIdentity)에 붙는다.
    /// 흐름: 로컬 입력 → Command(서버) → 서버 필터링 → ClientRpc(전원) / TargetRpc(파티) → ChatEvents로 로컬 발행.
    /// (DUNGEON_AND_MULTIPLAYER.md §3 "채팅=2채널", §4 "순간 이벤트=ClientRpc 릴레이" 방향을 따른다.)
    ///
    /// 폴더=네임스페이스 규칙상 ProjectS.Networking. UnityEngine의 레거시 Network 타입과
    /// 세그먼트가 겹치지 않게 "Network"가 아니라 "Networking"으로 둔다(단순명 가림 회피).
    ///
    /// NetworkBehaviour는 같은 GameObject의 NetworkIdentity를 전제로 한다(없으면 Command/Rpc가 성립 못 함).
    /// 이 오브젝트는 접속 시 서버가 각 커넥션 소유로 스폰하는 네트워크 플레이어 오브젝트라,
    /// 소유권(authority)이 있어 로컬 클라의 Command가 서버에서 허용된다.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class ChatManager : NetworkBehaviour
    {
        /// <summary>
        /// 씬을 넘어 이 채팅 오브젝트를 유지한다(서버 스폰본). 씬 전환은 Mirror가 아니라
        /// <see cref="ProjectS.Managers.GameSceneManager"/>가 싱글 모드로 처리하는데, 싱글 로드는 이전 씬의
        /// 오브젝트를 전부 파괴한다. DDOL이 없으면 던전↔마을을 오갈 때 이 오브젝트가 파괴돼
        /// <see cref="OnStopLocalPlayer"/>로 구독이 끊기고, 호스트가 살아 있어 <see cref="GameNetworkManager.ConnectFromVillage"/>가
        /// 재스폰을 건너뛰므로 채팅이 영영 죽는다. 채팅은 상시 채널이고 위치 의미가 없어 씬과 무관하게 살아야 한다.
        /// </summary>
        public override void OnStartServer()
        {
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 씬을 넘어 이 채팅 오브젝트를 유지한다(클라 복제본). 전용 서버+원격 클라 구성에서도
        /// 클라 쪽 복제본이 씬 전환에 파괴되지 않게 한다. Host 모드에서는 서버/클라가 같은 오브젝트라
        /// <see cref="OnStartServer"/>와 함께 불려도 DDOL은 멱등이라 안전하다.
        /// </summary>
        public override void OnStartClient()
        {
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 로컬 플레이어일 때만 입력 허브에 붙는다. 원격 복제본까지 구독하면
        /// 한 번 보낸 메시지가 여러 Command로 중복 전송된다.
        /// </summary>
        public override void OnStartLocalPlayer()
        {
            Debug.Log("[Chat] OnStartLocalPlayer — 로컬 채팅 준비됨(서버 접속·스폰 성공).");
            ChatEvents.OnSendRequested += HandleLocalSend;
        }

        public override void OnStopLocalPlayer()
        {
            ChatEvents.OnSendRequested -= HandleLocalSend;
        }

        /// <summary>UI가 발행한 전송 요청을 받아 서버로 올린다(로컬 플레이어 전용).</summary>
        private void HandleLocalSend(ChatChannel channel, string text)
        {
            CharacterSaveData save = GameSession.SelectedCharacter;
            string senderName = save != null ? save.name : "Player";
            CmdSend(channel, senderName, text);
        }

        // ── 클라 → 서버 ─────────────────────────────────────────────

        /// <summary>
        /// 클라가 보낸 메시지를 서버가 받는다. 서버 권위 지점 —
        /// 발신자 이름은 클라 입력을 믿지 말고 서버가 아는 커넥션 정보로 채운다.
        /// </summary>
        [Command]
        private void CmdSend(ChatChannel channel, string senderName, string text)
        {
            Debug.Log($"[Chat] CmdSend(서버 수신) ({channel}): {text}");
            // TODO: 서버측 검증 — 길이 제한, 공백/빈 문자열 컷, (선택) 스팸 레이트리밋·욕설 필터.
            if (string.IsNullOrWhiteSpace(text)) return;

            ChatMessage message = new ChatMessage
            {
                sender = string.IsNullOrWhiteSpace(senderName) ? "Player" : senderName,
                channel = channel,
                text = text,
            };

            switch (channel)
            {
                case ChatChannel.General:
                    RpcReceive(message);        // 접속 전원에게
                    break;

                case ChatChannel.Party:
                    // TODO: PartyManager 도입 후 파티원 커넥션마다 TargetReceive(conn, message) 호출.
                    //       지금은 파티 채널 미지원 → 일반으로 폴백하거나 무시.
                    RpcReceive(message);
                    break;
            }
        }

        // ── 서버 → 클라 ─────────────────────────────────────────────

        /// <summary>서버가 전원에게 뿌리는 일반 채팅.</summary>
        [ClientRpc]
        private void RpcReceive(ChatMessage message)
        {
            Debug.Log($"[Chat] RpcReceive(클라 수신) → FireMessageReceived: {message.sender}: {message.text}");
            ChatEvents.FireMessageReceived(message);
        }

        /// <summary>
        /// (예정) 특정 커넥션에게만 보내는 파티 채팅. PartyManager가 대상 커넥션을 넘겨준다.
        /// </summary>
        [TargetRpc]
        private void TargetReceive(NetworkConnectionToClient target, ChatMessage message)
        {
            ChatEvents.FireMessageReceived(message);
        }
    }
}
