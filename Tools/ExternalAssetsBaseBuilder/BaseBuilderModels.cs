using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsBaseBuilder;

internal sealed record ExternalAssetsSource(
    string Id,
    string DisplayName,
    string ExternalAssetsPath,
    bool IsBaseline,
    ExternalAssetsSourceKind Kind,
    IReadOnlySet<string> RequiredDirectoryMetaPaths,
    IReadOnlyDictionary<string, string> ExpectedFileSha256);

/// <summary>
/// A normal source is a complete <c>Assets/ExternalAssets</c> tree. A contribution
/// source is deliberately partial: it contains only the payload declared by a
/// contribution package, so it must not be rejected merely because the baseline
/// already owns an unchanged ancestor folder's .meta file.
/// </summary>
internal enum ExternalAssetsSourceKind
{
    FullExternalAssets,
    ContributionPartial,
}

internal sealed record AdditionalExternalAssetsInput(
    string DisplayName,
    string ExternalAssetsPath,
    ExternalAssetsSourceKind Kind,
    IReadOnlySet<string>? RequiredDirectoryMetaPaths = null,
    IReadOnlyDictionary<string, string>? ExpectedFileSha256 = null);

internal sealed record SourceFile(
    string SourceId,
    string RelativePath,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string? ExpectedSha256);

internal enum ConflictKind
{
    SameRelativePath,
    CaseOnlyPathCollision,
    FileDirectoryCollision,
}

internal sealed record MergeConflict(
    string Id,
    string LogicalPath,
    ConflictKind Kind,
    IReadOnlyList<string> RelativePaths,
    IReadOnlyList<SourceFile> Candidates);

internal sealed record SourceValidationIssue(
    string SourceId,
    string RelativePath,
    string Message);

internal sealed record GuidCollision(
    string Guid,
    IReadOnlyList<string> RelativeMetaPaths);

internal sealed record BaseMergePlan(
    IReadOnlyList<ExternalAssetsSource> Sources,
    IReadOnlyDictionary<string, SourceFile> UniqueFiles,
    IReadOnlyList<string> Directories,
    IReadOnlyList<MergeConflict> Conflicts,
    IReadOnlyList<SourceValidationIssue> SourceValidationIssues,
    int BaselineFileCount,
    int UniqueAdditionFileCount);

internal sealed record PlanValidationResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<GuidCollision> GuidCollisions)
{
    public bool IsValid => Errors.Count == 0 && GuidCollisions.Count == 0;
}

internal sealed record BasePackageBuildResult(
    string ZipPath,
    string Sha256,
    long SizeBytes,
    int FileEntryCount,
    int DirectoryEntryCount,
    string ReportPath);

internal sealed record ContributionImportValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

internal sealed class MergeReport
{
    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("sources")]
    public List<MergeReportSource> Sources { get; init; } = [];

    [JsonPropertyName("conflicts")]
    public List<MergeReportConflict> Conflicts { get; init; } = [];

    [JsonPropertyName("zip")]
    public MergeReportZip? Zip { get; init; }
}

internal sealed class MergeReportSource
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("isBaseline")]
    public bool IsBaseline { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;
}

internal sealed class MergeReportConflict
{
    [JsonPropertyName("logicalPath")]
    public string LogicalPath { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("relativePaths")]
    public List<string> RelativePaths { get; init; } = [];

    [JsonPropertyName("selectedSource")]
    public string SelectedSource { get; init; } = string.Empty;
}

internal sealed class MergeReportZip
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("fileEntryCount")]
    public int FileEntryCount { get; init; }

    [JsonPropertyName("directoryEntryCount")]
    public int DirectoryEntryCount { get; init; }
}

internal sealed record BuildProgress(string Status, int CompletedFiles, int TotalFiles);
