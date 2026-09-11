# ProjectS realtime bug log endpoint

The Unity side starts automatically after the first scene loads. It writes every captured log to
`Application.persistentDataPath/BugLogs/*.jsonl`, and sends only `Error` and `Exception` entries
to this endpoint by default.

## Deploy the Apps Script

1. Create the destination Google Spreadsheet. Copy its ID from the URL.
2. Create a standalone Google Apps Script project, then paste in [Code.gs](Code.gs) and
   [appsscript.json](appsscript.json).
3. In **Project Settings → Script properties**, add:
   - `BUG_LOG_SHEET_ID`: the spreadsheet ID from step 1
   - `BUG_LOG_SHARED_SECRET`: a newly generated, temporary test-only value
4. Deploy → New deployment → Web app. The web app must execute as the account that owns (or can
   edit) the spreadsheet. Allow unauthenticated access appropriate for the test environment, then
   copy its `/exec` URL.
5. In Unity, edit [LogSettings.asset](../../Assets/Resources/Logging/LogSettings.asset) and set
   `Apps Script Url`, `Shared Secret`, `Build Id`, and a tester-specific `Tester Id`. Do not commit
   a real endpoint/secret unless the team has explicitly agreed that it is safe to distribute.

The endpoint creates a `yyyy-MM-dd` sheet tab using each entry's UTC timestamp converted to KST.
Within a tab it writes:

`Timestamp(KST) | Source | Category | Severity | Message | StackTrace | BuildId | User`

Rows for a single request are appended with one range write per date tab. A script lock prevents
concurrent test clients from interleaving header creation or row batches.

## Client behavior and use

- F8 toggles the in-game overlay; its source/category/severity buttons cycle their filters.
- Default remote batching is 20 records or 5 seconds, with two retries. Change these values in
  `LogSettings` before a test build.
- `Tester Id` is deliberately an explicit setting. If left empty, the selected character name is
  used; before character selection it falls back to `anonymous`. Firebase UID and device ID are
  never sent automatically.
- Write intentional diagnostic records through `GameLog`, for example:

```csharp
using ProjectS.Logging;

GameLog.Error(LogSource.Server, LogCategory.Network, "패킷 파싱 실패", exception);
GameLog.Warning(LogSource.Client, LogCategory.Combat, "콤보 버퍼 초과");
```

Existing `Debug.Log*` calls remain visible and are captured automatically as
`Unity/Uncategorized`. The new API is the only route that preserves a precise source/category.

## Security boundary

The web-app URL and shared secret provide only test-environment spam resistance. Both can be
recovered from a client build, so this is not authentication. Never send passwords, email
addresses, Firebase UIDs, tokens, or other sensitive data in `GameLog` messages or stack traces.
Rotate the temporary secret when a test build is distributed beyond the team.
