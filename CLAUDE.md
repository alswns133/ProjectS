# ProjectS Code Guide

Unity 3D 액션 RPG 프로젝트의 코드 작업 가이드입니다. 런타임 스크립트는 주로 `Assets/Scripts` 아래에 있습니다.
이 문서는 새 기능을 추가하거나 기존 코드를 수정할 때 유지해야 할 아키텍처 규칙을 기록합니다.

## 아키텍처 규칙

### Player: 중앙 컨텍스트 + 기능 컴포넌트 + 상태 머신

- `Player`는 플레이어의 중앙 컨텍스트입니다. 모든 로직을 직접 처리하지 않고, 역할별 컴포넌트를 연결하고 중재합니다.
- 기능은 다음 컴포넌트로 분리합니다.
  - `PlayerInputHandler`: Unity Input System 입력을 게임 로직이 쓰기 쉬운 값과 이벤트로 변환합니다.
  - `PlayerMovement`: CharacterController 기반 이동, 카메라 기준 방향, 중력, 점프, 회전을 담당합니다.
  - `PlayerAnimation`: Animator 파라미터와 트리거를 제어하는 유일한 통로입니다.
  - `PlayerCombat`: 공격 콤보, 입력 버퍼, 스킬 쿨다운, 히트 판정, 데미지 호출을 담당합니다.
  - `PlayerStats`: HP, 사망 판정, 플레이어 스탯 이벤트 발행을 담당합니다.
- 컴포넌트 참조는 `{ get; private set; }` 형태의 읽기 전용 프로퍼티로 공개하고 `Awake`에서 한 번만 캐싱합니다.
- `Player`는 기능을 조율할 수 있지만, 세부 구현은 각 기능 컴포넌트 안에 둡니다.
- 공격/스킬 중 이동 제한은 `Player.LockMovement()` / `UnlockMovement()`로 조율합니다. 애니메이션 규칙이 이동 코드 안으로 새어 들어가지 않게 하기 위한 규칙입니다.

### 상태 머신

- 플레이어 상태는 `IState` -> `BaseState` -> 구체 상태 클래스 구조를 따릅니다.
- 상태 전환은 반드시 `PlayerStateMachine.ChangeState()`를 거쳐야 합니다. 그래야 이전 상태의 `Exit()`와 새 상태의 `Enter()` 호출 순서가 보장됩니다.
- 상태 클래스는 생성자에서 `Player` 컨텍스트를 받고, 필요한 기능은 `player.Movement`, `player.Animation`처럼 컨텍스트를 통해 접근합니다.
- 현재 상태:
  - `PlayerFreeState`: 일반 이동과 이동 애니메이션 갱신.
  - `PlayerDeadState`: 사망 애니메이션 실행 및 조작 차단.
- 대시, 피격, 상호작용, 컷신 제어 같은 기능은 `Player.Update()`에 조건문을 늘리기보다 새 상태 클래스로 추가합니다.

### Enemy: 플레이어와 동일한 패턴의 몬스터 구조

- `Enemy`는 몬스터의 중앙 컨텍스트입니다. Player와 같은 방식으로 역할별 컴포넌트를 `Awake`에서 캐싱하고 읽기 전용 프로퍼티로 공개합니다.
- 기능은 다음 컴포넌트로 분리합니다.
  - `EnemyStats`: HP, 사망 판정, `IDamageable` 구현(피격 진입점), `CombatEvents` 발행.
  - `EnemyMovement`: NavMeshAgent 기반 이동과 추적. 플레이어(CharacterController)와 달리 길 찾기가 필요해 NavMesh를 사용합니다.
  - `EnemyAnimation`: 몬스터 Animator 파라미터와 트리거를 제어하는 유일한 통로.
  - `EnemyCombat`: 공격 판정(미리 할당한 Collider 버퍼)과 공격 쿨다운. 히트 프레임은 Animation Event로 연결합니다.
- 상태 머신은 `IState` 인터페이스를 플레이어와 공유하고, `EnemyBaseState` -> 구체 상태 클래스 구조를 따릅니다. 전환은 반드시 `EnemyStateMachine.ChangeState()`를 거칩니다.
- 현재 상태:
  - `EnemyIdleState`: 대기. 감지 반경 안에 플레이어가 들어오면 Chase로 전환.
  - `EnemyChaseState`: 추적. 공격 사거리에 들어오면 Attack, 추적 범위를 벗어나면 Idle로 전환.
  - `EnemyAttackState`: 공격 재생. 종료 후 거리에 따라 Chase 또는 Idle로 전환.
  - `EnemyDeadState`: 사망 연출과 AI/충돌 비활성화. 다른 상태로 전환되지 않습니다.
