using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsDelta;

/// <summary>
/// 외부 에셋 델타 파일 형식의 공통 상수다. 현재는 호환되지 않는 형식을
/// 조용히 받아들이지 않도록 각 문서의 버전을 명시적으로 검증한다.
/// </summary>
public static class ExternalAssetsDeltaFormat
{
    public const int SchemaVersion = 1;
    public const string SeedIndexKind = "project-s-external-assets-seed-index";
    public const string ContributionKind = "project-s-external-assets-contribution";
    public const string ContributionManifestEntryName = "contribution.json";
    public const string PayloadPrefix = "payload/";
}

public enum SeedEntryType
{
    File,
    Directory,
}

public enum DeltaComparisonKind
{
    Added,
    Unchanged,
    Modified,
    Missing,
}

public enum DeltaIssueSeverity
{
    Warning,
    Error,
}

public enum ContributionChangeKind
{
    Added,
    Modified,
    /// <summary>
    /// 변경된 Unity asset/.meta 쌍을 원자적으로 병합하기 위해 함께 실은,
    /// 기준과 동일한 파일이다. baseline SHA-256과 payload SHA-256이 같아야 한다.
    /// </summary>
    Support,
}

public enum ContributionMetaTargetKind
{
    Asset,
    Folder,
}

public sealed class SeedIndex
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ExternalAssetsDeltaFormat.SchemaVersion;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = ExternalAssetsDeltaFormat.SeedIndexKind;

    /// <summary>기준본을 식별하는 UUID. 다른 기준본의 기여 ZIP 혼합을 막는다.</summary>
    [JsonPropertyName("baselineId")]
    public string BaselineId { get; init; } = string.Empty;

    /// <summary>기준 내용의 결정적 지문. seed JSON의 들여쓰기/생성 시각에는 영향받지 않는다.</summary>
    [JsonPropertyName("baselineContentSha256")]
    public string BaselineContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("externalAssetsRootGuid")]
    public string ExternalAssetsRootGuid { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("entries")]
    public List<SeedIndexEntry> Entries { get; init; } = [];
}

public sealed class SeedIndexEntry
{
    [JsonPropertyName("path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("entryType")]
    [JsonConverter(typeof(JsonStringEnumConverter<SeedEntryType>))]
    public SeedEntryType EntryType { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    /// <summary>.meta 파일의 Unity GUID. 디렉터리/일반 파일에는 비어 있어야 한다.</summary>
    [JsonPropertyName("guid")]
    public string? Guid { get; init; }

    [JsonPropertyName("metaTargetKind")]
    [JsonConverter(typeof(JsonStringEnumConverter<ContributionMetaTargetKind>))]
    public ContributionMetaTargetKind? MetaTargetKind { get; init; }
}

/// <summary>로컬 ExternalAssets 전체를 seed와 비교한 한 경로의 결과다.</summary>
public sealed record DeltaComparisonEntry(
    string RelativePath,
    SeedEntryType EntryType,
    DeltaComparisonKind ChangeKind,
    SeedIndexEntry? BaselineEntry,
    LocalExternalAssetsEntry? LocalEntry);

/// <summary>
/// 비교는 가능한 한 모든 차이를 보고한다. <see cref="Issues"/> 중 Error가 있으면
/// 기여 ZIP을 만들 수 없으며, Missing은 초기 Base 병합에서는 보고 전용이다.
/// </summary>
public sealed class ExternalAssetsComparison
{
    public string LocalExternalAssetsPath { get; init; } = string.Empty;
    public SeedIndex SeedIndex { get; init; } = new();
    public string LocalExternalAssetsRootGuid { get; init; } = string.Empty;
    public IReadOnlyList<DeltaComparisonEntry> Entries { get; init; } = [];
    public IReadOnlyList<DeltaIssue> Issues { get; init; } = [];

    public bool HasErrors => Issues.Any(issue => issue.Severity == DeltaIssueSeverity.Error);
    public bool IsExactMatch => !HasErrors && Entries.All(entry => entry.ChangeKind == DeltaComparisonKind.Unchanged);
}

public sealed record SeedMatchValidation(
    bool IsMatch,
    ExternalAssetsComparison Comparison);

public sealed record DeltaIssue(
    DeltaIssueSeverity Severity,
    string Code,
    string Message,
    string? RelativePath = null);

/// <summary>실제 로컬 파일/폴더 스냅샷 항목. root ExternalAssets.meta는 포함하지 않는다.</summary>
public sealed class LocalExternalAssetsEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public SeedEntryType EntryType { get; init; }
    public string? FullPath { get; init; }
    public long? SizeBytes { get; init; }
    public string? Sha256 { get; init; }
    public string? Guid { get; init; }
    public ContributionMetaTargetKind? MetaTargetKind { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
}

public sealed class ContributionManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ExternalAssetsDeltaFormat.SchemaVersion;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = ExternalAssetsDeltaFormat.ContributionKind;

    [JsonPropertyName("contributionId")]
    public string ContributionId { get; init; } = string.Empty;

    [JsonPropertyName("baselineId")]
    public string BaselineId { get; init; } = string.Empty;

    [JsonPropertyName("baselineContentSha256")]
    public string BaselineContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("externalAssetsRootGuid")]
    public string ExternalAssetsRootGuid { get; init; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("contributorName")]
    public string ContributorName { get; init; } = string.Empty;

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("directories")]
    public List<ContributionDirectoryEntry> Directories { get; init; } = [];

