using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// 원격 클라의 로그를 서버로 전달하는 sink. 클라에서 찍힌 모든 로그(Log·Warning·Error·Exception)가 서버 콘솔에도 나온다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>팀 규칙(2026-09-17): 클라의 디버그는 서버에서 전부 볼 수 있어야 한다.</b> 로그마다 전송 코드를 붙이지 않도록
    /// LogManager의 sink로 두어, <c>Debug.Log</c>를 쓰기만 하면 자동으로 서버에 모인다. 수신·출력은 <see cref="ClientLogRelay"/>.
    /// </para>
    /// <para>
    /// <b>순수 클라에서만 보낸다.</b> 호스트·전용 서버는 이미 서버 콘솔 자체라 보낼 필요가 없고, 보내면 자기 로그가 두 번 찍힌다.
    /// </para>
    /// <para>
    /// <b>폭주 방지.</b> 접속 전·로딩 중 쌓인 로그는 상한까지만 보관했다가 접속되면 보낸다. 초당 전송 수에 상한을 두고,
    /// 넘치는 줄은 버린 개수만 한 줄로 알린다 — 매 프레임 찍히는 로그가 네트워크를 막지 않게 하기 위함이다.
    /// 릴리스 빌드에서는 동작하지 않는다(<see cref="Debug.isDebugBuild"/>). 설정은 <see cref="LogSettings"/>의 Server forward.
    /// </para>
    /// </remarks>
    public sealed class ServerForwardLogSink : ILogSink, ILogTickableSink
    {
        // 에러 스택은 앞 몇 줄이면 원인 위치를 찾기에 충분하다. 통째로 보내면 한 줄이 수 KB가 된다.
        private const int StackTraceLines = 6;
        private const int MaxMessageLength = 1000;

        private readonly LogSettings settings;
        private readonly Queue<ClientLogMessage> pending = new();

        private float windowTimer;
        private int sentInWindow;
        private int droppedCount;

        /// <summary>설정을 받아 sink를 만든다.</summary>
        /// <param name="logSettings">전달 여부·최소 심각도·초당 상한을 담은 설정.</param>
        public ServerForwardLogSink(LogSettings logSettings)
        {
            settings = logSettings;
        }

        private bool Enabled => settings.ForwardClientLogsToServer && Debug.isDebugBuild;

        // 순수 클라인가(서버 콘솔이 따로 있는 쪽). 아직 접속 전이어도 큐에 모아 두기 위해 "서버가 아님"만 본다.
        private static bool IsPureClientProcess => !NetworkServer.active;

        /// <inheritdoc/>
        public void Write(in LogEntry entry)
        {
            if (!Enabled || !IsPureClientProcess) return;
            if (entry.Severity < settings.ForwardMinimumSeverity) return;

            if (pending.Count >= settings.ForwardMaximumPendingEntries)
            {
                droppedCount++;
                return;
            }

            string message = entry.Message ?? string.Empty;
            if (message.Length > MaxMessageLength) message = message.Substring(0, MaxMessageLength) + "…";

            pending.Enqueue(new ClientLogMessage
            {
                TimestampUtcMs = entry.TimestampUtcMs,
                Severity = (byte)entry.Severity,
                Message = message,
                StackTrace = entry.Severity >= LogSeverity.Error ? ShortStack(entry.StackTrace) : string.Empty,
            });
        }

        /// <inheritdoc/>
        public void Tick(float unscaledDeltaTime)
        {
            if (!Enabled) return;

            // 서버가 된 프로세스(호스트 시작 등)라면 쌓인 것을 버린다 — 이미 이 콘솔에 찍혀 있다.
            if (!IsPureClientProcess)
            {
                pending.Clear();
                droppedCount = 0;
                return;
            }

            if (!NetworkClient.isConnected || !NetworkClient.ready) return;

            windowTimer += unscaledDeltaTime;
            if (windowTimer >= 1f)
            {
                windowTimer = 0f;
                sentInWindow = 0;
            }

            int budget = settings.ForwardMaxPerSecond - sentInWindow;
            while (budget-- > 0 && pending.Count > 0)
            {
                NetworkClient.Send(pending.Dequeue());
                sentInWindow++;
            }

            // 넘쳐서 버린 줄이 있으면 개수만 알린다(조용히 사라지면 "클라 로그가 안 온다"로 오해한다).
            if (droppedCount > 0 && sentInWindow < settings.ForwardMaxPerSecond)
            {
                NetworkClient.Send(new ClientLogMessage
                {
                    TimestampUtcMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Severity = (byte)LogSeverity.Warning,
                    Message = $"[ServerForwardLogSink] 전송 상한 초과로 클라 로그 {droppedCount}줄을 버렸습니다.",
                    StackTrace = string.Empty,
                });
                sentInWindow++;
                droppedCount = 0;
            }
        }

        /// <inheritdoc/>
        public void Flush()
        {
            // 종료 직전에는 연결이 끊기는 중일 수 있어 무리해서 보내지 않는다. 남은 줄은 클라 로컬 파일에 이미 있다.
        }

        private static string ShortStack(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return string.Empty;

            string[] lines = stackTrace.Split('\n');
            if (lines.Length <= StackTraceLines) return stackTrace.TrimEnd();

            return string.Join("\n", lines, 0, StackTraceLines).TrimEnd() + "\n  …";
        }
    }
}
