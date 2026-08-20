using System.IO.Compression;
using System.Security.Cryptography;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class ExternalAssetsUpdater
{
    private readonly RemoteManifestClient _manifestClient;

    public ExternalAssetsUpdater(RemoteManifestClient manifestClient)
    {
        _manifestClient = manifestClient;
    }

    public async Task<UpdatePlan> CheckForUpdatesAsync(
        string projectPath,
        string manifestUrl,
        CancellationToken cancellationToken)
    {
        var manifest = await _manifestClient.GetManifestAsync(manifestUrl, cancellationToken);
        var state = await ProjectServices.LoadInstalledStateAsync(projectPath);
        var externalAssetsLock = await ProjectServices.LoadLockAsync(projectPath);
        var requiredVersion = externalAssetsLock?.RequiredVersion > 0
            ? externalAssetsLock.RequiredVersion
            : manifest.LatestVersion;

        if (requiredVersion > manifest.LatestVersion)
        {
            throw new InvalidOperationException(
                $"프로젝트가 외부 에셋 v{requiredVersion}을 요구하지만 서버 최신 버전은 v{manifest.LatestVersion}입니다.");
        }

        if (state.InstalledVersion > requiredVersion)
        {
            return new UpdatePlan(manifest, state.InstalledVersion, requiredVersion, []);
        }

        var packages = manifest.Packages
            .Where(package => package.Version > state.InstalledVersion && package.Version <= requiredVersion)
            .OrderBy(package => package.Version)
            .ToList();

        var expectedVersion = state.InstalledVersion + 1;
        foreach (var package in packages)
        {
            ValidatePackage(package, expectedVersion);
            expectedVersion++;
        }

        if (expectedVersion - 1 != requiredVersion)
        {
            throw new InvalidOperationException(
                $"v{state.InstalledVersion}에서 v{requiredVersion}으로 가는 패치 정보가 완전하지 않습니다.");
        }

        return new UpdatePlan(manifest, state.InstalledVersion, requiredVersion, packages);
    }

    public async Task InstallAsync(
        string projectPath,
        UpdatePlan plan,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (plan.RequiresReset)
        {
            throw new InvalidOperationException(
                "현재 설치된 외부 에셋이 프로젝트 요구 버전보다 새롭습니다. 전체본으로 복구하는 기능은 아직 지원하지 않습니다.");
        }

        var externalAssetsPath = ProjectServices.GetExternalAssetsPath(projectPath);
        Directory.CreateDirectory(externalAssetsPath);

        foreach (var package in plan.Packages)
        {
            await InstallPackageAsync(externalAssetsPath, package, progress, cancellationToken);
            await ProjectServices.SaveInstalledStateAsync(projectPath, package.Version);
        }
    }

    private async Task InstallPackageAsync(
        string externalAssetsPath,
        ExternalAssetsPackage package,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ProjectSExternalAssetsLauncher", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(temporaryRoot, "package.zip");
        var stagingPath = Path.Combine(temporaryRoot, "staging");

        try
        {
            progress?.Report(new DownloadProgress($"v{package.Version} {package.Name} 준비", 0, null));
            await _manifestClient.DownloadFileAsync(package.DownloadUrl, packagePath, progress, cancellationToken);
            await VerifySha256Async(packagePath, package.Sha256, cancellationToken);
            await ExtractToStagingAsync(packagePath, stagingPath, cancellationToken);
            ValidateMetaFiles(stagingPath, externalAssetsPath);
            ApplyStaging(stagingPath, externalAssetsPath, package.RemovedPaths);
            progress?.Report(new DownloadProgress($"v{package.Version} 설치 완료", 0, null));
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

        if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"v{package.Version}의 다운로드 링크가 올바르지 않습니다.");
        }

        if (package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException($"v{package.Version}의 SHA-256 값이 올바르지 않습니다.");
        }
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

    private static async Task ExtractToStagingAsync(string packagePath, string stagingPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingPath);
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var destinationPath = GetSafePath(stagingPath, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var destinationStream = File.Create(destinationPath);
            await entryStream.CopyToAsync(destinationStream, cancellationToken);
        }
    }

    private static void ValidateMetaFiles(string stagingPath, string externalAssetsPath)
    {
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

    private static void ApplyStaging(string stagingPath, string externalAssetsPath, IReadOnlyCollection<string> removedPaths)
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "ProjectSExternalAssetsLauncher", "backup", Guid.NewGuid().ToString("N"));
        var operations = new List<FileOperation>();

        try
        {
            foreach (var sourcePath in Directory.EnumerateFiles(stagingPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(stagingPath, sourcePath);
                var destinationPath = GetSafePath(externalAssetsPath, relativePath);
                operations.Add(BackupExistingFile(destinationPath, externalAssetsPath, backupPath));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, true);
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
