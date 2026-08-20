# ProjectS External Assets Base Builder

여러 팀원의 변경분을 **원본 수정 없이** 분석해, 최초 기준본 `Base_v1.zip`을 만드는 배포자 전용 Windows 도구입니다.

기본 흐름은 각 팀원의 20GB 전체 폴더를 다시 받는 방식이 아닙니다. 배포 담당자가 작은 기준 목록(`seed-index.json`)을 만들고, 팀원은 자기 로컬 `ExternalAssets`와 비교해 **새 파일·수정 파일만 담은 Contribution ZIP**을 제출합니다.

일반 팀원용 런처나 이후 변경분을 만드는 Publisher와 역할이 다릅니다.

```text
Base Builder
  기준 인덱스 생성 / Contribution ZIP 검증·병합 / 충돌 해결 / GUID 검사 / Base_v1.zip 생성

Publisher
  확정된 Base 또는 Patch를 Drive에 배포하고 manifest를 기록

Launcher
  팀원 PC에 필요한 Base/Patch를 설치
```

## Contribution 기반 최초 Base 흐름

```text
배포 담당자 현재 ExternalAssets
  → Base Builder: seed-index.json 생성
  → 제한된 Drive에 seed-index.json 전달

팀원 각자 로컬 ExternalAssets
  + seed-index.json
  → Contributor: Added / Modified만 Contribution ZIP으로 생성
  → 제한된 Drive 제출 폴더에 ZIP 업로드

배포 담당자
  + 같은 seed-index.json
  + Contribution ZIP들
  → Base Builder: 최종 Base_v1.zip 생성
```

Drive는 파일을 전달하는 장소일 뿐입니다. 비교와 해시 검사는 각 팀원의 PC와 배포 담당자의 PC에서 실행됩니다.

`추가 ExternalAssets` 입력은 이전처럼 직접 접근 가능한 완전한 폴더 병합용으로 남아 있습니다. Contribution ZIP 방식과 함께 쓸 수 있지만, 팀원 전체 원본을 수동으로 모으지 않아도 되는 기본 경로는 Contribution ZIP입니다.

## 안전 규칙

- 입력한 프로젝트와 `Assets/ExternalAssets`는 읽기 전용입니다. 파일을 이동, 삭제, 덮어쓰기하거나 `.meta`를 생성하지 않습니다.
- 임시 staging 폴더에 20GB를 한 벌 더 복사하지 않습니다. 선택된 원본 파일을 ZIP에 직접 기록합니다.
- Contribution ZIP은 검증된 작은 payload만 앱 전용 임시 폴더에 풉니다. 분석을 다시 시작하거나 도구를 닫으면 해당 staging 폴더를 삭제합니다.
- Contribution을 받기 전, 현재 기준 `ExternalAssets`가 선택한 `seed-index.json`과 **완전히 일치**하는지 확인합니다. 기준이 달라졌으면 새 seed를 만든 뒤 팀원에게 다시 배포해야 합니다.
- Contribution의 `baselineId`, 기준 내용 해시, `Assets/ExternalAssets.meta`의 루트 GUID, 각 변경 파일의 기준 해시를 다시 확인합니다.
- 기존 경로의 `.meta`는 importer 설정을 수정할 수 있어도 `guid:` 값은 기준과 같아야 합니다. 기존 GUID 변경은 Git의 참조를 깨므로 Base 생성 전에 차단합니다.
- 같은 상대 경로의 파일 또는 `.meta`는 자동으로 덮어쓰지 않고 충돌로 표시합니다.
- 충돌마다 기준 원본 또는 추가 원본을 사용자가 명시적으로 선택해야 합니다.
- Contribution의 기준과 같은 Support 파일은 충돌로 표시하지 않지만, 변경된 asset과 `.meta`를 같은 Contribution에서 원자적으로 선택할 수 있도록 후보는 보존합니다.
- 일반 파일 `.meta`, 폴더 `.meta`, GUID 형식, 서로 다른 경로의 GUID 중복을 검증합니다.
- `Assets/ExternalAssets.meta`는 ZIP에 넣지 않습니다. 이 루트 폴더 메타는 Git으로 추적해 GUID를 고정합니다.
- 출력 ZIP/보고서는 입력 ExternalAssets 내부에 만들 수 없으며, 기존 ZIP이나 보고서를 덮어쓰지 않습니다.

## 사용 흐름

