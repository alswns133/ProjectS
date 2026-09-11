using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// 원격 버그 로그의 빌드별 설정이다. Resources/Logging/LogSettings.asset으로 로드된다.
    /// URL과 시크릿은 빌드를 보호하는 인증 정보가 아니므로 민감한 개인정보를 넣으면 안 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "LogSettings", menuName = "ProjectS/Logging/Log Settings")]
    public class LogSettings : ScriptableObject
    {
        [Header("Client identity")]
        [SerializeField] private string buildId = "";
        [SerializeField] private string testerId = "";

        [Header("Capture")]
        [SerializeField, Min(1)] private int maxCapturedLogsPerFrame = 100;

        [Header("Overlay")]
        [SerializeField, Min(10)] private int overlayCapacity = 300;
        [SerializeField] private bool overlayStartsVisible;

        [Header("Local file")]
        [SerializeField] private bool writeLocalFile = true;
        [SerializeField, Min(0.1f)] private float localFlushIntervalSeconds = 1f;

        [Header("Google Apps Script")]
        [Tooltip("Apps Script 웹앱의 /exec URL. 비어 있으면 원격 전송을 건너뛴다.")]
        [SerializeField] private string appsScriptUrl = "";
        [Tooltip("테스트용 스팸 차단 키. 실제 보안 자격 증명이나 개인정보를 넣지 않는다.")]
        [SerializeField] private string sharedSecret = "";
        [SerializeField] private LogSeverity remoteMinimumSeverity = LogSeverity.Error;
        [SerializeField, Min(1)] private int remoteBatchSize = 20;
        [SerializeField, Min(0.1f)] private float remoteBatchIntervalSeconds = 5f;
        [SerializeField, Min(1)] private int remoteMaximumPendingEntries = 500;
        [SerializeField, Range(0, 5)] private int remoteRetryCount = 2;
        [SerializeField, Min(0.1f)] private float remoteRetryDelaySeconds = 1f;

        public string BuildId => string.IsNullOrWhiteSpace(buildId) ? Application.version : buildId.Trim();
        public string TesterId => testerId?.Trim() ?? string.Empty;
        public int MaxCapturedLogsPerFrame => Mathf.Max(1, maxCapturedLogsPerFrame);
        public int OverlayCapacity => Mathf.Max(10, overlayCapacity);
        public bool OverlayStartsVisible => overlayStartsVisible;
        public bool WriteLocalFile => writeLocalFile;
        public float LocalFlushIntervalSeconds => Mathf.Max(0.1f, localFlushIntervalSeconds);
        public string AppsScriptUrl => appsScriptUrl?.Trim() ?? string.Empty;
        public string SharedSecret => sharedSecret ?? string.Empty;
        public LogSeverity RemoteMinimumSeverity => remoteMinimumSeverity;
        public int RemoteBatchSize => Mathf.Max(1, remoteBatchSize);
        public float RemoteBatchIntervalSeconds => Mathf.Max(0.1f, remoteBatchIntervalSeconds);
        public int RemoteMaximumPendingEntries => Mathf.Max(RemoteBatchSize, remoteMaximumPendingEntries);
        public int RemoteRetryCount => Mathf.Clamp(remoteRetryCount, 0, 5);
        public float RemoteRetryDelaySeconds => Mathf.Max(0.1f, remoteRetryDelaySeconds);

        public bool HasRemoteConfiguration =>
            !string.IsNullOrWhiteSpace(AppsScriptUrl) && !string.IsNullOrWhiteSpace(SharedSecret);

        public bool ShouldSendRemotely(LogSeverity severity) => severity >= RemoteMinimumSeverity;
    }
}
