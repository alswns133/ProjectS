using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectS.ExternalAssetsPublisher;

/// <summary>
/// 제한된 Google Drive에 파일을 올리고(신규) 덮어쓰는(기존) 최소 REST 클라이언트.
/// - 새 ZIP·스냅샷: multipart 업로드로 폴더에 만들고 파일 ID를 돌려준다.
/// - manifest: files.update(media)로 <b>같은 파일 ID를 유지한 채</b> 덮어쓴다(런처 참조가 안 끊기게).
/// - 여러 배포자 충돌 방지: 덮어쓰기 전 headRevisionId를 비교해, 내가 받은 뒤 누가 먼저 올렸으면 막는다.
/// Shared Drive도 지원하도록 supportsAllDrives=true를 붙인다.
/// </summary>
internal sealed class DriveUploadClient : IDisposable
{
    private const string FilesBase = "https://www.googleapis.com/drive/v3/files";
    private const string UploadBase = "https://www.googleapis.com/upload/drive/v3/files";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    public void Dispose() => _httpClient.Dispose();

    /// <summary>링크나 raw ID에서 Drive 파일/폴더 ID를 뽑는다.</summary>
    public static string ExtractId(string source)
    {
        var trimmed = (source ?? string.Empty).Trim();
        if (IsRawId(trimmed))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var marker in (string[])["/file/d/", "/folders/", "/d/"])
            {
                var index = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var candidate = Uri.UnescapeDataString(uri.AbsolutePath[(index + marker.Length)..].Split('/')[0]);
                    if (IsRawId(candidate))
                    {
                        return candidate;
                    }
                }
            }

            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2
                    && string.Equals(Uri.UnescapeDataString(parts[0]), "id", StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = Uri.UnescapeDataString(parts[1]);
                    if (IsRawId(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Drive 링크 또는 ID가 올바르지 않습니다: {source}");
    }

    private static bool IsRawId(string value) =>
        value.Length >= 10 && value.All(character =>
            (character >= 'a' && character <= 'z')
            || (character >= 'A' && character <= 'Z')
            || (character >= '0' && character <= '9')
            || character is '_' or '-');

    public async Task<DriveFileInfo> GetFileInfoAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        var url = $"{FilesBase}/{Uri.EscapeDataString(fileId)}?fields=id,name,headRevisionId&supportsAllDrives=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowIfFailedAsync(response, content, $"파일 정보 조회(fileId={fileId})");
        var info = JsonSerializer.Deserialize<DriveFileInfo>(content, JsonOptions)
            ?? throw new InvalidOperationException("Drive 파일 정보를 읽을 수 없습니다.");
        return info;
    }

    public async Task<string> DownloadTextAsync(string accessToken, string fileId, CancellationToken cancellationToken)
    {
        var url = $"{FilesBase}/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowIfFailedAsync(response, content, $"파일 다운로드(fileId={fileId})");
        return content;
    }

    /// <summary>새 파일을 지정 폴더에 만들고 파일 ID를 돌려준다(multipart 업로드).</summary>
    public async Task<string> UploadNewFileAsync(
        string accessToken,
        string parentFolderId,
        string filePath,
        string name,
        string contentType,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["name"] = name,
            ["parents"] = new[] { parentFolderId },
        });

        using var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        await using var fileStream = File.OpenRead(filePath);
        var mediaContent = new StreamContent(fileStream);
        mediaContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(mediaContent);

        var url = $"{UploadBase}?uploadType=multipart&supportsAllDrives=true&fields=id";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = multipart };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowIfFailedAsync(response, content, $"새 파일 업로드({name})");
        var info = JsonSerializer.Deserialize<DriveFileInfo>(content, JsonOptions);
        if (info is null || string.IsNullOrWhiteSpace(info.Id))
        {
            throw new InvalidOperationException($"업로드는 됐지만 파일 ID를 받지 못했습니다: {name}");
        }

        return info.Id;
    }

    /// <summary>기존 파일 내용을 덮어쓴다(파일 ID 유지). 새 headRevisionId를 돌려준다.</summary>
    public async Task<string> UpdateFileMediaAsync(
        string accessToken,
        string fileId,
        string filePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        var url = $"{UploadBase}/{Uri.EscapeDataString(fileId)}?uploadType=media&supportsAllDrives=true&fields=id,headRevisionId";
        await using var fileStream = File.OpenRead(filePath);
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StreamContent(fileStream),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        await ThrowIfFailedAsync(response, content, $"파일 덮어쓰기(fileId={fileId})");
        var info = JsonSerializer.Deserialize<DriveFileInfo>(content, JsonOptions);
        return info?.HeadRevisionId ?? string.Empty;
    }

    private static Task ThrowIfFailedAsync(HttpResponseMessage response, string content, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        var reason = string.IsNullOrWhiteSpace(content) ? response.ReasonPhrase : content;
        var hint = response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized
            ? " (권한/스코프 문제일 수 있습니다. 쓰기 스코프로 다시 로그인했는지, 이 계정에 편집 권한이 있는지 확인하세요.)"
            : string.Empty;
        throw new InvalidOperationException($"Drive {action} 실패: {(int)response.StatusCode} {reason}{hint}");
    }

    internal sealed class DriveFileInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HeadRevisionId { get; set; } = string.Empty;
    }
}
