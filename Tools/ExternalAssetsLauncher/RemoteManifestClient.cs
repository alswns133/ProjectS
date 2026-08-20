using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectS.ExternalAssetsLauncher;

/// <summary>
/// 제한된 Google Drive 파일을 OAuth access token으로 읽는다.
/// manifest와 ZIP 모두 Drive file ID만 사용하며 공개 다운로드 URL로 변환하지 않는다.
/// </summary>
internal sealed class GoogleDriveClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly GoogleOAuthClient _oauthClient;
    private readonly HttpClient _httpClient;

    public GoogleDriveClient(GoogleOAuthClient oauthClient)
    {
        _oauthClient = oauthClient;
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ProjectSExternalAssetsLauncher", "2.0"));
    }

    public Task SignInAsync(string oauthClientConfigurationPath, CancellationToken cancellationToken) =>
        _oauthClient.SignInAsync(oauthClientConfigurationPath, cancellationToken);

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        _oauthClient.SignOutAsync(cancellationToken);

    public bool HasSavedCredentials => _oauthClient.HasSavedCredentials;

    public async Task<ExternalAssetsManifest> GetManifestAsync(
        string manifestFileSource,
        string oauthClientConfigurationPath,
        CancellationToken cancellationToken)
    {
        var manifestFileId = GoogleDriveFileId.Parse(manifestFileSource);
        var content = await DownloadTextAsync(manifestFileId, oauthClientConfigurationPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ExternalAssetsManifest>(content, JsonOptions)
            ?? throw new InvalidOperationException("매니페스트 JSON을 읽을 수 없습니다.");

        if (manifest.SchemaVersion != 2)
        {
            throw new InvalidOperationException(
                $"지원하지 않는 매니페스트 형식입니다: {manifest.SchemaVersion}. 제한된 Drive용 schemaVersion 2 manifest를 사용하세요.");
        }

        if (manifest.LatestVersion < 1)
        {
            throw new InvalidOperationException("latestVersion은 1 이상이어야 합니다.");
        }

        return manifest;
    }

    public async Task DownloadFileAsync(
        string driveFileSource,
        string oauthClientConfigurationPath,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var driveFileId = GoogleDriveFileId.Parse(driveFileSource);
        using var response = await SendDownloadRequestAsync(driveFileId, oauthClientConfigurationPath, cancellationToken);
        EnsureDriveSuccess(response);

        if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("Google Drive가 파일 대신 HTML을 반환했습니다. 팀 Drive 권한과 파일 ID를 확인하세요.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);

        var totalBytes = response.Content.Headers.ContentLength;
        var buffer = new byte[1024 * 128];
        long receivedBytes = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            receivedBytes += read;
            progress?.Report(new DownloadProgress("다운로드 중", receivedBytes, totalBytes));
        }

        progress?.Report(new DownloadProgress("다운로드 완료", receivedBytes, totalBytes));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _oauthClient.Dispose();
    }

    private async Task<string> DownloadTextAsync(
        string driveFileId,
        string oauthClientConfigurationPath,
        CancellationToken cancellationToken)
    {
        using var response = await SendDownloadRequestAsync(driveFileId, oauthClientConfigurationPath, cancellationToken);
        EnsureDriveSuccess(response);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(
        string driveFileId,
        string oauthClientConfigurationPath,
        CancellationToken cancellationToken)
    {
        var accessToken = await _oauthClient.GetAccessTokenAsync(oauthClientConfigurationPath, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(driveFileId)}?alt=media&supportsAllDrives=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static void EnsureDriveSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Google 로그인 정보가 만료되었습니다. 런처에서 다시 로그인하세요.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("현재 로그인한 Google 계정에는 이 외부 에셋 파일 접근 권한이 없습니다.");
        }

        throw new InvalidOperationException($"Google Drive 파일을 다운로드하지 못했습니다. HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}

internal static class GoogleDriveFileId
{
    private static readonly Regex FileIdPattern = new("^[A-Za-z0-9_-]{10,}$", RegexOptions.CultureInvariant);
    private static readonly Regex PathIdPattern = new("/file/d/([^/?#]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Parse(string source)
    {
        if (TryParse(source, out var fileId))
        {
            return fileId;
        }

        throw new InvalidOperationException("Google Drive 파일 링크 또는 파일 ID가 올바르지 않습니다.");
    }

    public static bool TryParse(string? source, out string fileId)
    {
        fileId = string.Empty;
        var trimmed = source?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (FileIdPattern.IsMatch(trimmed))
        {
            fileId = trimmed;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathMatch = PathIdPattern.Match(uri.AbsolutePath);
        if (pathMatch.Success && TryValidate(pathMatch.Groups[1].Value, out fileId))
        {
            return true;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2
                && string.Equals(Uri.UnescapeDataString(parts[0]), "id", StringComparison.OrdinalIgnoreCase)
                && TryValidate(Uri.UnescapeDataString(parts[1]), out fileId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryValidate(string candidate, out string fileId)
    {
        fileId = Uri.UnescapeDataString(candidate);
        if (FileIdPattern.IsMatch(fileId))
        {
            return true;
        }

        fileId = string.Empty;
        return false;
    }
}
