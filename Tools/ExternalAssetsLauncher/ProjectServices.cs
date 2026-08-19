using System.Diagnostics;
using System.Text.Json;

namespace ProjectS.ExternalAssetsLauncher;

internal static class ProjectServices
{
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

    public static string GetStatePath(string projectPath) =>
        Path.Combine(projectPath, "Library", "ExternalAssetsLauncher", "installed-state.json");

    public static string GetLockPath(string projectPath) =>
        Path.Combine(projectPath, "ExternalAssets.lock.json");

    public static bool IsUnityEditorRunning()
    {
        var unityProcesses = Process.GetProcessesByName("Unity");
        try
        {
            return unityProcesses.Length > 0;
        }
        finally
        {
            foreach (var process in unityProcesses)
            {
                process.Dispose();
            }
        }
    }

    public static async Task<LauncherSettings> LoadSettingsAsync()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return new LauncherSettings();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions)
            ?? new LauncherSettings();
    }

    public static async Task SaveSettingsAsync(LauncherSettings settings)
    {
        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await WriteJsonAsync(path, settings);
    }

    public static async Task<InstalledState> LoadInstalledStateAsync(string projectPath)
    {
        var path = GetStatePath(projectPath);
        if (!File.Exists(path))
        {
            return new InstalledState();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<InstalledState>(stream, JsonOptions)
            ?? new InstalledState();
    }

    public static Task SaveInstalledStateAsync(string projectPath, int version) =>
        WriteJsonAsync(GetStatePath(projectPath), new InstalledState
        {
            InstalledVersion = version,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

    public static async Task<ExternalAssetsLock?> LoadLockAsync(string projectPath)
    {
        var path = GetLockPath(projectPath);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ExternalAssetsLock>(stream, JsonOptions);
    }

    public static string? FindUnityExecutable(string projectPath)
    {
        var versionPath = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(versionPath))
        {
            return null;
        }

        var versionLine = File.ReadLines(versionPath)
            .FirstOrDefault(line => line.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
        var version = versionLine?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidate = Path.Combine(programFiles, "Unity", "Hub", "Editor", version, "Editor", "Unity.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string GetSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectSExternalAssetsLauncher",
        "settings.json");

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
        }

        File.Move(temporaryPath, path, true);
    }
}