    [JsonPropertyName("entries")]
    public List<ContributionPayloadEntry> Entries { get; init; } = [];
}

/// <summary>
/// payload가 없는 디렉터리 선언. 빈 신규 폴더를 잃지 않고 안전하게 추출하기 위한 정보다.
/// </summary>
public sealed class ContributionDirectoryEntry
{
    [JsonPropertyName("path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("changeKind")]
    [JsonConverter(typeof(JsonStringEnumConverter<ContributionChangeKind>))]
    public ContributionChangeKind ChangeKind { get; init; } = ContributionChangeKind.Added;
}

public sealed class ContributionPayloadEntry
{
    [JsonPropertyName("path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("payloadPath")]
    public string PayloadPath { get; init; } = string.Empty;

    [JsonPropertyName("changeKind")]
    [JsonConverter(typeof(JsonStringEnumConverter<ContributionChangeKind>))]
    public ContributionChangeKind ChangeKind { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>
    /// Modified/Support에서 seed 기준 파일의 SHA-256이다. owner가 stale 기여를
    /// 병합하기 전에 검출할 수 있도록 항상 선언한다.
    /// </summary>
    [JsonPropertyName("baselineSha256")]
    public string? BaselineSha256 { get; init; }

    [JsonPropertyName("baselineSizeBytes")]
    public long? BaselineSizeBytes { get; init; }

    [JsonPropertyName("guid")]
    public string? Guid { get; init; }

    [JsonPropertyName("metaTargetKind")]
    [JsonConverter(typeof(JsonStringEnumConverter<ContributionMetaTargetKind>))]
    public ContributionMetaTargetKind? MetaTargetKind { get; init; }
}

public sealed class ContributionBuildOptions
{
    public required string OutputZipPath { get; init; }
    public required string ContributorName { get; init; }
    public string? Note { get; init; }
}

public sealed record DeltaProgress(string Status, int CompletedItems, int TotalItems);

public sealed record ContributionPackageBuildResult(
    string ZipPath,
    string ArchiveSha256,
    long SizeBytes,
    ContributionManifest Manifest);

public sealed class ContributionPayloadDescriptor
{
    public ContributionPayloadEntry ManifestEntry { get; init; } = new();
    public string ArchiveEntryName { get; init; } = string.Empty;
}

/// <summary>
/// ZIP 전체 SHA-256과 이미 검증된 manifest/payload 목록. ZIP은 필요할 때 다시 열며,
/// 호출자는 이 객체를 신뢰 경계 밖의 입력을 재검증한 결과로 취급할 수 있다.
/// </summary>
public sealed class LoadedContributionPackage
{
    public string ZipPath { get; init; } = string.Empty;
    public string ArchiveSha256 { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public ContributionManifest Manifest { get; init; } = new();
    public IReadOnlyList<ContributionPayloadDescriptor> PayloadEntries { get; init; } = [];
}

public sealed record ContributionExtractionResult(
    string DestinationExternalAssetsPath,
    int PayloadFileCount,
    int DirectoryCount);

public sealed record ContributionBaselineValidation(
    bool IsValid,
    IReadOnlyList<DeltaIssue> Issues);
