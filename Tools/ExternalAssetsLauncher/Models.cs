using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class ExternalAssetsManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("latestVersion")]
    public int LatestVersion { get; init; }

    [JsonPropertyName("packages")]
    public List<ExternalAssetsPackage> Packages { get; init; } = [];
}

internal sealed class ExternalAssetsPackage
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "patch";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("removedPaths")]
    public List<string> RemovedPaths { get; init; } = [];
}

internal sealed class ExternalAssetsLock
{
    [JsonPropertyName("requiredVersion")]
    public int RequiredVersion { get; init; }
}

internal sealed class LauncherSettings
{
    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [JsonPropertyName("manifestUrl")]
    public string ManifestUrl { get; set; } = string.Empty;

    [JsonPropertyName("unityExecutablePath")]
    public string UnityExecutablePath { get; set; } = string.Empty;
}

internal sealed class InstalledState
{
    [JsonPropertyName("installedVersion")]
    public int InstalledVersion { get; init; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

internal sealed record DownloadProgress(string Status, long BytesReceived, long? TotalBytes);

internal sealed record UpdatePlan(
    ExternalAssetsManifest Manifest,
    int InstalledVersion,
    int RequiredVersion,
    IReadOnlyList<ExternalAssetsPackage> Packages)
{
    public bool IsCurrent => Packages.Count == 0 && InstalledVersion == RequiredVersion;
    public bool RequiresReset => InstalledVersion > RequiredVersion;
}
