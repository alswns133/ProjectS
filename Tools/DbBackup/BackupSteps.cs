using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;

namespace ProjectS.DbBackup;

/// <summary>
/// 백업의 각 단계. <see cref="Program"/>이 순서대로 부른다.
/// </summary>
/// <remarks>
/// 폴더 구조(BackupRoot 기준):
/// <code>
/// hourly/2026-09-18/14.json          ← 정각마다 새 파일
/// daily/2026-09-17.json              ← 그날 마지막 정상 시간본(지난 날짜가 되면 확정)
/// characters/2026-09-17/{uid}_{uniqueId}_{이름}.json   ← 캐릭터 하나만(그 노드에 가져오기용)
/// </code>
/// </remarks>
internal static class BackupSteps
{
    private const string DateFormat = "yyyy-MM-dd";

    // 사람이 열어 보기 쉽게 들여쓰기. 한글 이름이 \uXXXX로 깨지지 않게 인코더를 완화한다.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // RTDB REST에 필요한 권한 범위. 둘 중 하나라도 빠지면 401이 난다.
    private static readonly string[] Scopes =
    {
        "https://www.googleapis.com/auth/firebase.database",
        "https://www.googleapis.com/auth/userinfo.email",
    };

    // ── ① 읽기 ─────────────────────────────────────────────

    /// <summary>
    /// ① DB 전체를 읽는다. 서비스 계정 키로 액세스 토큰을 받아 REST <c>GET {DatabaseUrl}/.json</c>을 부른다.
    /// </summary>
    /// <remarks>토큰은 1시간짜리 관리자 출입증이라 로그에 절대 찍지 않는다.</remarks>
    /// <exception cref="HttpRequestException">응답이 성공 코드가 아닐 때(401=scope/키 문제, 404=주소 오타).</exception>
    /// <exception cref="InvalidDataException">DB가 비어 있을 때(본문이 null) — 빈 DB를 정상 백업으로 남기지 않는다.</exception>
    public static async Task<JsonNode> FetchDatabaseAsync(BackupConfig config)
    {
        GoogleCredential credential = GoogleCredential
            .FromFile(config.ServiceAccountKeyPath)
            .CreateScoped(Scopes);

        string token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{config.DatabaseUrl.TrimEnd('/')}/.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        string text = await response.Content.ReadAsStringAsync();
        JsonNode? root = JsonNode.Parse(text);
        if (root == null) throw new InvalidDataException("DB가 비어 있습니다(응답 본문 null). 백업하지 않습니다.");

        return root;
    }

    // ── ② 검증 ─────────────────────────────────────────────

    /// <summary>
    /// ② 저장해도 되는 상태인지 검사한다. false면 저장하지 않는다.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>루트에 <c>Users</c> 객체가 있고 캐릭터가 1개 이상인가 — 2026-09-18 루트 덮어쓰기 사고(루트가 장비 목록으로 바뀜)를 거른다.</item>
    /// <item>캐릭터 수가 <b>직전 백업</b> 대비 <see cref="BackupConfig.MinCharacterRatio"/> 미만으로 줄지 않았는가.
    /// 직전 백업이 없으면(첫 실행) 이 비교는 건너뛴다.</item>
    /// </list>
    /// <c>CharacterNames</c>는 없어도 통과시킨다 — 이름 중복 방지용 인덱스일 뿐이라, 이것 때문에 백업이 통째로 멈추면
    /// 캐릭터 데이터까지 못 지킨다. 없으면 <paramref name="reason"/>에 참고로 남긴다.
    /// </remarks>
    /// <param name="reason">실패 사유, 또는 통과했지만 알아둘 점(로그용). 문제 없으면 빈 문자열.</param>
    public static bool Validate(BackupConfig config, JsonNode root, out string reason)
    {
        if (root is not JsonObject obj || obj["Users"] is not JsonObject)
        {
            reason = "루트에 Users 객체가 없습니다(루트가 덮어쓰였을 가능성).";
            return false;
        }

        int count = CountCharacters(root);
        if (count == 0)
        {
            reason = "Users 아래 캐릭터가 0개입니다.";
            return false;
        }

        string? previousPath = FindLatestBackup(config);
        if (previousPath != null)
        {
            int previous = CountCharacters(JsonNode.Parse(File.ReadAllText(previousPath)));
            if (previous > 0 && count < previous * config.MinCharacterRatio)
            {
                reason = $"캐릭터 수 급감: 직전 {previous}개 → 지금 {count}개 (기준 {config.MinCharacterRatio:P0}). 직전 백업: {previousPath}";
                return false;
            }
        }

        reason = obj["CharacterNames"] is JsonObject ? "" : "참고: 루트에 CharacterNames가 없습니다(이름 중복 방지 인덱스).";
        return true;
    }

