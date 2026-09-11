using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// 원격 전송 결과와 독립적으로 로그를 JSON Lines 파일에 남긴다.
    /// 파일 I/O 실패를 Unity 로그로 다시 남기지 않아 수집 재귀를 만들지 않는다.
    /// </summary>
    internal sealed class LocalFileLogSink : ILogSink, ILogTickableSink, IDisposable
    {
        private readonly StringBuilder lineBuilder = new(1024);
        private readonly float flushIntervalSeconds;
        private StreamWriter writer;
        private float elapsedSinceFlush;

        public LocalFileLogSink(LogSettings settings)
        {
            flushIntervalSeconds = settings.LocalFlushIntervalSeconds;

            if (!settings.WriteLocalFile) return;

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "BugLogs");
                Directory.CreateDirectory(directory);

                string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss'Z'");
                string path = Path.Combine(directory, $"bug-log_{timestamp}.jsonl");
                writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
                // persistentDataPath가 읽기 전용인 플랫폼도 있으므로 로컬 sink만 비활성화한다.
                writer = null;
            }
        }

        public void Write(in LogEntry entry)
        {
            if (writer == null) return;

            try
            {
                lineBuilder.Clear();
                lineBuilder.Append(JsonConvert.SerializeObject(entry));
                writer.WriteLine(lineBuilder.ToString());
            }
            catch
            {
                Dispose();
            }
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (writer == null) return;

            elapsedSinceFlush += unscaledDeltaTime;
            if (elapsedSinceFlush < flushIntervalSeconds) return;

            Flush();
        }

        public void Flush()
        {
            elapsedSinceFlush = 0f;

            try
            {
                writer?.Flush();
            }
            catch
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            try
            {
                writer?.Dispose();
            }
            catch
            {
                // 종료 중 I/O 예외는 외부로 전파하지 않는다.
            }
            finally
            {
                writer = null;
            }
        }
    }
}
