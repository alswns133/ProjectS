# ProjectS External Assets Contributor

팀원이 자기 PC의 `Assets/ExternalAssets`를 기준 목록(`seed-index.json`)과 비교해, 새 파일과 수정된 파일만 담은 작은 Contribution ZIP을 만드는 Windows 도구입니다.

이 도구는 팀원의 원본 폴더를 수정하지 않으며, `Assets/ExternalAssets.meta`도 ZIP에 포함하지 않습니다. 루트 GUID는 Git으로 추적되는 파일을 그대로 사용합니다.

## 흐름

```text
배포 담당자: 기준 ExternalAssets → seed-index.json
팀원: seed-index.json + 자기 ExternalAssets → Contribution ZIP
배포 담당자: 기준 ExternalAssets + Contribution ZIP들 → 최종 Base_v1.zip
```

Drive는 기준 목록과 Contribution ZIP을 전달하는 장소입니다. 비교는 각 팀원의 PC에서 실행됩니다. 최초 버전에서는 Contribution ZIP을 팀별 제한된 제출 폴더에 수동 업로드합니다. 자동 업로드는 Drive 쓰기 권한을 별도로 설계한 다음 단계입니다.

## 비교 규칙

- `추가됨`: 기준에 없는 로컬 파일/폴더입니다. 실제 파일과 `.meta`를 ZIP에 넣습니다.
- `수정됨`: 같은 경로지만 파일 또는 `.meta` 해시가 다릅니다. 파일과 `.meta` 전체를 함께 ZIP에 넣습니다.
- `같음`: ZIP에 넣지 않습니다.
- `기준에만 있음`: 삭제하지 않고 보고만 합니다.

새 폴더의 폴더 `.meta`는 포함해야 합니다. 기준에 이미 있는 조상 폴더의 변하지 않은 `.meta`는 포함하지 않습니다.

## 빌드

```powershell
dotnet publish .\Tools\ExternalAssetsContributor\ExternalAssetsContributor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Tools\ExternalAssetsContributor\publish
```
