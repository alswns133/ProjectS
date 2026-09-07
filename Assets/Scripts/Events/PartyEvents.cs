using System;
using UnityEngine;

namespace ProjectS.Events
{
    /// <summary>
    /// 나에게 온 파티 초대 한 건. 네트워크(PartyManager)가 TargetRpc로 받아 받는 쪽 팝업으로 넘긴다.
    /// Mirror에 의존하지 않는 순수 데이터라 UI가 그대로 읽는다(ChatMessage와 같은 취지).
    /// </summary>
    public struct PartyInviteOffer
    {
        /// <summary>초대한 사람의 netId(수락/거절을 되돌려 보낼 손잡이). UI는 해석하지 않고 그대로 돌려준다.</summary>
        public uint inviterNetId;

        /// <summary>초대한 사람 표시 이름(팝업 문구용).</summary>
        public string inviterName;

        // TODO(선택): 어느 던전·난이도로의 초대인지 함께 실어 받는 쪽에서 보여줄 수 있다
        //             (docs/PARTY_WINDOW_UI.md §2 — 어디로 가는지 모르면 보여줄 내용이 없다).
    }

    /// <summary>
    /// 파티 알림 허브. 네트워크(<c>PartyManager</c>)가 발행하고 UI(받는 쪽 팝업·파티 다리)가 구독한다.
    /// 네트워크 계층과 UI 계층을 떼어 놓는 경계다(<see cref="ChatEvents"/>와 동일한 static 허브 패턴).
    /// </summary>
    /// <remarks>
    /// 발행은 <c>FireXxx</c>로만 한다. 도메인 리로드를 꺼도 죽은 구독자가 남지 않게 플레이 시작 시 리셋한다.
    /// </remarks>
    public static class PartyEvents
    {
        /// <summary>
        /// 나에게 파티 초대가 왔다. 받는 쪽 프롬프터가 구독해 수락/거절 팝업을 띄운다.
        /// </summary>
        public static event Action<PartyInviteOffer> OnInviteReceived;

        /// <summary>
        /// 내 파티 상태가 바뀌었다(성립·해체·초대 대기 시작/종료). 파티 다리(<c>NetworkPartySource</c>)가
        /// 이 신호로 슬롯을 다시 그린다.
        /// </summary>
        public static event Action OnChanged;

        /// <summary>초대 수신을 발행한다(PartyManager의 TargetRpc에서 호출).</summary>
        public static void FireInviteReceived(PartyInviteOffer offer)
            => OnInviteReceived?.Invoke(offer);

        /// <summary>파티 상태 변화를 발행한다(성립·해체·대기 상태 변경 시).</summary>
        public static void FireChanged()
            => OnChanged?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnInviteReceived = null;
            OnChanged = null;
        }
    }
}
