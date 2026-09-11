using System;
using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// 게임 코드가 의도적으로 남기는 유일한 태그 로그 API다.
    /// 기존 Debug.Log 계열은 자동 수집 시 Unity/Uncategorized로만 들어간다.
    /// </summary>
    public static class GameLog
    {
        public static void Info(LogSource source, LogCategory category, string message)
        {
            Write(source, category, LogSeverity.Info, message, null);
        }

        public static void Warning(LogSource source, LogCategory category, string message)
        {
            Write(source, category, LogSeverity.Warning, message, null);
        }

        public static void Error(LogSource source, LogCategory category, string message, Exception exception = null)
        {
            Write(source, category, LogSeverity.Error, message, exception);
        }

        public static void Exception(LogSource source, LogCategory category, string message, Exception exception)
        {
            Write(source, category, LogSeverity.Exception, message, exception);
        }

        private static void Write(LogSource source, LogCategory category, LogSeverity severity,
            string message, Exception exception)
        {
            string safeMessage = message ?? string.Empty;
            string stackTrace = exception?.ToString() ?? string.Empty;
            var entry = new LogEntry(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                source,
                category,
                severity,
                safeMessage,
                stackTrace);

            LogManager.PublishTagged(in entry);
            WriteToUnityConsole(in entry);
        }

        private static void WriteToUnityConsole(in LogEntry entry)
        {
            string consoleMessage = $"{LogManager.TaggedConsolePrefix}[{entry.Source}/{entry.Category}/{entry.Severity}] {entry.Message}";
            if (!string.IsNullOrEmpty(entry.StackTrace)) consoleMessage += $"\n{entry.StackTrace}";

            switch (entry.Severity)
            {
                case LogSeverity.Warning:
                    Debug.LogWarning(consoleMessage);
                    break;
                case LogSeverity.Error:
                case LogSeverity.Exception:
                    Debug.LogError(consoleMessage);
                    break;
                default:
                    Debug.Log(consoleMessage);
                    break;
            }
        }
    }
}
