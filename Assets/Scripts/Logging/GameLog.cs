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
        /// <summary>정보 로그를 남긴다.</summary>
        /// <param name="source">로그가 난 쪽(클라/서버).</param>
        /// <param name="category">분류(네트워크·전투 등).</param>
        /// <param name="message">내용.</param>
        public static void Info(LogSource source, LogCategory category, string message)
        {
            Write(source, category, LogSeverity.Info, message, null);
        }

        /// <summary>경고 로그를 남긴다. 인자는 <see cref="Info"/>와 같다.</summary>
        public static void Warning(LogSource source, LogCategory category, string message)
        {
            Write(source, category, LogSeverity.Warning, message, null);
        }

        /// <summary>에러 로그를 남긴다. 예외가 있으면 스택 트레이스를 함께 기록한다.</summary>
        public static void Error(LogSource source, LogCategory category, string message, Exception exception = null)
        {
            Write(source, category, LogSeverity.Error, message, exception);
        }

        /// <summary>예외 로그를 남긴다. 원격 전송 기본 기준(Error 이상)에 걸려 시트로 올라간다.</summary>
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
