# ProjectS

Unity 3D 액션 RPG 프로토타입입니다. 핵심 목표는 플레이어 기능 분리, 이벤트 기반 UI, 비동기 씬 전환, 데이터 기반 리소스 관리 구조를 갖춘 확장 가능한 게임 코드 아키텍처를 만드는 것입니다.

## 포트폴리오 요약

`ProjectS`는 하나의 거대한 플레이어 컨트롤러에 모든 로직을 넣지 않고, 입력/이동/애니메이션/전투/스탯을 독립 컴포넌트로 분리했습니다. UI는 Panel, Popup, Presenter로 역할을 나누고, 씬은 `BaseScene` 생명주기와 `GameSceneManager`를 통해 전환됩니다. 데이터와 사운드는 Addressables와 JSON 테이블을 기반으로 관리합니다.

이 구조 덕분에 새 상태, 새 UI, 새 사운드, 새 데이터 테이블, 새 씬 로직을 추가할 때 기존 핵심 루프를 크게 수정하지 않아도 됩니다.

## 주요 아키텍처

### 1. 플레이어 모듈형 구조

`Player`는 중앙 컨텍스트이자 조율자입니다. 필요한 컴포넌트를 `Awake`에서 캐싱하고 읽기 전용 프로퍼티로 제공합니다.

주요 스크립트:

- `Player/Player.cs`
- `Player/PlayerInputHandler.cs`
- `Player/PlayerMovement.cs`
- `Player/PlayerAnimation.cs`
- `Player/PlayerCombat.cs`
- `Player/PlayerStats.cs`

특징:

- 입력, 이동, 애니메이션, 전투, 스탯을 역할별로 분리했습니다.
- `[RequireComponent]`로 필수 컴포넌트 누락을 방지합니다.
- 이동은 `CharacterController`와 카메라 기준 방향을 사용합니다.
- Animator 제어는 `PlayerAnimation`으로 격리하고, 파라미터 해시는 캐싱합니다.
- 전투는 입력 버퍼, 콤보 단계, 스킬 쿨다운, NonAlloc 히트 판정을 사용합니다.
- HP 변경과 사망은 이벤트로 발행되어 UI와 상태 시스템이 느슨하게 연결됩니다.

### 2. 플레이어 상태 머신

플레이어 상태는 `IState`, `BaseState`, `PlayerStateMachine`으로 구성됩니다.

현재 상태:

- `PlayerFreeState`: 일반 이동과 이동 애니메이션 갱신.
- `PlayerDeadState`: 사망 애니메이션 실행 및 조작 차단.

설계 의도:

- 모든 상태 전환은 `ChangeState`를 통과하므로 `Exit`와 `Enter` 호출 순서가 일정합니다.
- 상태는 `Player` 컨텍스트를 통해 필요한 컴포넌트에 접근하므로 중복 `GetComponent`가 없습니다.
- 대시, 피격, 상호작용, 컷신 상태를 독립 클래스로 확장하기 쉽습니다.

### 3. 이벤트 기반 시스템 연결

시스템 간 통신은 static 이벤트 허브로 분리했습니다.

예시:

- `PlayerEvents`: HP, SG, 레벨, 경험치, 골드, 사망.
- `InventoryEvents`: 아이템 추가, 제거, 장착, 해제.
- `QuestEvents`: 퀘스트 수락, 완료, 진행도 갱신.

특징:

- 이벤트 필드는 `OnXxx` 이름을 사용합니다.
- 이벤트 발행은 `FireXxx()` 메서드를 통해 수행합니다.
- 구독은 `OnEnable`, 해제는 `OnDisable`에서 대칭으로 처리합니다.
- `PlayerEvents`는 플레이 모드 리로드 후 남는 구독자를 방지하기 위해 static 이벤트를 리셋합니다.

### 4. UI 프레임워크

UI는 Panel, Popup, Presenter로 나뉩니다.

주요 스크립트:

- `UI/Framework/BasePanel.cs`
- `UI/Framework/BasePopup.cs`
- `UI/Framework/BasePresenter.cs`
- `Managers/UIManager.cs`
- `UI/Panels/HUDPanel.cs`
- `UI/Presenter/HUDPresenter.cs`

특징:

