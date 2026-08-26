using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProjectS.ExternalAssetsPublisher;

/// <summary>
/// 배포자(Publisher)용 Google OAuth. 런처와 달리 manifest·ZIP·스냅샷을 <b>업로드/덮어쓰기</b>해야 하므로
/// 읽기 전용이 아닌 쓰기 스코프(<c>drive</c>)를 요청한다. 런처 토큰(readonly)과 섞이지 않도록
/// 별도 토큰 파일에 DPAPI로 저장한다. Desktop App Authorization Code + PKCE 흐름을 쓴다.
/// </summary>
internal sealed class PublisherGoogleOAuthClient : IDisposable
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevocationEndpoint = "https://oauth2.googleapis.com/revoke";

    // 기존 파일(웹에서 만든 manifest 등)을 덮어쓰려면 drive.file(앱 생성분만)로는 부족하다.
    // 전체 drive 스코프가 있어야 이미 존재하는 파일을 files.update로 덮어쓸 수 있다.
    private const string DriveWriteScope = "https://www.googleapis.com/auth/drive";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly byte[] TokenEntropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("ProjectS.ExternalAssetsPublisher.GoogleOAuth.v1"));

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    public bool HasSavedCredentials => File.Exists(GetTokenPath());

    public static string GetDefaultConfigurationPath() =>
        Path.Combine(AppContext.BaseDirectory, "google-oauth-client.json");

    public async Task SignInAsync(string configurationPath, CancellationToken cancellationToken)
    {
        var configuration = await LoadConfigurationAsync(configurationPath, cancellationToken);
        var token = await RunInteractiveAuthorizationAsync(configuration, cancellationToken);
        await SaveTokenAsync(token, cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(string configurationPath, CancellationToken cancellationToken)
    {
        var configuration = await LoadConfigurationAsync(configurationPath, cancellationToken);
        var token = await LoadTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("먼저 배포자에서 'Google 로그인(쓰기)' 버튼을 눌러 편집 권한이 있는 계정으로 로그인하세요.");

        if (!string.IsNullOrWhiteSpace(token.AccessToken)
            && token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return token.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException("Google 로그인 정보가 만료되었습니다. 다시 로그인하세요.");
        }

        var refreshedToken = await RefreshAccessTokenAsync(configuration, token, cancellationToken);
        await SaveTokenAsync(refreshedToken, cancellationToken);
        return refreshedToken.AccessToken;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = await LoadTokenAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(token?.RefreshToken))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, RevocationEndpoint)
                    {
                        Content = new FormUrlEncodedContent(
                        [
                            new KeyValuePair<string, string>("token", token.RefreshToken),
                        ]),
                    };
                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException)
                {
                    // 네트워크 오류여도 현재 PC의 토큰은 반드시 삭제한다.
                }
            }
        }
        finally
        {
            var tokenPath = GetTokenPath();
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<GoogleOAuthToken> RunInteractiveAuthorizationAsync(
        GoogleOAuthConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var loginTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, loginTimeout.Token);
        var loginCancellationToken = linkedCancellation.Token;
        var redirectUri = $"http://127.0.0.1:{GetUnusedLoopbackPort()}/";
        var state = CreateRandomUrlSafeValue();
        var codeVerifier = CreateRandomUrlSafeValue();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authorizationUri = BuildAuthorizationUri(configuration.ClientId, redirectUri, state, codeChallenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException exception)
        {
            throw new InvalidOperationException("Google 로그인용 로컬 콜백 서버를 열 수 없습니다. 방화벽 또는 보안 프로그램을 확인하세요.", exception);
        }

        try
        {
            Process.Start(new ProcessStartInfo(authorizationUri)
            {
                UseShellExecute = true,
            });

            var context = await listener.GetContextAsync().WaitAsync(loginCancellationToken);
            var responseState = context.Request.QueryString["state"];
            var authorizationError = context.Request.QueryString["error"];
            var authorizationErrorDescription = context.Request.QueryString["error_description"];

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state),
                    Encoding.UTF8.GetBytes(responseState ?? string.Empty)))
            {
                await WriteBrowserResponseAsync(context, false, "로그인 검증에 실패했습니다. 배포자로 돌아가 다시 시도하세요.");
                throw new InvalidOperationException("Google 로그인 응답의 state 검증에 실패했습니다.");
            }

            if (!string.IsNullOrWhiteSpace(authorizationError))
            {
                await WriteBrowserResponseAsync(context, false, "Google 로그인이 취소되었거나 권한이 거부되었습니다.");
                throw new InvalidOperationException(
                    $"Google 로그인에 실패했습니다: {authorizationError}{(string.IsNullOrWhiteSpace(authorizationErrorDescription) ? string.Empty : $" ({authorizationErrorDescription})")}");
            }

            var code = context.Request.QueryString["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(context, false, "인증 코드를 받지 못했습니다. 배포자로 돌아가 다시 시도하세요.");
                throw new InvalidOperationException("Google 로그인 응답에 인증 코드가 없습니다.");
            }

            await WriteBrowserResponseAsync(context, true, "Google 로그인이 완료되었습니다. 이 창을 닫고 배포자로 돌아가세요.");
            return await ExchangeAuthorizationCodeAsync(configuration, code, redirectUri, codeVerifier, loginCancellationToken);
        }
        catch (OperationCanceledException) when (loginTimeout.IsCancellationRequested)
        {
            throw new InvalidOperationException("Google 로그인이 5분 안에 완료되지 않았습니다. 다시 로그인하세요.");
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private async Task<GoogleOAuthToken> ExchangeAuthorizationCodeAsync(
        GoogleOAuthConfiguration configuration,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        };
        AddClientSecret(values, configuration.ClientSecret);
        var response = await RequestTokenAsync(values, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            throw new InvalidOperationException("Google 로그인에서 refresh token을 받지 못했습니다. 다시 로그인하세요.");
        }

        return ToStoredToken(response, response.RefreshToken);
    }

    private async Task<GoogleOAuthToken> RefreshAccessTokenAsync(
        GoogleOAuthConfiguration configuration,
        GoogleOAuthToken currentToken,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["refresh_token"] = currentToken.RefreshToken,
            ["grant_type"] = "refresh_token",
        };
        AddClientSecret(values, configuration.ClientSecret);
        var response = await RequestTokenAsync(values, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.AccessToken))
        {
            throw new InvalidOperationException("Google access token을 갱신하지 못했습니다. 다시 로그인하세요.");
        }

        return ToStoredToken(response, currentToken.RefreshToken);
    }

    private async Task<GoogleOAuthTokenResponse> RequestTokenAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values),
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<GoogleOAuthTokenResponse>(content, JsonOptions)
            ?? new GoogleOAuthTokenResponse();

        if (!response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace(tokenResponse.Error))
        {
            var reason = string.IsNullOrWhiteSpace(tokenResponse.ErrorDescription)
                ? tokenResponse.Error
                : $"{tokenResponse.Error}: {tokenResponse.ErrorDescription}";
            throw new InvalidOperationException($"Google OAuth 토큰 요청에 실패했습니다. {reason}".Trim());
        }

        return tokenResponse;
    }

    private static GoogleOAuthToken ToStoredToken(GoogleOAuthTokenResponse response, string refreshToken) => new()
    {
        AccessToken = response.AccessToken,
        RefreshToken = refreshToken,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(response.ExpiresIn, 60)),
    };

    private static void AddClientSecret(IDictionary<string, string> values, string? clientSecret)
    {
        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            values["client_secret"] = clientSecret;
        }
    }

    private static string BuildAuthorizationUri(string clientId, string redirectUri, string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveWriteScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        };

        return AuthorizationEndpoint + "?" + string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static int GetUnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateRandomUrlSafeValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string CreateCodeChallenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static async Task WriteBrowserResponseAsync(HttpListenerContext context, bool succeeded, string message)
    {
        var encodedMessage = WebUtility.HtmlEncode(message);
        var color = succeeded ? "#16803c" : "#b42318";
        var html = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>ProjectS Publisher</title></head><body style=\"font-family:Segoe UI,sans-serif;padding:32px\"><h2 style=\"color:{color}\">{encodedMessage}</h2></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = succeeded ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static async Task<GoogleOAuthConfiguration> LoadConfigurationAsync(string configurationPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(configurationPath ?? string.Empty);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException("Google OAuth Desktop 앱 JSON 파일을 선택하세요.");
        }

        await using var stream = File.OpenRead(fullPath);
        var file = await JsonSerializer.DeserializeAsync<GoogleOAuthConfigurationFile>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Google OAuth JSON 파일을 읽을 수 없습니다.");
        var source = file.Installed ?? file;
        var clientId = source.ClientIdSnake ?? source.ClientIdCamel;
        var clientSecret = source.ClientSecretSnake ?? source.ClientSecretCamel;
        if (string.IsNullOrWhiteSpace(clientId)
            || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google OAuth Desktop 앱 Client ID가 올바르지 않습니다.");
        }

        return new GoogleOAuthConfiguration(clientId, clientSecret ?? string.Empty);
    }

    private static async Task<GoogleOAuthToken?> LoadTokenAsync(CancellationToken cancellationToken)
    {
        var tokenPath = GetTokenPath();
        if (!File.Exists(tokenPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(tokenPath, cancellationToken);
            var json = ProtectedData.Unprotect(protectedBytes, TokenEntropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<GoogleOAuthToken>(json, JsonOptions);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("저장된 Google 로그인 정보를 읽을 수 없습니다. 로그아웃 후 다시 로그인하세요.", exception);
        }
    }

    private static async Task SaveTokenAsync(GoogleOAuthToken token, CancellationToken cancellationToken)
    {
        var tokenPath = GetTokenPath();
        Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(token, JsonOptions);
        var protectedBytes = ProtectedData.Protect(json, TokenEntropy, DataProtectionScope.CurrentUser);
        var temporaryPath = tokenPath + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
        File.Move(temporaryPath, tokenPath, true);
    }

    private static string GetTokenPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectSExternalAssetsPublisher",
        "google-oauth-token.dat");

    private sealed record GoogleOAuthConfiguration(string ClientId, string ClientSecret);

    private sealed class GoogleOAuthConfigurationFile
    {
        [JsonPropertyName("installed")]
        public GoogleOAuthConfigurationFile? Installed { get; init; }

        [JsonPropertyName("client_id")]
        public string? ClientIdSnake { get; init; }

        [JsonPropertyName("clientId")]
        public string? ClientIdCamel { get; init; }

        [JsonPropertyName("client_secret")]
        public string? ClientSecretSnake { get; init; }

        [JsonPropertyName("clientSecret")]
        public string? ClientSecretCamel { get; init; }
    }

    private sealed class GoogleOAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string Error { get; init; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string ErrorDescription { get; init; } = string.Empty;
    }

    private sealed class GoogleOAuthToken
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expiresAtUtc")]
        public DateTimeOffset ExpiresAtUtc { get; init; }
    }
}
