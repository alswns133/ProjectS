using System;
using UnityEngine;
using ProjectS.Core;

namespace ProjectS.Events
{
    /// <summary>
    /// 채팅 수신 알림 허브. 네트워크(ChatManager)가 메시지를 받아 이 허브로 발행하고,
    /// UI(ChatWindow)가 구독해 화면에 찍는다. 네트워크 계층과 UI 계층을 떼어 놓는 경계다.
    /// (PlayerEvents와 동일한 static 허브 패턴 — 발행은 FireXxx로만 한다.)
    /// </summary>
    public static class ChatEvents
    {
        /// <summary>
        /// 채팅 한 줄 수신. ChatManager가 ClientRpc/TargetRpc로 받은 메시지를 로컬로 흘릴 때 발행한다.
        /// </summary>
        public static event Action<ChatMessage> OnMessageReceived;

        /// <summary>
        /// 로컬에서 "보내기"를 눌렀을 때 발행(입력창 → 네트워크 전송 요청).
        /// ChatManager(로컬 플레이어 소유)가 구독해 Command로 서버에 올린다.
        /// UI가 ChatManager 인스턴스를 직접 몰라도 되게 하려는 분리다.
        /// </summary>
        public static event Action<ChatChannel, string> OnSendRequested;

        /// <summary>수신 메시지를 UI로 발행한다.</summary>
        public static void FireMessageReceived(ChatMessage message)
            => OnMessageReceived?.Invoke(message);

        /// <summary>로컬 전송 요청을 발행한다(입력창에서 호출).</summary>
        public static void FireSendRequested(ChatChannel channel, string text)
            => OnSendRequested?.Invoke(channel, text);

        /// <summary>
        /// 도메인 리로드를 꺼도 플레이 시작 시 죽은 구독자가 남지 않게 초기화한다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnMessageReceived = null;
            OnSendRequested = null;
        }
    }
}
