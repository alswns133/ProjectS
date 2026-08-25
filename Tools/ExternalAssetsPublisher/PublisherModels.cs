using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsPublisher;

internal sealed class PublisherManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("latestVersion")]
    public int LatestVersion { get; set; }

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("packages")]
    public List<PublisherManifestPackage> Packages { get; set; } = [];
}

internal sealed class PublisherManifestPackage
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "patch";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("driveFileId")]
    public string DriveFileId { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    // 이 버전 시점의 ExternalAssets 전체 스냅샷(release-snapshot-vN.json)의 Drive 파일 ID.
    // 다음 배포자가 "직전 버전 기준"을 정확히 받아 diff하도록 manifest에 함께 기록한다.
    // 런처는 이 필드를 읽지 않으므로(무시) 기존 설치와 호환된다. 선택 필드.
    [JsonPropertyName("snapshotDriveFileId")]
    public string SnapshotDriveFileId { get; set; } = string.Empty;

    [JsonPropertyName("removedPaths")]
    public List<string> RemovedPaths { get; set; } = [];
}

internal sealed record SourceSelection(string FullPath, bool IsFolder);

internal enum PackageBuildOrigin
{
    ProjectPatch,
    ImportedBase,
}

internal sealed record PackageBuildResult(
    string ZipPath,
    string PackageName,
    string Sha256,
    long SizeBytes,
    int FileEntryCount,
    IReadOnlyList<string> ArchiveEntries,
    DateTime LastWriteTimeUtc,
    PackageBuildOrigin Origin);