1. Unity Editor를 종료하는 것을 권장합니다.
2. `ProjectSExternalAssetsBaseBuilder.exe`를 실행하고 기준이 될 Unity 프로젝트 또는 `Assets/ExternalAssets` 폴더를 선택합니다.
3. 처음 한 번은 **기준 인덱스 생성**을 눌러 `seed-index.json`을 만듭니다. 이 JSON을 제한된 Drive에 올려 팀원에게 전달합니다.
4. 팀원은 Contributor 도구로 그 JSON과 자신의 로컬 폴더를 비교해 Contribution ZIP을 제출합니다.
5. Base Builder에서 동일한 **기준 인덱스**를 선택하고, 제출받은 **Contribution ZIP 추가**를 누릅니다.
6. 필요할 때만 직접 접근 가능한 다른 Unity 프로젝트 또는 `Assets/ExternalAssets` 폴더를 `추가 ExternalAssets`에 넣습니다.
7. **병합 계획 분석**을 누릅니다. 이 단계에서 baseline/seed 일치, ZIP 구조, SHA-256, 루트 GUID, 기존 `.meta` GUID 변경을 검사하고 payload만 임시 staging에 풉니다.
8. 충돌 목록에서 각 경로에 사용할 원본을 선택합니다.
   - `미해결 충돌: 기준 원본 유지` 버튼도 명시적 선택입니다.
   - 파일과 폴더가 같은 경로를 쓰는 충돌은 원본 구조를 정리한 뒤 다시 분석해야 합니다.
9. **병합 결과 검증**을 누릅니다.
10. 검증을 통과하면 **검증된 Base ZIP 생성**을 누릅니다.

생성 결과는 다음과 같습니다.

```text
Base_v1.zip
Base_v1.merge-report.json
```

ZIP 최상위에는 `Assets/`나 `ExternalAssets/`를 넣지 않습니다.

```text
Base_v1.zip
├─ Synty.meta
├─ Synty/Test.meta
├─ Synty/Test/Dat.meta
├─ Synty/Test/Dat/Monster.prefab
└─ Synty/Test/Dat/Monster.prefab.meta
```

따라서 팀원 런처는 이를 아래 경로에 정확히 복원합니다.

```text
Assets/ExternalAssets/Synty/Test/Dat/Monster.prefab
```

검증을 마친 뒤에는 Publisher에서 **Base Builder ZIP 선택**으로 이 `Base_v1.zip`을 등록합니다. Publisher가 ZIP 바이트·폴더 `.meta`·GUID·SHA-256을 다시 확인한 뒤 `schemaVersion: 2`의 `v1/base` manifest를 만듭니다.

## 충돌과 GUID 주의사항

최초 Base 통합에서의 충돌과 팀원 런처의 Patch 적용은 다릅니다.

- **Base Builder**: 같은 상대 경로는 사람이 선택합니다. 자동 덮어쓰지 않습니다.
- **Patch Installer**: 확정된 배포 패치의 파일 교체는 의도된 업데이트이므로 덮어쓸 수 있습니다.

특히 서로 다른 경로의 `.meta`가 같은 `guid:`를 가지면 Unity 참조가 잘못 연결될 수 있습니다. 또한 기존 경로의 `.meta` GUID를 바꾸면 Git의 씬·프리팹 참조가 끊어집니다. Base Builder는 둘 다 ZIP 생성 전에 막습니다. `.meta`를 새로 만들거나 GUID를 임의로 바꾸지 말고, 올바른 원본을 선택해 원본 구조를 정리한 뒤 다시 분석하세요.

Builder는 외부 의존성을 자동으로 따라가지 않습니다. 프리팹이 다른 외부 머티리얼·텍스처·애니메이션을 참조하면, Base를 확정한 뒤 깨끗한 프로젝트 복사본에서 실제 Unity 검증을 해야 합니다.

## 배포 전 필수 검증

1. 깨끗한 ProjectS 복사본에서 `Assets/ExternalAssets` 내부를 비운 상태로 Base를 런처로 설치합니다.
2. Unity를 열어 GUID 충돌·누락 `.meta`·Missing reference 오류를 확인합니다.
3. 주요 프리팹, 애니메이션, 머티리얼 참조를 확인합니다.
4. 검증이 끝난 ZIP만 제한된 Google Drive에 업로드합니다.

## 빌드

```powershell
dotnet publish .\Tools\ExternalAssetsBaseBuilder\ExternalAssetsBaseBuilder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\Tools\ExternalAssetsBaseBuilder\publish
```

생성되는 `publish\ProjectSExternalAssetsBaseBuilder.exe`는 Base를 확정하는 배포 담당자만 사용합니다. ZIP, 병합 보고서, publish 산출물은 Git에 커밋하지 않습니다.