- 순찰, 피격 경직, 그로기 같은 새 AI 행동은 `Enemy.Update()`에 조건문을 늘리기보다 새 상태 클래스로 추가합니다.
- `TrainingDummy`는 공격 판정·데미지 검증용 최소 구현으로 별도 유지합니다.

### 입력 경계

- 플레이어 게임플레이 입력은 `PlayerInputHandler`만 직접 `InputAction`을 읽습니다.
- 지속 입력은 `MoveInput`, `ZoomDelta`, `JumpHeld`, `AttackHeld` 같은 프로퍼티로 노출합니다.
- 순간 입력은 `Attacked`, `SkillPressed` 같은 C# 이벤트로 노출합니다.
- 입력 이벤트 구독은 `OnEnable`, 해제는 `OnDisable`에서 짝을 맞춥니다.

### 전투와 데미지

- 전투 판정은 미리 할당한 Collider 버퍼를 사용해 런타임 할당을 줄입니다.
- 데미지를 받는 대상은 구체 클래스가 아니라 `IDamageable` 인터페이스에 의존합니다.
- 히트 프레임, 콤보 입력 가능 구간, 콤보 리셋처럼 타이밍이 중요한 로직은 Animation Event로 연결합니다.
- 현재 히트 판정은 인스펙터에서 조정하는 히트 박스를 기준으로 `Physics.OverlapBoxNonAlloc`을 사용합니다.

### 이벤트 시스템

- 시스템 간 알림은 `PlayerEvents`, `InventoryEvents`, `QuestEvents` 같은 static 이벤트 허브를 사용합니다.
- 이벤트는 외부에서 직접 Invoke하지 않고 `FireXxx()` 메서드를 통해 발행합니다.
- 플레이 모드 리로드 후에도 남을 수 있는 static 이벤트는 `RuntimeInitializeOnLoadMethod` 같은 초기화 경로에서 null로 리셋합니다.
- UI와 Presenter는 구독/해제를 항상 대칭으로 작성합니다.

### UI: Panel, Popup, Presenter

- `UIManager`는 싱글톤이며, 자식 오브젝트의 `BasePanel`, `BasePopup`을 찾아 타입별 Dictionary로 관리합니다.
- Panel은 스택 구조입니다. 새 패널을 열면 기존 최상단 패널은 Pause되고, 뒤로가기는 현재 패널을 닫고 이전 패널을 Resume합니다.
- Popup은 여러 개가 동시에 열릴 수 있으므로 리스트로 관리합니다.
- `BasePanel`과 `BasePopup`은 `OnInit`, `OnShow`, `OnHide` 생명주기를 제공합니다. Panel은 추가로 `OnPause`, `OnResume`을 가집니다.
- Presenter는 `BasePresenter`를 상속하고, 게임 이벤트를 받아 View 메서드 호출로 변환합니다.
- HUD 흐름 예시: `PlayerEvents.FireHpChanged()` -> `HUDPresenter.OnHpChanged()` -> `HUDPanel.SetHp()` -> `FillGauge.SetRatio()`.

### 씬 흐름

- 씬 로직은 `BaseScene`을 상속하고 `Initialize`, `Enter`, `Exit`, `Progress`를 구현합니다.
- `GameSceneManager`는 씬 등록, 활성 씬 선택, 비동기 로딩, 로딩 UI 갱신을 담당합니다.
- 씬 변경은 `RequestSceneChange<T>()` 또는 `RequestSceneChangeWithDelay<T>()`를 통해 요청합니다.
- 씬 전환 중 이전 씬 리소스, UI 패널/팝업, 사운드 클립을 정리하고 다음 씬을 활성화합니다.
- 씬 고유 동작은 각 씬 클래스에, 공통 전환 규칙은 `GameSceneManager`에 둡니다.

### 데이터와 Addressables

