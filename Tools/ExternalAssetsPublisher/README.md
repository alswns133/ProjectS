# ProjectS 외부 에셋 배포자

외부 에셋을 배포하는 담당자만 사용하는 Windows용 패키징 도구입니다.

- 최초 기준본은 Base Builder가 만든 `Base_v1.zip`을 다시 검사·등록합니다.
- 이후 Patch는 선택한 원본의 `Assets/ExternalAssets` 기준 상대 경로와 필요한 `.meta` 파일을 자동으로 ZIP에 기록합니다.

예를 들어 아래 파일 하나를 선택하면:

```text
Assets/ExternalAssets/Synty/Test/Dat/Monster.prefab
```

생성 ZIP에는 아래 항목들이 들어갑니다.

```text
Synty/Test/Dat/Monster.prefab
Synty/Test/Dat/Monster.prefab.meta
Synty.meta
Synty/Test.meta
Synty/Test/Dat.meta
```

팀원용 런처는 ZIP 내부 경로를 그대로 `Assets/ExternalAssets` 아래에 설치하므로, 원래 위치가 정확히 복원됩니다.

## 최초 Base v1 등록

1. Base Builder에서 `Base_v1.zip`과 병합 보고서를 생성합니다.
2. 깨끗한 ProjectS 복사본에서 런처 설치와 Unity 참조 검증을 마칩니다.
3. Publisher에서 **Base Builder ZIP 선택**을 눌러 검증된 `Base_v1.zip`을 고릅니다.
   - ZIP을 다시 만들지 않습니다. 실제 ZIP 구조, 모든 파일·폴더의 `.meta`, GUID, SHA-256을 다시 확인합니다.
   - 현재 ProjectS의 Git 추적 대상인 `Assets/ExternalAssets.meta` GUID와도 충돌이 없는지 확인합니다.
   - 버전은 `v1`, 종류는 `base`, 삭제 경로는 비어 있는 상태로 고정됩니다.
4. 선택한 ZIP 바이트를 그대로 **제한된** Google Drive 외부 에셋 폴더에 업로드합니다.
5. ZIP의 제한된 Drive 파일 링크 또는 파일 ID를 붙여 넣고 **manifest.json 저장**을 누릅니다.
6. 생성된 manifest를 Drive의 기존 `manifest.json` 파일에 새 버전으로 교체해 파일 ID를 유지합니다.

Base v1을 배포한 뒤 ZIP 바이트나 `channelId`를 바꾸지 마세요. 새 Base가 필요하면 새 배포 채널과 별도 전환 절차가 필요합니다.

## Patch v2 이후 배포 흐름

1. `ProjectSExternalAssetsPublisher.exe`를 실행합니다. 일반 ZIP 생성은 `Patch v2`부터 시작하며, Publisher가 만든 ZIP을 `Base v1`으로 등록할 수는 없습니다.
2. ProjectS 프로젝트 폴더를 선택합니다.
3. **파일 추가** 또는 **폴더 추가**로 변경한 원본을 고릅니다.
4. 다음 버전 번호와 ZIP 이름을 입력하고 **ZIP 생성**을 누릅니다.
5. 생성 ZIP을 **제한된** Google Drive 외부 에셋 폴더에 업로드합니다.
6. ZIP의 제한된 Drive 파일 링크 또는 파일 ID를 붙여 넣습니다. Publisher는 URL이 아닌 `driveFileId`만 manifest에 기록합니다.
7. `manifest.json` 저장 위치를 선택하고 **manifest.json 저장**을 누릅니다.
8. 생성된 manifest를 Drive의 기존 `manifest.json` 파일에 새 버전으로 교체합니다. 같은 Drive 파일의 버전을 관리하면 팀원 런처의 manifest 파일 ID가 유지됩니다.

한 버전은 ZIP 하나입니다. 같은 업데이트에서 여러 폴더가 바뀌어도 한 `Patch_vN.zip` 안에 각 파일의 원래 상대 경로가 함께 저장됩니다.

## 주의사항

- 도구는 `Assets/ExternalAssets` 내부 항목만 선택할 수 있습니다.
- 파일을 고르면 해당 파일 `.meta`와 상위 폴더 `.meta`를 자동 포함합니다.
- 폴더를 고르면 폴더 내부 전체와 모든 `.meta`를 포함합니다.
- 삭제는 `삭제할 상대 경로`에 한 줄씩 입력합니다. 예: `Synty/Test/Dat/Old.prefab` 및 `Synty/Test/Dat/Old.prefab.meta`.
- `Assets/ExternalAssets.meta`는 ZIP에 넣지 않습니다. 이 파일 하나는 Git으로 추적해 모든 팀원의 외부 에셋 루트 GUID를 고정합니다.
- 배포 전에 `Assets/ExternalAssets.meta`가 Git에 실제로 커밋되어 있는지 확인하세요. 내부 외부 에셋 폴더는 Git 제외 대상이지만, 이 루트 `.meta` 하나는 공개 저장소에 포함해야 새 클론의 GUID가 고정됩니다.
- Drive 업로드 자체는 첫 버전에서 자동화하지 않습니다. Google Drive 로그인/OAuth 권한을 배포자 도구에 넣지 않기 위해서입니다.
- Base ZIP 등록은 선택한 로컬 ZIP의 SHA-256을 계산하지만, 제한된 Drive에 업로드한 파일이 같은 바이트인지 원격으로 확인할 수는 없습니다. 업로드 직후 그 파일 ID를 등록하세요. 런처는 다운로드 시 SHA-256이 다르면 설치를 차단합니다.
- Publisher가 만드는 manifest는 `schemaVersion: 2`이며, ZIP URL 대신 `driveFileId`를 기록합니다. 새 manifest를 처음 만들 때 고유 `channelId`를 자동 생성하며, 이후 패치에서는 값을 보존합니다. 배포 중에는 임의로 바꾸지 마세요. 기존 공개 링크 방식(schemaVersion 1)의 manifest는 새로 만들어야 합니다.
- `ExternalAssetsReleases/`에는 실제 Drive 파일 ID가 들어 있는 `manifest.json`과 라이선스 ZIP이 생성됩니다. 이 폴더는 Git에서 제외되어 있으므로, 공개 GitHub에 직접 추가하지 마세요.
- Drive 폴더, ZIP, manifest는 모두 `제한됨`으로 두고 팀원 Google 계정에만 공유하세요. `링크가 있는 모든 사용자` 공유는 사용하지 않습니다.

## 빌드

```powershell
dotnet publish .\Tools\ExternalAssetsPublisher\ExternalAssetsPublisher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Tools\ExternalAssetsPublisher\publish
```

생성된 `publish\ProjectSExternalAssetsPublisher.exe`는 배포자만 사용합니다.
