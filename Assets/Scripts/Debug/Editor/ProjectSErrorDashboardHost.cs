#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ProjectS.Debugging.Editor
{
    [InitializeOnLoad]
    internal static class ProjectSErrorDashboardHost
    {
        internal const int Port = 43891;

        private const int MaxRecords = 500;
        private const int MaxSelection = 100;
        private const string DashboardFileName = "ProjectSErrorDashboard.html";
        private const string SelectionFileName = "codex-selected-errors.json";

        private static readonly object SelectionLock = new object();
        private static readonly string BugLogsDirectory;
        private static readonly string DashboardPath;

        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool isRunning;

        static ProjectSErrorDashboardHost()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            BugLogsDirectory = Path.Combine(Application.persistentDataPath, "BugLogs");
            DashboardPath = Path.Combine(projectRoot, "Assets", "Scripts", "Debug", "Editor", DashboardFileName);

            Start();
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static void Start()
        {
            if (isRunning) return;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                isRunning = true;
                acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "ProjectS Error Dashboard"
                };
                acceptThread.Start();
            }
            catch (SocketException exception)
            {
                Debug.LogWarning($"[ProjectSErrorDashboard] {Port} 포트를 열 수 없습니다: {exception.Message}");
                Stop();
            }
        }

        private static void Stop()
        {
            if (!isRunning && listener == null) return;

            isRunning = false;
            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
                // 에디터 종료와 어셈블리 리로드 중 닫힌 리스너는 무시한다.
            }
            finally
            {
                listener = null;
                acceptThread = null;
            }
        }

        private static void AcceptLoop()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (SocketException)
                {
                    if (!isRunning) return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, true))
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                string requestLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                string[] requestParts = requestLine.Split(' ');
                if (requestParts.Length < 2)
                {
                    WriteResponse(stream, 400, "text/plain; charset=utf-8", "Invalid request.");
                    return;
                }

                int contentLength = 0;
                string headerLine;
                while (!string.IsNullOrEmpty(headerLine = reader.ReadLine()))
                {
                    int separator = headerLine.IndexOf(':');
                    if (separator <= 0) continue;

                    string name = headerLine.Substring(0, separator).Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(headerLine.Substring(separator + 1).Trim(), out contentLength);
                }

                RouteRequest(stream, requestParts[0], requestParts[1].Split('?')[0], ReadBody(reader, contentLength));
            }
        }

        private static string ReadBody(TextReader reader, int contentLength)
        {
            if (contentLength <= 0 || contentLength > 32768) return string.Empty;

            var buffer = new char[contentLength];
            int read = 0;
            while (read < buffer.Length)
            {
                int count = reader.Read(buffer, read, buffer.Length - read);
                if (count <= 0) break;
                read += count;
            }
            return new string(buffer, 0, read);
        }

        private static void RouteRequest(Stream stream, string method, string path, string body)
        {
            if (method == "GET" && (path == "/" || path == "/index.html"))
            {
                ServeDashboard(stream);
                return;
            }
            if (method == "GET" && path == "/api/errors")
            {
                WriteJson(stream, BuildDashboardPayload());
                return;
            }
            if (method == "PUT" && path == "/api/selection")
            {
                HandleSelection(stream, body);
                return;
            }

            WriteResponse(stream, 404, "text/plain; charset=utf-8", "Not found.");
        }

        private static void ServeDashboard(Stream stream)
        {
            try
            {
                WriteResponse(stream, 200, "text/html; charset=utf-8", File.ReadAllText(DashboardPath, Encoding.UTF8));
            }
            catch (IOException)
            {
                WriteResponse(stream, 500, "text/plain; charset=utf-8", "Dashboard asset is unavailable.");
            }
        }

        private static void HandleSelection(Stream stream, string body)
        {
            try
            {
                JObject request = JObject.Parse(body);
                JArray rawIds = request["ids"] as JArray;
                if (rawIds == null || rawIds.Any(token => token.Type != JTokenType.String))
                    throw new JsonException("ids must be a string array.");

                List<string> savedIds = WriteSelection(rawIds.Values<string>());
                WriteJson(stream, new JObject { ["ids"] = new JArray(savedIds) });
            }
            catch (JsonException)
            {
                WriteJson(stream, new JObject { ["error"] = "Invalid selection request." }, 400);
            }
            catch (IOException)
            {
                WriteJson(stream, new JObject { ["error"] = "Selection could not be saved." }, 500);
            }
        }

        private static JObject BuildDashboardPayload()
        {
            List<DashboardRecord> records = ReadRecords(out FileInfo activeLog);
            HashSet<string> availableIds = new HashSet<string>(records.Select(record => record.Id));
            List<string> selectedIds = ReadSelection().Where(availableIds.Contains).ToList();

            return new JObject
            {
                ["inboxAvailable"] = activeLog != null,
                ["recordCount"] = records.Count,
                ["selectedCount"] = selectedIds.Count,
                ["session"] = new JObject
                {
                    ["fileName"] = activeLog?.Name ?? string.Empty,
                    ["lastWriteUtc"] = activeLog == null ? string.Empty : activeLog.LastWriteTimeUtc.ToString("O")
                },
                ["errors"] = new JArray(records.Select(record => record.ToJson())),
                ["selectedIds"] = new JArray(selectedIds),
                ["selectionLimit"] = MaxSelection
            };
        }

        private static List<DashboardRecord> ReadRecords(out FileInfo activeLog)
        {
            activeLog = FindLatestLog();
            var records = new List<DashboardRecord>();
            if (activeLog == null) return records;

            try
            {
                int lineNumber = 0;
                foreach (string line in File.ReadLines(activeLog.FullName, Encoding.UTF8))
                {
                    lineNumber++;
                    JObject source;
                    try
                    {
                        source = JObject.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    string severity = EnumName(source["Severity"], new[] { "Info", "Warning", "Error", "Exception" }, "Error");
                    if (severity != "Error" && severity != "Exception") continue;

                    string timestampRaw = source["TimestampUtcMs"]?.ToString(Formatting.None) ?? string.Empty;
                    records.Add(new DashboardRecord(
                        $"{activeLog.Name}:{lineNumber}:{timestampRaw}",
                        ToUtcIso(timestampRaw),
                        severity,
                        EnumName(source["Source"], new[] { "Client", "Server", "Unity" }, "Unity"),
                        EnumName(source["Category"], new[] { "Uncategorized", "Network", "Combat", "UI", "Data", "Sound", "Scene" }, "Uncategorized"),
                        source["Message"]?.ToString() ?? string.Empty,
                        source["StackTrace"]?.ToString() ?? string.Empty));

                    if (records.Count > MaxRecords) records.RemoveAt(0);
                }
            }
            catch (IOException)
            {
                return new List<DashboardRecord>();
            }

            return records;
        }

        private static FileInfo FindLatestLog()
        {
            try
            {
                var directory = new DirectoryInfo(BugLogsDirectory);
                return directory.Exists
                    ? directory.GetFiles("bug-log_*.jsonl").OrderBy(file => file.LastWriteTimeUtc).LastOrDefault()
                    : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static List<string> ReadSelection()
        {
            string path = Path.Combine(BugLogsDirectory, SelectionFileName);
            try
            {
                if (!File.Exists(path)) return new List<string>();

                JObject document = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                JArray ids = document["ids"] as JArray;
                return ids == null ? new List<string>() : ids.Values<string>().Where(id => !string.IsNullOrEmpty(id)).ToList();
            }
            catch (IOException)
            {
                return new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private static List<string> WriteSelection(IEnumerable<string> requestedIds)
        {
            lock (SelectionLock)
            {
                HashSet<string> availableIds = new HashSet<string>(ReadRecords(out _).Select(record => record.Id));
                var savedIds = new List<string>();
                foreach (string id in requestedIds)
                {
                    if (availableIds.Contains(id) && !savedIds.Contains(id))
                    {
                        savedIds.Add(id);
                        if (savedIds.Count == MaxSelection) break;
                    }
                }

                Directory.CreateDirectory(BugLogsDirectory);
                string path = Path.Combine(BugLogsDirectory, SelectionFileName);
                string temporaryPath = path + ".tmp";
                var document = new JObject
                {
                    ["schemaVersion"] = 1,
                    ["updatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                    ["ids"] = new JArray(savedIds)
                };
                File.WriteAllText(temporaryPath, document.ToString(Formatting.None), new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporaryPath, path);
                return savedIds;
            }
        }

        private static string EnumName(JToken token, string[] names, string fallback)
        {
            if (token == null) return fallback;
            if (token.Type == JTokenType.String) return token.Value<string>();
            if (token.Type != JTokenType.Integer) return fallback;

            int index = token.Value<int>();
            return index >= 0 && index < names.Length ? names[index] : fallback;
        }

        private static string ToUtcIso(string rawMilliseconds)
        {
            return long.TryParse(rawMilliseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds)
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime.ToString("O")
                : string.Empty;
        }

        private static void WriteJson(Stream stream, JObject payload, int statusCode = 200)
        {
            WriteResponse(stream, statusCode, "application/json; charset=utf-8", payload.ToString(Formatting.None));
        }

        private static void WriteResponse(Stream stream, int statusCode, string contentType, string content)
        {
            byte[] body = Encoding.UTF8.GetBytes(content);
            string header = $"HTTP/1.1 {statusCode} {GetStatusDescription(statusCode)}\r\n" +
                            $"Content-Type: {contentType}\r\n" +
                            "Cache-Control: no-store\r\n" +
                            "Connection: close\r\n" +
                            $"Content-Length: {body.Length}\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        private static string GetStatusDescription(int statusCode) => statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "Internal Server Error"
        };

        private sealed class DashboardRecord
        {
            public readonly string Id;
            private readonly string capturedAtUtc;
            private readonly string logType;
            private readonly string source;
            private readonly string category;
            private readonly string message;
            private readonly string stackTrace;

            public DashboardRecord(string id, string capturedAtUtc, string logType, string source, string category,
                string message, string stackTrace)
            {
                Id = id;
                this.capturedAtUtc = capturedAtUtc;
                this.logType = logType;
                this.source = source;
                this.category = category;
                this.message = message;
                this.stackTrace = stackTrace;
            }

            public JObject ToJson() => new JObject
            {
                ["id"] = Id,
                ["capturedAtUtc"] = capturedAtUtc,
                ["logType"] = logType,
                ["source"] = source,
                ["category"] = category,
                ["message"] = message,
                ["stackTrace"] = stackTrace
            };
        }
    }
}
#endif
