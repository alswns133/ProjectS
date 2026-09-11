using System;

namespace ProjectS.Logging
{
    public enum LogSource
    {
        Client,
        Server,
        Unity,
    }

    public enum LogCategory
    {
        Uncategorized,
        Network,
        Combat,
        UI,
        Data,
        Sound,
        Scene,
    }

    public enum LogSeverity
    {
        Info,
        Warning,
        Error,
        Exception,
    }

    /// <summary>
    /// 수집·분류·저장 계층이 공유하는 로그 계약이다.
    /// TimestampUtcMs는 어떤 기기의 로컬 시간에도 의존하지 않는 UTC epoch milliseconds다.
    /// </summary>
    [Serializable]
    public struct LogEntry
    {
        public long TimestampUtcMs;
        public LogSource Source;
        public LogCategory Category;
        public LogSeverity Severity;
        public string Message;
        public string StackTrace;

        public LogEntry(long timestampUtcMs, LogSource source, LogCategory category,
            LogSeverity severity, string message, string stackTrace)
        {
            TimestampUtcMs = timestampUtcMs;
            Source = source;
            Category = category;
            Severity = severity;
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }
    }
}