    // Users/*/Characters/* 개수. 구조가 다르면 0.
    private static int CountCharacters(JsonNode? root)
    {
        if (root?["Users"] is not JsonObject users) return 0;

        int count = 0;
        foreach (KeyValuePair<string, JsonNode?> user in users)
            if (user.Value?["Characters"] is JsonObject characters) count += characters.Count;
        return count;
    }

    // 가장 최근 백업 파일: hourly의 최신 날짜 폴더의 최신 시간 파일, 없으면 daily의 최신 파일. 둘 다 없으면 null.
    private static string? FindLatestBackup(BackupConfig config)
    {
        foreach (DateTime day in DatedEntries(config.HourlyDir, directories: true).OrderByDescending(d => d.Date).Select(d => d.Date))
        {
            string? latestHour = LatestHourFile(Path.Combine(config.HourlyDir, day.ToString(DateFormat)));
            if (latestHour != null) return latestHour;
        }

        return DatedEntries(config.DailyDir, directories: false)
            .OrderByDescending(d => d.Date)
            .Select(d => d.Path)
            .FirstOrDefault();
    }

    // ── ③ 시간별 저장 ──────────────────────────────────────

    /// <summary>
    /// ③ hourly/{yyyy-MM-dd}/{HH}.json으로 저장하고 경로를 돌려준다. <b>이미 같은 파일이 있으면 덮어쓰지 않는다</b>
    /// (스케줄러가 같은 시각에 두 번 돌아도 먼저 받은 정상본을 지킨다).
    /// </summary>
    /// <remarks>
    /// 임시 파일에 다 쓴 뒤 이름을 바꾼다. 쓰는 도중 꺼지면 반쪽짜리 파일이 "최신 정상본"으로 잡혀
    /// 다음 검증·확정을 망치기 때문이다.
    /// </remarks>
    public static string SaveHourly(BackupConfig config, JsonNode root, DateTime now)
    {
        string dir = Path.Combine(config.HourlyDir, now.ToString(DateFormat));
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, $"{now:HH}.json");
        if (File.Exists(path)) return path;

