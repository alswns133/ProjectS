using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectS.ExternalAssetsPublisher;

internal static class PublisherServices
{
    private const string ChannelIdPrefix = "projects-externalassets-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static bool IsUnityProject(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(projectPath, "Assets"))
            && Directory.Exists(Path.Combine(projectPath, "Packages"))
            && Directory.Exists(Path.Combine(projectPath, "ProjectSettings"));
    }

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

    public static string GetExternalAssetsPath(string projectPath) =>
        Path.Combine(projectPath, "Assets", "ExternalAssets");

    public static async Task<PublisherManifest> LoadManifestAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new PublisherManifest();
        }

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync<PublisherManifest>(stream, JsonOptions)
            ?? throw new InvalidOperationException("manifest.json을 읽을 수 없습니다.");
    }

    public static async Task AppendPackageToManifestAsync(
        string manifestPath,
        int version,
        string packageType,
        PackageBuildResult package,
        string driveFileSource,
        IReadOnlyList<string> removedPaths)
    {
        EnsurePackageStillMatches(package);
        var driveFileId = ParseGoogleDriveFileId(driveFileSource);

        if (packageType is not ("base" or "patch"))
        {
            throw new InvalidOperationException("패키지 종류는 base 또는 patch여야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(package.PackageName)
            || package.Sha256.Length != 64
            || !package.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("manifest에 기록할 패키지 이름 또는 SHA-256이 올바르지 않습니다.");
        }

        var normalizedRemovedPaths = NormalizeRemovedPaths(removedPaths);
        var manifest = await LoadManifestAsync(manifestPath);
        if (manifest.Packages.Count > 0 && manifest.SchemaVersion != 2)
        {
            throw new InvalidOperationException(
                "기존 schemaVersion 1 manifest는 공개 링크 방식입니다. 제한된 Drive용 새 schemaVersion 2 manifest를 생성하세요.");
        }

        ValidateExistingManifestForAppend(manifest);

        if (manifest.Packages.Count == 0 && string.IsNullOrWhiteSpace(manifest.ChannelId))
        {
            manifest.ChannelId = ChannelIdPrefix + Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(manifest.ChannelId))
        {
            throw new InvalidOperationException("manifest의 channelId가 없습니다. 새 제한된 Drive manifest를 만드세요.");
        }

        var expectedVersion = manifest.Packages.Count == 0 ? 1 : manifest.LatestVersion + 1;
        if (version != expectedVersion)
        {
            throw new InvalidOperationException(
                $"manifest의 다음 버전은 v{expectedVersion}입니다. v{version}은 저장할 수 없습니다.");
        }

        var expectedPackageType = expectedVersion == 1 ? "base" : "patch";
        if (!string.Equals(packageType, expectedPackageType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"v{expectedVersion} 패키지 종류는 {expectedPackageType}이어야 합니다.");
        }

        var expectedOrigin = expectedVersion == 1
            ? PackageBuildOrigin.ImportedBase
            : PackageBuildOrigin.ProjectPatch;
        if (package.Origin != expectedOrigin)
        {
            var message = expectedVersion == 1
                ? "Base v1은 Base Builder가 만든 ZIP을 'Base Builder ZIP 선택'으로 검사·등록해야 합니다."
                : "v2 이후 패치는 현재 프로젝트의 변경 파일로 새 ZIP을 생성해야 합니다.";
            throw new InvalidOperationException(message);
        }

        if (expectedVersion == 1 && normalizedRemovedPaths.Count > 0)
        {
            throw new InvalidOperationException("Base v1에는 삭제할 상대 경로를 넣을 수 없습니다.");
        }

        var removedArchiveEntries = normalizedRemovedPaths
            .Where(path => package.ArchiveEntries.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (removedArchiveEntries.Length > 0)
        {
            throw new InvalidOperationException(
                $"삭제할 상대 경로가 같은 패키지에 포함되어 있습니다: {string.Join(", ", removedArchiveEntries)}");
        }

        if (manifest.Packages.Any(existing => existing.Version == version))
        {
            throw new InvalidOperationException($"v{version} 패키지가 이미 manifest에 있습니다.");
        }

        manifest.SchemaVersion = 2;
        manifest.LatestVersion = version;
        manifest.Packages.Add(new PublisherManifestPackage
        {
            Version = version,
            Type = packageType,
            Name = package.PackageName,
            DriveFileId = driveFileId,
            Sha256 = package.Sha256,
            RemovedPaths = normalizedRemovedPaths.ToList(),
        });

        await WriteJsonAsync(manifestPath, manifest);
    }

    public static PackageBuildResult CreatePackage(
        string projectPath,
        IReadOnlyCollection<SourceSelection> selections,
        string outputDirectory,
        string packageName)
    {
        if (!IsUnityProject(projectPath))
        {
            throw new InvalidOperationException("올바른 Unity 프로젝트 폴더를 선택하세요.");
        }

        if (selections.Count == 0)
        {
            throw new InvalidOperationException("압축할 파일 또는 폴더를 하나 이상 추가하세요.");
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("ZIP 저장 폴더를 선택하세요.");
        }

        packageName = ValidatePackageName(packageName);
        var externalAssetsPath = GetExternalAssetsPath(projectPath);
        if (!Directory.Exists(externalAssetsPath))
        {
            throw new InvalidOperationException("Assets/ExternalAssets 폴더를 찾을 수 없습니다.");
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            var fullPath = Path.GetFullPath(selection.FullPath);
            if (selection.IsFolder)
            {
                IncludeFolder(fullPath, externalAssetsPath, files, directories);
            }
            else
            {
                IncludeFileSelection(fullPath, externalAssetsPath, files, directories);
            }
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("압축할 파일이 없습니다.");
        }

        var outputFullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputFullDirectory);
        var zipPath = Path.Combine(outputFullDirectory, packageName);
        if (File.Exists(zipPath))
        {
            throw new InvalidOperationException($"같은 이름의 ZIP이 이미 있습니다: {zipPath}");
        }

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var relativeDirectory in directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                archive.CreateEntry(ToArchivePath(relativeDirectory).TrimEnd('/') + "/");
            }

            foreach (var pair in files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = archive.CreateEntry(ToArchivePath(pair.Key), CompressionLevel.Optimal);
                using var source = File.OpenRead(pair.Value);
                using var destination = entry.Open();
                source.CopyTo(destination);
            }
        }

        var hash = ComputeSha256(zipPath);
        var zipInfo = new FileInfo(zipPath);
        return new PackageBuildResult(
            zipPath,
            packageName,
            hash,
            zipInfo.Length,
            files.Count,
            files.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            zipInfo.LastWriteTimeUtc,
            PackageBuildOrigin.ProjectPatch);
    }

    /// <summary>
    /// Base Builder가 이미 검증해 만든 ZIP을 다시 압축하지 않고 manifest 등록용으로 읽는다.
    /// ZIP 구조와 .meta/GUID 기본 규칙을 다시 확인한 뒤 실제 파일의 SHA-256을 계산한다.
    /// </summary>
    public static PackageBuildResult LoadExistingBasePackage(string zipPath, string projectPath)
    {
        if (!IsUnityProject(projectPath))
        {
            throw new InvalidOperationException("Base ZIP을 등록할 ProjectS Unity 프로젝트를 선택하세요.");
        }

        if (string.IsNullOrWhiteSpace(zipPath))
        {
            throw new InvalidOperationException("Base Builder가 만든 ZIP 파일을 선택하세요.");
        }

        var fullPath = Path.GetFullPath(zipPath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("선택한 Base ZIP을 찾을 수 없습니다.", fullPath);
        }

        if (!fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Base Builder가 만든 .zip 파일만 등록할 수 있습니다.");
        }

        var fileInfo = new FileInfo(fullPath);
        var lengthBeforeValidation = fileInfo.Length;
        var lastWriteBeforeValidation = fileInfo.LastWriteTimeUtc;
        var rootMetaPath = GetExternalAssetsPath(Path.GetFullPath(projectPath.Trim())) + ".meta";
        if (!File.Exists(rootMetaPath))
        {
            throw new InvalidOperationException("Assets/ExternalAssets.meta가 없습니다. Git으로 추적 중인 루트 .meta를 먼저 복구하세요.");
        }

        var archiveEntries = ValidateExistingExternalAssetsArchive(fullPath, rootMetaPath);
        fileInfo.Refresh();
        if (fileInfo.Length != lengthBeforeValidation || fileInfo.LastWriteTimeUtc != lastWriteBeforeValidation)
        {
            throw new InvalidOperationException("검증 중 Base ZIP이 바뀌었습니다. 다시 선택하세요.");
        }

        var sha256 = ComputeSha256(fullPath);
        fileInfo.Refresh();
        if (fileInfo.Length != lengthBeforeValidation || fileInfo.LastWriteTimeUtc != lastWriteBeforeValidation)
        {
            throw new InvalidOperationException("SHA-256 계산 중 Base ZIP이 바뀌었습니다. 다시 선택하세요.");
        }

        return new PackageBuildResult(
            fullPath,
            Path.GetFileName(fullPath),
            sha256,
            fileInfo.Length,
            archiveEntries.Count,
            archiveEntries,
            fileInfo.LastWriteTimeUtc,
            PackageBuildOrigin.ImportedBase);
    }

    public static string GetRelativePathForDisplay(string projectPath, SourceSelection selection)
    {
        var externalAssetsPath = GetExternalAssetsPath(projectPath);
        return GetRelativePathUnderRoot(externalAssetsPath, selection.FullPath).Replace('\\', '/');
    }

    private static void IncludeFileSelection(
        string filePath,
        string externalAssetsPath,
        IDictionary<string, string> files,
        ISet<string> directories)
    {
        if (!File.Exists(filePath) || filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(".meta가 아닌 실제 파일을 선택하세요.");
        }

        EnsureUnderRoot(externalAssetsPath, filePath);
        AddFile(filePath, externalAssetsPath, files);
        var metaPath = filePath + ".meta";
        if (!File.Exists(metaPath))
        {
            throw new InvalidOperationException($"'{GetRelativePathUnderRoot(externalAssetsPath, filePath)}'의 .meta 파일이 없습니다.");
        }

        AddFile(metaPath, externalAssetsPath, files);
        AddAncestorFolderMetadata(Path.GetDirectoryName(filePath)!, externalAssetsPath, files, directories);
    }

    private static void IncludeFolder(
        string folderPath,
        string externalAssetsPath,
        IDictionary<string, string> files,
        ISet<string> directories)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new InvalidOperationException("선택한 폴더를 찾을 수 없습니다.");
        }

        EnsureUnderRoot(externalAssetsPath, folderPath);
        AddAncestorFolderMetadata(folderPath, externalAssetsPath, files, directories);

        foreach (var directory in Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories).Prepend(folderPath))
        {
            AddDirectory(directory, externalAssetsPath, directories);
            if (!PathsEqual(directory, externalAssetsPath))
            {
                var metaPath = directory + ".meta";
                if (!File.Exists(metaPath))
                {
                    throw new InvalidOperationException($"'{GetRelativePathUnderRoot(externalAssetsPath, directory)}' 폴더의 .meta 파일이 없습니다.");
                }

                AddFile(metaPath, externalAssetsPath, files);
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            AddFile(filePath, externalAssetsPath, files);
        }
    }

    private static void AddAncestorFolderMetadata(
        string folderPath,
        string externalAssetsPath,
        IDictionary<string, string> files,
        ISet<string> directories)
    {
        var current = Path.GetFullPath(folderPath);
        while (!PathsEqual(current, externalAssetsPath))
        {
            EnsureUnderRoot(externalAssetsPath, current);
            AddDirectory(current, externalAssetsPath, directories);

            var metaPath = current + ".meta";
            if (!File.Exists(metaPath))
            {
                throw new InvalidOperationException($"'{GetRelativePathUnderRoot(externalAssetsPath, current)}' 폴더의 .meta 파일이 없습니다.");
            }

            AddFile(metaPath, externalAssetsPath, files);
            current = Directory.GetParent(current)?.FullName
                ?? throw new InvalidOperationException("외부 에셋 루트의 상위 폴더를 확인할 수 없습니다.");
        }
    }

    private static void AddFile(string filePath, string externalAssetsPath, IDictionary<string, string> files)
    {
        var fullPath = Path.GetFullPath(filePath);
        EnsureUnderRoot(externalAssetsPath, fullPath);
        var relativePath = GetRelativePathUnderRoot(externalAssetsPath, fullPath);
        if (files.TryGetValue(relativePath, out var existingPath))
        {
            if (!PathsEqual(existingPath, fullPath))
            {
                throw new InvalidOperationException($"ZIP 안에 같은 경로가 중복됩니다: {relativePath}");
            }

            return;
        }

        files.Add(relativePath, fullPath);
    }

    private static void AddDirectory(string directoryPath, string externalAssetsPath, ISet<string> directories)
    {
        var relativePath = GetRelativePathUnderRoot(externalAssetsPath, directoryPath);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            directories.Add(relativePath);
        }
    }

    private static void EnsureUnderRoot(string rootPath, string candidatePath)
    {
        _ = GetRelativePathUnderRoot(rootPath, candidatePath);
    }

    private static string GetRelativePathUnderRoot(string rootPath, string candidatePath)
    {
        var rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidateFullPath = Path.GetFullPath(candidatePath);
        if (PathsEqual(rootFullPath, candidateFullPath))
        {
            return string.Empty;
        }

        var rootPrefix = rootFullPath + Path.DirectorySeparatorChar;
        if (!candidateFullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Assets/ExternalAssets 내부의 파일과 폴더만 패키징할 수 있습니다.");
        }

        return Path.GetRelativePath(rootFullPath, candidateFullPath);
    }

    private static string ValidatePackageName(string packageName)
    {
        packageName = packageName.Trim();
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new InvalidOperationException("ZIP 파일명을 입력하세요.");
        }

        if (!packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            packageName += ".zip";
        }

        if (packageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || packageName.Contains(Path.DirectorySeparatorChar)
            || packageName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("ZIP 파일명에는 폴더 경로를 넣을 수 없습니다.");
        }

        return packageName;
    }

    private static IReadOnlyList<string> NormalizeRemovedPaths(IReadOnlyList<string> removedPaths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in removedPaths)
        {
            var trimmed = path.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (Path.IsPathRooted(trimmed) || trimmed.Split(Path.DirectorySeparatorChar).Any(segment => segment == ".."))
            {
                throw new InvalidOperationException("삭제 경로는 ExternalAssets 기준 상대 경로만 사용할 수 있습니다.");
            }

            normalized.Add(trimmed.Replace(Path.DirectorySeparatorChar, '/'));
        }

        return normalized.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateExistingManifestForAppend(PublisherManifest manifest)
    {
        if (manifest.Packages.Count == 0)
        {
            if (manifest.LatestVersion != 0)
            {
                throw new InvalidOperationException("패키지가 없는 manifest의 latestVersion은 0이어야 합니다.");
            }

            return;
        }

        if (manifest.SchemaVersion != 2)
        {
            throw new InvalidOperationException("기존 manifest는 schemaVersion 2여야 합니다.");
        }

        var packages = manifest.Packages.OrderBy(package => package.Version).ToArray();
        if (manifest.LatestVersion != packages.Length)
        {
            throw new InvalidOperationException("기존 manifest의 latestVersion과 패키지 버전 연속성이 맞지 않습니다.");
        }

        for (var index = 0; index < packages.Length; index++)
        {
            var package = packages[index];
            var expectedVersion = index + 1;
            var expectedType = expectedVersion == 1 ? "base" : "patch";
            if (package.Version != expectedVersion
                || !string.Equals(package.Type, expectedType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"기존 manifest의 v{expectedVersion} 패키지는 {expectedType}이어야 합니다.");
            }

            if (string.IsNullOrWhiteSpace(package.Name)
                || !IsGoogleDriveFileId(package.DriveFileId)
                || package.Sha256.Length != 64
                || !package.Sha256.All(Uri.IsHexDigit)
                || package.RemovedPaths is null)
            {
                throw new InvalidOperationException($"기존 manifest의 v{expectedVersion} 패키지 정보가 올바르지 않습니다.");
            }

            var normalizedRemovedPaths = NormalizeRemovedPaths(package.RemovedPaths);
            if (expectedVersion == 1 && normalizedRemovedPaths.Count > 0)
            {
                throw new InvalidOperationException("기존 manifest의 Base v1에는 삭제할 상대 경로가 있으면 안 됩니다.");
            }
        }
    }

    private static void EnsurePackageStillMatches(PackageBuildResult package)
    {
        if (string.IsNullOrWhiteSpace(package.ZipPath) || !File.Exists(package.ZipPath))
        {
            throw new InvalidOperationException("등록할 ZIP 파일을 더 이상 찾을 수 없습니다. 다시 생성하거나 선택하세요.");
        }

        var fullPath = Path.GetFullPath(package.ZipPath);
        if (!string.Equals(Path.GetFileName(fullPath), package.PackageName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("등록할 ZIP 파일 이름이 검증 당시와 다릅니다. 다시 선택하세요.");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length != package.SizeBytes || fileInfo.LastWriteTimeUtc != package.LastWriteTimeUtc)
        {
            throw new InvalidOperationException("검증 또는 생성 뒤 ZIP 파일이 바뀌었습니다. SHA-256을 다시 확인하려면 다시 선택하거나 생성하세요.");
        }
    }

    private static IReadOnlyList<string> ValidateExistingExternalAssetsArchive(string zipPath, string rootMetaPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count == 0)
        {
            throw new InvalidOperationException("Base ZIP이 비어 있습니다.");
        }

        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var relativePath = ValidateExternalAssetsArchivePath(entry.FullName);
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            if (isDirectory)
            {
                if (!explicitDirectories.Add(relativePath) || files.ContainsKey(relativePath))
                {
                    throw new InvalidOperationException($"Base ZIP 안에 중복 경로가 있습니다: {relativePath}");
                }

                directories.Add(relativePath);
                continue;
            }

            if (!files.TryAdd(relativePath, entry) || directories.Contains(relativePath))
            {
                throw new InvalidOperationException($"Base ZIP 안에 중복 경로가 있습니다: {relativePath}");
            }

            AddArchiveAncestorDirectories(relativePath, directories);
        }

        var fileDirectoryCollisions = files.Keys
            .Where(path => directories.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (fileDirectoryCollisions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Base ZIP 안에 파일과 폴더가 같은 경로를 사용합니다: {string.Join(", ", fileDirectoryCollisions)}");
        }

        if (files.Count == 0)
        {
            throw new InvalidOperationException("Base ZIP에 설치할 파일이 없습니다.");
        }

        var guidPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryReadMetaGuid(rootMetaPath, out var rootGuid))
        {
            throw new InvalidOperationException("Assets/ExternalAssets.meta에서 Unity GUID를 읽을 수 없습니다.");
        }

        guidPaths.Add(rootGuid, "<Assets/ExternalAssets.meta>");
        foreach (var pair in files)
        {
            if (!pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                if (!files.ContainsKey(pair.Key + ".meta"))
                {
                    throw new InvalidOperationException($"Base ZIP의 '{pair.Key}'에 대응하는 .meta 파일이 없습니다.");
                }

                continue;
            }

            var metaTargetPath = pair.Key[..^".meta".Length];
            if (!files.ContainsKey(metaTargetPath) && !directories.Contains(metaTargetPath))
            {
                throw new InvalidOperationException($"Base ZIP의 '{pair.Key}'는 대응 파일 또는 폴더가 없는 고아 .meta입니다.");
            }

            if (!TryReadMetaGuid(pair.Value, out var guid))
            {
                throw new InvalidOperationException($"Base ZIP의 '{pair.Key}'에서 Unity GUID를 읽을 수 없습니다.");
            }

            if (guidPaths.TryGetValue(guid, out var existingPath))
            {
                throw new InvalidOperationException($"Base ZIP 안에서 Unity GUID가 중복됩니다: {existingPath}, {pair.Key}");
            }

            guidPaths.Add(guid, pair.Key);
        }

        foreach (var directory in directories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!files.ContainsKey(directory + ".meta"))
            {
                throw new InvalidOperationException($"Base ZIP의 '{directory}' 폴더 .meta 파일이 없습니다.");
            }
        }

        return files.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ValidateExternalAssetsArchivePath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.Contains('\\'))
        {
            throw new InvalidOperationException("Base ZIP에는 '/'를 사용하는 ExternalAssets 상대 경로만 포함할 수 있습니다.");
        }

        var relativePath = entryName.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.StartsWith("/", StringComparison.Ordinal)
            || relativePath.Contains(':'))
        {
            throw new InvalidOperationException($"Base ZIP 경로가 올바르지 않습니다: {entryName}");
        }

        var segments = relativePath.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new InvalidOperationException($"Base ZIP 경로가 올바르지 않습니다: {entryName}");
        }

        if (segments[0].Equals("Assets", StringComparison.OrdinalIgnoreCase)
            || segments[0].Equals("ExternalAssets", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("ExternalAssets.meta", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Base ZIP 최상위에는 Assets/, ExternalAssets/, Assets/ExternalAssets.meta를 넣을 수 없습니다.");
        }

        return relativePath;
    }

    private static void AddArchiveAncestorDirectories(string relativeFilePath, ISet<string> directories)
    {
        var separatorIndex = relativeFilePath.LastIndexOf('/');
        while (separatorIndex >= 0)
        {
            var directoryPath = relativeFilePath[..separatorIndex];
            directories.Add(directoryPath);
            separatorIndex = directoryPath.LastIndexOf('/');
        }
    }

    private static bool TryReadMetaGuid(ZipArchiveEntry entry, out string guid)
    {
        const long maximumMetaLength = 4L * 1024 * 1024;
        if (entry.Length > maximumMetaLength)
        {
            guid = string.Empty;
            return false;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: false);
        string? line;
        while ((line = reader.ReadLine()) is not null)
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

    private static bool TryReadMetaGuid(string metaPath, out string guid)
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

    private static string ParseGoogleDriveFileId(string source)
    {
        var trimmed = source.Trim();
        if (IsGoogleDriveFileId(trimmed))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ZIP의 제한된 Google Drive 파일 링크 또는 파일 ID를 입력하세요.");
        }

        const string filePathPrefix = "/file/d/";
        var startIndex = uri.AbsolutePath.IndexOf(filePathPrefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex >= 0)
        {
            var remainder = uri.AbsolutePath[(startIndex + filePathPrefix.Length)..];
            var candidate = Uri.UnescapeDataString(remainder.Split('/')[0]);
            if (IsGoogleDriveFileId(candidate))
            {
                return candidate;
            }
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2
                && string.Equals(Uri.UnescapeDataString(parts[0]), "id", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = Uri.UnescapeDataString(parts[1]);
                if (IsGoogleDriveFileId(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("ZIP의 Google Drive 파일 ID를 링크에서 찾을 수 없습니다.");
    }

    private static bool IsGoogleDriveFileId(string value) =>
        value.Length >= 10 && value.All(character =>
            (character >= 'a' && character <= 'z')
            || (character >= 'A' && character <= 'Z')
            || (character >= '0' && character <= '9')
            || character is '_' or '-');

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task WriteJsonAsync(string path, PublisherManifest manifest)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions);
        }

        File.Move(temporaryPath, fullPath, true);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string ToArchivePath(string path) => path.Replace('\\', '/');
}
