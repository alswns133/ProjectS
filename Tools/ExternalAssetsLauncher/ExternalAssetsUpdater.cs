using System.IO.Compression;
using System.Security.Cryptography;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class ExternalAssetsUpdater
{
    private readonly GoogleDriveClient _driveClient;

    public ExternalAssetsUpdater(GoogleDriveClient driveClient)
    {
        _driveClient = driveClient;
    }

    public async Task<UpdatePlan> CheckForUpdatesAsync(
        string projectPath,
        string manifestFileId,
        string oauthClientConfigurationPath,
        CancellationToken cancellationToken)
    {
        var manifest = await _driveClient.GetManifestAsync(
            manifestFileId,
            oauthClientConfigurationPath,
            cancellationToken);
        var state = await ProjectServices.LoadInstalledStateAsync(projectPath);
        ValidateManifest(manifest);

        var externalAssetsPath = ProjectServices.GetExternalAssetsPath(projectPath);
        var requiresLegacyMigration = state.StateSchemaVersion < 2
            && (state.InstalledVersion > 0 || HasExternalAssetsContents(externalAssetsPath));
        if (!requiresLegacyMigration
            && state.InstalledVersion > 0
            && !string.Equals(state.ChannelId, manifest.ChannelId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "현재 설치된 외부 에셋은 다른 배포 채널에서 왔습니다. 자동으로 덮어쓰지 않았습니다. 전체본으로 수동 복구한 뒤 다시 시도하세요.");
        }

        var installedVersion = requiresLegacyMigration ? 0 : state.InstalledVersion;
        var externalAssetsLock = await ProjectServices.LoadLockAsync(projectPath);
        var requiredVersion = externalAssetsLock?.RequiredVersion > 0
            ? externalAssetsLock.RequiredVersion
            : manifest.LatestVersion;

        if (requiredVersion > manifest.LatestVersion)
        {
            throw new InvalidOperationException(
                $"프로젝트가 외부 에셋 v{requiredVersion}을 요구하지만 서버 최신 버전은 v{manifest.LatestVersion}입니다.");
        }

        if (installedVersion > requiredVersion)
        {
            return new UpdatePlan(manifest, installedVersion, requiredVersion, []);
        }

        var packages = manifest.Packages
            .Where(package => package.Version > installedVersion && package.Version <= requiredVersion)
            .OrderBy(package => package.Version)
            .ToList();

        var expectedVersion = installedVersion + 1;
        foreach (var package in packages)
        {
            ValidatePackage(package, expectedVersion);
            expectedVersion++;
        }

        if (expectedVersion - 1 != requiredVersion)
        {
            throw new InvalidOperationException(
                $"v{installedVersion}에서 v{requiredVersion}으로 가는 패치 정보가 완전하지 않습니다.");
        }

        return new UpdatePlan(manifest, installedVersion, requiredVersion, packages)
        {
            RequiresLegacyMigration = requiresLegacyMigration,
        };
    }

    public async Task<InstallationResult> InstallAsync(
        string projectPath,
        UpdatePlan plan,
        string oauthClientConfigurationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (plan.RequiresReset)
        {
            throw new InvalidOperationException(
                "현재 설치된 외부 에셋이 프로젝트 요구 버전보다 새롭습니다. 전체본으로 복구하는 기능은 아직 지원하지 않습니다.");
        }

        var externalAssetsPath = ProjectServices.GetExternalAssetsPath(projectPath);
        string? legacyBackupPath = null;
        var installedNewPackage = false;
        try
        {
            if (plan.RequiresLegacyMigration)
            {
                legacyBackupPath = ArchiveLegacyExternalAssets(projectPath);
            }

            Directory.CreateDirectory(externalAssetsPath);
            foreach (var package in plan.Packages)
            {
                await InstallPackageAsync(externalAssetsPath, package, oauthClientConfigurationPath, progress, cancellationToken);
                await ProjectServices.SaveInstalledStateAsync(projectPath, package.Version, plan.Manifest.ChannelId);
                installedNewPackage = true;
            }

            return new InstallationResult(legacyBackupPath);
        }
        catch (Exception exception)
        {
            if (plan.RequiresLegacyMigration && !installedNewPackage && legacyBackupPath is not null)
            {
                try
                {
                    RestoreLegacyExternalAssets(externalAssetsPath, legacyBackupPath);
                }
                catch (Exception restorationException)
                {
                    throw new InvalidOperationException(
                        "보안 전환 설치에 실패했고 기존 외부 에셋 백업의 자동 복원에도 실패했습니다. 프로젝트 루트의 ExternalAssetsLegacyBackups를 확인하세요.",
                        new AggregateException(exception, restorationException));
                }
            }

            throw;
        }
    }

    private async Task InstallPackageAsync(
        string externalAssetsPath,
        ExternalAssetsPackage package,
        string oauthClientConfigurationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ProjectSExternalAssetsLauncher", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(temporaryRoot, "package.zip");
        var stagingPath = Path.Combine(temporaryRoot, "staging");

        try
        {
            progress?.Report(new DownloadProgress($"v{package.Version} {package.Name} 준비", 0, null));
            await _driveClient.DownloadFileAsync(
                package.DriveFileId,
                oauthClientConfigurationPath,
                packagePath,
                progress,
                cancellationToken);
            await VerifySha256Async(packagePath, package.Sha256, cancellationToken);
            await ExtractToStagingAsync(packagePath, stagingPath, package.Version, progress, cancellationToken);
            progress?.Report(new DownloadProgress($"⏳ v{package.Version} 파일 검사 중 — 정상 진행 중이에요. 창을 닫지 마세요.", 0, null));
            ValidateMetaFiles(stagingPath, externalAssetsPath);
            ApplyStaging(stagingPath, externalAssetsPath, package.RemovedPaths, package.Version, progress);
            progress?.Report(new DownloadProgress($"✅ v{package.Version} 설치 완료", 0, null));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }

    private static void ValidatePackage(ExternalAssetsPackage package, int expectedVersion)
    {
        if (package.Version != expectedVersion)
        {
            throw new InvalidOperationException($"패치 버전이 연속되지 않습니다. v{expectedVersion} 패키지가 필요합니다.");
        }

        var expectedType = expectedVersion == 1 ? "base" : "patch";
        if (!string.Equals(package.Type, expectedType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"v{package.Version} 패키지 종류는 {expectedType}이어야 합니다.");
        }

        if (!GoogleDriveFileId.TryParse(package.DriveFileId, out _))
        {
            throw new InvalidOperationException($"v{package.Version}의 Google Drive 파일 ID가 올바르지 않습니다.");
        }

        if (package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException($"v{package.Version}의 SHA-256 값이 올바르지 않습니다.");
        }
    }

    private static void ValidateManifest(ExternalAssetsManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.ChannelId))
        {
            throw new InvalidOperationException("manifest의 channelId가 없습니다. 제한된 Drive용 새 manifest를 생성하세요.");
        }
    }

    private static string? ArchiveLegacyExternalAssets(string projectPath)
    {
        var externalAssetsPath = ProjectServices.GetExternalAssetsPath(projectPath);
        if (!Directory.Exists(externalAssetsPath))
        {
            return null;
        }

        var backupRoot = Path.Combine(projectPath, "ExternalAssetsLegacyBackups");
        Directory.CreateDirectory(backupRoot);
        var backupPath = Path.Combine(
            backupRoot,
            $"ExternalAssets-before-secure-migration-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
        Directory.Move(externalAssetsPath, backupPath);
        return backupPath;
    }

    private static bool HasExternalAssetsContents(string externalAssetsPath) =>
        Directory.Exists(externalAssetsPath)
        && Directory.EnumerateFileSystemEntries(externalAssetsPath).Any();

    private static void RestoreLegacyExternalAssets(string externalAssetsPath, string legacyBackupPath)
    {
        if (!Directory.Exists(legacyBackupPath))
        {
            throw new DirectoryNotFoundException($"기존 외부 에셋 백업을 찾을 수 없습니다: {legacyBackupPath}");
        }

        if (Directory.Exists(externalAssetsPath))
        {
            Directory.Delete(externalAssetsPath, true);
        }

        Directory.Move(legacyBackupPath, externalAssetsPath);
    }

    private static async Task VerifySha256Async(string packagePath, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(packagePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualHash = Convert.ToHexString(hash);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("다운로드한 ZIP의 SHA-256 검사가 실패했습니다. 기존 외부 에셋은 변경하지 않았습니다.");
        }
    }

    private static async Task ExtractToStagingAsync(
        string packagePath,
        string stagingPath,
        int version,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingPath);
        using var archive = ZipFile.OpenRead(packagePath);
        var totalEntries = archive.Entries.Count;
        var processedEntries = 0;
        foreach (var entry in archive.Entries)
        {
            var destinationPath = GetSafePath(stagingPath, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var entryStream = entry.Open();
                await using var destinationStream = File.Create(destinationPath);
                await entryStream.CopyToAsync(destinationStream, cancellationToken);
            }

            processedEntries++;
            // 항목마다 UI를 갱신하면 과부하라 일정 간격과 마지막 항목에서만 보고한다.
            if (processedEntries % 200 == 0 || processedEntries == totalEntries)
            {
                progress?.Report(new DownloadProgress(
                    $"⏳ v{version} 압축 해제 {processedEntries}/{totalEntries} — 창을 닫지 마세요.",
                    processedEntries,
                    totalEntries));
            }
        }
    }

    private static void ValidateMetaFiles(string stagingPath, string externalAssetsPath)
    {
        foreach (var sourceDirectory in Directory.EnumerateDirectories(stagingPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(stagingPath, sourceDirectory);
            var stagedMetaPath = sourceDirectory + ".meta";
            var existingMetaPath = GetSafePath(externalAssetsPath, relativePath + ".meta");
            if (!File.Exists(stagedMetaPath) && !File.Exists(existingMetaPath))
            {
                throw new InvalidOperationException(
                    $"'{relativePath}' 폴더의 .meta 파일이 없습니다. 기존 외부 에셋은 변경하지 않았습니다.");
            }
        }

        foreach (var sourcePath in Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories))
        {
            if (sourcePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(stagingPath, sourcePath);
            var stagedMetaPath = sourcePath + ".meta";
            var existingMetaPath = GetSafePath(externalAssetsPath, relativePath + ".meta");
            if (!File.Exists(stagedMetaPath) && !File.Exists(existingMetaPath))
            {
                throw new InvalidOperationException(
                    $"'{relativePath}'에 대응하는 .meta 파일이 없습니다. 기존 외부 에셋은 변경하지 않았습니다.");
            }
        }
    }

    private static void ApplyStaging(
        string stagingPath,
        string externalAssetsPath,
        IReadOnlyCollection<string> removedPaths,
        int version,
        IProgress<DownloadProgress>? progress)
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "ProjectSExternalAssetsLauncher", "backup", Guid.NewGuid().ToString("N"));
        var operations = new List<FileOperation>();
        var createdDirectories = new List<string>();

        try
        {
            foreach (var sourceDirectory in Directory.EnumerateDirectories(stagingPath, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(stagingPath, sourceDirectory);
                var destinationPath = GetSafePath(externalAssetsPath, relativePath);
                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                    createdDirectories.Add(destinationPath);
                }
            }

            var sourceFiles = Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories).ToList();
            var totalFiles = sourceFiles.Count;
            var copiedFiles = 0;
            foreach (var sourcePath in sourceFiles)
            {
                var relativePath = Path.GetRelativePath(stagingPath, sourcePath);
                var destinationPath = GetSafePath(externalAssetsPath, relativePath);
                operations.Add(BackupExistingFile(destinationPath, externalAssetsPath, backupPath));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, true);

                copiedFiles++;
                // 대량 파일 복사가 이 단계의 병목이라, 여기서 퍼센트를 갱신해 "멈춘 게 아님"을 보여준다.
                if (copiedFiles % 100 == 0 || copiedFiles == totalFiles)
                {
                    progress?.Report(new DownloadProgress(
                        $"⏳ v{version} 설치 중 {copiedFiles}/{totalFiles} — 창을 닫지 마세요.",
                        copiedFiles,
                        totalFiles));
                }
            }

            foreach (var relativePath in removedPaths)
            {
                var destinationPath = GetSafePath(externalAssetsPath, relativePath);
                if (!File.Exists(destinationPath))
                {
                    continue;
                }

                operations.Add(BackupExistingFile(destinationPath, externalAssetsPath, backupPath));
                File.Delete(destinationPath);
            }
        }
        catch
        {
            RestoreFiles(operations);
            foreach (var directory in createdDirectories.OrderByDescending(path => path.Length))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
        }
    }

    private static FileOperation BackupExistingFile(string destinationPath, string externalAssetsPath, string backupPath)
    {
        var existed = File.Exists(destinationPath);
        var backupFilePath = string.Empty;
        if (existed)
        {
            var relativePath = Path.GetRelativePath(externalAssetsPath, destinationPath);
            backupFilePath = GetSafePath(backupPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
            File.Copy(destinationPath, backupFilePath, true);
        }

        return new FileOperation(destinationPath, backupFilePath, existed);
    }

    private static void RestoreFiles(IEnumerable<FileOperation> operations)
    {
        foreach (var operation in operations.Reverse())
        {
            if (operation.Existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationPath)!);
                File.Copy(operation.BackupPath, operation.DestinationPath, true);
            }
            else if (File.Exists(operation.DestinationPath))
            {
                File.Delete(operation.DestinationPath);
            }
        }
    }

    private static string GetSafePath(string rootPath, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("절대 경로는 외부 에셋 패키지에 포함할 수 없습니다.");
        }

        var rootFullPath = Path.GetFullPath(rootPath);
        var destinationPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));
        var rootPrefix = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("패키지에 허용되지 않은 상위 경로가 포함되어 있습니다.");
        }

        return destinationPath;
    }

    private sealed record FileOperation(string DestinationPath, string BackupPath, bool Existed);
}
