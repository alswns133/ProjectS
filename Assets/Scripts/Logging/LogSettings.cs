using UnityEngine;

namespace ProjectS.Logging
{
    /// <summary>
    /// 원격 버그 로그의 빌드별 설정이다. 로컬 오버라이드가 있으면 우선 로드된다.
    /// URL과 시크릿은 빌드를 보호하는 인증 정보가 아니므로 민감한 개인정보를 넣으면 안 된다.
    /// </summary>
    [CreateAssetMenu(fileName = "LogSettings", menuName = "ProjectS/Logging/Log Settings")]
    public class LogSettings : ScriptableObject
    {
        // LogSettings.asset is the shared, credential-free template.
        // LogSettings.local.asset is an optional Git-ignored local override.
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

        [Header("Server forward")]
        [Tooltip("원격 클라의 로그를 서버 콘솔로 보낸다(팀 규칙: 클라 디버그는 서버에서 전부 보여야 한다). 릴리스 빌드에서는 꺼진다.")]
        [SerializeField] private bool forwardClientLogsToServer = true;
        [Tooltip("이 심각도 이상만 보낸다. 기본 Info = 전부.")]
        [SerializeField] private LogSeverity forwardMinimumSeverity = LogSeverity.Info;
        [Tooltip("초당 최대 전송 줄 수. 넘치면 대기열에 남았다가 다음 초에 나간다.")]
        [SerializeField, Min(1)] private int forwardMaxPerSecond = 60;
        [Tooltip("접속 전·전송 대기 중 보관할 최대 줄 수. 넘치면 버리고 버린 개수만 알린다.")]
        [SerializeField, Min(10)] private int forwardMaximumPendingEntries = 500;

        /// <summary>빌드 ID. 비워 두면 Application.version을 쓴다.</summary>
        public string BuildId => string.IsNullOrWhiteSpace(buildId) ? Application.version : buildId.Trim();

        /// <summary>테스터 식별자(원격 로그의 user 칸). 없으면 빈 문자열.</summary>
        public string TesterId => testerId?.Trim() ?? string.Empty;

        /// <summary>한 프레임에 처리할 최대 수집 로그 수(최소 1). 로그 폭주 시 프레임 멈춤을 막는다.</summary>
        public int MaxCapturedLogsPerFrame => Mathf.Max(1, maxCapturedLogsPerFrame);

        /// <summary>오버레이 콘솔이 보관할 최대 줄 수(최소 10).</summary>
        public int OverlayCapacity => Mathf.Max(10, overlayCapacity);

        /// <summary>오버레이 콘솔을 켠 상태로 시작할지.</summary>
        public bool OverlayStartsVisible => overlayStartsVisible;

        /// <summary>로컬 로그 파일을 쓸지.</summary>
        public bool WriteLocalFile => writeLocalFile;

        /// <summary>로컬 파일 flush 주기(초, 최소 0.1).</summary>
        public float LocalFlushIntervalSeconds => Mathf.Max(0.1f, localFlushIntervalSeconds);

        /// <summary>원격 전송 대상 Apps Script /exec URL. 비어 있으면 원격 전송을 건너뛴다.</summary>
        public string AppsScriptUrl => appsScriptUrl?.Trim() ?? string.Empty;

        /// <summary>원격 전송 스팸 차단 키(보안 자격 증명 아님).</summary>
        public string SharedSecret => sharedSecret ?? string.Empty;

        /// <summary>원격 전송할 최소 심각도.</summary>
        public LogSeverity RemoteMinimumSeverity => remoteMinimumSeverity;

        /// <summary>원격 전송 한 번에 묶을 줄 수(최소 1). 이만큼 쌓이면 주기를 기다리지 않고 보낸다.</summary>
        public int RemoteBatchSize => Mathf.Max(1, remoteBatchSize);

        /// <summary>원격 전송 주기(초, 최소 0.1).</summary>
        public float RemoteBatchIntervalSeconds => Mathf.Max(0.1f, remoteBatchIntervalSeconds);

        /// <summary>원격 전송 대기열 최대 줄 수. 넘치면 오래된 것부터 버린다(최소 배치 크기).</summary>
        public int RemoteMaximumPendingEntries => Mathf.Max(RemoteBatchSize, remoteMaximumPendingEntries);

        /// <summary>원격 전송 실패 시 재시도 횟수(0~5).</summary>
        public int RemoteRetryCount => Mathf.Clamp(remoteRetryCount, 0, 5);

        /// <summary>재시도 간격(초, 최소 0.1).</summary>
        public float RemoteRetryDelaySeconds => Mathf.Max(0.1f, remoteRetryDelaySeconds);

        /// <summary>원격 클라 로그를 서버 콘솔로 보낼지.</summary>
        public bool ForwardClientLogsToServer => forwardClientLogsToServer;

        /// <summary>서버로 보낼 최소 심각도.</summary>
        public LogSeverity ForwardMinimumSeverity => forwardMinimumSeverity;

        /// <summary>서버로 보낼 초당 최대 줄 수(최소 1).</summary>
        public int ForwardMaxPerSecond => Mathf.Max(1, forwardMaxPerSecond);

        /// <summary>서버 전송 대기열 최대 줄 수(최소 10).</summary>
        public int ForwardMaximumPendingEntries => Mathf.Max(10, forwardMaximumPendingEntries);

        /// <summary>URL과 시크릿이 모두 있어 원격 전송이 가능한지.</summary>
        public bool HasRemoteConfiguration =>
            !string.IsNullOrWhiteSpace(AppsScriptUrl) && !string.IsNullOrWhiteSpace(SharedSecret);

        /// <summary>이 심각도가 원격 전송 대상인지.</summary>
        public bool ShouldSendRemotely(LogSeverity severity) => severity >= RemoteMinimumSeverity;

        /// <summary>모든 값을 커밋용 템플릿 기본값으로 되돌린다(URL·시크릿 비움). 로컬 오버라이드로 옮긴 뒤 공유 에셋을 비우는 데 쓴다.</summary>
        public void ResetToTemplateDefaults()
        {
            buildId = "";
            testerId = "";
            maxCapturedLogsPerFrame = 100;
            overlayCapacity = 300;
            overlayStartsVisible = false;
            writeLocalFile = true;
            localFlushIntervalSeconds = 1f;
            appsScriptUrl = "";
            sharedSecret = "";
            remoteMinimumSeverity = LogSeverity.Error;
            remoteBatchSize = 20;
            remoteBatchIntervalSeconds = 5f;
            remoteMaximumPendingEntries = 500;
            remoteRetryCount = 2;
            remoteRetryDelaySeconds = 1f;
            forwardClientLogsToServer = true;
            forwardMinimumSeverity = LogSeverity.Info;
            forwardMaxPerSecond = 60;
            forwardMaximumPendingEntries = 500;
        }
    }
}
