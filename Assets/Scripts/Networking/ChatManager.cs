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
        /// 서버가 허용하는 채팅 한 줄 최대 길이. 이보다 길면 서버가 잘라낸다.
        /// 무제한으로 두면 아주 긴 문자열이 Mirror의 최대 패킷 크기를 넘겨 보낸 클라가 통째로
        /// 연결 해제된다(전송 실패=disconnect). 밸런스가 아니라 네트워크 안전장치라 상수로 둔다.
        /// </summary>
        private const int MaxChatLength = 200;

        /// <summary>
        /// 서버가 보관하는 이 커넥션의 발신자 이름. 접속 직후 <see cref="CmdRegisterName"/>로 1회 등록되고,
        /// 이후 모든 채팅은 클라가 매번 보내는 이름이 아니라 이 값으로 고정 발행된다(매 메시지 닉네임 위조 차단).
        /// SyncVar라 서버가 정한 값이 전 클라로 복제된다.
        ///
        /// 한계(trust-on-first-use): 등록 값 자체는 아직 클라가 보낸 값이라, 접속 시점의 위조까지는 막지 못한다.
        /// 완전한 권위는 커넥션↔계정 매핑(로그인이 네트워크와 연결되는 단계)이 붙을 때 서버가 계정에서
        /// 직접 이름을 조회하도록 바꾸면 완성된다.
        /// </summary>
        [SyncVar] private string ownerName = "Player";

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

            // 접속 직후 내 이름을 서버에 1회 등록한다. 이후 채팅은 서버 보관 이름(ownerName)으로 나가므로
            // 클라가 매 메시지에 이름을 실어 보낼 필요가 없다(=매 메시지 위조 경로 제거).
            CharacterSaveData save = GameSession.SelectedCharacter;
            CmdRegisterName(save != null ? save.name : "Player");

            ChatEvents.OnSendRequested += HandleLocalSend;
        }

        public override void OnStopLocalPlayer()
        {
            ChatEvents.OnSendRequested -= HandleLocalSend;
        }

        /// <summary>
        /// 접속 시 발신자 이름을 서버에 1회 등록한다(trust-on-first-use). 서버가 보관하므로 이후 위조 불가.
        /// 빈 이름이면 커넥션 id 기반의 안전한 대체 이름을 쓴다(클라가 못 위조하는 서버측 값).
        /// </summary>
        [Command]
        private void CmdRegisterName(string name)
        {
            name = name?.Trim();
            ownerName = string.IsNullOrWhiteSpace(name)
                ? $"Player {connectionToClient.connectionId}"
                : name;
        }

        /// <summary>UI가 발행한 전송 요청을 받아 서버로 올린다(로컬 플레이어 전용). 이름은 서버가 채우므로 보내지 않는다.</summary>
        private void HandleLocalSend(ChatChannel channel, string text)
        {
            CmdSend(channel, text);
        }

        // ── 클라 → 서버 ─────────────────────────────────────────────

        /// <summary>
        /// 클라가 보낸 메시지를 서버가 받는다. 서버 권위 지점 —
        /// 발신자 이름은 클라 입력을 믿지 않고 서버가 보관한 <see cref="ownerName"/>으로 채운다.
        /// </summary>
        [Command]
        private void CmdSend(ChatChannel channel, string text)
        {
            Debug.Log($"[Chat] CmdSend(서버 수신) ({channel}): {text}");
            // TODO: 서버측 검증 — (선택) 스팸 레이트리밋·욕설 필터. 길이 제한은 아래에서 처리.
            if (string.IsNullOrWhiteSpace(text)) return;

            // 최대 길이 초과분은 서버가 잘라낸다(패킷 크기 초과로 인한 클라 강제 끊김 방지).
            // 클라 입력을 믿지 않고 서버가 최종 결정하는 지점이라, 잘라낸 값으로 브로드캐스트한다.
            if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);

            ChatMessage message = new ChatMessage
            {
                sender = ownerName,   // 클라가 보낸 이름이 아니라 서버 보관 이름을 쓴다(위조 차단).
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
