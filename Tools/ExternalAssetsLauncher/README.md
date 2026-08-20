# ProjectS 외부 에셋 런처

`Assets/ExternalAssets`에 Git으로 관리하지 않는 대용량 외부 에셋을 설치하는 Windows용 런처입니다.
런처는 Google 로그인 권한으로 **제한된 Google Drive**의 `manifest.json`을 읽고, 필요한 전체본 또는 추가 패치 ZIP만 내려받습니다.

## 팀원 사용 흐름

1. 실행 중인 Unity Editor를 모두 종료합니다.
2. `ProjectSLauncher.exe`를 실행합니다.
3. 처음 한 번은 Google Cloud에서 내려받은 **OAuth Desktop 앱 JSON**을 선택합니다.
4. **Google 로그인**을 눌러 권한을 받은 팀원 Google 계정으로 로그인합니다.
5. 제한된 Drive의 `manifest.json` 파일 링크 또는 파일 ID를 입력합니다. 이후에는 로컬 PC에만 자동 저장됩니다.
6. **업데이트 확인**을 누릅니다.
7. 설치할 패치가 있으면 **업데이트 설치**를 누릅니다.
8. 설치가 끝나면 **Unity 실행**을 누릅니다.

런처는 ZIP을 임시 폴더에 풀고 모든 파일·새 폴더의 `.meta`를 검사한 후에만 `Assets/ExternalAssets`에 반영합니다.
Unity Editor가 하나라도 실행 중이면 **업데이트 설치**를 차단하고 종료 안내를 표시합니다.

## Drive 구조

```text
ProjectS-ExternalAssets/
├─ Base_v1.zip
├─ Patch_v2.zip
├─ Patch_v3.zip
└─ manifest.json
```

- `Base_v1.zip`: 신규 팀원이 처음 받는 전체 외부 에셋입니다.
- `Patch_vN.zip`: 해당 버전에서 추가·수정된 파일과 각 파일의 `.meta`만 포함합니다.
- ZIP의 최상위에는 `Assets/ExternalAssets`가 아니라 **그 폴더 안에 들어갈 내용**을 넣습니다.
- 파일 삭제가 필요하면 `manifest.json`의 해당 패키지 `removedPaths`에 `SomePackage/Old.prefab`과 `SomePackage/Old.prefab.meta`를 모두 적습니다.
- Drive 폴더, ZIP, manifest는 모두 **제한됨**으로 설정하고 팀원 Google 계정에만 권한을 부여합니다. 런처를 쓰는 팀원은 **뷰어**, manifest와 ZIP을 올리는 배포 담당자만 **편집자/소유자**로 둡니다.

`manifest.example.json`은 schemaVersion 2 예시입니다. ZIP URL이 아닌 Drive **파일 ID**, SHA-256, 고유한 `channelId`를 기록합니다. 신규 배포는 [ExternalAssetsPublisher](../ExternalAssetsPublisher/README.md)를 사용하는 것을 권장합니다.

기존 공개 링크 방식(schemaVersion 1)을 쓰던 팀은 제한된 Drive에 전체본 `Base_v1.zip`을 다시 올리고, schemaVersion 2 manifest를 새로 만들어야 합니다. 런처는 기존 v1 설치 상태를 자동 신뢰하지 않으므로, 보안 방식으로 처음 전환할 때 기존 `Assets/ExternalAssets`를 프로젝트 루트의 `ExternalAssetsLegacyBackups/`에 백업한 뒤 전체본을 한 번 다시 설치합니다. 이 폴더는 Git에서 제외되며, 설치 완료·검증 전에는 삭제하지 않습니다.

ZIP 내부 경로는 반드시 `Assets/ExternalAssets` 기준 상대 경로여야 합니다. 예를 들어 `Synty/Test/Dat/Monster.prefab`은 ZIP 안에도 `Synty/Test/Dat/Monster.prefab`으로 들어 있어야 합니다. `Assets/ExternalAssets` 폴더를 ZIP 안에 한 번 더 넣으면 안 됩니다.

## SHA-256과 manifest 만들기