- `UIManager`는 자식 오브젝트의 Panel/Popup을 찾아 타입별로 관리합니다.
- Panel은 스택 구조로 관리하여 뒤로가기 흐름을 지원합니다.
- Popup은 여러 개가 동시에 열릴 수 있도록 리스트로 관리합니다.
- `BasePanel`은 `OnInit`, `OnShow`, `OnHide`, `OnPause`, `OnResume` 생명주기를 제공합니다.
- `BasePresenter`는 게임 이벤트를 구독하고 View 호출로 변환합니다.
- HUD 예시 흐름: `PlayerEvents` -> `HUDPresenter` -> `HUDPanel` -> `FillGauge`.

### 5. 씬 생명주기와 로딩 흐름

씬 로직은 `BaseScene`을 상속하고 `GameSceneManager`가 전환을 담당합니다.

주요 스크립트:

- `Scene/BaseScene.cs`
- `Managers/GameSceneManager.cs`
- `Scene/InGame.cs`
- `Scene/Tutorial.cs`
- `Scene/BootstrapTest.cs`

씬 생명주기:

1. `Initialize()`에서 씬 등록 직후 필요한 초기화를 수행합니다.
2. `Exit()`에서 이전 씬 정리를 수행합니다.
3. `Progress(float progress)`에서 로딩 중 연출을 갱신할 수 있습니다.
4. `Enter()`에서 씬 활성화 후 로직을 시작합니다.

특징:

- 씬 전환은 `RequestSceneChange<T>()`처럼 타입 기반으로 요청합니다.
- 로딩은 사전 준비 구간과 Unity 씬 로딩 구간으로 나뉩니다.
- 로딩 UI는 `UIManager`를 통해 갱신합니다.
- 씬 전환 시 이전 UI 스택과 사운드 클립을 정리합니다.

### 6. 데이터 기반 테이블

`JsonManager`는 Addressables에서 JSON 테이블을 비동기로 로드하고 타입별 Dictionary로 저장합니다.

주요 스크립트:

- `Managers/JsonManager.cs`
- `Datas/IDataRow.cs`
- `Datas/SoundTable.cs`

특징:

- 테이블 행은 `IDataRow`를 구현합니다.
- 각 행은 `Validate`로 스스로 유효성을 검사합니다.
- 데이터는 `ReadyTask` 완료 후 접근하는 흐름을 사용합니다.
- 현재 사운드 테이블은 `JsonManager.Instance.SoundDict`로 노출됩니다.

### 7. 사운드 시스템

`SoundManager`는 BGM, 2D SFX, 3D SFX, 클립 캐싱, AudioMixer 볼륨, Addressables Release를 담당합니다.

주요 스크립트:

- `Managers/SoundManager.cs`
- `Datas/SoundTable.cs`
- `SoundID.cs`

특징:

- 사운드 메타데이터는 `SoundTable`에서 가져옵니다.
- 클립은 Addressables로 로드하고 파일명 기준으로 캐싱합니다.
- 핸들을 저장해 두었다가 씬 정리 시 Release합니다.
- SFX는 AudioSource 풀을 사용해 런타임 생성 비용을 줄입니다.
- 볼륨은 AudioMixer 파라미터를 통해 dB 단위로 제어합니다.

### 8. 카메라와 상호작용 경계

`CameraRig`는 Cinemachine ThirdPersonFollow의 카메라 거리를 입력에 따라 조절합니다. 플레이어 이동과 카메라 제어를 분리해 각 시스템을 독립적으로 확장할 수 있습니다.

`IDamageable`은 데미지를 받을 수 있는 대상의 최소 계약입니다. `TrainingDummy`는 이 인터페이스를 구현한 테스트용 타깃입니다.

## 폴더 구조

```text
Assets/Scripts
├─ Camera          # Cinemachine 카메라 제어
├─ Datas           # 데이터 행 계약과 테이블 클래스
├─ Enemy           # 테스트 적/데미지 대상
├─ Events          # static 이벤트 허브
├─ Managers        # 씬, UI, 사운드, JSON, Addressables 매니저
├─ Player          # 플레이어 컴포넌트와 상태 머신
├─ Scene           # 씬 생명주기와 부트스트랩
└─ UI              # UI 프레임워크, 패널, 프레젠터
```

## 개발 규칙 요약