        WriteAtomic(path, root.ToJsonString(WriteOptions));
        return path;
    }

    // ── ④ 하루 확정 ───────────────────────────────────────

    /// <summary>
    /// 오늘 이전 날짜 중 hourly 폴더가 남아 있는(=아직 확정 안 된) 날짜들. 오래된 순.
    /// </summary>
    /// <remarks>
    /// 자정에만 확정하면 그 시각에 서버 PC가 꺼져 있던 날은 영영 확정되지 않는다. 그래서 매 실행마다
    /// "지난 날짜인데 hourly가 남은" 날을 찾아 확정한다(확정하면 hourly 폴더가 지워져 다시 잡히지 않는다).
    /// </remarks>
    public static IEnumerable<DateTime> PendingDays(BackupConfig config, DateTime today)
        => DatedEntries(config.HourlyDir, directories: true)
            .Select(d => d.Date)
            .Where(d => d < today.Date)
            .OrderBy(d => d)
            .ToList();

    /// <summary>
    /// ④ 하루치 시간별 파일 중 <b>가장 늦은 파일</b>을 daily/{yyyy-MM-dd}.json으로 확정하고, 그날 hourly 폴더를 지운다.
    /// 확정할 파일이 없으면 null(폴더는 남겨 둔다 — 사람이 확인할 수 있게).
    /// </summary>
    /// <remarks>hourly에는 검증을 통과한 것만 저장되므로 "가장 늦은 파일 = 그날 마지막 정상본"이다.</remarks>
    public static string? FinalizeDay(BackupConfig config, DateTime day)
    {
        string hourlyDir = Path.Combine(config.HourlyDir, day.ToString(DateFormat));
        string? latest = LatestHourFile(hourlyDir);
        if (latest == null) return null;

        Directory.CreateDirectory(config.DailyDir);
        string dailyPath = Path.Combine(config.DailyDir, $"{day.ToString(DateFormat)}.json");
        File.Copy(latest, dailyPath, overwrite: true);

        Directory.Delete(hourlyDir, recursive: true);
        return dailyPath;
    }

    // 폴더 안의 {HH}.json 중 시(0~23)가 가장 큰 파일. 이름이 숫자가 아니면 무시. 없으면 null.
    private static string? LatestHourFile(string dir)
    {
        if (!Directory.Exists(dir)) return null;

        return Directory.GetFiles(dir, "*.json")
            .Select(p => (Path: p, Ok: int.TryParse(Path.GetFileNameWithoutExtension(p), out int h), Hour: h))
            .Where(x => x.Ok && x.Hour is >= 0 and <= 23)
            .OrderByDescending(x => x.Hour)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    /// <summary>
    /// ④-2 일일본을 캐릭터 하나씩 잘라 characters/{yyyy-MM-dd}/{uid}_{uniqueId}_{이름}.json으로 저장한다. 만든 개수를 돌려준다.
    /// </summary>
    /// <remarks>
    /// 조각 파일 내용은 <b>캐릭터 노드 자체</b>(Users/{uid}/Characters/{uniqueId}의 값)다 — 콘솔에서 그 노드를 선택하고
    /// 가져오면 그 캐릭터만 되돌아간다. 파일명의 uniqueId는 노드 <b>키</b>를 쓴다(필드 값은 2^53 초과로 끝자리가 깎여 있다).
    /// </remarks>
    public static int SplitCharacters(BackupConfig config, string dailyPath, DateTime day)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(dailyPath));
        if (root?["Users"] is not JsonObject users) return 0;

        string dir = Path.Combine(config.CharactersDir, day.ToString(DateFormat));
        Directory.CreateDirectory(dir);

        int count = 0;
        foreach (KeyValuePair<string, JsonNode?> user in users)
        {
            if (user.Value?["Characters"] is not JsonObject characters) continue;

            foreach (KeyValuePair<string, JsonNode?> character in characters)
            {
                if (character.Value == null) continue;

                string name = character.Value["name"]?.GetValueKind() == JsonValueKind.String
                    ? character.Value["name"]!.GetValue<string>()
                    : "noname";

                string fileName = SafeFileName($"{user.Key}_{character.Key}_{name}") + ".json";
                WriteAtomic(Path.Combine(dir, fileName), character.Value.ToJsonString(WriteOptions));
                count++;
            }
        }
        return count;
    }

    // 파일명 금지 문자(\ / : * ? " < > | 등)를 _로 바꾼다. 캐릭터 이름은 사용자가 정해 무엇이든 올 수 있다.
    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>④-3 2차 보관 폴더가 설정돼 있으면 일일본을 복사한다. 비어 있으면 아무것도 안 한다.</summary>
    /// <returns>복사한 경로. 설정이 비어 복사하지 않았으면 null.</returns>
    public static string? CopySecondary(BackupConfig config, string dailyPath)
    {
        if (string.IsNullOrWhiteSpace(config.SecondaryCopyDir)) return null;

        Directory.CreateDirectory(config.SecondaryCopyDir);
        string target = Path.Combine(config.SecondaryCopyDir, Path.GetFileName(dailyPath));
        File.Copy(dailyPath, target, overwrite: true);   // 같은 날짜 재확정 대비 덮어쓰기 허용
        return target;
    }

    // ── ⑤ 보관 정리 ───────────────────────────────────────

    /// <summary>
    /// ⑤ 보관 기간(<see cref="BackupConfig.DailyRetentionDays"/>)이 지난 daily 파일·characters 날짜 폴더·hourly 날짜 폴더를 지운다.
    /// 이름이 날짜(yyyy-MM-dd)가 아닌 것은 건드리지 않는다(사람이 넣어 둔 파일 보호). 지운 개수를 돌려준다.
    /// </summary>
    public static int PruneOld(BackupConfig config, DateTime now)
    {
        DateTime cutoff = now.Date.AddDays(-Math.Max(1, config.DailyRetentionDays));
        int removed = 0;

        foreach ((string path, DateTime date) in DatedEntries(config.DailyDir, directories: false))
            if (date < cutoff) { File.Delete(path); removed++; }

        foreach ((string path, DateTime date) in DatedEntries(config.CharactersDir, directories: true))
            if (date < cutoff) { Directory.Delete(path, recursive: true); removed++; }

        // 확정이 실패해 남은 hourly 날짜 폴더도 보관 기간이 지나면 정리한다.
        foreach ((string path, DateTime date) in DatedEntries(config.HourlyDir, directories: true))
            if (date < cutoff) { Directory.Delete(path, recursive: true); removed++; }

        return removed;
    }

    // ── 공용 ─────────────────────────────────────────────

    // 폴더 안에서 이름이 yyyy-MM-dd(파일이면 확장자 제외)인 항목과 그 날짜. 폴더가 없으면 빈 목록.
    private static IEnumerable<(string Path, DateTime Date)> DatedEntries(string dir, bool directories)
    {
        if (!Directory.Exists(dir)) return Array.Empty<(string, DateTime)>();

        IEnumerable<string> entries = directories ? Directory.GetDirectories(dir) : Directory.GetFiles(dir, "*.json");
        var list = new List<(string, DateTime)>();
        foreach (string entry in entries)
        {
            string name = directories ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry);
            if (DateTime.TryParseExact(name, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                list.Add((entry, date));
        }
        return list;
    }

    // 임시 파일에 다 쓴 뒤 이름을 바꾼다(쓰는 도중 꺼져도 반쪽 파일이 정식 이름으로 남지 않게).
    private static void WriteAtomic(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
