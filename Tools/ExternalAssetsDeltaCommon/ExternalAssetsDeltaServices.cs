using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectS.ExternalAssetsDelta;

/// <summary>
/// 기준 인덱스와 Contribution ZIP의 신뢰 경계를 한 곳에서 처리한다.
/// 입력 ExternalAssets는 읽기만 하며, ZIP 추출은 호출자가 제공한 전용 staging 폴더에만 수행한다.
/// </summary>
public static class ExternalAssetsDeltaServices
{
    private const long MaximumMetaBytes = 4L * 1024 * 1024;
    // contribution.json은 변경 파일 하나당 JSON 객체 하나라, 대규모 신규 에셋 팩을
    // 통째로 기여하면(수만 개 Added) 8MB를 넘긴다. 전체 base 규모 기여(약 5만 항목,
    // ~25MB)까지 여유롭게 받도록 64MB로 둔다. 손상/악의적 매니페스트의 메모리 폭주를
    // 막는 상한 역할은 유지한다.
    private const long MaximumManifestBytes = 64L * 1024 * 1024;
    private const int MaximumArchiveEntries = 1_000_000;
    private static readonly Regex GuidPattern = new("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string NormalizeExternalAssetsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Unity 프로젝트 또는 Assets/ExternalAssets 폴더를 선택하세요.");
        }

        var fullPath = Path.GetFullPath(path.Trim());
        var projectExternalAssetsPath = Path.Combine(fullPath, "Assets", "ExternalAssets");
        if (Directory.Exists(projectExternalAssetsPath))
        {
            return Path.TrimEndingDirectorySeparator(projectExternalAssetsPath);
        }

        if (Directory.Exists(fullPath)
            && string.Equals(new DirectoryInfo(fullPath).Name, "ExternalAssets", StringComparison.OrdinalIgnoreCase))
        {
            return Path.TrimEndingDirectorySeparator(fullPath);
        }

        throw new InvalidOperationException("Unity 프로젝트 또는 그 안의 Assets/ExternalAssets 폴더를 선택하세요.");
    }

    public static async Task<SeedIndex> GenerateSeedIndexAsync(
        string externalAssetsPath,
        string? displayName,
        IProgress<DeltaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var snapshot = await ScanExternalAssetsAsync(externalAssetsPath, progress, cancellationToken);
        ThrowIfSnapshotHasErrors(snapshot, "기준 인덱스를 만들 수 없습니다");

        var entries = snapshot.Entries
            .Select(entry => new SeedIndexEntry
            {
                RelativePath = entry.RelativePath,
                EntryType = entry.EntryType,
                SizeBytes = entry.SizeBytes,
                Sha256 = entry.Sha256,
                Guid = entry.Guid,
                MetaTargetKind = entry.MetaTargetKind,
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();

        return new SeedIndex
        {
            BaselineId = Guid.NewGuid().ToString("D"),
            BaselineContentSha256 = CalculateBaselineContentSha256(snapshot.RootGuid, entries),
            ExternalAssetsRootGuid = snapshot.RootGuid,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "ExternalAssets baseline" : displayName.Trim(),
            Entries = entries,
        };
    }

    public static async Task<SeedIndex> LoadSeedIndexAsync(string seedIndexPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(seedIndexPath))
        {
            throw new InvalidOperationException("seed-index.json을 선택하세요.");
        }

        var fullPath = Path.GetFullPath(seedIndexPath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"seed-index.json을 찾을 수 없습니다: {fullPath}");
        }

        await using var stream = File.OpenRead(fullPath);
        var seed = await JsonSerializer.DeserializeAsync<SeedIndex>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("seed-index.json을 읽을 수 없습니다.");
        ValidateSeedIndex(seed);
        return seed;
    }

    public static async Task<ExternalAssetsComparison> CompareLocalExternalAssetsAsync(
        string localExternalAssetsPath,
        SeedIndex seedIndex,
        IProgress<DeltaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateSeedIndex(seedIndex);
        var snapshot = await ScanExternalAssetsAsync(localExternalAssetsPath, progress, cancellationToken);
        var issues = snapshot.Issues.ToList();

        if (!string.Equals(snapshot.RootGuid, seedIndex.ExternalAssetsRootGuid, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new DeltaIssue(
                DeltaIssueSeverity.Error,
                "root-guid-mismatch",
                "Assets/ExternalAssets.meta의 GUID가 기준 인덱스와 다릅니다. 이 프로젝트는 같은 ExternalAssets 루트를 사용하지 않습니다."));
        }

        var seedEntries = ToExactPathDictionary(seedIndex.Entries, "seed-index.json");
        var localEntries = ToExactPathDictionary(snapshot.Entries, "로컬 ExternalAssets", issues);
        var result = new List<DeltaComparisonEntry>(seedEntries.Count + localEntries.Count);

        foreach (var pair in seedEntries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!localEntries.TryGetValue(pair.Key, out var local))
            {
                result.Add(new DeltaComparisonEntry(pair.Key, pair.Value.EntryType, DeltaComparisonKind.Missing, pair.Value, null));
                continue;
            }

            var isSame = IsSameEntry(pair.Value, local);
            result.Add(new DeltaComparisonEntry(
                pair.Key,
                pair.Value.EntryType,
                isSame ? DeltaComparisonKind.Unchanged : DeltaComparisonKind.Modified,
                pair.Value,
                local));

            if (!isSame
                && pair.Value.MetaTargetKind is not null
                && !string.Equals(pair.Value.Guid, local.Guid, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new DeltaIssue(
                    DeltaIssueSeverity.Error,
                    "existing-meta-guid-changed",
                    "기준에 이미 있던 .meta의 GUID가 바뀌었습니다. Git에 기록된 참조를 유지하려면 GUID를 변경하면 안 됩니다.",
                    pair.Key));
            }
        }

        foreach (var pair in localEntries
                     .Where(pair => !seedEntries.ContainsKey(pair.Key))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Add(new DeltaComparisonEntry(pair.Key, pair.Value.EntryType, DeltaComparisonKind.Added, null, pair.Value));
        }

        return new ExternalAssetsComparison
        {
            LocalExternalAssetsPath = snapshot.RootPath,
            SeedIndex = seedIndex,
            LocalExternalAssetsRootGuid = snapshot.RootGuid,
            Entries = result,
            Issues = issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.RelativePath, StringComparer.Ordinal)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ToArray(),
        };
    }

    public static async Task<ContributionPackageBuildResult> CreateContributionPackageAsync(
        ExternalAssetsComparison comparison,
        ContributionBuildOptions options,
        IProgress<DeltaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(options);
        ValidateSeedIndex(comparison.SeedIndex);
        if (comparison.HasErrors)
        {
            throw new InvalidOperationException("비교 결과에 오류가 있어 Contribution ZIP을 만들 수 없습니다.");
        }

        if (string.IsNullOrWhiteSpace(options.ContributorName))
        {
            throw new InvalidOperationException("제출자 이름을 입력하세요.");
        }

        if (options.ContributorName.Trim().Length > 120 || ContainsControlCharacter(options.ContributorName))
        {
            throw new InvalidOperationException("제출자 이름 형식이 올바르지 않습니다.");
        }

        var outputZipPath = ValidateOutputZipPath(options.OutputZipPath);
        var localEntries = ToExactPathDictionary(
            comparison.Entries
                .Where(entry => entry.LocalEntry is not null)
                .Select(entry => entry.LocalEntry!)
                .ToArray(),
            "로컬 ExternalAssets");
        var baselineEntries = ToExactPathDictionary(comparison.SeedIndex.Entries, "seed-index.json");
        var selectedPaths = new HashSet<string>(StringComparer.Ordinal);
        var directories = new Dictionary<string, ContributionDirectoryEntry>(StringComparer.Ordinal);

        foreach (var entry in comparison.Entries.Where(entry => entry.ChangeKind is DeltaComparisonKind.Added or DeltaComparisonKind.Modified))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.LocalEntry is null)
            {
                throw new InvalidOperationException($"{entry.RelativePath}: 로컬 파일 정보를 읽을 수 없습니다.");
            }

            if (entry.LocalEntry.EntryType == SeedEntryType.Directory)
            {
                if (entry.ChangeKind == DeltaComparisonKind.Added)
                {
                    directories.Add(entry.RelativePath, new ContributionDirectoryEntry
                    {
                        RelativePath = entry.RelativePath,
                        ChangeKind = ContributionChangeKind.Added,
                    });
                }

                continue;
            }

            AddPayloadPath(entry.RelativePath, selectedPaths, localEntries, baselineEntries, entry.ChangeKind);
            if (!entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                AddPayloadPath(entry.RelativePath + ".meta", selectedPaths, localEntries, baselineEntries, null);
                continue;
            }

            if (entry.LocalEntry.MetaTargetKind == ContributionMetaTargetKind.Asset)
            {
                AddPayloadPath(entry.RelativePath[..^".meta".Length], selectedPaths, localEntries, baselineEntries, null);
            }
            else if (entry.LocalEntry.MetaTargetKind == ContributionMetaTargetKind.Folder
                     && entry.ChangeKind == DeltaComparisonKind.Added)
            {
                var directoryPath = entry.RelativePath[..^".meta".Length];
                directories.TryAdd(directoryPath, new ContributionDirectoryEntry
                {
                    RelativePath = directoryPath,
                    ChangeKind = ContributionChangeKind.Added,
                });
            }
        }

        if (selectedPaths.Count == 0 && directories.Count == 0)
        {
            throw new InvalidOperationException("제출할 Added 또는 Modified 파일이 없습니다.");
        }

        var payloadEntries = selectedPaths
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => CreateContributionPayloadEntry(path, localEntries, baselineEntries))
            .ToList();
        ValidateContributionShape(payloadEntries, directories.Values);

        var manifest = new ContributionManifest
        {
            ContributionId = Guid.NewGuid().ToString("D"),
            BaselineId = comparison.SeedIndex.BaselineId,
            BaselineContentSha256 = comparison.SeedIndex.BaselineContentSha256,
            ExternalAssetsRootGuid = comparison.LocalExternalAssetsRootGuid,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ContributorName = options.ContributorName.Trim(),
            Note = string.IsNullOrWhiteSpace(options.Note) ? null : options.Note.Trim(),
            Directories = directories.Values.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList(),
            Entries = payloadEntries,
        };

        var temporaryPath = outputZipPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
            progress?.Report(new DeltaProgress("Contribution ZIP을 준비하는 중", 0, payloadEntries.Count));
            using (var archive = ZipFile.Open(temporaryPath, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry(ExternalAssetsDeltaFormat.ContributionManifestEntryName, CompressionLevel.Optimal);
                await using (var manifestStream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }

                for (var index = 0; index < payloadEntries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var payload = payloadEntries[index];
                    var local = localEntries[payload.RelativePath];
                    EnsureLocalEntryUnchanged(local);
                    var archiveEntry = archive.CreateEntry(payload.PayloadPath, CompressionLevel.Optimal);
                    await using var source = File.OpenRead(local.FullPath!);
                    await using var destination = archiveEntry.Open();
                    await source.CopyToAsync(destination, 1024 * 128, cancellationToken);
                    EnsureLocalEntryUnchanged(local);
                    if (index % 100 == 0 || index == payloadEntries.Count - 1)
                    {
                        progress?.Report(new DeltaProgress($"Contribution 압축 중: {payload.RelativePath}", index + 1, payloadEntries.Count));
                    }
                }
            }

            var loaded = await LoadContributionPackageAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, outputZipPath);
            var info = new FileInfo(outputZipPath);
            progress?.Report(new DeltaProgress("Contribution ZIP 검증 완료", payloadEntries.Count, payloadEntries.Count));
            return new ContributionPackageBuildResult(outputZipPath, loaded.ArchiveSha256, info.Length, loaded.Manifest);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<LoadedContributionPackage> LoadContributionPackageAsync(
        string zipPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new InvalidOperationException("Contribution ZIP을 선택하세요.");
        }

        var fullPath = Path.GetFullPath(zipPath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Contribution ZIP을 찾을 수 없습니다: {fullPath}");
        }

        var archiveSha256 = await ComputeFileSha256Async(fullPath, cancellationToken);
        using var archive = ZipFile.OpenRead(fullPath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidOperationException("Contribution ZIP의 항목 수가 올바르지 않습니다.");
        }

        var exactEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var canonicalEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Contribution ZIP에는 명시적인 폴더 항목을 넣을 수 없습니다. 폴더는 contribution.json의 directories로 선언하세요.");
            }

            if (!exactEntries.TryAdd(entry.FullName, entry)
                || !canonicalEntries.TryAdd(entry.FullName, entry.FullName))
            {
                throw new InvalidOperationException($"Contribution ZIP 안에 중복 또는 대소문자 충돌 경로가 있습니다: {entry.FullName}");
            }
        }

        if (!exactEntries.TryGetValue(ExternalAssetsDeltaFormat.ContributionManifestEntryName, out var manifestArchiveEntry)
            || manifestArchiveEntry.Length > MaximumManifestBytes)
        {
            throw new InvalidOperationException("Contribution ZIP에 contribution.json이 없거나 너무 큽니다.");
        }

        ContributionManifest manifest;
        await using (var stream = manifestArchiveEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<ContributionManifest>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Contribution ZIP의 contribution.json을 읽을 수 없습니다.");
        }

        ValidateContributionManifestHeader(manifest);
        ValidateContributionShape(manifest.Entries, manifest.Directories);
        var descriptorPaths = new HashSet<string>(StringComparer.Ordinal);
        var descriptors = new List<ContributionPayloadDescriptor>(manifest.Entries.Count);
        foreach (var payload in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!descriptorPaths.Add(payload.PayloadPath)
                || !exactEntries.TryGetValue(payload.PayloadPath, out var archiveEntry))
            {
                throw new InvalidOperationException($"Contribution ZIP의 선언된 payload가 없거나 중복됩니다: {payload.PayloadPath}");
            }

            if (archiveEntry.Length != payload.SizeBytes)
            {
                throw new InvalidOperationException($"Contribution ZIP payload 크기가 선언과 다릅니다: {payload.RelativePath}");
            }

            var hash = await ComputeArchiveEntrySha256Async(archiveEntry, cancellationToken);
            if (!string.Equals(hash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Contribution ZIP payload SHA-256이 선언과 다릅니다: {payload.RelativePath}");
            }

            if (payload.MetaTargetKind is not null)
            {
                var guid = await ReadMetaGuidAsync(archiveEntry, cancellationToken);
                if (!string.Equals(guid, payload.Guid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Contribution ZIP .meta GUID가 선언과 다릅니다: {payload.RelativePath}");
                }
            }

            descriptors.Add(new ContributionPayloadDescriptor
            {
                ManifestEntry = payload,
                ArchiveEntryName = archiveEntry.FullName,
            });
        }

        var allowedEntries = new HashSet<string>(descriptorPaths, StringComparer.Ordinal)
        {
            ExternalAssetsDeltaFormat.ContributionManifestEntryName,
        };
        var unexpected = exactEntries.Keys.Where(path => !allowedEntries.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault();
        if (unexpected is not null)
        {
            throw new InvalidOperationException($"Contribution ZIP에 선언되지 않은 파일이 있습니다: {unexpected}");
        }

        var recheckedArchiveSha256 = await ComputeFileSha256Async(fullPath, cancellationToken);
        if (!string.Equals(archiveSha256, recheckedArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("검증 중 Contribution ZIP 내용이 바뀌었습니다. 다시 시도하세요.");
        }

        return new LoadedContributionPackage
        {
            ZipPath = fullPath,
            ArchiveSha256 = archiveSha256,
            SizeBytes = new FileInfo(fullPath).Length,
            Manifest = manifest,
            PayloadEntries = descriptors,
        };
    }

    public static async Task<ContributionBaselineValidation> ValidateContributionAgainstBaselineAsync(
        LoadedContributionPackage package,
        string baselineExternalAssetsPath,
        SeedIndex seedIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateSeedIndex(seedIndex);
        await EnsureContributionPackageUnchangedAsync(package, cancellationToken);
        var reloadedPackage = await LoadContributionPackageAsync(package.ZipPath, cancellationToken);
        if (!string.Equals(reloadedPackage.ArchiveSha256, package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("검증한 뒤 Contribution ZIP 내용이 바뀌었습니다. 다시 추가하세요.");
        }

        package = reloadedPackage;

        var issues = new List<DeltaIssue>();
        if (!string.Equals(package.Manifest.BaselineId, seedIndex.BaselineId, StringComparison.Ordinal)
            || !string.Equals(package.Manifest.BaselineContentSha256, seedIndex.BaselineContentSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(package.Manifest.ExternalAssetsRootGuid, seedIndex.ExternalAssetsRootGuid, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new DeltaIssue(
                DeltaIssueSeverity.Error,
                "stale-or-foreign-contribution",
                "Contribution ZIP이 현재 seed-index.json과 다른 기준본에서 만들어졌습니다."));
            return new ContributionBaselineValidation(false, issues);
        }

        var baselineComparison = await CompareLocalExternalAssetsAsync(baselineExternalAssetsPath, seedIndex, progress: null, cancellationToken);
        if (!baselineComparison.IsExactMatch)
        {
            issues.Add(new DeltaIssue(
                DeltaIssueSeverity.Error,
                "baseline-does-not-match-seed",
                "현재 기준 ExternalAssets가 seed-index.json과 일치하지 않습니다."));
            return new ContributionBaselineValidation(false, issues);
        }

        var baselineEntries = ToExactPathDictionary(seedIndex.Entries, "seed-index.json");
        var baselineGuids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [seedIndex.ExternalAssetsRootGuid] = "<Assets/ExternalAssets.meta>",
        };
        foreach (var meta in seedIndex.Entries.Where(entry => entry.MetaTargetKind is not null))
        {
            baselineGuids[meta.Guid!] = meta.RelativePath;
        }

        foreach (var entry in package.Manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            baselineEntries.TryGetValue(entry.RelativePath, out var baseline);
            switch (entry.ChangeKind)
            {
                case ContributionChangeKind.Added when baseline is not null:
                    issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "added-path-already-in-baseline", "Added 항목이 이미 기준본에 있습니다.", entry.RelativePath));
                    break;
                case ContributionChangeKind.Added:
                    if (entry.MetaTargetKind is not null && baselineGuids.TryGetValue(entry.Guid!, out var existingPath))
                    {
                        issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "added-guid-collides-with-baseline", $"새 .meta GUID가 기준본과 중복됩니다: {existingPath}", entry.RelativePath));
                    }

                    break;
                case ContributionChangeKind.Modified:
                    ValidateExistingContributionEntry(entry, baseline, requireSamePayload: false, issues);
                    break;
                case ContributionChangeKind.Support:
                    ValidateExistingContributionEntry(entry, baseline, requireSamePayload: true, issues);
                    break;
                default:
                    issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "invalid-change-kind", "지원하지 않는 Contribution changeKind입니다.", entry.RelativePath));
                    break;
            }
        }

        foreach (var directory in package.Manifest.Directories)
        {
            if (baselineEntries.ContainsKey(directory.RelativePath))
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "added-directory-already-in-baseline", "새 폴더가 이미 기준본에 있습니다.", directory.RelativePath));
            }
        }

        return new ContributionBaselineValidation(
            !issues.Any(issue => issue.Severity == DeltaIssueSeverity.Error),
            issues);
    }

    public static async Task<ContributionExtractionResult> ExtractContributionPayloadAsync(
        LoadedContributionPackage package,
        string destinationExternalAssetsPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(destinationExternalAssetsPath))
        {
            throw new InvalidOperationException("Contribution payload를 풀 전용 staging 폴더를 지정하세요.");
        }

        await EnsureContributionPackageUnchangedAsync(package, cancellationToken);
        var reloadedPackage = await LoadContributionPackageAsync(package.ZipPath, cancellationToken);
        if (!string.Equals(reloadedPackage.ArchiveSha256, package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("검증한 뒤 Contribution ZIP 내용이 바뀌었습니다. 다시 추가하세요.");
        }

        package = reloadedPackage;
        var destinationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationExternalAssetsPath));
        EnsureNoReparsePointInExistingPath(destinationRoot);
        Directory.CreateDirectory(destinationRoot);
        EnsureNoReparsePointInExistingPath(destinationRoot);

        using var archive = ZipFile.OpenRead(package.ZipPath);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);
        var writtenPaths = new List<string>();
        try
        {
            foreach (var directory in package.Manifest.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(GetSafeDestinationPath(destinationRoot, directory.RelativePath));
            }

            foreach (var descriptor in package.PayloadEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entries.TryGetValue(descriptor.ArchiveEntryName, out var archiveEntry))
                {
                    throw new InvalidOperationException($"Contribution ZIP payload를 다시 찾을 수 없습니다: {descriptor.ManifestEntry.RelativePath}");
                }

                var entry = descriptor.ManifestEntry;
                if (entry.MetaTargetKind == ContributionMetaTargetKind.Folder)
                {
                    Directory.CreateDirectory(GetSafeDestinationPath(destinationRoot, entry.RelativePath[..^".meta".Length]));
                }

                var destinationPath = GetSafeDestinationPath(destinationRoot, entry.RelativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException("Contribution payload 출력 경로를 확인할 수 없습니다.");
                Directory.CreateDirectory(destinationDirectory);
                EnsureNoReparsePointInExistingPath(destinationDirectory);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    throw new InvalidOperationException($"Contribution staging에 이미 같은 경로가 있습니다: {entry.RelativePath}");
                }

                var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    await using (var source = archiveEntry.Open())
                    await using (var destination = File.Create(temporaryPath))
                    {
                        await source.CopyToAsync(destination, 1024 * 128, cancellationToken);
                    }

                    var hash = await ComputeFileSha256Async(temporaryPath, cancellationToken);
                    if (new FileInfo(temporaryPath).Length != entry.SizeBytes
                        || !string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Contribution payload 무결성 검증에 실패했습니다: {entry.RelativePath}");
                    }

                    if (entry.MetaTargetKind is not null)
                    {
                        var guid = await ReadMetaGuidAsync(temporaryPath, cancellationToken);
                        if (!string.Equals(guid, entry.Guid, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Contribution .meta GUID 검증에 실패했습니다: {entry.RelativePath}");
                        }
                    }

                    File.Move(temporaryPath, destinationPath);
                    writtenPaths.Add(destinationPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }

            return new ContributionExtractionResult(destinationRoot, package.PayloadEntries.Count, package.Manifest.Directories.Count);
        }
        catch
        {
            foreach (var writtenPath in writtenPaths.AsEnumerable().Reverse())
            {
                if (File.Exists(writtenPath))
                {
                    File.Delete(writtenPath);
                }
            }

            throw;
        }
    }

    private static void ValidateExistingContributionEntry(
        ContributionPayloadEntry contribution,
        SeedIndexEntry? baseline,
        bool requireSamePayload,
        ICollection<DeltaIssue> issues)
    {
        if (baseline is null)
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "existing-path-not-in-baseline", "Modified 또는 Support 항목이 기준본에 없습니다.", contribution.RelativePath));
            return;
        }

        if (baseline.EntryType != SeedEntryType.File
            || !string.Equals(baseline.Sha256, contribution.BaselineSha256, StringComparison.OrdinalIgnoreCase)
            || baseline.SizeBytes != contribution.BaselineSizeBytes)
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "baseline-precondition-mismatch", "Contribution의 기준 SHA-256/크기 조건이 현재 기준본과 다릅니다.", contribution.RelativePath));
        }

        var payloadMatchesBaseline = string.Equals(contribution.Sha256, baseline.Sha256, StringComparison.OrdinalIgnoreCase)
            && contribution.SizeBytes == baseline.SizeBytes;
        if (requireSamePayload && !payloadMatchesBaseline)
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "support-payload-changed", "Support 항목은 기준본과 바이트가 완전히 같아야 합니다.", contribution.RelativePath));
        }

        if (!requireSamePayload && payloadMatchesBaseline)
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "modified-payload-unchanged", "Modified 항목의 바이트가 기준본과 같습니다. Support로 기록되어야 합니다.", contribution.RelativePath));
        }

        if (baseline.MetaTargetKind is not null
            && !string.Equals(baseline.Guid, contribution.Guid, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "existing-meta-guid-changed", "기존 .meta의 GUID는 변경할 수 없습니다.", contribution.RelativePath));
        }
    }

    private static void AddPayloadPath(
        string relativePath,
        ISet<string> selectedPaths,
        IReadOnlyDictionary<string, LocalExternalAssetsEntry> localEntries,
        IReadOnlyDictionary<string, SeedIndexEntry> baselineEntries,
        DeltaComparisonKind? requiredChangeKind)
    {
        if (!localEntries.TryGetValue(relativePath, out var local) || local.EntryType != SeedEntryType.File)
        {
            throw new InvalidOperationException($"{relativePath}: Contribution에 넣을 실제 파일 또는 .meta가 없습니다.");
        }

        if (requiredChangeKind is DeltaComparisonKind.Added && baselineEntries.ContainsKey(relativePath))
        {
            throw new InvalidOperationException($"{relativePath}: 기준에 이미 있는 파일을 Added로 기록할 수 없습니다.");
        }

        selectedPaths.Add(relativePath);
    }

    private static ContributionPayloadEntry CreateContributionPayloadEntry(
        string relativePath,
        IReadOnlyDictionary<string, LocalExternalAssetsEntry> localEntries,
        IReadOnlyDictionary<string, SeedIndexEntry> baselineEntries)
    {
        var local = localEntries[relativePath];
        if (local.EntryType != SeedEntryType.File
            || string.IsNullOrWhiteSpace(local.FullPath)
            || local.SizeBytes is null
            || string.IsNullOrWhiteSpace(local.Sha256))
        {
            throw new InvalidOperationException($"{relativePath}: Contribution payload 파일 정보가 올바르지 않습니다.");
        }

        baselineEntries.TryGetValue(relativePath, out var baseline);
        var changeKind = baseline is null
            ? ContributionChangeKind.Added
            : IsSameEntry(baseline, local)
                ? ContributionChangeKind.Support
                : ContributionChangeKind.Modified;
        return new ContributionPayloadEntry
        {
            RelativePath = relativePath,
            PayloadPath = ExternalAssetsDeltaFormat.PayloadPrefix + relativePath,
            ChangeKind = changeKind,
            SizeBytes = local.SizeBytes.Value,
            Sha256 = local.Sha256,
            BaselineSha256 = baseline?.Sha256,
            BaselineSizeBytes = baseline?.SizeBytes,
            Guid = local.Guid,
            MetaTargetKind = local.MetaTargetKind,
        };
    }

    private static void ValidateSeedIndex(SeedIndex seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        if (seed.SchemaVersion != ExternalAssetsDeltaFormat.SchemaVersion
            || !string.Equals(seed.Kind, ExternalAssetsDeltaFormat.SeedIndexKind, StringComparison.Ordinal)
            || !Guid.TryParse(seed.BaselineId, out _)
            || !IsSha256(seed.BaselineContentSha256)
            || !IsUnityGuid(seed.ExternalAssetsRootGuid))
        {
            throw new InvalidOperationException("seed-index.json의 헤더 형식이 올바르지 않습니다.");
        }

        if (seed.Entries is null)
        {
            throw new InvalidOperationException("seed-index.json의 entries가 없습니다.");
        }

        ValidateSeedShape(seed.Entries);
        EnsureNoDuplicateGuids(
            seed.Entries
                .Where(entry => entry.MetaTargetKind is not null)
                .Select(entry => (entry.Guid!, entry.RelativePath))
                .Prepend((seed.ExternalAssetsRootGuid, "<Assets/ExternalAssets.meta>")),
            "seed-index.json");
        var calculated = CalculateBaselineContentSha256(seed.ExternalAssetsRootGuid, seed.Entries);
        if (!string.Equals(calculated, seed.BaselineContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("seed-index.json의 기준 내용 SHA-256이 일치하지 않습니다.");
        }
    }

    private static void ValidateSeedShape(IReadOnlyList<SeedIndexEntry> entries)
    {
        var exact = new Dictionary<string, SeedIndexEntry>(StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            ValidateRelativePath(entry.RelativePath, "seed-index.json 경로");
            if (!Enum.IsDefined(entry.EntryType)
                || !exact.TryAdd(entry.RelativePath, entry)
                || !caseInsensitive.TryAdd(entry.RelativePath, entry.RelativePath))
            {
                throw new InvalidOperationException($"seed-index.json에 중복 또는 대소문자 충돌 경로가 있습니다: {entry.RelativePath}");
            }

            if (entry.EntryType == SeedEntryType.Directory)
            {
                if (entry.SizeBytes is not null || entry.Sha256 is not null || entry.Guid is not null || entry.MetaTargetKind is not null)
                {
                    throw new InvalidOperationException($"seed-index.json의 폴더 항목 형식이 올바르지 않습니다: {entry.RelativePath}");
                }

                continue;
            }

            if (entry.SizeBytes is null || entry.SizeBytes < 0 || !IsSha256(entry.Sha256))
            {
                throw new InvalidOperationException($"seed-index.json 파일 항목의 SHA-256 또는 크기가 올바르지 않습니다: {entry.RelativePath}");
            }

            var isMeta = entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
            if (isMeta != (entry.MetaTargetKind is not null))
            {
                throw new InvalidOperationException($"seed-index.json의 .meta 구분 정보가 올바르지 않습니다: {entry.RelativePath}");
            }

            if (entry.MetaTargetKind is not null && !IsUnityGuid(entry.Guid))
            {
                throw new InvalidOperationException($"seed-index.json의 .meta GUID가 올바르지 않습니다: {entry.RelativePath}");
            }

            if (entry.MetaTargetKind is null && entry.Guid is not null)
            {
                throw new InvalidOperationException($"seed-index.json의 일반 파일에 GUID를 넣을 수 없습니다: {entry.RelativePath}");
            }
        }

        ValidateEntryTree(exact, Enumerable.Empty<ContributionDirectoryEntry>());
        EnsureNoDuplicateGuids(entries.Where(entry => entry.MetaTargetKind is not null).Select(entry => (entry.Guid!, entry.RelativePath)), "seed-index.json");
    }

    private static void ValidateContributionManifestHeader(ContributionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ExternalAssetsDeltaFormat.SchemaVersion
            || !string.Equals(manifest.Kind, ExternalAssetsDeltaFormat.ContributionKind, StringComparison.Ordinal)
            || !Guid.TryParse(manifest.ContributionId, out _)
            || !Guid.TryParse(manifest.BaselineId, out _)
            || !IsSha256(manifest.BaselineContentSha256)
            || !IsUnityGuid(manifest.ExternalAssetsRootGuid)
            || string.IsNullOrWhiteSpace(manifest.ContributorName)
            || manifest.ContributorName.Trim().Length > 120
            || ContainsControlCharacter(manifest.ContributorName))
        {
            throw new InvalidOperationException("Contribution ZIP의 contribution.json 헤더 형식이 올바르지 않습니다.");
        }

        if (manifest.Note is { Length: > 2000 } || (manifest.Note is not null && ContainsControlCharacter(manifest.Note)))
        {
            throw new InvalidOperationException("Contribution ZIP의 메모 형식이 올바르지 않습니다.");
        }
    }

    private static void ValidateContributionShape(
        IEnumerable<ContributionPayloadEntry> payloadEnumerable,
        IEnumerable<ContributionDirectoryEntry> directoryEnumerable)
    {
        var payloads = payloadEnumerable?.ToArray()
            ?? throw new InvalidOperationException("Contribution ZIP의 entries가 없습니다.");
        var directories = directoryEnumerable?.ToArray()
            ?? throw new InvalidOperationException("Contribution ZIP의 directories가 없습니다.");
        if (payloads.Length == 0 && directories.Length == 0)
        {
            throw new InvalidOperationException("Contribution ZIP에는 변경 파일 또는 새 폴더가 하나 이상 있어야 합니다.");
        }

        var exact = new Dictionary<string, ContributionPayloadEntry>(StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads)
        {
            ValidateRelativePath(payload.RelativePath, "Contribution payload 경로");
            ValidatePayloadPath(payload.PayloadPath, payload.RelativePath);
            if (!Enum.IsDefined(payload.ChangeKind)
                || payload.SizeBytes < 0
                || !IsSha256(payload.Sha256)
                || !exact.TryAdd(payload.RelativePath, payload)
                || !caseInsensitive.TryAdd(payload.RelativePath, payload.RelativePath))
            {
                throw new InvalidOperationException($"Contribution ZIP에 중복 또는 잘못된 payload가 있습니다: {payload.RelativePath}");
            }

            var isMeta = payload.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
            if (isMeta != (payload.MetaTargetKind is not null)
                || (payload.MetaTargetKind is not null && !IsUnityGuid(payload.Guid))
                || (payload.MetaTargetKind is null && payload.Guid is not null))
            {
                throw new InvalidOperationException($"Contribution ZIP의 .meta 정보가 올바르지 않습니다: {payload.RelativePath}");
            }

            if (payload.ChangeKind == ContributionChangeKind.Added)
            {
                if (payload.BaselineSha256 is not null || payload.BaselineSizeBytes is not null)
                {
                    throw new InvalidOperationException($"Added 항목에는 기준 SHA-256을 넣을 수 없습니다: {payload.RelativePath}");
                }
            }
            else if (!IsSha256(payload.BaselineSha256) || payload.BaselineSizeBytes is null || payload.BaselineSizeBytes < 0)
            {
                throw new InvalidOperationException($"Modified 또는 Support 항목에는 기준 SHA-256과 크기가 필요합니다: {payload.RelativePath}");
            }
        }

        var directoryExact = new Dictionary<string, ContributionDirectoryEntry>(StringComparer.Ordinal);
        var directoryCaseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        {
            ValidateRelativePath(directory.RelativePath, "Contribution 디렉터리 경로");
            if (directory.ChangeKind != ContributionChangeKind.Added
                || !directoryExact.TryAdd(directory.RelativePath, directory)
                || !directoryCaseInsensitive.TryAdd(directory.RelativePath, directory.RelativePath)
                || exact.ContainsKey(directory.RelativePath))
            {
                throw new InvalidOperationException($"Contribution ZIP의 새 폴더 선언이 올바르지 않습니다: {directory.RelativePath}");
            }
        }

        ValidateEntryTree(
            exact.ToDictionary(pair => pair.Key, pair => new SeedIndexEntry
            {
                RelativePath = pair.Value.RelativePath,
                EntryType = SeedEntryType.File,
                SizeBytes = pair.Value.SizeBytes,
                Sha256 = pair.Value.Sha256,
                Guid = pair.Value.Guid,
                MetaTargetKind = pair.Value.MetaTargetKind,
            }, StringComparer.Ordinal),
            directories);

        foreach (var payload in payloads.Where(payload => !payload.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            if (!exact.TryGetValue(payload.RelativePath + ".meta", out var meta)
                || meta.MetaTargetKind != ContributionMetaTargetKind.Asset)
            {
                throw new InvalidOperationException($"Contribution ZIP의 파일에는 같은 ZIP 안의 .meta가 필요합니다: {payload.RelativePath}");
            }
        }

        foreach (var meta in payloads.Where(payload => payload.MetaTargetKind == ContributionMetaTargetKind.Asset))
        {
            var targetPath = meta.RelativePath[..^".meta".Length];
            if (!exact.ContainsKey(targetPath))
            {
                throw new InvalidOperationException($"Contribution ZIP의 자산 .meta에는 실제 파일이 필요합니다: {meta.RelativePath}");
            }
        }

        foreach (var directory in directories)
        {
            if (!exact.TryGetValue(directory.RelativePath + ".meta", out var meta)
                || meta.MetaTargetKind != ContributionMetaTargetKind.Folder
                || meta.ChangeKind != ContributionChangeKind.Added)
            {
                throw new InvalidOperationException($"새 폴더에는 Added 폴더 .meta가 필요합니다: {directory.RelativePath}");
            }
        }

        foreach (var meta in payloads.Where(payload => payload.MetaTargetKind == ContributionMetaTargetKind.Folder && payload.ChangeKind == ContributionChangeKind.Added))
        {
            var targetPath = meta.RelativePath[..^".meta".Length];
            if (!directoryExact.ContainsKey(targetPath))
            {
                throw new InvalidOperationException($"Added 폴더 .meta에는 새 폴더 선언이 필요합니다: {meta.RelativePath}");
            }
        }

        EnsureNoDuplicateGuids(payloads.Where(payload => payload.MetaTargetKind is not null).Select(payload => (payload.Guid!, payload.RelativePath)), "Contribution ZIP");
    }

    private static async Task<LocalSnapshot> ScanExternalAssetsAsync(
        string externalAssetsPath,
        IProgress<DeltaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rootPath = NormalizeExternalAssetsPath(externalAssetsPath);
        EnsureNoReparsePointInExistingPath(rootPath);
        var issues = new List<DeltaIssue>();
        var rootMetaPath = rootPath + ".meta";
        var rootGuid = string.Empty;
        try
        {
            rootGuid = await ReadMetaGuidAsync(rootMetaPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "root-meta-invalid", "Assets/ExternalAssets.meta의 GUID를 읽을 수 없습니다: " + exception.Message));
        }

        var directoryPaths = new List<string>();
        var filePaths = new List<string>();
        var queue = new Stack<string>();
        queue.Push(rootPath);
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Pop();
            try
            {
                foreach (var childDirectory in Directory.EnumerateDirectories(current))
                {
                    if (IsReparsePoint(childDirectory))
                    {
                        issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "reparse-point", "심볼릭 링크 또는 junction은 ExternalAssets에 사용할 수 없습니다.", GetRelativePath(rootPath, childDirectory)));
                        continue;
                    }

                    directoryPaths.Add(childDirectory);
                    queue.Push(childDirectory);
                }

                foreach (var childFile in Directory.EnumerateFiles(current))
                {
                    if (IsReparsePoint(childFile))
                    {
                        issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "reparse-point", "심볼릭 링크 또는 junction 파일은 ExternalAssets에 사용할 수 없습니다.", GetRelativePath(rootPath, childFile)));
                        continue;
                    }

                    filePaths.Add(childFile);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "enumeration-failed", exception.Message, GetRelativePath(rootPath, current)));
            }
        }

        var entries = new List<LocalExternalAssetsEntry>(directoryPaths.Count + filePaths.Count);
        foreach (var directoryPath in directoryPaths)
        {
            entries.Add(new LocalExternalAssetsEntry
            {
                RelativePath = GetRelativePath(rootPath, directoryPath),
                EntryType = SeedEntryType.Directory,
                FullPath = directoryPath,
                LastWriteTimeUtc = Directory.GetLastWriteTimeUtc(directoryPath),
            });
        }

        progress?.Report(new DeltaProgress("ExternalAssets 파일 SHA-256을 계산하는 중", 0, filePaths.Count));
        for (var index = 0; index < filePaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = filePaths[index];
            var relativePath = GetRelativePath(rootPath, filePath);
            var fileInfo = new FileInfo(filePath);
            try
            {
                var sha256 = await ComputeFileSha256Async(filePath, cancellationToken);
                ContributionMetaTargetKind? targetKind = null;
                string? guid = null;
                if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    var targetPath = filePath[..^".meta".Length];
                    if (Directory.Exists(targetPath))
                    {
                        targetKind = ContributionMetaTargetKind.Folder;
                    }
                    else if (File.Exists(targetPath))
                    {
                        targetKind = ContributionMetaTargetKind.Asset;
                    }
                    else
                    {
                        issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "orphan-meta", "대응 파일 또는 폴더가 없는 고아 .meta입니다.", relativePath));
                    }

                    try
                    {
                        guid = await ReadMetaGuidAsync(filePath, cancellationToken);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "invalid-meta-guid", "Unity GUID를 읽을 수 없거나 형식이 올바르지 않습니다: " + exception.Message, relativePath));
                    }
                }
                else if (!File.Exists(filePath + ".meta"))
                {
                    issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "missing-file-meta", "대응하는 .meta 파일이 없습니다.", relativePath));
                }

                entries.Add(new LocalExternalAssetsEntry
                {
                    RelativePath = relativePath,
                    EntryType = SeedEntryType.File,
                    FullPath = filePath,
                    SizeBytes = fileInfo.Length,
                    Sha256 = sha256,
                    Guid = guid,
                    MetaTargetKind = targetKind,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "hash-failed", exception.Message, relativePath));
            }

            if (index % 100 == 0 || index == filePaths.Count - 1)
            {
                progress?.Report(new DeltaProgress($"ExternalAssets 해시 계산 중: {relativePath}", index + 1, filePaths.Count));
            }
        }

        var exactEntries = new Dictionary<string, LocalExternalAssetsEntry>(StringComparer.Ordinal);
        var caseInsensitiveEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            try
            {
                ValidateRelativePath(entry.RelativePath, "ExternalAssets 경로");
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "invalid-relative-path", exception.Message, entry.RelativePath));
            }

            if (!exactEntries.TryAdd(entry.RelativePath, entry)
                || !caseInsensitiveEntries.TryAdd(entry.RelativePath, entry.RelativePath))
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "duplicate-or-case-path", "중복 또는 대소문자만 다른 경로가 있습니다.", entry.RelativePath));
            }
        }

        foreach (var directory in entries.Where(entry => entry.EntryType == SeedEntryType.Directory))
        {
            if (!File.Exists(directory.FullPath + ".meta"))
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "missing-folder-meta", "폴더 .meta 파일이 없습니다.", directory.RelativePath));
            }

            if (exactEntries.TryGetValue(directory.RelativePath, out var fileAtDirectoryPath)
                && fileAtDirectoryPath.EntryType == SeedEntryType.File)
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "file-directory-collision", "파일과 폴더가 같은 경로를 사용합니다.", directory.RelativePath));
            }
        }

        foreach (var meta in entries.Where(entry => entry.MetaTargetKind is not null))
        {
            var targetRelativePath = meta.RelativePath[..^".meta".Length];
            if (!exactEntries.TryGetValue(targetRelativePath, out var target))
            {
                continue;
            }

            var expectedTargetKind = target.EntryType == SeedEntryType.Directory
                ? ContributionMetaTargetKind.Folder
                : ContributionMetaTargetKind.Asset;
            if (meta.MetaTargetKind != expectedTargetKind)
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "meta-target-kind-mismatch", " .meta의 대상 파일/폴더 구분이 올바르지 않습니다.", meta.RelativePath));
            }
        }

        var guidPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (IsUnityGuid(rootGuid))
        {
            guidPaths.Add(rootGuid, "<Assets/ExternalAssets.meta>");
        }

        foreach (var meta in entries.Where(entry => entry.MetaTargetKind is not null && IsUnityGuid(entry.Guid)))
        {
            if (!guidPaths.TryAdd(meta.Guid!, meta.RelativePath))
            {
                issues.Add(new DeltaIssue(DeltaIssueSeverity.Error, "duplicate-guid", $"Unity GUID가 다른 경로와 중복됩니다: {guidPaths[meta.Guid!]}", meta.RelativePath));
            }
        }

        return new LocalSnapshot(
            rootPath,
            rootGuid,
            entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToArray(),
            issues);
    }

    private static void ValidateEntryTree(
        IReadOnlyDictionary<string, SeedIndexEntry> entries,
        IEnumerable<ContributionDirectoryEntry> declaredDirectories)
    {
        var explicitDirectories = new HashSet<string>(
            entries.Values.Where(entry => entry.EntryType == SeedEntryType.Directory).Select(entry => entry.RelativePath),
            StringComparer.Ordinal);
        foreach (var directory in declaredDirectories)
        {
            explicitDirectories.Add(directory.RelativePath);
        }

        foreach (var entry in entries.Values.Where(entry => entry.EntryType == SeedEntryType.File))
        {
            if (explicitDirectories.Contains(entry.RelativePath))
            {
                throw new InvalidOperationException($"파일과 폴더가 같은 경로를 사용합니다: {entry.RelativePath}");
            }

            if (!entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (!entries.TryGetValue(entry.RelativePath + ".meta", out var meta)
                    || meta.MetaTargetKind != ContributionMetaTargetKind.Asset)
                {
                    throw new InvalidOperationException($"파일에 대응하는 .meta가 없습니다: {entry.RelativePath}");
                }

                continue;
            }

            if (entry.MetaTargetKind == ContributionMetaTargetKind.Asset)
            {
                var target = entry.RelativePath[..^".meta".Length];
                if (!entries.TryGetValue(target, out var targetEntry) || targetEntry.EntryType != SeedEntryType.File)
                {
                    throw new InvalidOperationException($"자산 .meta에 대응하는 파일이 없습니다: {entry.RelativePath}");
                }
            }
        }

        // A folder .meta in a sparse Contribution can point to an existing baseline folder.
        // Added folder metas are checked by ValidateContributionShape above; a full seed has
        // explicit directory entries and is therefore also covered here.
        foreach (var directory in explicitDirectories)
        {
            if (entries.Values.Any(entry => entry.EntryType == SeedEntryType.Directory && entry.RelativePath == directory)
                && (!entries.TryGetValue(directory + ".meta", out var meta)
                    || meta.MetaTargetKind != ContributionMetaTargetKind.Folder))
            {
                throw new InvalidOperationException($"폴더 .meta가 없습니다: {directory}");
            }
        }
    }

    private static Dictionary<string, SeedIndexEntry> ToExactPathDictionary(
        IEnumerable<SeedIndexEntry> entries,
        string sourceName)
    {
        var result = new Dictionary<string, SeedIndexEntry>(StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!result.TryAdd(entry.RelativePath, entry)
                || !caseInsensitive.TryAdd(entry.RelativePath, entry.RelativePath))
            {
                throw new InvalidOperationException($"{sourceName}에 중복 또는 대소문자 충돌 경로가 있습니다: {entry.RelativePath}");
            }
        }

        return result;
    }

    private static Dictionary<string, LocalExternalAssetsEntry> ToExactPathDictionary(
        IEnumerable<LocalExternalAssetsEntry> entries,
        string sourceName,
        ICollection<DeltaIssue>? issues = null)
    {
        var result = new Dictionary<string, LocalExternalAssetsEntry>(StringComparer.Ordinal);
        var caseInsensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (result.TryAdd(entry.RelativePath, entry)
                && caseInsensitive.TryAdd(entry.RelativePath, entry.RelativePath))
            {
                continue;
            }

            var issue = new DeltaIssue(
                DeltaIssueSeverity.Error,
                "duplicate-or-case-path",
                $"{sourceName}에 중복 또는 대소문자 충돌 경로가 있습니다.",
                entry.RelativePath);
            if (issues is not null)
            {
                issues.Add(issue);
                continue;
            }

            throw new InvalidOperationException(issue.Message + " " + entry.RelativePath);
        }

        return result;
    }

    private static bool IsSameEntry(SeedIndexEntry baseline, LocalExternalAssetsEntry local)
    {
        if (baseline.EntryType != local.EntryType)
        {
            return false;
        }

        if (baseline.EntryType == SeedEntryType.Directory)
        {
            return true;
        }

        return baseline.SizeBytes == local.SizeBytes
            && string.Equals(baseline.Sha256, local.Sha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(baseline.Guid, local.Guid, StringComparison.OrdinalIgnoreCase)
            && baseline.MetaTargetKind == local.MetaTargetKind;
    }

    private static async Task EnsureContributionPackageUnchangedAsync(
        LoadedContributionPackage package,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(package.ZipPath) || !File.Exists(package.ZipPath))
        {
            throw new InvalidOperationException("검증한 Contribution ZIP을 더 이상 찾을 수 없습니다.");
        }

        var fileInfo = new FileInfo(package.ZipPath);
        if (fileInfo.Length != package.SizeBytes)
        {
            throw new InvalidOperationException("검증한 뒤 Contribution ZIP 크기가 바뀌었습니다. 다시 추가하세요.");
        }

        var actualSha256 = await ComputeFileSha256Async(package.ZipPath, cancellationToken);
        if (!string.Equals(actualSha256, package.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("검증한 뒤 Contribution ZIP 내용이 바뀌었습니다. 다시 추가하세요.");
        }
    }

    private static void EnsureLocalEntryUnchanged(LocalExternalAssetsEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.FullPath) || entry.EntryType != SeedEntryType.File)
        {
            throw new InvalidOperationException($"{entry.RelativePath}: Contribution 원본 파일 정보를 확인할 수 없습니다.");
        }

        var fileInfo = new FileInfo(entry.FullPath);
        if (!fileInfo.Exists
            || fileInfo.Length != entry.SizeBytes
            || fileInfo.LastWriteTimeUtc != entry.LastWriteTimeUtc)
        {
            throw new InvalidOperationException($"비교 뒤 원본 파일이 바뀌었습니다: {entry.RelativePath}. 다시 비교하세요.");
        }
    }

    private static string ValidateOutputZipPath(string outputZipPath)
    {
        if (string.IsNullOrWhiteSpace(outputZipPath))
        {
            throw new InvalidOperationException("Contribution ZIP 출력 경로를 입력하세요.");
        }

        var fullPath = Path.GetFullPath(outputZipPath.Trim());
        if (!fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Contribution 출력 파일은 .zip이어야 합니다.");
        }

        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException($"같은 이름의 Contribution ZIP이 이미 있습니다: {fullPath}");
        }

        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Contribution ZIP 출력 폴더를 확인할 수 없습니다.");
        EnsureNoReparsePointInExistingPath(directory);
        return fullPath;
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ComputeArchiveEntrySha256Async(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> ReadMetaGuidAsync(string filePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException(".meta 파일을 찾을 수 없습니다.", filePath);
        }

        if (info.Length > MaximumMetaBytes)
        {
            throw new InvalidOperationException(".meta 파일이 허용된 크기를 초과합니다.");
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await ReadMetaGuidAsync(stream, cancellationToken);
    }

    private static async Task<string> ReadMetaGuidAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length > MaximumMetaBytes)
        {
            throw new InvalidOperationException(".meta 파일이 허용된 크기를 초과합니다.");
        }

        await using var stream = entry.Open();
        return await ReadMetaGuidAsync(stream, cancellationToken);
    }

    private static async Task<string> ReadMetaGuidAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var guid = trimmed["guid:".Length..].Trim();
            if (IsUnityGuid(guid))
            {
                return guid.ToLowerInvariant();
            }

            break;
        }

        throw new InvalidOperationException("Unity GUID를 찾을 수 없거나 형식이 올바르지 않습니다.");
    }

    private static string CalculateBaselineContentSha256(string rootGuid, IEnumerable<SeedIndexEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendCanonicalValue(hash, "rootGuid", rootGuid.ToLowerInvariant());
        foreach (var entry in entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            AppendCanonicalValue(hash, "path", entry.RelativePath);
            AppendCanonicalValue(hash, "entryType", entry.EntryType.ToString());
            AppendCanonicalValue(hash, "size", entry.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            AppendCanonicalValue(hash, "sha256", entry.Sha256?.ToLowerInvariant() ?? string.Empty);
            AppendCanonicalValue(hash, "guid", entry.Guid?.ToLowerInvariant() ?? string.Empty);
            AppendCanonicalValue(hash, "metaTargetKind", entry.MetaTargetKind?.ToString() ?? string.Empty);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendCanonicalValue(IncrementalHash hash, string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "=" + value + "\n");
        hash.AppendData(bytes);
    }

    private static void ValidateRelativePath(string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\')
            || relativePath.StartsWith("/", StringComparison.Ordinal)
            || relativePath.Contains(':'))
        {
            throw new InvalidOperationException($"{description}가 올바르지 않습니다: {relativePath}");
        }

        var segments = relativePath.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidOperationException($"{description}가 올바르지 않습니다: {relativePath}");
        }

        if (segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase)
            || segments[0].Equals("ExternalAssets", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("ExternalAssets.meta", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{description}에는 Assets/, ExternalAssets/, ExternalAssets.meta를 넣을 수 없습니다: {relativePath}");
        }
    }

    private static void ValidatePayloadPath(string payloadPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(payloadPath)
            || payloadPath.Contains('\\')
            || !payloadPath.StartsWith(ExternalAssetsDeltaFormat.PayloadPrefix, StringComparison.Ordinal)
            || !string.Equals(payloadPath[ExternalAssetsDeltaFormat.PayloadPrefix.Length..], relativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Contribution payload 경로가 올바르지 않습니다: {payloadPath}");
        }

        ValidateRelativePath(payloadPath[ExternalAssetsDeltaFormat.PayloadPrefix.Length..], "Contribution payload 상대 경로");
    }

    private static string GetRelativePath(string rootPath, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidate = Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(root, candidate).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        ValidateRelativePath(relative, "ExternalAssets 상대 경로");
        return relative;
    }

    private static string GetSafeDestinationPath(string destinationRoot, string relativePath)
    {
        ValidateRelativePath(relativePath, "Contribution 추출 경로");
        var destination = Path.GetFullPath(Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot)) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Contribution 추출 경로가 staging 밖을 가리킵니다: {relativePath}");
        }

        return destination;
    }

    private static void EnsureNoReparsePointInExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = new DirectoryInfo(fullPath);
        while (!existing.Exists && existing.Parent is not null)
        {
            existing = existing.Parent;
        }

        var chain = new Stack<DirectoryInfo>();
        for (var current = existing; current is not null; current = current.Parent)
        {
            chain.Push(current);
        }

        while (chain.Count > 0)
        {
            var current = chain.Pop();
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"심볼릭 링크 또는 junction 경로는 사용할 수 없습니다: {current.FullName}");
            }
        }
    }

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsUnityGuid(string? value) => value is not null && GuidPattern.IsMatch(value);

    private static bool ContainsControlCharacter(string value) => value.Any(char.IsControl);

    private static void EnsureNoDuplicateGuids(IEnumerable<(string Guid, string Path)> values, string sourceName)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (guid, path) in values)
        {
            if (!paths.TryAdd(guid, path))
            {
                throw new InvalidOperationException($"{sourceName} 안에서 Unity GUID가 중복됩니다: {paths[guid]}, {path}");
            }
        }
    }

    private static void ThrowIfSnapshotHasErrors(LocalSnapshot snapshot, string prefix)
    {
        var issue = snapshot.Issues.FirstOrDefault(item => item.Severity == DeltaIssueSeverity.Error);
        if (issue is not null)
        {
            throw new InvalidOperationException($"{prefix}. {issue.RelativePath ?? "<루트>"}: {issue.Message}");
        }
    }

    private sealed record LocalSnapshot(
        string RootPath,
        string RootGuid,
        IReadOnlyList<LocalExternalAssetsEntry> Entries,
        IReadOnlyList<DeltaIssue> Issues);
}