- 기능은 해당 기능을 소유한 컴포넌트에 둡니다.
- 시스템 간 연결은 직접 참조보다 이벤트를 우선합니다.
- UI 데이터 바인딩은 `BasePresenter`를 통해 처리합니다.
- 새 씬 로직은 `BaseScene` 하위 클래스로 추가합니다.
- 공용 데이터는 `JsonManager`와 `IDataRow.Validate`를 통해 관리합니다.
- Addressables로 로드한 리소스는 사용이 끝나면 Release합니다.
- 데미지 대상은 구체 클래스가 아니라 `IDamageable`로 다룹니다.

주요 설계 결정과 그 근거는 [docs/decisions/](docs/decisions/)에 ADR(Architecture Decision Record)로 기록합니다.

- [ADR-001: JSON 테이블과 Addressables 적용 범위](docs/decisions/001-addressables-and-data-scope.md)

## 현재 강점

- 플레이어 기능이 역할별 컴포넌트로 명확히 분리되어 있습니다.
- 상태 머신을 통해 플레이어 행동 확장이 쉽습니다.
- 이벤트와 Presenter를 사용해 게임 로직과 UI 결합도를 낮췄습니다.
- 씬 전환, 로딩 UI, 리소스 정리 지점이 구조화되어 있습니다.
- Addressables 기반 데이터/사운드 로딩 흐름을 갖췄습니다.
- 전투 히트 판정에서 NonAlloc 버퍼를 사용해 런타임 할당을 줄였습니다.

## 다음 개선 포인트

- `ItemData`, `QuestData`에 실제 데이터 필드와 검증 로직 추가.
- 스킬 쿨다운, 데미지, 히트 크기 등을 데이터 테이블로 이전.
- 피격, 회피, 상호작용, 컷신 상태를 상태 머신에 추가.
- 이벤트 리셋, 씬 전환, 전투 히트 판정에 대한 PlayMode 테스트 추가.
- `HUDPresenter.OnExpChanged`에서 `cur / max`가 정수 나눗셈이 되지 않도록 float 캐스팅 적용.

## 협업 가이드

이 프로젝트는 Unity 프로젝트 특성상 씬, 프리팹, 메타 파일, 대용량 에셋 충돌이 쉽게 발생할 수 있습니다. 따라서 코드 작업뿐 아니라 Git, 에셋, Addressables 관리 규칙을 함께 지키는 것을 목표로 합니다.

### 1. 프로젝트 초기 설정

- Unity 프로젝트는 `.gitignore`를 먼저 적용한 뒤 첫 커밋을 진행합니다.
- `Library/`, `Temp/`, `obj/`, `Build/`처럼 Unity가 자동 생성하는 폴더는 Git에 포함하지 않습니다.
- `Assets/**/*.meta` 파일은 반드시 추적합니다. `.meta` 파일이 빠지면 프리팹, 머티리얼, 씬 참조가 깨질 수 있습니다.
- Unity Editor 설정은 다음을 권장합니다.
  - Version Control Mode: `Visible Meta Files`
  - Asset Serialization Mode: `Force Text`
- 씬과 프리팹 충돌을 줄이기 위해 UnityYAMLMerge 설정을 권장합니다.

### 2. Git LFS 규칙

이미지, 사운드, 모델, 영상, 폰트처럼 용량이 큰 바이너리 에셋은 Git LFS로 관리합니다.

권장 추적 대상:

```bash
git lfs track "*.png" "*.jpg" "*.jpeg" "*.psd" "*.tga" "*.tif" "*.exr"
git lfs track "*.fbx" "*.obj" "*.blend"
git lfs track "*.wav" "*.mp3" "*.ogg" "*.aiff"
git lfs track "*.mp4" "*.mov"
git lfs track "*.ttf" "*.otf"
git lfs track "*.cubemap" "*.unity3d"
```

- `.gitattributes`도 함께 커밋합니다.
- 팀원은 각자 PC에서 `git lfs install`을 한 번 실행해야 합니다.
- 불필요한 대용량 원본 파일은 저장소에 넣지 않고 별도 공유 드라이브로 관리합니다.

### 3. 브랜치 전략

