using System;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace ProjectS.Logging
{
    /// <summary>
    /// Apps Script 웹앱으로 오류 로그를 배치 전송한다.
    /// 전송 실패분은 앞쪽으로 되돌리며, 항상 별도의 LocalFileLogSink에도 이미 기록돼 있다.
    /// </summary>
    internal sealed class GoogleSheetsLogSink : ILogSink, ILogTickableSink, IDisposable
    {
        private readonly LogSettings settings;
        private readonly Func<string> buildIdProvider;
        private readonly Func<string> testerIdProvider;
        private readonly List<LogEntry> pendingEntries = new();
        private readonly List<LogEntry> sendingEntries = new();
        private readonly CancellationTokenSource cancellation = new();

        private bool sendInFlight;
        private float elapsedSinceSend;

        public GoogleSheetsLogSink(LogSettings settings, Func<string> buildIdProvider, Func<string> testerIdProvider)
        {
            this.settings = settings;
            this.buildIdProvider = buildIdProvider;
            this.testerIdProvider = testerIdProvider;
        }

        public void Write(in LogEntry entry)
        {
            if (!settings.HasRemoteConfiguration || !settings.ShouldSendRemotely(entry.Severity)) return;

            pendingEntries.Add(entry);
            TrimPendingEntries();
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (!settings.HasRemoteConfiguration || sendInFlight || pendingEntries.Count == 0) return;

            elapsedSinceSend += unscaledDeltaTime;
            if (pendingEntries.Count < settings.RemoteBatchSize &&
                elapsedSinceSend < settings.RemoteBatchIntervalSeconds) return;

            StartSend();
        }

        public void Flush()
        {
            elapsedSinceSend = settings.RemoteBatchIntervalSeconds;
            if (!sendInFlight && pendingEntries.Count > 0) StartSend();
        }

        private void StartSend()
        {
            if (sendInFlight || pendingEntries.Count == 0 || cancellation.IsCancellationRequested) return;

            int count = Math.Min(settings.RemoteBatchSize, pendingEntries.Count);
            sendingEntries.Clear();
            sendingEntries.AddRange(pendingEntries.GetRange(0, count));
            pendingEntries.RemoveRange(0, count);

            elapsedSinceSend = 0f;
            sendInFlight = true;
            SendAsync(cancellation.Token).Forget();
        }

        private async UniTaskVoid SendAsync(CancellationToken cancellationToken)
        {
            bool succeeded = false;

            try
            {
                string payload = BuildPayloadJson();
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);

                for (int attempt = 0; attempt <= settings.RemoteRetryCount; attempt++)
                {
                    succeeded = await TryPostAsync(payloadBytes, cancellationToken);
                    if (succeeded || cancellationToken.IsCancellationRequested) break;

                    float delay = settings.RemoteRetryDelaySeconds * (attempt + 1);
                    await UniTask.Delay(TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 앱 종료·오브젝트 파괴 시에는 로컬 파일이 최종 폴백이다.
            }
            catch
            {
                // 네트워크 오류 자체를 Debug.Log로 남기면 수집 루프가 생길 수 있어 조용히 폴백한다.
            }
            finally
            {
                if (!succeeded && !cancellationToken.IsCancellationRequested)
                {
                    pendingEntries.InsertRange(0, sendingEntries);
                    TrimPendingEntries();
                }

                sendingEntries.Clear();
                sendInFlight = false;
            }
        }

        private async UniTask<bool> TryPostAsync(byte[] payloadBytes, CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(settings.AppsScriptUrl, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(payloadBytes),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            await request.SendWebRequest().WithCancellation(cancellationToken);

            if (request.result != UnityWebRequest.Result.Success) return false;

            try
            {
                AppsScriptResponse response = JsonConvert.DeserializeObject<AppsScriptResponse>(request.downloadHandler.text);
                return response != null && response.Ok;
            }
            catch
            {
                return false;
            }
        }

        private string BuildPayloadJson()
        {
            string buildId = buildIdProvider?.Invoke() ?? string.Empty;
            string testerId = testerIdProvider?.Invoke() ?? string.Empty;
            var logs = new List<RemoteLogDto>(sendingEntries.Count);

            for (int i = 0; i < sendingEntries.Count; i++)
                logs.Add(new RemoteLogDto(sendingEntries[i], buildId, testerId));

            return JsonConvert.SerializeObject(new RemoteBatchPayload(settings.SharedSecret, logs));
        }

        private void TrimPendingEntries()
        {
            int excess = pendingEntries.Count - settings.RemoteMaximumPendingEntries;
            if (excess > 0) pendingEntries.RemoveRange(0, excess);
        }

        public void Dispose()
        {
            if (!cancellation.IsCancellationRequested) cancellation.Cancel();
            cancellation.Dispose();
            pendingEntries.Clear();
            sendingEntries.Clear();
        }

        private sealed class RemoteBatchPayload
        {
            [JsonProperty("secret")] public readonly string Secret;
            [JsonProperty("logs")] public readonly List<RemoteLogDto> Logs;

            public RemoteBatchPayload(string secret, List<RemoteLogDto> logs)
            {
                Secret = secret;
                Logs = logs;
            }
        }

        private sealed class RemoteLogDto
        {
            [JsonProperty("ts")] public readonly long TimestampUtcMs;
            [JsonProperty("source")] public readonly string Source;
            [JsonProperty("category")] public readonly string Category;
            [JsonProperty("severity")] public readonly string Severity;
            [JsonProperty("message")] public readonly string Message;
            [JsonProperty("stack")] public readonly string StackTrace;
            [JsonProperty("buildId")] public readonly string BuildId;
            [JsonProperty("user")] public readonly string User;

            public RemoteLogDto(LogEntry entry, string buildId, string user)
            {
                TimestampUtcMs = entry.TimestampUtcMs;
                Source = entry.Source.ToString();
                Category = entry.Category.ToString();
                Severity = entry.Severity.ToString();
                Message = entry.Message;
                StackTrace = entry.StackTrace;
                BuildId = buildId;
                User = user;
            }
        }

        private sealed class AppsScriptResponse
        {
            [JsonProperty("ok")] public bool Ok;
        }
    }
}
