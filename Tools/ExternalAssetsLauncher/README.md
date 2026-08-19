# ProjectS 외부 에셋 런처

`Assets/ExternalAssets`에 Git으로 관리하지 않는 대용량 외부 에셋을 설치하는 Windows용 런처입니다.
런처는 Google Drive의 `manifest.json`을 읽고 필요한 전체본 또는 추가 패치 ZIP만 내려받습니다.

## 팀원 사용 흐름

1. 실행 중인 Unity Editor를 모두 종료합니다.
2. `ProjectSLauncher.exe`를 실행합니다.
3. Unity 프로젝트 폴더와 Drive의 `manifest.json` 파일 공유 링크를 입력합니다. 이후에는 자동 저장됩니다.
4. **업데이트 확인**을 누릅니다.
5. 설치할 패치가 있으면 **업데이트 설치**를 누릅니다.
6. 설치가 끝나면 **Unity 실행**을 누릅니다.

런처는 ZIP을 임시 폴더에 풀고 파일과 `.meta` 쌍을 검사한 후에만 `Assets/ExternalAssets`에 반영합니다.
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

`manifest.example.json`을 복사해 `manifest.json`으로 만들고, 실제 Drive 파일 공유 링크와 SHA-256을 채운 다음 Drive에 올립니다.
Google Drive에서는 `manifest.json` 파일과 각 ZIP 파일 모두 **링크가 있는 모든 사용자 / 뷰어**로 공유해야 합니다.

## SHA-256 만들기

`SHA-256`은 ZIP 파일이 깨지거나 다른 파일로 바뀌지 않았는지 판별하는 파일 지문입니다.
직접 값을 복사할 필요 없이, 제공된 스크립트로 ZIP과 `manifest.json`을 한 번에 만들 수 있습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\ExternalAssetsLauncher\Create-Manifest.ps1
```

실행하면 ZIP 파일 선택창이 열립니다. ZIP을 고르면 같은 폴더에 `manifest.json`이 생성됩니다. 그 파일을 Drive에 올리고 **그 파일의 공유 링크**를 런처에 넣으면 됩니다.
해시가 맞지 않으면 런처는 기존 외부 에셋을 변경하지 않습니다.

## 프로젝트 버전 고정

기본값은 Drive의 `latestVersion`을 설치합니다. 브랜치마다 다른 외부 에셋 버전이 필요해지면, 프로젝트 루트에 아래 파일을 Git으로 커밋합니다.

```json
{
  "requiredVersion": 2
}
```

파일명은 `ExternalAssets.lock.json`입니다. `ExternalAssets.lock.example.json`을 복사해서 사용합니다.

## 빌드

```powershell
dotnet publish .\Tools\ExternalAssetsLauncher\ExternalAssetsLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Tools\ExternalAssetsLauncher\publish
```

생성된 `publish\ProjectSLauncher.exe`를 팀원에게 배포합니다. 빌드 결과물은 Git에 커밋하지 않습니다.

## 현재 제한

- Google Drive 공개 링크로 ZIP을 자동 다운로드하는 첫 MVP입니다.
- 기존 설치 버전보다 낮은 버전으로 되돌릴 때는 전체본 복구가 필요하며, 자동 복구는 다음 단계로 남겨 두었습니다.
- Drive 업로드와 기존 manifest에 다음 패치를 누적하는 배포자 도구는 아직 포함하지 않았습니다.
