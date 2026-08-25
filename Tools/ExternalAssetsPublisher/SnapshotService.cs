using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsPublisher;

/// <summary>
/// 한 배포 버전 시점의 ExternalAssets 전체를 "경로 → 해시" 목록으로 남긴 스냅샷.
/// 다음 패치를 만들 때 이 스냅샷과 현재 상태를 비교(diff)해 바뀐 파일만 자동으로 골라낸다.
/// base의 seed-index와 같은 형식(경로별 SHA-256)이라, 여러 배포자가 Drive로 공유해
/// "직전 버전 기준"을 맞추는 데 쓴다.
/// </summary>
internal sealed class ReleaseSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>ExternalAssets 기준 상대 경로('/') → 파일 해시·크기.</summary>
    [JsonPropertyName("entries")]
    public Dictionary<string, ReleaseSnapshotEntry> Entries { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class ReleaseSnapshotEntry
{
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

/// <summary>직전 스냅샷 대비 변경 결과. Added/Modified는 패치에 담고, Removed는 삭제 경로로 나간다.</summary>
internal sealed record SnapshotDelta(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Removed)
{
    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}

internal static class SnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// 현재 ExternalAssets 전체(.meta 포함)를 해시해 스냅샷을 만든다.
    /// 패치 패키징이 담는 파일 집합과 동일한 범위를 훑어야 diff가 어긋나지 않으므로,
    /// 루트 하위 모든 파일을 대상으로 한다(루트 밖 Assets/ExternalAssets.meta는 제외).
    /// </summary>
    public static ReleaseSnapshot CreateFromExternalAssets(string projectPath, int version, string channelId)
    {
        var externalAssetsPath = PublisherServices.GetExternalAssetsPath(projectPath);
        if (!Directory.Exists(externalAssetsPath))
        {
            throw new InvalidOperationException("Assets/ExternalAssets 폴더를 찾을 수 없습니다.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(externalAssetsPath));
        var entries = new Dictionary<string, ReleaseSnapshotEntry>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            entries[relativePath] = new ReleaseSnapshotEntry
            {
                Sha256 = sha,
                Size = new FileInfo(file).Length,
            };
        }

        return new ReleaseSnapshot
        {
            SchemaVersion = 1,
            Version = version,
            ChannelId = channelId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Entries = entries,
        };
    }

    public static async Task<ReleaseSnapshot> ReadAsync(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            throw new InvalidOperationException("직전 버전 스냅샷(release-snapshot-vN.json)을 선택하세요.");
        }

        await using var stream = File.OpenRead(snapshotPath);
        var snapshot = await JsonSerializer.DeserializeAsync<ReleaseSnapshot>(stream, JsonOptions);
        if (snapshot is null || snapshot.Entries is null)
        {
            throw new InvalidOperationException("스냅샷 파일을 읽을 수 없습니다.");
        }

        return snapshot;
    }

    public static async Task WriteAsync(string snapshotPath, ReleaseSnapshot snapshot)
    {
        var fullPath = Path.GetFullPath(snapshotPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions);
        }

        File.Move(temporaryPath, fullPath, true);
    }

    /// <summary>
    /// baseline에 '선택된 변경'만 적용한 다음 버전 스냅샷을 만든다. 체크리스트에서 일부만 골라 압축할 때,
    /// 실제 게시되는 버전은 baseline + 선택분이므로 스냅샷도 그에 맞춰야 다음 diff가 어긋나지 않는다.
    /// (현재 전체를 그대로 스냅샷으로 쓰면, 제외한 변경까지 '이미 올라간 것'으로 기록돼 다음에 누락된다.)
    /// </summary>
    public static ReleaseSnapshot BuildNextSnapshot(
        ReleaseSnapshot baseline,
        ReleaseSnapshot current,
        IEnumerable<string> includedAddedOrModified,
        IEnumerable<string> includedRemoved,
        int version,
        string channelId)
    {
        var entries = new Dictionary<string, ReleaseSnapshotEntry>(baseline.Entries, StringComparer.Ordinal);
        foreach (var path in includedAddedOrModified)
        {
            var key = path.Replace('\\', '/');
            if (current.Entries.TryGetValue(key, out var entry))
            {
                entries[key] = entry;
            }
        }

        foreach (var path in includedRemoved)
        {
            entries.Remove(path.Replace('\\', '/'));
        }

        return new ReleaseSnapshot
        {
            SchemaVersion = 1,
            Version = version,
            ChannelId = channelId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Entries = entries,
        };
    }

    /// <summary>baseline(직전 버전) 대비 current(지금)의 추가/수정/삭제 파일을 계산한다.</summary>
    public static SnapshotDelta ComputeDelta(ReleaseSnapshot baseline, ReleaseSnapshot current)
    {
        var added = new List<string>();
        var modified = new List<string>();
        var removed = new List<string>();

        foreach (var (path, entry) in current.Entries)
        {
            if (!baseline.Entries.TryGetValue(path, out var baselineEntry))
            {
                added.Add(path);
            }
            else if (!string.Equals(baselineEntry.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                modified.Add(path);
            }
        }

        foreach (var path in baseline.Entries.Keys)
        {
            if (!current.Entries.ContainsKey(path))
            {
                removed.Add(path);
            }
        }

        added.Sort(StringComparer.Ordinal);
        modified.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);
        return new SnapshotDelta(added, modified, removed);
    }
}
