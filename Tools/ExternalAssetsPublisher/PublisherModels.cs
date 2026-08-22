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
