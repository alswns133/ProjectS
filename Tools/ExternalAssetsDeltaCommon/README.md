# External Assets Delta Common

`ExternalAssetsBaseBuilder`와 `ExternalAssetsContributor`가 함께 쓰는 기준 목록과 Contribution ZIP 검증 라이브러리입니다.

## 계약

- `seed-index.json`은 실제 에셋 바이트 없이, `Assets/ExternalAssets` 내부의 상대 경로·크기·SHA-256·`.meta` GUID만 기록합니다.
- Contribution ZIP은 `contribution.json`과 `payload/<ExternalAssets 상대 경로>` 파일만 포함합니다.
- `Assets/`, `ExternalAssets/`, `ExternalAssets.meta`는 ZIP과 seed entry에 넣지 않습니다. 루트 GUID는 Git에서 추적하는 `Assets/ExternalAssets.meta`가 담당합니다.
- 변경 파일은 항상 대응 `.meta`와 함께 패키징합니다. 변경되지 않은 반대편 파일은 `Support`로 함께 넣어 병합 시 원자적으로 선택합니다.
- 최초 병합에서는 `Missing`을 삭제 명령으로 사용하지 않습니다.

## 안전 검사

- 경로 탈출, 절대 경로, 중복/대소문자 충돌, 파일/폴더 충돌을 거부합니다.
- 새 파일은 새 GUID만 허용하고, 기준에 이미 있던 `.meta`의 GUID 변경은 거부합니다.
- Contribution을 풀기 전과 풀 때 ZIP 및 payload SHA-256·`.meta` GUID를 재검증합니다.
- Contribution은 같은 `baselineId`, 기준 내용 SHA-256, 루트 GUID를 가진 seed에서만 병합할 수 있습니다.