- JSON 테이블 행은 `IDataRow`를 구현하고 `Index`, `Validate(out string error)`를 제공합니다.
- `JsonManager`는 Addressables의 `TextAsset` JSON을 비동기로 로드하고, 행 검증 후 타입별 Dictionary로 저장합니다.
- 데이터 접근은 `JsonManager.IsReady` 확인 후 또는 `JsonManager.ReadyTask` await 이후에 수행합니다.
- Addressables 핸들은 파싱 완료 후 또는 캐시된 런타임 에셋이 더 필요 없을 때 Release합니다.

### 사운드

- `SoundManager`는 BGM/SFX 재생, AudioMixer 볼륨 제어, 클립 캐싱, Addressables Release를 담당합니다.
- 사운드 메타데이터는 `SoundTable`에서 가져오며, 코드에서는 가능하면 `SoundID` 상수를 사용합니다.
- SFX는 AudioSource 풀을 사용해 반복 생성 비용을 줄입니다.
- 씬 전환 시 씬 단위로 로드한 사운드는 `ReleaseAllClips()`로 정리합니다.

### 매니저 규칙

- 매니저는 `public static XxxManager Instance { get; private set; }` 형태의 싱글톤을 사용합니다.
- 중복 인스턴스는 `Awake`에서 제거합니다.
- 씬을 넘어 유지되어야 하는 매니저만 `DontDestroyOnLoad(gameObject)`를 사용합니다.
- 매니저는 공통 흐름만 담당하고, 플레이어/적/UI View의 세부 로직을 가져가지 않습니다.

## 코딩 컨벤션

### 이름 규칙

- 클래스, 메서드, 프로퍼티, 상수: `PascalCase`.
- 지역 변수, 매개변수, private 필드: 기존 파일 스타일을 우선하되 기본은 `camelCase`.
- 이벤트: `OnXxx`.
- 이벤트 발행 메서드: `FireXxx`.
- bool 값: `IsXxx`, `HasXxx`, `CanXxx` 형태를 우선합니다.

### Unity 규칙

- `GetComponent`는 `Awake`에서 한 번 캐싱합니다.
- 구조적으로 필요한 컴포넌트는 `[RequireComponent]`로 명시합니다.
- 인스펙터 조정 값은 `[SerializeField] private`로 둡니다.
- 설정 그룹이 많으면 `[Header("...")]`로 묶습니다.
- Animator 파라미터는 `Animator.StringToHash`로 캐싱합니다.
- Animation Event가 참조하는 메서드 이름은 Unity가 문자열로 참조하므로 변경에 주의합니다.

### 주석 규칙

- public 클래스, public 메서드, public 프로퍼티, public 이벤트에는 XML summary 주석을 작성합니다.
- 필요한 경우 `<param>`, `<returns>`를 추가합니다.
- 주석은 코드가 “무엇을 하는지”보다 “왜 이렇게 하는지”, “언제 호출되는지”, “빠지면 어떤 문제가 생기는지”를 설명합니다.

### 포맷

- 들여쓰기는 4칸 스페이스를 사용합니다.
- 여러 줄 블록은 Allman brace 스타일을 우선합니다.
- 짧은 guard clause는 한 줄 허용, 본문이 길어지면 중괄호를 사용합니다.
- 논리적으로 다른 멤버 그룹 사이에는 빈 줄을 둡니다.

## 기능 추가 체크리스트

- 플레이어 입력이면 `PlayerInputHandler`.
- 이동, 회전, 중력, 점프면 `PlayerMovement`.
- Animator 파라미터와 트리거면 `PlayerAnimation`.
- 공격, 스킬, 히트, 쿨다운, 데미지 타이밍이면 `PlayerCombat`.
- HP, 사망, 스탯 변화면 `PlayerStats`와 `PlayerEvents`.
- 몬스터 HP·피격이면 `EnemyStats`, 추적·이동이면 `EnemyMovement`, 공격 판정·쿨다운이면 `EnemyCombat`, Animator 제어면 `EnemyAnimation`.
- 몬스터 AI 행동 추가면 `EnemyBaseState`를 상속한 새 상태 클래스.
- UI 화면 생명주기면 `BasePanel` 또는 `BasePopup` 하위 클래스.
- UI 데이터 바인딩이면 `BasePresenter` 하위 클래스.
- 씬 고유 로직이면 `BaseScene` 하위 클래스.
- 공용 데이터 테이블이면 `IDataRow` 행 클래스와 `JsonManager` 등록.
