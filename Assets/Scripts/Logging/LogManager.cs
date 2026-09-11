using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ProjectS.Managers;
using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// Layer 1(스레드 안전 수집)과 Layer 3(sink 브로드캐스트)를 관리하는 전역 매니저.
    /// 씬 배치를 잊어도 동작하도록 첫 씬 로드 뒤 자동 생성된다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class LogManager : MonoBehaviour
    {
        internal const string TaggedConsolePrefix = "[ProjectS.GameLog] ";

        private static readonly ConcurrentQueue<LogEntry> pendingTaggedEntries = new();
        private static bool bootstrapped;

        public static LogManager Instance { get; private set; }

        private readonly ConcurrentQueue<LogEntry> capturedEntries = new();
        private readonly List<ILogSink> sinks = new();
        private int mainThreadId;
        private bool subscribed;
        private bool quitting;
        private LogSettings settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            bootstrapped = false;
            while (pendingTaggedEntries.TryDequeue(out _)) { }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (bootstrapped || Instance != null) return;

            bootstrapped = true;
            GameObject go = new GameObject("[LogManager]");
            go.AddComponent<LogManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            DontDestroyOnLoad(gameObject);

            // Developers can keep endpoint credentials in a Git-ignored local override.
            settings = Resources.Load<LogSettings>("Logging/LogSettings.local");
            if (settings == null)
            {
                settings = Resources.Load<LogSettings>("Logging/LogSettings");
            }

            if (settings == null) settings = ScriptableObject.CreateInstance<LogSettings>();

            InitializeSinks();
            DrainPendingTaggedEntries();
        }

        private void OnEnable()
        {
            if (subscribed) return;

            Application.logMessageReceivedThreaded += OnLogMessageReceivedThreaded;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed) return;

            Application.logMessageReceivedThreaded -= OnLogMessageReceivedThreaded;
            subscribed = false;
        }

        private void Update()
        {
            DrainPendingTaggedEntries();

            int remaining = settings.MaxCapturedLogsPerFrame;
            while (remaining-- > 0 && capturedEntries.TryDequeue(out LogEntry entry))
                Broadcast(in entry);

            float deltaTime = Time.unscaledDeltaTime;
            for (int i = 0; i < sinks.Count; i++)
                if (sinks[i] is ILogTickableSink tickable) tickable.Tick(deltaTime);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushAll();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) FlushAll();
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            FlushAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            OnDisable();
            FlushAll();

            for (int i = 0; i < sinks.Count; i++)
                if (sinks[i] is IDisposable disposable) disposable.Dispose();
            sinks.Clear();
        }

        /// <summary>GameLog의 명시 태그 로그 진입점. 작업 스레드 호출도 안전하게 큐로 넘긴다.</summary>
        internal static void PublishTagged(in LogEntry entry)
        {
            LogManager manager = Instance;
            if (manager == null)
            {
                pendingTaggedEntries.Enqueue(entry);
                return;
            }

            manager.ReceiveTagged(in entry);
        }

        private void InitializeSinks()
        {
            sinks.Add(new LocalFileLogSink(settings));

            LogOverlayConsole overlay = GetComponent<LogOverlayConsole>();
            if (overlay == null) overlay = gameObject.AddComponent<LogOverlayConsole>();
            overlay.Configure(settings);
            sinks.Add(overlay);

            sinks.Add(new GoogleSheetsLogSink(settings, ResolveBuildId, ResolveTesterId));
        }

        private void OnLogMessageReceivedThreaded(string message, string stackTrace, LogType type)
        {
            // GameLog가 콘솔에 함께 남긴 줄은 이미 정확한 태그로 PublishTagged를 거쳤다.
            // 이 접두어를 건너뛰어 자동 Unity/Uncategorized 중복과 재귀를 막는다.
            if (!string.IsNullOrEmpty(message) && message.StartsWith(TaggedConsolePrefix, StringComparison.Ordinal)) return;

            var entry = new LogEntry(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LogSource.Unity,
                LogCategory.Uncategorized,
                MapSeverity(type),
                message,
                stackTrace);
            capturedEntries.Enqueue(entry);
        }

        private void ReceiveTagged(in LogEntry entry)
        {
            if (quitting) return;

            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
                Broadcast(in entry);
            else
                capturedEntries.Enqueue(entry);
        }

        private void DrainPendingTaggedEntries()
        {
            while (pendingTaggedEntries.TryDequeue(out LogEntry entry))
                ReceiveTagged(in entry);
        }

        private void Broadcast(in LogEntry entry)
        {
            for (int i = 0; i < sinks.Count; i++)
                sinks[i].Write(in entry);
        }

        private void FlushAll()
        {
            for (int i = 0; i < sinks.Count; i++)
                if (sinks[i] is ILogTickableSink tickable) tickable.Flush();
        }

        private string ResolveBuildId() => settings.BuildId;

        private string ResolveTesterId()
        {
            if (!string.IsNullOrWhiteSpace(settings.TesterId)) return settings.TesterId;

            string characterName = GameSession.SelectedCharacter?.name;
            return string.IsNullOrWhiteSpace(characterName) ? "anonymous" : characterName;
        }

        private static LogSeverity MapSeverity(LogType type) => type switch
        {
            LogType.Warning => LogSeverity.Warning,
            LogType.Exception => LogSeverity.Exception,
            LogType.Error => LogSeverity.Error,
            LogType.Assert => LogSeverity.Error,
            _ => LogSeverity.Info,
        };
    }
}
