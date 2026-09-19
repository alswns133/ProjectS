using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectS.DbBackup;

/// <summary>
/// 백업 프로그램 설정. exe 옆의 <c>dbbackup.config.json</c>에서 읽는다(예시: <c>dbbackup.config.example.json</c>).
/// </summary>
/// <remarks>
/// 설정 파일에는 키 자체가 아니라 <b>키 파일 경로</b>만 둔다. 키(서비스 계정 JSON)는 DB 전체를 읽고 쓸 수 있는
/// 관리자 권한이라, 저장소·빌드·로그 어디에도 들어가면 안 된다 — 서버 PC의 저장소 밖 폴더에만 둔다.
/// </remarks>
public sealed class BackupConfig
{
    /// <summary>RTDB 주소(끝 슬래시 없이). 예: https://projects-6da1b-default-rtdb.firebaseio.com</summary>
    [JsonPropertyName("databaseUrl")] public string DatabaseUrl { get; init; } = "";

    /// <summary>서비스 계정 키(JSON) 파일 경로. 저장소 밖이어야 한다.</summary>
    [JsonPropertyName("serviceAccountKeyPath")] public string ServiceAccountKeyPath { get; init; } = "";

    /// <summary>백업 루트 폴더. 아래에 hourly/ daily/ characters/ backup.log가 생긴다.</summary>
    [JsonPropertyName("backupRoot")] public string BackupRoot { get; init; } = "";

    /// <summary>daily/·characters/ 보관 일수. 이보다 오래된 날짜 폴더·파일은 정리한다.</summary>
    [JsonPropertyName("dailyRetentionDays")] public int DailyRetentionDays { get; init; } = 30;

    /// <summary>
    /// 캐릭터 수 급감 판정 비율. 직전 백업 대비 이 비율 미만이면 "이상"으로 보고 저장하지 않는다
    /// (2026-09-18 루트 덮어쓰기 사고처럼 망가진 DB가 정상 백업을 밀어내는 것을 막는다).
    /// </summary>
    [JsonPropertyName("minCharacterRatio")] public double MinCharacterRatio { get; init; } = 0.5;

    /// <summary>daily 확정본을 한 번 더 복사할 2차 보관 폴더(예: 구글 드라이브 동기화 폴더). 비우면 생략.</summary>
    [JsonPropertyName("secondaryCopyDir")] public string SecondaryCopyDir { get; init; } = "";

    public string HourlyDir => Path.Combine(BackupRoot, "hourly");
    public string DailyDir => Path.Combine(BackupRoot, "daily");
    public string CharactersDir => Path.Combine(BackupRoot, "characters");
    public string LogPath => Path.Combine(BackupRoot, "backup.log");

    /// <summary>exe 옆의 설정 파일을 읽는다. 없거나 필수값이 비면 예외 — 잘못된 설정으로 조용히 도는 것보다 낫다.</summary>
    public static BackupConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"설정 파일이 없습니다: {path} (dbbackup.config.example.json을 복사해 만드세요)");

        BackupConfig config = JsonSerializer.Deserialize<BackupConfig>(File.ReadAllText(path))
                              ?? throw new InvalidDataException("설정 파일을 읽지 못했습니다.");

        if (string.IsNullOrWhiteSpace(config.DatabaseUrl)) throw new InvalidDataException("databaseUrl이 비었습니다.");
        if (!File.Exists(config.ServiceAccountKeyPath)) throw new FileNotFoundException($"서비스 계정 키가 없습니다: {config.ServiceAccountKeyPath}");
        if (string.IsNullOrWhiteSpace(config.BackupRoot)) throw new InvalidDataException("backupRoot가 비었습니다.");

        return config;
    }
}
