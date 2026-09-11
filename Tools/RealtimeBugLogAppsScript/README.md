# ProjectS 실시간 버그 로그 엔드포인트

Unity 클라이언트는 첫 씬이 로드된 뒤 자동으로 시작합니다. 수집한 모든 로그는
`Application.persistentDataPath/BugLogs/*.jsonl`에 저장하며, 기본적으로 `Error`와
`Exception`만 이 엔드포인트로 보냅니다.

## Apps Script 배포

1. 로그를 저장할 Google 스프레드시트를 만들고, URL에서 스프레드시트 ID를 복사합니다.
2. 독립형 Google Apps Script 프로젝트를 만든 뒤 [Code.gs](Code.gs)와
   [appsscript.json](appsscript.json)의 내용을 붙여 넣습니다.
3. **프로젝트 설정 → 스크립트 속성**에서 아래 값을 추가합니다.
   - `BUG_LOG_SHEET_ID`: 1단계에서 복사한 스프레드시트 ID
   - `BUG_LOG_SHARED_SECRET`: 테스트용으로 새로 만든 임시 공유 시크릿
4. 배포 → 새 배포 → 웹 앱을 선택합니다. 웹 앱은 스프레드시트를 소유하거나 수정할 수 있는 계정으로
   실행해야 합니다. 테스트 환경에 맞게 로그인하지 않은 요청도 허용한 뒤, 배포된 `/exec` URL을 복사합니다.
5. Unity 메뉴에서 `Tools > ProjectS > Logging > Migrate Log Settings To Local Override`를 한 번 실행합니다.
6. 생성된 `Assets/Resources/Logging/LogSettings.local.asset`을 열어 `Apps Script Url`,
   `Shared Secret`, `Build Id`, 테스터별 `Tester Id`를 설정합니다.

`LogSettings.local.asset`은 Git에서 무시되는 개발자별 설정 파일입니다. 공유되는
`LogSettings.asset`은 비밀값을 넣지 않는 빈 템플릿으로 유지하세요.

엔드포인트는 각 로그의 UTC 타임스탬프를 KST로 변환해 `yyyy-MM-dd` 이름의 시트 탭을 만들고,
탭 안에는 아래 컬럼을 기록합니다.

`Timestamp(KST) | Source | Category | Severity | Message | StackTrace | BuildId | User`

한 요청의 행은 날짜 탭별로 한 번의 범위 쓰기로 추가합니다. 스크립트 잠금은 여러 테스트 클라이언트가
동시에 접근해 헤더 생성이나 행 배치가 뒤섞이는 일을 막습니다.

## 클라이언트 동작 및 사용법

- F8 키로 게임 내 오버레이를 열고 닫습니다. Source/Category/Severity 버튼을 누르면 해당 필터가 순환합니다.
- 원격 전송 기본값은 20건 또는 5초이며, 실패 시 두 번 재시도합니다. 테스트 빌드 전에 `LogSettings`에서
  값을 조정할 수 있습니다.
- `Tester Id`는 의도적으로 명시 설정값으로 두었습니다. 비워두면 선택한 캐릭터 이름을 사용하고,
  캐릭터 선택 전에는 `anonymous`를 사용합니다. Firebase UID와 기기 ID는 자동 전송하지 않습니다.
- 의도적으로 남기는 진단 로그는 `GameLog`를 사용합니다. 예시는 아래와 같습니다.

```csharp
using ProjectS.Logging;

GameLog.Error(LogSource.Server, LogCategory.Network, "패킷 파싱 실패", exception);
GameLog.Warning(LogSource.Client, LogCategory.Combat, "콤보 버퍼 초과");
```

기존 `Debug.Log*` 호출도 콘솔에 남으며, 자동으로 `Unity/Uncategorized`로 수집됩니다.
정확한 Source/Category를 보존하는 경로는 새 `GameLog` API뿐입니다.

## 보안 경계

웹 앱 URL과 공유 시크릿은 테스트 환경에서의 스팸 방지 수준입니다. 두 값 모두 클라이언트 빌드에서
복구될 수 있으므로 인증 수단이 아닙니다. `GameLog` 메시지나 스택 트레이스에는 비밀번호, 이메일 주소,
Firebase UID, 토큰 등 민감한 데이터를 절대 넣지 마세요. 테스트 빌드가 팀 밖으로 배포되면 임시 시크릿을
교체해야 합니다.
