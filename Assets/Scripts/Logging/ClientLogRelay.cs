using System;
using Mirror;
using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>클라가 서버로 보내는 로그 한 줄. <see cref="ServerForwardLogSink"/>가 보내고 <see cref="ClientLogRelay"/>가 받는다.</summary>
    public struct ClientLogMessage : NetworkMessage
    {
        /// <summary>클라에서 로그가 난 시각(UTC epoch ms). 서버 콘솔에서 서버 로그와 순서를 맞춰 보는 데 쓴다.</summary>
        public long TimestampUtcMs;

        /// <summary><see cref="LogSeverity"/> 값.</summary>
        public byte Severity;

        /// <summary>로그 본문.</summary>
        public string Message;

        /// <summary>에러·예외일 때만 싣는 짧은 스택(앞 몇 줄).</summary>
        public string StackTrace;
    }

    /// <summary>
    /// 서버 쪽 수신부: 원격 클라가 보낸 로그를 서버 콘솔에 그대로 남긴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜(2026-09-17 팀 규칙).</b> 멀티 버그는 "서버에선 맞는데 클라에선 틀린" 형태가 많은데, 클라 콘솔은 다른 창·다른 PC에
    /// 있어 같은 순간을 나란히 보기 어렵다. 클라 로그를 서버 콘솔로 모아 <b>한 곳에서 시간순으로</b> 비교하게 한다.
    /// </para>
    /// <para>
    /// 서버가 남긴 줄은 서버의 LogManager가 다시 수집해 로컬 파일·오버레이에도 들어간다. 원격 클라 줄은
    /// <c>[원격클라 conn=N]</c> 접두어로 구분된다.
    /// </para>
    /// </remarks>
    public static class ClientLogRelay
    {
        /// <summary>
        /// 서버에 수신 핸들러를 등록한다. 서버가 시작될 때마다 불러야 한다(<c>GameNetworkManager.OnStartServer</c>)
        /// — 서버가 내려가면 Mirror가 핸들러 목록을 비운다.
        /// </summary>
        public static void RegisterServerHandler()
        {
            NetworkServer.ReplaceHandler<ClientLogMessage>(OnClientLog);
        }

        private static void OnClientLog(NetworkConnectionToClient conn, ClientLogMessage msg)
        {
            string clientTime = DateTimeOffset.FromUnixTimeMilliseconds(msg.TimestampUtcMs).ToLocalTime().ToString("HH:mm:ss.fff");
            string who = conn.identity != null ? $"conn={conn.connectionId} netId={conn.identity.netId}" : $"conn={conn.connectionId}";
            string line = $"[원격클라 {who} {clientTime}] {msg.Message}";

            if (!string.IsNullOrEmpty(msg.StackTrace)) line += "\n" + msg.StackTrace;

            switch ((LogSeverity)msg.Severity)
            {
                case LogSeverity.Warning:
                    Debug.LogWarning(line);
                    break;
                case LogSeverity.Error:
                case LogSeverity.Exception:
                    Debug.LogError(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }
    }
}