`SHA-256`은 ZIP 파일이 깨지거나 다른 파일로 바뀌지 않았는지 판별하는 파일 지문입니다.
새 제한 Drive 배포 채널은 **Base Builder → Publisher** 흐름으로 만드세요. 아래 스크립트는 기존 수동 배포를 옮기거나 점검할 때만 남겨 둔 보조 경로이며, Base Builder ZIP 등록 규칙까지 강제하지는 않습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\ExternalAssetsLauncher\Create-Manifest.ps1 -DriveFileId '제한된 Drive ZIP 링크 또는 파일 ID' -OutputPath .\ExternalAssetsReleases\manifest.json
```

실행하면 ZIP 파일 선택창이 열립니다. ZIP을 고르면 `manifest.json`이 생성됩니다. 실제 manifest와 ZIP은 `ExternalAssetsReleases/`에만 보관하고, 제한된 Drive에 올립니다. 이 폴더는 Git에서 제외됩니다.

## 프로젝트 버전 고정

기본값은 Drive의 `latestVersion`을 설치합니다. 브랜치마다 다른 외부 에셋 버전이 필요해지면, 프로젝트 루트에 아래 파일을 Git으로 커밋합니다.

```json
{
  "requiredVersion": 2
}
```

파일명은 `ExternalAssets.lock.json`입니다. `ExternalAssets.lock.example.json`을 복사해서 사용합니다.

## 루트 폴더 메타

`Assets/ExternalAssets.meta`는 외부 에셋 내용물이 아니라 루트 폴더의 GUID입니다. 이 파일 하나는 Git으로 추적하고, `Assets/ExternalAssets/` 내부만 Drive로 배포합니다. 그래야 모든 팀원의 루트 폴더 GUID도 동일하게 유지됩니다.

## 접근 제어

- 공개 GitHub에는 런처 소스와 예시 파일만 둡니다. 실제 Drive 링크, `manifest.json`, ZIP, OAuth 설정 파일은 절대 커밋하지 않습니다.
- `schemaVersion: 2` manifest는 ZIP URL 대신 `driveFileId`만 기록합니다. 파일 ID를 알아도 Drive 권한이 없는 계정은 다운로드할 수 없습니다.
- `channelId`는 설치 상태가 다른 외부 에셋 배포 채널과 섞이는 것을 막습니다. Publisher는 새 manifest를 처음 만들 때 고유 ID를 자동 생성하며, 배포 도중에는 값을 바꾸지 않습니다.
- 런처는 OAuth Desktop 앱 + 기본 브라우저 로그인 + 127.0.0.1 콜백 + PKCE를 사용합니다. refresh token은 `%LOCALAPPDATA%\ProjectSExternalAssetsLauncher`에 현재 Windows 사용자용으로 암호화해 저장합니다.
- 팀원이 다운로드한 원본의 재배포 문제는 기술적 접근 제어가 아니라 에셋 라이선스, NDA, 팀 계약으로 관리합니다.

## Google Cloud 최초 설정

배포 담당자가 한 번만 설정합니다.

1. Google Cloud 프로젝트를 만들고 **Google Drive API**를 활성화합니다.
2. OAuth 동의 화면을 설정합니다. Google Workspace 조직 계정이 있다면 조직 내부 앱으로, 개인 Google 계정 팀이면 외부 앱으로 설정합니다.
3. OAuth Client를 **Desktop app** 유형으로 만듭니다.
4. 생성한 JSON을 내려받아 `google-oauth-client.json`이라는 이름으로 런처 EXE 옆에 두거나, 런처에서 직접 선택합니다. 이 파일은 공개 Git에 커밋하지 않습니다.
5. Drive의 외부 에셋 폴더와 모든 ZIP·manifest를 **제한됨**으로 바꾸고, 팀원 Google 계정만 추가합니다.

개인 Google 계정 기반 외부 앱은 테스트 상태에서 팀원 재로그인이 자주 필요할 수 있으므로, 실제 팀 운영 전에는 OAuth 동의 화면의 게시 상태와 테스트 사용자를 확인하세요.

## 빌드

```powershell
dotnet publish .\Tools\ExternalAssetsLauncher\ExternalAssetsLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Tools\ExternalAssetsLauncher\publish
```

생성된 `publish\ProjectSLauncher.exe`를 팀원에게 배포합니다. 빌드 결과물은 Git에 커밋하지 않습니다.

## 현재 제한

- 기존 설치 버전보다 낮은 버전으로 되돌릴 때는 전체본 복구가 필요하며, 자동 복구는 다음 단계로 남겨 두었습니다.
- Drive 업로드와 기존 manifest에 다음 패치를 누적하는 배포자 도구는 [ExternalAssetsPublisher](../ExternalAssetsPublisher/README.md)로 제공합니다.
