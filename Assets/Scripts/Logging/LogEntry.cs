using System;

namespace ProjectS.Logging
{
    /// <summary>로그가 발생한 쪽.</summary>
    public enum LogSource
    {
        Client,
        Server,
        Unity,
    }

    /// <summary>로그 분류(어느 시스템에서 난 로그인지). 태그 없이 Debug.Log로 들어온 것은 Uncategorized.</summary>
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

    /// <summary>로그 심각도. 값 순서가 곧 비교 순서라(Info &lt; Warning &lt; Error &lt; Exception) 최소 심각도 필터에 쓰인다.</summary>
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
        /// <summary>UTC epoch milliseconds 기록 시각.</summary>
        public long TimestampUtcMs;

        /// <summary>로그가 발생한 쪽.</summary>
        public LogSource Source;

        /// <summary>로그 분류.</summary>
        public LogCategory Category;

        /// <summary>로그 심각도.</summary>
        public LogSeverity Severity;

        /// <summary>로그 본문.</summary>
        public string Message;

        /// <summary>스택 트레이스. 없으면 null 또는 빈 문자열.</summary>
        public string StackTrace;

        /// <summary>로그 한 줄을 만든다.</summary>
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
