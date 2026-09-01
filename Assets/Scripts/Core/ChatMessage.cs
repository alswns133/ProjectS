namespace ProjectS.Core
{
    /// <summary>
    /// 채팅 채널. 일반은 접속 전원 broadcast, 파티는 파티원만 수신한다.
    /// (파티 채널은 PartyManager 도입 후 TargetRpc로 배달 — 지금은 enum만 선점해 둔다.)
    /// </summary>
    public enum ChatChannel
    {
        General = 0,
        Party = 1,
    }

    /// <summary>
    /// 네트워크로 오가는 채팅 한 줄의 데이터 계약. 순수 struct라 Mirror Weaver가
    /// Command/Rpc에서 자동으로 직렬화한다(별도 어트리뷰트 불필요).
    /// UnityEngine·Mirror에 의존하지 않게 Core에 둔다(공용 계약).
    /// </summary>
    public struct ChatMessage
    {
        /// <summary>보낸 사람 표시 이름(캐릭터명).</summary>
        public string sender;

        /// <summary>채널(일반/파티).</summary>
        public ChatChannel channel;

        /// <summary>메시지 본문.</summary>
        public string text;
    }
}