- `main` 브랜치는 항상 실행 가능한 안정 버전으로 유지합니다.
- 개인 작업은 별도 브랜치에서 진행합니다.
- 브랜치 이름은 `develop-이름` 또는 기능 단위 이름을 사용합니다.
  - 예시: `develop-janghwan`, `feature-player-combat`, `fix-hud-exp`
- 작업 시작 전에는 최신 `main`을 받아 개인 브랜치에 반영합니다.
- 충돌 가능성이 큰 씬/프리팹 작업은 시작 전에 팀원에게 공유합니다.

### 4. 커밋 규칙

커밋 메시지는 `[분류] 내용` 형식을 사용합니다.

예시:

```text
[Player] 공격 콤보 입력 버퍼 추가
[UI] HUD 게이지 Presenter 연결
[Scene] 로딩 패널 진행도 갱신 추가
[Sound] 씬 단위 사운드 프리로드 추가
[Fix] EXP 게이지 정수 나눗셈 오류 수정
[Data] SoundTable 검증 로직 추가
```

- 커밋은 가능한 작은 단위로 나눕니다.
- 한 커밋에 여러 기능을 섞지 않습니다.
- 작업이 중간에 끊겨도 컴파일 가능한 상태라면 커밋합니다.

### 5. 작업 공유와 PR

- 작업 내용을 공유할 때는 변경 목적, 수정 파일, 테스트 여부를 함께 남깁니다.
- GitHub Pull Request는 기능 하나 또는 수정 목적 하나 단위로 작게 만듭니다.
- PR 설명에는 다음 내용을 포함합니다.
  - 변경 요약
  - 테스트한 내용
  - 영향을 받는 시스템
  - 씬/프리팹/Addressables 변경 여부
- 리뷰가 끝난 뒤 `main`에 병합합니다.

### 6. 외부 에셋 관리

- 외부 에셋은 `Assets/ExternalAssets/` 아래에 모읍니다.
- 용량이 크거나 라이선스상 저장소에 포함하기 어려운 에셋은 Git에 직접 넣지 않고 공유 드라이브로 관리합니다.
- 외부 에셋을 공유할 때는 `.meta` 파일을 포함한 압축본으로 전달합니다.
- 각자 에셋 스토어에서 따로 임포트하면 GUID가 달라져 프리팹 참조가 깨질 수 있으므로, 기준 작업자가 만든 동일한 폴더 구조를 공유합니다.

### 7. Addressables 관리

- Addressables 빌드 결과물은 Unity 기본 생성 경로에 만들어지며, 일반적으로 Git 추적 대상이 아닙니다.
- 팀 내 기준 작업자가 Addressables를 빌드하고 필요한 번들/카탈로그 파일을 공유합니다.
- 공유 대상 예시는 다음과 같습니다.
  - 플랫폼별 번들 폴더
  - `catalog.bin`
  - `catalog.hash`
  - `settings.json`
- 다른 팀원은 동일한 로컬 경로에 공유받은 파일을 배치한 뒤 실행 또는 빌드를 진행합니다.
- Player Build 전에 Addressables Groups의 Play Mode Script가 필요한 설정인지 확인합니다.

### 8. Unity 작업 주의사항

- 씬, 프리팹, ScriptableObject를 동시에 여러 명이 수정하면 충돌 가능성이 높으므로 작업 전 담당자를 정합니다.
- `.meta` 파일 삭제나 재생성은 참조 깨짐으로 이어질 수 있으므로 주의합니다.
- 에디터 전용 코드는 반드시 `Editor` 폴더에 두거나 `#if UNITY_EDITOR`로 감싸 빌드 오류를 방지합니다.
- 코드만으로 끝나지 않는 작업은 인스펙터 설정, 애니메이션 이벤트, Animator 파라미터, Addressables 주소까지 함께 기록합니다.

### 9. 팀 커뮤니케이션 규칙

- Push 또는 PR 전에는 변경 내용을 Discord 등 팀 채널에 공유합니다.
- 공유할 내용은 다음을 기준으로 합니다.
  - 작업한 기능 또는 수정한 문제
  - 확인한 테스트 상황
  - 다른 팀원이 주의해야 할 씬/프리팹/에셋 변경
  - 다음 작업자가 이어받아야 할 TODO
- 충돌이 예상되는 파일은 작업 시작 전에 먼저 알립니다.
