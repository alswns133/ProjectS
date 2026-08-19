using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectS.ExternalAssetsLauncher;

internal sealed class RemoteManifestClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;

    public RemoteManifestClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ProjectSExternalAssetsLauncher", "1.0"));
    }

    public async Task<ExternalAssetsManifest> GetManifestAsync(string manifestUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new InvalidOperationException("매니페스트 링크를 입력하세요.");
        }

        var content = await DownloadTextAsync(manifestUrl, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ExternalAssetsManifest>(content, JsonOptions)
            ?? throw new InvalidOperationException("매니페스트 JSON을 읽을 수 없습니다.");

        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"지원하지 않는 매니페스트 형식입니다: {manifest.SchemaVersion}");
        }

        if (manifest.LatestVersion < 1)
        {
            throw new InvalidOperationException("latestVersion은 1 이상이어야 합니다.");
        }

        return manifest;
    }

    public async Task DownloadFileAsync(
        string sourceUrl,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var requestUrl = NormalizeGoogleDriveUrl(sourceUrl);
        using var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException("Drive가 파일 대신 HTML 페이지를 반환했습니다. 파일 공유 권한과 링크를 확인하세요.");
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

    public void Dispose() => _httpClient.Dispose();

    private async Task<string> DownloadTextAsync(string sourceUrl, CancellationToken cancellationToken)
    {
        var requestUrl = NormalizeGoogleDriveUrl(sourceUrl);
        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string NormalizeGoogleDriveUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || !uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return sourceUrl;
        }

        var match = Regex.Match(uri.AbsolutePath, @"/file/d/([^/]+)", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return sourceUrl;
        }

        var fileId = Uri.EscapeDataString(match.Groups[1].Value);
        return $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t";
    }
}
