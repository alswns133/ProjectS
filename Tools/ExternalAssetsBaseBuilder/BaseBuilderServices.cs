using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ProjectS.ExternalAssetsDelta;

namespace ProjectS.ExternalAssetsBaseBuilder;

/// <summary>
/// 여러 ExternalAssets 원본과 검증된 partial Contribution payload를 읽기 전용으로 분석해,
/// 사람이 선택한 병합 계획만 ZIP으로 기록한다. 입력 프로젝트나 Assets/ExternalAssets 자체는 절대 수정하지 않는다.
/// </summary>
internal static class BaseBuilderServices
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static bool IsUnityProject(string? projectPath) =>
        !string.IsNullOrWhiteSpace(projectPath)
        && Directory.Exists(projectPath)
        && Directory.Exists(Path.Combine(projectPath, "Assets"))
        && Directory.Exists(Path.Combine(projectPath, "Packages"))
        && Directory.Exists(Path.Combine(projectPath, "ProjectSettings"));

    public static string? FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (IsUnityProject(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public static string NormalizeExternalAssetsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("기준 또는 추가 ExternalAssets 폴더를 선택하세요.");
        }

        var fullPath = Path.GetFullPath(path.Trim());
        var projectExternalAssetsPath = Path.Combine(fullPath, "Assets", "ExternalAssets");
        if (Directory.Exists(projectExternalAssetsPath))
        {
            return projectExternalAssetsPath;
        }

        if (Directory.Exists(fullPath)
            && string.Equals(new DirectoryInfo(fullPath).Name, "ExternalAssets", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        throw new InvalidOperationException(
            "Unity 프로젝트 폴더 또는 그 안의 Assets/ExternalAssets 폴더를 선택하세요.");
    }

    private static string NormalizePartialExternalAssetsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Contribution ZIP의 임시 ExternalAssets 경로를 확인할 수 없습니다.");
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"Contribution ZIP의 임시 ExternalAssets 경로가 없습니다: {fullPath}");
        }

        return fullPath;
    }

    private static IReadOnlySet<string> NormalizeRequiredDirectoryMetaPaths(
        IReadOnlySet<string>? requiredDirectoryMetaPaths,
        ExternalAssetsSourceKind kind)
    {
        if (kind == ExternalAssetsSourceKind.FullExternalAssets)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawPaths = requiredDirectoryMetaPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in rawPaths)
        {
            result.Add(NormalizeContributionRelativePath(rawPath, "폴더 .meta"));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> NormalizeExpectedFileSha256(
        IReadOnlyDictionary<string, string>? expectedFileSha256,
        ExternalAssetsSourceKind kind)
    {
        if (kind == ExternalAssetsSourceKind.FullExternalAssets)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawPath, rawSha256) in expectedFileSha256 ?? new Dictionary<string, string>())
        {
            var relativePath = NormalizeContributionRelativePath(rawPath, "payload");
            if (string.IsNullOrWhiteSpace(rawSha256)
                || rawSha256.Length != 64
                || !rawSha256.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException($"Contribution ZIP의 payload SHA-256이 올바르지 않습니다: {rawPath}");
            }

            if (!result.TryAdd(relativePath, rawSha256.ToLowerInvariant()))
            {
                throw new InvalidOperationException($"Contribution ZIP의 payload 경로가 중복됩니다: {rawPath}");
            }
        }

        return result;
    }

    private static string NormalizeContributionRelativePath(string rawPath, string description)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException($"Contribution ZIP의 {description} 경로가 비어 있습니다.");
        }

        var normalized = rawPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.EndsWith("/", StringComparison.Ordinal)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or "..")
            || normalized.Length == 0)
        {
            throw new InvalidOperationException($"Contribution ZIP의 {description} 경로가 올바르지 않습니다: {rawPath}");
        }

        return normalized;
    }

    public static BaseMergePlan Analyze(
        string baselinePath,
        IReadOnlyCollection<string> additionalPaths)
    {
        return Analyze(
            baselinePath,
            additionalPaths
                .Select((path, index) => new AdditionalExternalAssetsInput(
                    $"추가 원본 {index + 1}",
                    path,
                    ExternalAssetsSourceKind.FullExternalAssets))
                .ToArray());
    }

    /// <summary>
    /// Analyzes the baseline together with normal complete sources and validated,
    /// extracted contribution sources. Contribution sources are intentionally
    /// partial and must therefore carry the set of folder metas which they
    /// explicitly declare.
    /// </summary>
    public static BaseMergePlan Analyze(
        string baselinePath,
        IReadOnlyCollection<AdditionalExternalAssetsInput> additionalSources)
    {
        var normalizedBaselinePath = NormalizeExternalAssetsPath(baselinePath);
        var sources = new List<ExternalAssetsSource>
        {
            new(
                "source-1",
                "기준 원본",
                normalizedBaselinePath,
                true,
                ExternalAssetsSourceKind.FullExternalAssets,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
        };

        foreach (var input in additionalSources)
        {
            var normalizedPath = input.Kind == ExternalAssetsSourceKind.FullExternalAssets
                ? NormalizeExternalAssetsPath(input.ExternalAssetsPath)
                : NormalizePartialExternalAssetsPath(input.ExternalAssetsPath);
            if (sources.Any(existing => PathsEqual(existing.ExternalAssetsPath, normalizedPath)))
            {
                continue;
            }

            var requiredDirectoryMetaPaths = NormalizeRequiredDirectoryMetaPaths(
                input.RequiredDirectoryMetaPaths,
                input.Kind);
            var expectedFileSha256 = NormalizeExpectedFileSha256(
                input.ExpectedFileSha256,
                input.Kind);
            sources.Add(new ExternalAssetsSource(
                $"source-{sources.Count + 1}",
                string.IsNullOrWhiteSpace(input.DisplayName) ? $"추가 원본 {sources.Count}" : input.DisplayName,
                normalizedPath,
                false,
                input.Kind,
                requiredDirectoryMetaPaths,
                expectedFileSha256));
        }

        var snapshots = sources.Select(CreateSnapshot).ToList();
        var sourceIssues = snapshots.SelectMany(snapshot => snapshot.ValidationIssues).ToList();

        var pathCandidates = new Dictionary<string, List<SourceFile>>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshots)
        {
            foreach (var directory in snapshot.Directories)
            {
                directories.Add(directory);
            }

            foreach (var file in snapshot.Files)
            {
                if (!pathCandidates.TryGetValue(file.RelativePath, out var candidates))
                {
                    candidates = [];
                    pathCandidates.Add(file.RelativePath, candidates);
                }

                candidates.Add(file);
            }
        }

        var allFilePaths = new HashSet<string>(pathCandidates.Keys, StringComparer.OrdinalIgnoreCase);
        var conflictPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pathCandidates)
        {
            // Do not turn byte-identical support files into a user-visible
            // conflict. Keep their candidates in the map, however: if the
            // paired asset is modified, conflict expansion below must still
            // select that identical .meta from the same contribution source.
            if (HasCaseOnlyCollision(pair.Value)
                || !CanAutoResolveByteIdenticalCandidates(pair.Value, sources)
                || !AllCandidatesHaveSameContent(pair.Value))
            {
                conflictPaths.Add(pair.Key);
            }
        }

        // A Unity asset and its .meta must be chosen as one unit. If either side
        // conflicts, add the existing companion path to the same logical conflict.
        // Otherwise selecting a source that lacks the .meta could accidentally pair
        // its asset with a unique .meta from another source.
        foreach (var path in conflictPaths.ToArray())
        {
            var logicalPath = GetLogicalPath(path, allFilePaths);
            if (!logicalPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (pathCandidates.ContainsKey(logicalPath))
                {
                    conflictPaths.Add(logicalPath);
                }

                if (pathCandidates.ContainsKey(logicalPath + ".meta"))
                {
                    conflictPaths.Add(logicalPath + ".meta");
                }
            }
        }

        var conflictsByLogicalPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in conflictPaths)
        {
            var logicalPath = GetLogicalPath(path, allFilePaths);
            if (!conflictsByLogicalPath.TryGetValue(logicalPath, out var paths))
            {
                paths = [];
                conflictsByLogicalPath.Add(logicalPath, paths);
            }

            paths.Add(path);
        }

        var conflicts = new List<MergeConflict>();
        var filesInConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in conflictsByLogicalPath.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var relatedPaths = pair.Value
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var relatedPath in relatedPaths)
            {
                filesInConflicts.Add(relatedPath);
            }

            var candidates = relatedPaths
                .SelectMany(path => pathCandidates[path])
                .ToList();
            var kind = relatedPaths.Any(path =>
                    pathCandidates[path]
                        .Select(candidate => candidate.RelativePath)
                        .Distinct(StringComparer.Ordinal)
                        .Count() > 1)
                ? ConflictKind.CaseOnlyPathCollision
                : ConflictKind.SameRelativePath;
            conflicts.Add(new MergeConflict(
                $"conflict-{conflicts.Count + 1}",
                pair.Key,
                kind,
                relatedPaths,
                candidates));
        }

        foreach (var filePath in pathCandidates.Keys)
        {
            if (directories.Contains(filePath))
            {
                var candidates = pathCandidates[filePath];
                conflicts.Add(new MergeConflict(
                    $"conflict-{conflicts.Count + 1}",
                    filePath,
                    ConflictKind.FileDirectoryCollision,
                    [filePath],
                    candidates));
                filesInConflicts.Add(filePath);
            }
        }

        var uniqueFiles = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pathCandidates)
        {
            if (!filesInConflicts.Contains(pair.Key))
            {
                uniqueFiles.Add(pair.Key, pair.Value[0]);
            }
        }

        var baselineFileCount = snapshots[0].Files.Count;
        var uniqueAdditionFileCount = uniqueFiles.Values.Count(file => file.SourceId != sources[0].Id);
        return new BaseMergePlan(
            sources,
            uniqueFiles,
            directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            conflicts,
            sourceIssues,
            baselineFileCount,
            uniqueAdditionFileCount);
    }

    public static IReadOnlyList<ExternalAssetsSource> GetSelectableSources(BaseMergePlan plan, MergeConflict conflict)
    {
        if (conflict.Kind == ConflictKind.FileDirectoryCollision)
        {
            return [];
        }

        return conflict.Candidates
            .GroupBy(file => file.SourceId, StringComparer.Ordinal)
            .Where(group => IsCompleteCandidate(plan, group, conflict.RelativePaths))
            .Select(group => plan.Sources.Single(source => string.Equals(source.Id, group.Key, StringComparison.Ordinal)))
            .ToArray();
    }

    public static PlanValidationResult ValidatePlan(
        BaseMergePlan plan,
        IReadOnlyDictionary<string, string> conflictSelections)
    {
        var errors = new List<string>();
        var selectedFiles = BuildSelectedFiles(plan, conflictSelections, errors);
        var selectedDirectories = GetSelectedDirectories(plan.Directories, selectedFiles);
        ValidateSelectedMetadata(plan, selectedDirectories, selectedFiles, errors, out var guidCollisions);
        return new PlanValidationResult(errors, guidCollisions);
    }

    /// <summary>
    /// Rechecks the contribution's declared precondition against the exact seed
    /// which was matched to the owner's baseline. In particular, an existing
    /// Unity .meta may change importer settings but may never replace its GUID:
    /// project files in Git refer to that stable GUID.
    /// </summary>
    public static ContributionImportValidation ValidateContributionManifestAgainstBaseline(
        SeedIndex baselineSeed,
        ContributionManifest contribution)
    {
        var errors = new List<string>();
        if (!string.Equals(contribution.BaselineId, baselineSeed.BaselineId, StringComparison.Ordinal))
        {
            errors.Add("Contribution ZIP의 baselineId가 선택한 기준 인덱스와 다릅니다.");
        }

        if (!string.Equals(contribution.BaselineContentSha256, baselineSeed.BaselineContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Contribution ZIP의 기준 내용 해시가 선택한 기준 인덱스와 다릅니다.");
        }

        if (!string.Equals(contribution.ExternalAssetsRootGuid, baselineSeed.ExternalAssetsRootGuid, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Contribution ZIP의 ExternalAssets 루트 GUID가 선택한 기준 인덱스와 다릅니다.");
        }

        var baselineEntries = BuildSeedEntryMap(baselineSeed, errors);
        ValidateContributionCompleteness(contribution, baselineEntries, errors);
        foreach (var entry in contribution.Entries)
        {
            baselineEntries.TryGetValue(entry.RelativePath, out var baselineEntry);
            switch (entry.ChangeKind)
            {
                case ContributionChangeKind.Added:
                    if (baselineEntry is not null)
                    {
                        errors.Add($"{entry.RelativePath}: 기존 기준 경로를 Added로 제출할 수 없습니다.");
                    }

                    break;

                case ContributionChangeKind.Modified:
                    ValidateExistingContributionPayload(entry, baselineEntry, errors, requireSameContent: false);
                    break;

                case ContributionChangeKind.Support:
                    ValidateExistingContributionPayload(entry, baselineEntry, errors, requireSameContent: true);
                    break;

                default:
                    errors.Add($"{entry.RelativePath}: 알 수 없는 Contribution 변경 종류입니다.");
                    break;
            }
        }

        foreach (var directory in contribution.Directories)
        {
            baselineEntries.TryGetValue(directory.RelativePath, out var baselineEntry);
            switch (directory.ChangeKind)
            {
                case ContributionChangeKind.Added when baselineEntry is not null:
                    errors.Add($"{directory.RelativePath}: 기존 기준 폴더를 Added로 제출할 수 없습니다.");
                    break;
                case ContributionChangeKind.Modified when baselineEntry is null:
                    errors.Add($"{directory.RelativePath}: 기준에 없는 폴더를 Modified로 제출할 수 없습니다.");
                    break;
                case ContributionChangeKind.Modified when baselineEntry?.EntryType != SeedEntryType.Directory:
                    errors.Add($"{directory.RelativePath}: 기준 폴더가 아닌 경로를 Modified로 제출할 수 없습니다.");
                    break;
                case ContributionChangeKind.Support:
                    errors.Add($"{directory.RelativePath}: 빈 폴더 선언에는 Support 변경 종류를 사용할 수 없습니다.");
                    break;
            }
        }

        return new ContributionImportValidation(errors);
    }

    public static async Task<BasePackageBuildResult> CreateBasePackageAsync(
        BaseMergePlan plan,
        IReadOnlyDictionary<string, string> conflictSelections,
        string outputDirectory,
        string zipName,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var validationErrors = new List<string>();
        var selectedFiles = BuildSelectedFiles(plan, conflictSelections, validationErrors);
        var selectedDirectories = GetSelectedDirectories(plan.Directories, selectedFiles);
        ValidateSelectedMetadata(plan, selectedDirectories, selectedFiles, validationErrors, out var guidCollisions);
        if (validationErrors.Count > 0 || guidCollisions.Count > 0)
        {
            var summary = validationErrors.FirstOrDefault()
                ?? $"서로 다른 경로에서 GUID {guidCollisions[0].Guid}가 중복됩니다.";
            throw new InvalidOperationException($"Base ZIP을 만들 수 없습니다. {summary}");
        }

        EnsureSourcesUnchanged(selectedFiles.Values);
        var outputFullDirectory = ValidateOutputDirectory(outputDirectory, plan.Sources);
        zipName = ValidateZipName(zipName);
        var zipPath = Path.Combine(outputFullDirectory, zipName);
        var reportPath = Path.Combine(outputFullDirectory, Path.GetFileNameWithoutExtension(zipName) + ".merge-report.json");
        if (File.Exists(zipPath))
        {
            throw new InvalidOperationException($"같은 이름의 ZIP이 이미 있습니다: {zipPath}");
        }

        if (File.Exists(reportPath))
        {
            throw new InvalidOperationException($"같은 이름의 병합 보고서가 이미 있습니다: {reportPath}");
        }

        var temporaryZipPath = zipPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(outputFullDirectory);
            var files = selectedFiles
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            progress?.Report(new BuildProgress("Base ZIP을 준비하는 중", 0, files.Length));

            using (var archive = ZipFile.Open(temporaryZipPath, ZipArchiveMode.Create))
            {
                foreach (var directory in selectedDirectories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    archive.CreateEntry(ToArchivePath(directory).TrimEnd('/') + "/");
                }

                for (var index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (relativePath, sourceFile) = files[index];
                    // The contribution staging file is re-hashed immediately
                    // before opening it. File.OpenRead keeps a write share lock
                    // for the copy below, preventing a same-size/mtime swap from
                    // silently entering the final Base ZIP.
                    EnsureSourceUnchanged(sourceFile, verifyExpectedSha256: true);
                    var entry = archive.CreateEntry(ToArchivePath(relativePath), CompressionLevel.Optimal);
                    await using var source = File.OpenRead(sourceFile.FullPath);
                    await using var destination = entry.Open();
                    await source.CopyToAsync(destination, 1024 * 128, cancellationToken);
                    EnsureSourceUnchanged(sourceFile, verifyExpectedSha256: false);
                    if (index % 100 == 0 || index == files.Length - 1)
                    {
                        progress?.Report(new BuildProgress(
                            $"압축 중 ({index + 1:N0}/{files.Length:N0}): {relativePath}",
                            index + 1,
                            files.Length));
                    }
                }
            }

            VerifyArchiveEntries(temporaryZipPath, files.Select(file => file.Key), selectedDirectories);
            progress?.Report(new BuildProgress("ZIP SHA-256 계산 중", files.Length, files.Length));
            var sha256 = await ComputeSha256Async(temporaryZipPath, cancellationToken);
            File.Move(temporaryZipPath, zipPath);
            var fileInfo = new FileInfo(zipPath);
            try
            {
                await WriteMergeReportAsync(reportPath, plan, conflictSelections, new MergeReportZip
                {
                    Path = zipPath,
                    Sha256 = sha256,
                    SizeBytes = fileInfo.Length,
                    FileEntryCount = files.Length,
                    DirectoryEntryCount = selectedDirectories.Count,
                }, cancellationToken);
            }
            catch
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                throw;
            }
            progress?.Report(new BuildProgress("검증된 Base ZIP 생성 완료", files.Length, files.Length));
            return new BasePackageBuildResult(
                zipPath,
                sha256,
                fileInfo.Length,
                files.Length,
                selectedDirectories.Count,
                reportPath);
        }
        finally
        {
            if (File.Exists(temporaryZipPath))
            {
                File.Delete(temporaryZipPath);
            }
        }
    }

    private static SourceSnapshot CreateSnapshot(ExternalAssetsSource source)
    {
        var files = new List<SourceFile>();
        var directories = new List<string>();
        var validationIssues = new List<SourceValidationIssue>();
        foreach (var directoryPath in EnumerateDirectories(source.ExternalAssetsPath))
        {
            directories.Add(GetRelativePath(source.ExternalAssetsPath, directoryPath));
        }

        foreach (var filePath in EnumerateFiles(source.ExternalAssetsPath))
        {
            var fileInfo = new FileInfo(filePath);
            var relativePath = GetRelativePath(source.ExternalAssetsPath, filePath);
            source.ExpectedFileSha256.TryGetValue(relativePath, out var expectedSha256);
            files.Add(new SourceFile(
                source.Id,
                relativePath,
                filePath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                expectedSha256));
            if (source.Kind == ExternalAssetsSourceKind.ContributionPartial && expectedSha256 is null)
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    relativePath,
                    "안전 추출 결과에 manifest에 없는 payload 파일이 있습니다."));
            }
        }

        var filePaths = new HashSet<string>(files.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var directoryPaths = new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!file.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                && !filePaths.Contains(file.RelativePath + ".meta"))
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    file.RelativePath,
                    "대응하는 .meta 파일이 없습니다."));
            }

            if (!file.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var metaTargetPath = file.RelativePath[..^".meta".Length];
            var explicitlyDeclaredFolderMeta = source.Kind == ExternalAssetsSourceKind.ContributionPartial
                && source.RequiredDirectoryMetaPaths.Contains(metaTargetPath);
            if (!filePaths.Contains(metaTargetPath)
                && !directoryPaths.Contains(metaTargetPath)
                && !explicitlyDeclaredFolderMeta)
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    file.RelativePath,
                    "대응하는 파일 또는 폴더가 없는 고아 .meta 파일입니다."));
            }

            if (!TryReadGuid(file.FullPath, out _))
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    file.RelativePath,
                    "Unity GUID를 읽을 수 없거나 형식이 올바르지 않습니다."));
            }
        }

        foreach (var directory in directories)
        {
            var requiresOwnMeta = source.Kind == ExternalAssetsSourceKind.FullExternalAssets
                || source.RequiredDirectoryMetaPaths.Contains(directory);
            if (requiresOwnMeta && !filePaths.Contains(directory + ".meta"))
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    directory,
                    "폴더 .meta 파일이 없습니다."));
            }
        }

        foreach (var expectedFile in source.ExpectedFileSha256.Keys)
        {
            if (!filePaths.Contains(expectedFile))
            {
                validationIssues.Add(new SourceValidationIssue(
                    source.Id,
                    expectedFile,
                    "Contribution manifest에는 있지만 안전 추출 결과에 없는 payload 파일입니다."));
            }
        }

        return new SourceSnapshot(source, files, directories, validationIssues);
    }

    private static Dictionary<string, SourceFile> BuildSelectedFiles(
        BaseMergePlan plan,
        IReadOnlyDictionary<string, string> conflictSelections,
        ICollection<string> errors)
    {
        var selectedFiles = new Dictionary<string, SourceFile>(plan.UniqueFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in plan.Conflicts)
        {
            if (conflict.Kind == ConflictKind.FileDirectoryCollision)
            {
                errors.Add($"{conflict.LogicalPath}: 파일과 폴더가 같은 경로를 사용합니다. 원본 구조를 정리하세요.");
                continue;
            }

            if (!conflictSelections.TryGetValue(conflict.Id, out var selectedSourceId))
            {
                errors.Add($"{conflict.LogicalPath}: 충돌 원본을 선택하세요.");
                continue;
            }

            if (!IsSelectableSource(plan, conflict, selectedSourceId))
            {
                errors.Add($"{conflict.LogicalPath}: 선택한 원본에는 파일과 .meta 묶음 또는 대상 폴더가 모두 없습니다.");
                continue;
            }

            foreach (var relativePath in conflict.RelativePaths)
            {
                var sourceFile = conflict.Candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceId, selectedSourceId, StringComparison.Ordinal)
                    && string.Equals(candidate.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
                if (sourceFile is null)
                {
                    errors.Add($"{conflict.LogicalPath}: 선택한 원본에는 '{relativePath}' 파일과 .meta 묶음이 모두 없습니다.");
                    continue;
                }

                selectedFiles[relativePath] = sourceFile;
            }
        }

        return selectedFiles;
    }

    private static bool IsSelectableSource(BaseMergePlan plan, MergeConflict conflict, string sourceId)
    {
        var candidates = conflict.Candidates
            .Where(candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal))
            .ToArray();
        return candidates.Length > 0 && IsCompleteCandidate(plan, candidates, conflict.RelativePaths);
    }

    private static bool IsCompleteCandidate(
        BaseMergePlan plan,
        IEnumerable<SourceFile> candidates,
        IReadOnlyList<string> relativePaths)
    {
        var candidateArray = candidates.ToArray();
        if (!relativePaths.All(relativePath => candidateArray.Any(file =>
                string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        var sourceId = candidateArray[0].SourceId;
        var source = plan.Sources.Single(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal));
        foreach (var candidate in candidateArray)
        {
            if (!candidate.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(candidate.FullPath + ".meta"))
                {
                    return false;
                }

                continue;
            }

            var targetPath = candidate.FullPath[..^".meta".Length];
            var targetRelativePath = candidate.RelativePath[..^".meta".Length];
            if (!File.Exists(targetPath)
                && !Directory.Exists(targetPath)
                && !(source.Kind == ExternalAssetsSourceKind.ContributionPartial
                    && source.RequiredDirectoryMetaPaths.Contains(targetRelativePath)))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateSelectedMetadata(
        BaseMergePlan plan,
        IReadOnlyList<string> directories,
        IReadOnlyDictionary<string, SourceFile> selectedFiles,
        ICollection<string> errors,
        out IReadOnlyList<GuidCollision> guidCollisions)
    {
        foreach (var pair in selectedFiles)
        {
            if (!pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (!selectedFiles.TryGetValue(pair.Key + ".meta", out var companionMeta))
                {
                    errors.Add($"{pair.Key}: 선택 결과에 대응하는 .meta 파일이 없습니다.");
                }
                else if (!string.Equals(pair.Value.SourceId, companionMeta.SourceId, StringComparison.Ordinal))
                {
                    errors.Add($"{pair.Key}: 파일과 .meta는 같은 원본에서 함께 선택되어야 합니다.");
                }

                continue;
            }

            var metaTargetPath = pair.Key[..^".meta".Length];
            if (selectedFiles.ContainsKey(metaTargetPath))
            {
                continue;
            }

            if (!directories.Contains(metaTargetPath, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"{pair.Key}: 선택 결과에 대응하는 파일 또는 폴더가 없는 고아 .meta 파일입니다.");
                continue;
            }

            var sourceDirectoryPath = pair.Value.FullPath[..^".meta".Length];
            var source = plan.Sources.Single(source => string.Equals(source.Id, pair.Value.SourceId, StringComparison.Ordinal));
            if (!Directory.Exists(sourceDirectoryPath)
                && !(source.Kind == ExternalAssetsSourceKind.ContributionPartial
                    && source.RequiredDirectoryMetaPaths.Contains(metaTargetPath)))
            {
                errors.Add($"{pair.Key}: 선택한 원본에 대응 폴더가 없습니다.");
            }
        }

        foreach (var directory in directories)
        {
            if (!selectedFiles.ContainsKey(directory + ".meta"))
            {
                errors.Add($"{directory}: 선택 결과에 폴더 .meta 파일이 없습니다.");
            }
        }

        var guidPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var baseline = plan.Sources.Single(source => source.IsBaseline);
        var rootMetaPath = Path.TrimEndingDirectorySeparator(baseline.ExternalAssetsPath) + ".meta";
        if (!File.Exists(rootMetaPath))
        {
            errors.Add("기준 ExternalAssets 루트의 .meta 파일이 없습니다. Git으로 추적 중인 Assets/ExternalAssets.meta를 확인하세요.");
        }
        else if (!TryReadGuid(rootMetaPath, out var rootGuid))
        {
            errors.Add("기준 ExternalAssets 루트 .meta의 Unity GUID를 읽을 수 없습니다.");
        }
        else
        {
            guidPaths.Add(rootGuid, ["<Assets/ExternalAssets.meta>"]);
        }

        foreach (var pair in selectedFiles.Where(pair => pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadGuid(pair.Value.FullPath, out var guid))
            {
                errors.Add($"{pair.Key}: Unity GUID를 읽을 수 없습니다.");
                continue;
            }

            if (!guidPaths.TryGetValue(guid, out var paths))
            {
                paths = [];
                guidPaths.Add(guid, paths);
            }

            paths.Add(pair.Key);
        }

        guidCollisions = guidPaths
            .Where(pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new GuidCollision(
                pair.Key,
                pair.Value.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    private static void ValidateExistingContributionPayload(
        ContributionPayloadEntry entry,
        SeedIndexEntry? baselineEntry,
        ICollection<string> errors,
        bool requireSameContent)
    {
        if (baselineEntry is null)
        {
            errors.Add($"{entry.RelativePath}: 기준에 없는 경로는 Modified/Support로 제출할 수 없습니다.");
            return;
        }

        if (baselineEntry.EntryType != SeedEntryType.File)
        {
            errors.Add($"{entry.RelativePath}: 기준 파일이 아닌 경로를 Modified/Support로 제출할 수 없습니다.");
            return;
        }

        if (!string.Equals(entry.BaselineSha256, baselineEntry.Sha256, StringComparison.OrdinalIgnoreCase)
            || entry.BaselineSizeBytes != baselineEntry.SizeBytes)
        {
            errors.Add($"{entry.RelativePath}: Contribution이 참조한 기준 파일 해시 또는 크기가 현재 기준 인덱스와 다릅니다.");
        }

        if (requireSameContent && !string.Equals(entry.Sha256, baselineEntry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{entry.RelativePath}: Support 파일은 기준과 동일한 SHA-256이어야 합니다.");
        }

        if (entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Guid, baselineEntry.Guid, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{entry.RelativePath}: 기존 .meta의 GUID를 변경할 수 없습니다. importer 설정만 변경하고 guid: 값은 기준과 같아야 합니다.");
        }
    }

    private static Dictionary<string, SeedIndexEntry> BuildSeedEntryMap(
        SeedIndex seed,
        ICollection<string> errors)
    {
        var entries = new Dictionary<string, SeedIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in seed.Entries)
        {
            string path;
            try
            {
                path = NormalizeContributionRelativePath(entry.RelativePath, "기준 인덱스");
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
                continue;
            }

            if (!string.Equals(path, entry.RelativePath, StringComparison.Ordinal))
            {
                errors.Add($"기준 인덱스 경로는 '/' 구분자와 정규화된 상대 경로여야 합니다: {entry.RelativePath}");
                continue;
            }

            if (!entries.TryAdd(path, entry))
            {
                errors.Add($"기준 인덱스에 중복 또는 대소문자 충돌 경로가 있습니다: {entry.RelativePath}");
            }
        }

        return entries;
    }

    private static void ValidateContributionCompleteness(
        ContributionManifest contribution,
        IReadOnlyDictionary<string, SeedIndexEntry> baselineEntries,
        ICollection<string> errors)
    {
        var payloadByPath = new Dictionary<string, ContributionPayloadEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in contribution.Entries)
        {
            string path;
            try
            {
                path = NormalizeContributionRelativePath(entry.RelativePath, "payload");
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
                continue;
            }

            if (!string.Equals(path, entry.RelativePath, StringComparison.Ordinal))
            {
                errors.Add($"Contribution payload 경로는 '/' 구분자와 정규화된 상대 경로여야 합니다: {entry.RelativePath}");
                continue;
            }

            if (!payloadByPath.TryAdd(path, entry))
            {
                errors.Add($"Contribution ZIP에 중복 또는 대소문자 충돌 payload 경로가 있습니다: {entry.RelativePath}");
            }
        }

        var directories = new Dictionary<string, ContributionDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in contribution.Directories)
        {
            string path;
            try
            {
                path = NormalizeContributionRelativePath(directory.RelativePath, "폴더 선언");
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
                continue;
            }

            if (!string.Equals(path, directory.RelativePath, StringComparison.Ordinal)
                || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Contribution 폴더 선언 경로가 올바르지 않습니다: {directory.RelativePath}");
                continue;
            }

            if (!directories.TryAdd(path, directory))
            {
                errors.Add($"Contribution ZIP에 중복 또는 대소문자 충돌 폴더 선언이 있습니다: {directory.RelativePath}");
            }
        }

        foreach (var entry in payloadByPath.Values)
        {
            var isMeta = entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
            if (!isMeta)
            {
                if (entry.MetaTargetKind is not null)
                {
                    errors.Add($"{entry.RelativePath}: 일반 파일에는 metaTargetKind를 선언할 수 없습니다.");
                }

                if (!payloadByPath.ContainsKey(entry.RelativePath + ".meta"))
                {
                    errors.Add($"{entry.RelativePath}: Contribution에는 asset과 대응 .meta를 함께 넣어야 합니다.");
                }

                continue;
            }

            var targetPath = entry.RelativePath[..^".meta".Length];
            if (entry.MetaTargetKind == ContributionMetaTargetKind.Folder)
            {
                var knownDirectory = baselineEntries.TryGetValue(targetPath, out var baselineEntry)
                    && baselineEntry?.EntryType == SeedEntryType.Directory;
                var declaredDirectory = directories.ContainsKey(targetPath);
                var hasChildPayload = payloadByPath.Keys.Any(path => path.StartsWith(targetPath + "/", StringComparison.OrdinalIgnoreCase));
                if (!knownDirectory && !declaredDirectory && !hasChildPayload)
                {
                    errors.Add($"{entry.RelativePath}: 새 빈 폴더의 .meta에는 해당 폴더 선언이 필요합니다.");
                }

                continue;
            }

            if (entry.MetaTargetKind != ContributionMetaTargetKind.Asset)
            {
                errors.Add($"{entry.RelativePath}: .meta 대상 종류가 Asset 또는 Folder로 선언되어야 합니다.");
                continue;
            }

            if (!payloadByPath.ContainsKey(targetPath))
            {
                errors.Add($"{entry.RelativePath}: 변경된 asset .meta에는 같은 Contribution의 asset 또는 Support asset이 필요합니다.");
            }
        }

        foreach (var directoryPath in directories.Keys)
        {
            if (!payloadByPath.TryGetValue(directoryPath + ".meta", out var folderMeta)
                || folderMeta.MetaTargetKind != ContributionMetaTargetKind.Folder)
            {
                errors.Add($"{directoryPath}: 폴더 선언에는 같은 Contribution의 Folder .meta payload가 필요합니다.");
            }
        }

        foreach (var payloadPath in payloadByPath.Keys.Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            var separatorIndex = payloadPath.LastIndexOf('/');
            while (separatorIndex >= 0)
            {
                var parentPath = payloadPath[..separatorIndex];
                var existsInBaseline = baselineEntries.TryGetValue(parentPath, out var baselineParent)
                    && baselineParent?.EntryType == SeedEntryType.Directory;
                if (!existsInBaseline
                    && (!payloadByPath.TryGetValue(parentPath + ".meta", out var parentMeta)
                        || parentMeta.MetaTargetKind != ContributionMetaTargetKind.Folder))
                {
                    errors.Add($"{parentPath}: 기준에 없는 새 폴더에는 Folder .meta payload가 필요합니다.");
                }

                separatorIndex = parentPath.LastIndexOf('/');
            }
        }
    }

    private static IReadOnlyList<string> GetSelectedDirectories(
        IReadOnlyList<string> allDirectories,
        IReadOnlyDictionary<string, SourceFile> selectedFiles)
    {
        var allDirectoryPaths = new HashSet<string>(allDirectories, StringComparer.OrdinalIgnoreCase);
        var selectedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in selectedFiles.Keys)
        {
            if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                var metaTargetPath = relativePath[..^".meta".Length];
                if (allDirectoryPaths.Contains(metaTargetPath))
                {
                    selectedDirectories.Add(metaTargetPath);
                }
            }

            var separatorIndex = relativePath.LastIndexOf('/');
            while (separatorIndex >= 0)
            {
                var directory = relativePath[..separatorIndex];
                if (allDirectoryPaths.Contains(directory))
                {
                    selectedDirectories.Add(directory);
                }

                separatorIndex = directory.LastIndexOf('/');
            }
        }

        return selectedDirectories
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureSourcesUnchanged(IEnumerable<SourceFile> sourceFiles)
    {
        foreach (var sourceFile in sourceFiles.DistinctBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            EnsureSourceUnchanged(sourceFile, verifyExpectedSha256: false);
        }
    }

    private static void EnsureSourceUnchanged(SourceFile sourceFile, bool verifyExpectedSha256 = true)
    {
        var fileInfo = new FileInfo(sourceFile.FullPath);
        if (!fileInfo.Exists
            || fileInfo.Length != sourceFile.SizeBytes
            || fileInfo.LastWriteTimeUtc != sourceFile.LastWriteTimeUtc)
        {
            throw new InvalidOperationException(
                $"분석 뒤 원본 파일이 바뀌었습니다: {sourceFile.RelativePath}. 다시 분석하세요.");
        }

        if (verifyExpectedSha256
            && sourceFile.ExpectedSha256 is not null
            && !string.Equals(ComputeSha256(sourceFile.FullPath), sourceFile.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Contribution 임시 payload의 SHA-256이 manifest와 다릅니다: {sourceFile.RelativePath}. 다시 분석하세요.");
        }
    }

    private static string ValidateOutputDirectory(string outputDirectory, IReadOnlyList<ExternalAssetsSource> sources)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Base ZIP을 저장할 출력 폴더를 선택하세요.");
        }

        var fullPath = Path.GetFullPath(outputDirectory.Trim());
        if (sources.Any(source => IsPathInside(fullPath, source.ExternalAssetsPath)))
        {
            throw new InvalidOperationException("출력 폴더는 입력 ExternalAssets 폴더 내부에 둘 수 없습니다.");
        }

        EnsureNoReparsePointInExistingPath(fullPath);

        return fullPath;
    }

    private static string ValidateZipName(string zipName)
    {
        zipName = zipName.Trim();
        if (string.IsNullOrWhiteSpace(zipName))
        {
            throw new InvalidOperationException("Base ZIP 파일명을 입력하세요.");
        }

        if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            zipName += ".zip";
        }

        if (zipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || zipName.Contains(Path.DirectorySeparatorChar)
            || zipName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("ZIP 파일명에는 폴더 경로를 넣을 수 없습니다.");
        }

        return zipName;
    }

    private static void VerifyArchiveEntries(string zipPath, IEnumerable<string> expectedFiles, IEnumerable<string> expectedDirectories)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = new HashSet<string>(archive.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);
        foreach (var expectedFile in expectedFiles)
        {
            if (!entries.Contains(ToArchivePath(expectedFile)))
            {
                throw new InvalidOperationException($"생성된 ZIP에 파일이 없습니다: {expectedFile}");
            }
        }

        foreach (var expectedDirectory in expectedDirectories)
        {
            if (!entries.Contains(ToArchivePath(expectedDirectory).TrimEnd('/') + "/"))
            {
                throw new InvalidOperationException($"생성된 ZIP에 폴더가 없습니다: {expectedDirectory}");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task WriteMergeReportAsync(
        string reportPath,
        BaseMergePlan plan,
        IReadOnlyDictionary<string, string> conflictSelections,
        MergeReportZip zip,
        CancellationToken cancellationToken)
    {
        var report = new MergeReport
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Sources = plan.Sources.Select(source => new MergeReportSource
            {
                Name = source.DisplayName,
                Path = source.ExternalAssetsPath,
                IsBaseline = source.IsBaseline,
                Kind = source.Kind.ToString(),
            }).ToList(),
            Conflicts = plan.Conflicts.Select(conflict => new MergeReportConflict
            {
                LogicalPath = conflict.LogicalPath,
                Kind = conflict.Kind.ToString(),
                RelativePaths = conflict.RelativePaths.ToList(),
                SelectedSource = conflictSelections.TryGetValue(conflict.Id, out var sourceId)
                    ? GetSourceName(plan, sourceId)
                    : string.Empty,
            }).ToList(),
            Zip = zip,
        };

        var temporaryPath = reportPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, reportPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var currentPath = pending.Pop();
            var currentDirectory = new DirectoryInfo(currentPath);
            if ((currentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"재분석 지점(심볼릭 링크)은 Base 원본에 포함할 수 없습니다: {currentPath}");
            }

            foreach (var directory in currentDirectory.EnumerateDirectories())
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"재분석 지점(심볼릭 링크)은 Base 원본에 포함할 수 없습니다: {directory.FullName}");
                }

                pending.Push(directory.FullName);
                yield return directory.FullName;
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var currentPath = pending.Pop();
            var currentDirectory = new DirectoryInfo(currentPath);
            if ((currentDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"재분석 지점(심볼릭 링크)은 Base 원본에 포함할 수 없습니다: {currentPath}");
            }

            foreach (var file in currentDirectory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"재분석 지점(심볼릭 링크)은 Base 원본에 포함할 수 없습니다: {file.FullName}");
                }

                yield return file.FullName;
            }

            foreach (var directory in currentDirectory.EnumerateDirectories())
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"재분석 지점(심볼릭 링크)은 Base 원본에 포함할 수 없습니다: {directory.FullName}");
                }

                pending.Push(directory.FullName);
            }
        }
    }

    private static bool TryReadGuid(string metaPath, out string guid)
    {
        foreach (var line in File.ReadLines(metaPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            guid = trimmed["guid:".Length..].Trim();
            return guid.Length == 32 && guid.All(Uri.IsHexDigit);
        }

        guid = string.Empty;
        return false;
    }

    private static string GetLogicalPath(string relativePath, ISet<string> allFilePaths)
    {
        if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
        {
            var withoutMeta = relativePath[..^".meta".Length];
            if (allFilePaths.Contains(withoutMeta))
            {
                return withoutMeta;
            }
        }

        return relativePath;
    }

    private static bool HasCaseOnlyCollision(IReadOnlyCollection<SourceFile> candidates) =>
        candidates.Select(candidate => candidate.RelativePath).Distinct(StringComparer.Ordinal).Count() > 1;

    private static bool AllCandidatesHaveSameContent(IReadOnlyList<SourceFile> candidates)
    {
        if (candidates.Count < 2)
        {
            return true;
        }

        var baseline = candidates[0];
        for (var index = 1; index < candidates.Count; index++)
        {
            if (!FilesHaveSameContent(baseline, candidates[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAutoResolveByteIdenticalCandidates(
        IReadOnlyList<SourceFile> candidates,
        IReadOnlyList<ExternalAssetsSource> sources)
    {
        if (candidates.Count < 2)
        {
            return true;
        }

        var sourcesById = sources.ToDictionary(source => source.Id, StringComparer.Ordinal);
        // Keep the historical behavior for two ordinary full folder inputs.
        // Automatic de-duplication exists specifically for a validated partial
        // contribution's Support entry plus its matching baseline counterpart.
        return candidates.All(candidate =>
        {
            var source = sourcesById[candidate.SourceId];
            return source.IsBaseline || source.Kind == ExternalAssetsSourceKind.ContributionPartial;
        });
    }

    private static bool FilesHaveSameContent(SourceFile left, SourceFile right)
    {
        if (left.SizeBytes != right.SizeBytes)
        {
            return false;
        }

        const int bufferSize = 128 * 1024;
        using var leftStream = File.OpenRead(left.FullPath);
        using var rightStream = File.OpenRead(right.FullPath);
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        while (true)
        {
            var leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static string GetRelativePath(string rootPath, string fullPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, fullPath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Split(Path.DirectorySeparatorChar).Any(segment => segment == ".."))
        {
            throw new InvalidOperationException($"ExternalAssets 밖의 경로는 Base 원본에 포함할 수 없습니다: {fullPath}");
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ToArchivePath(string relativePath) => relativePath.Replace('\\', '/');

    private static bool IsPathInside(string candidatePath, string rootPath)
    {
        var candidateFullPath = Path.GetFullPath(candidatePath);
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        return string.Equals(candidateFullPath, rootFullPath, StringComparison.OrdinalIgnoreCase)
            || candidateFullPath.StartsWith(rootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePointInExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException("출력 폴더의 루트를 확인할 수 없습니다.");
        var currentPath = root;
        var relativePath = fullPath[root.Length..];
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!Directory.Exists(currentPath))
            {
                break;
            }

            if ((new DirectoryInfo(currentPath).Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("출력 폴더 경로에는 junction 또는 심볼릭 링크를 사용할 수 없습니다.");
            }
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string GetSourceName(BaseMergePlan plan, string sourceId) =>
        plan.Sources.FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal))?.DisplayName
        ?? sourceId;

    private sealed record SourceSnapshot(
        ExternalAssetsSource Source,
        IReadOnlyList<SourceFile> Files,
        IReadOnlyList<string> Directories,
        IReadOnlyList<SourceValidationIssue> ValidationIssues);
}
