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
- **근접/원거리는 클래스를 파생하지 않고 `EnemyCombat`의 공격 패턴 `kind`(Melee/Projectile)로 구분합니다.**
  Projectile 공격은 `OnAttackHit`에서 판정 대신 `ProjectileSpawner`로 투사체를 쏘고, 데미지·피격 이펙트·벽
  탄흔은 날아가는 `Projectile`이 적중 시점에 처리합니다. `minRange`로 "붙으면 근접"을, `detectAttack`으로
  "발견 시 돌진/조우 사격"을 표현하므로, 궁수 같은 혼합 몬스터도 슬롯 조합만으로 만듭니다.
- 상태 머신은 `IState` 인터페이스를 플레이어와 공유하고, `EnemyBaseState` -> 구체 상태 클래스 구조를 따릅니다. 전환은 반드시 `EnemyStateMachine.ChangeState()`를 거칩니다.
- 현재 상태:
  - `EnemyIdleState`: 대기. 감지 반경 안에 플레이어가 들어오면 Chase로 전환.
  - `EnemyChaseState`: 추적. 공격 사거리에 들어오면 Attack, 추적 범위를 벗어나면 Idle로 전환.
  - `EnemyDetectState`: 최초 발견 연출(+근접 돌진/원거리 조우 사격). 발견 클립이 끝나면 Chase로 전환.
  - `EnemyAttackState`: 공격 재생. 공격 클립이 끝나면 거리에 따라 Chase 또는 Idle로 전환.
  - `EnemyDeadState`: 사망 연출과 AI/충돌 비활성화. 다른 상태로 전환되지 않습니다.
- **Attack/Detect 상태 종료는 인스펙터 수동 시간이 아니라 애니메이터 클립 종료로 판정합니다**
  (`EnemyAnimation.IsCurrentStateFinished`의 normalizedTime 기준). 클립 길이를 손으로 적던
  `duration`/`detectDuration`은 제거됐습니다 — 클립을 바꿔도 값이 어긋나지 않게 하기 위함입니다.
  - 이 판정이 성립하려면 **애니메이터에서 공격 State에는 `Attack`, 발견/조우 State에는 `Detect` 태그가
    반드시 있어야 합니다.** 태그가 상태 진입 감지(`IsPlaying`)의 유일한 근거이기 때문입니다.
    새 공격/발견 클립을 추가할 때 태그를 빠뜨리면, 안전 타임아웃까지(약 1초) 어색하게 굳었다 넘어갑니다.
  - `cooldown`(공격 간격)처럼 클립 길이와 무관한 밸런스 값은 그대로 인스펙터에 둡니다.
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

### 데이터/에셋 운영 방침 (2026-07 확정, 단계적 적용)

JSON 테이블과 어드레서블의 적용 범위 기준입니다. 개발 중인 기능은 인스펙터로 완성하고,
수치가 안정되거나 종류(행)가 늘어나기 시작할 때 아래 기준으로 옮깁니다.

- **JSON 테이블로 뽑는 것**: 기획자가 시트에서 바꿀 밸런스 수치, 종류가 계속 늘어나는 데이터.
  우선순위: 몬스터 스탯 > 스킬(쿨타임·데미지·게이지) > 아이템/드랍 > 퀘스트 텍스트 > 플레이어 스탯.
  아이템/퀘스트는 처음부터 JSON으로 시작합니다(나중에 옮기는 비용이 더 큼).
- **인스펙터에 남기는 것**: 프리팹·파티클·Transform 같은 참조 전부, 히트박스 배치(애니메이션과 한 몸),
  연출 감각 값, 시스템 튜닝 값(속도 평활화, NavMesh 샘플 반경, 회피 우선순위 등).
  기획 밸런스가 아닌 "시스템을 굴러가게 하는 나사"는 JSON으로 뽑지 않습니다.
- **어드레서블 분류는 자산 종류가 아니라 수명 기준**:
  - 항상 메모리에 있는 것(플레이어, HUD, 매니저, 코어 UI) → 어드레서블 제외, 직접 참조/씬 배치.
  - 씬/스테이지 단위로 바뀌는 것(스테이지별 몬스터·이펙트 프리팹, BGM, 스테이지 데이터) → 어드레서블.
    씬 진입 시 프리로드, 씬 전환 시 일괄 Release. 그룹은 "같이 로드되고 같이 내려가는 것끼리" 나눕니다.
  - 여러 번들이 공유하는 머티리얼/텍스처/셰이더 → "Shared" 그룹에 등록. 로드 대상이 아니라
    번들 간 중복 포함을 막기 위함입니다. Analyze의 Check Duplicate Bundle Dependencies로 주기 점검.
- **프리팹 로드 구조**: 테이블 행에 어드레서블 주소 문자열을 두고, 로더가 `LoadAssetAsync`로 프리팹을
  1회 로드해 캐싱한 뒤 인스턴스 생성은 풀링(`PooledSpawner`)에 맡깁니다. `InstantiateAsync`는 쓰지 않습니다
  (인스턴스별 참조 카운트가 풀링과 충돌). 풀 비우기 → 핸들 Release 순서를 지킵니다. 본보기: `SoundManager`.
- **같은 에셋을 어드레서블과 직접 참조로 동시에 쓰지 않습니다** (빌드에 두 벌 포함되어 메모리 이중 사용).

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
- 지역 변수, 매개변수, private 필드: `camelCase`. 언더바 접두사(`_view` 같은 형태)는 사용하지 않습니다.
  기존 코드의 언더바 필드는 2026-07에 일괄 제거했습니다. `[SerializeField]` 필드를 리네임할 때는
  인스펙터 연결이 끊기지 않게 `[FormerlySerializedAs("옛이름")]`을 함께 붙입니다.
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

## 협업 규칙

- 저장소: github.com/alswns133/ProjectS. 팀원별로 `develop-이름` 브랜치에서 작업하고 PR로 `main`에 머지합니다.
  (`develop-minjun`, `develop-geunchan`, `develop-JS`, `develop-xogk2222`)
- **커밋/푸쉬 전에 반드시 최신 main을 머지하거나 pull 받은 상태인지 확인합니다.**
  오래된 로컬 상태에서 푸쉬해 최신 스크립트가 옛날 버전으로 덮어씌워진 사고가 실제로 있었습니다 (2026-07-07).
  파일을 수정하기 전, 그 파일의 최근 커밋 이력을 확인해 내 로컬이 뒤처져 있지 않은지 점검하는 것을 권장합니다.
- **CI (2026-07-22 도입)**: PR·main 푸쉬 시 GitHub Actions가 `unity-test-runner` EditMode로
  스크립트 컴파일만 검사합니다(`.github/workflows/ci.yml`). 위 2026-07-07 덮어쓰기 사고처럼 컴파일이
  깨진 코드가 main에 들어오는 것을 머지 전에 자동으로 막는 것이 도입 목적입니다.
  씬을 빌드하지 않는 이유는 씬들이 `ExternalAssets`(깃 제외)를 참조해 CI에선 Missing Prefab이
  발생하고 풀 빌드가 60분+ 걸리기 때문입니다.
  - **CI 초록불 = "컴파일됨"까지의 보증.** 씬 참조 깨짐·Missing·인스펙터 누락 같은 에셋 문제는
    CI가 못 잡으니, 머지 전 플레이 테스트로 확인합니다.
  - 라이선스는 CI 전용 Unity 계정으로 GitHub Secrets 관리. asmdef 미도입 결정과 무관하게 동작합니다
    (테스트 0개 EditMode 실행이라 테스트 asmdef 불필요). 나중에 실제 EditMode 테스트를 추가하면
    그때는 테스트용 asmdef가 필요해집니다.
  - **셀프 호스티드 러너는 필요 시 도입 가능성만 열어둔 상태(현재 미도입).** 씬 참조 깨짐·Missing·
    실제 풀 빌드까지 CI가 검증하게 하려면, `ExternalAssets`가 존재하는 개발 PC를 러너로 쓰는
    셀프 호스티드가 유일한 길입니다(GitHub 호스티드는 외부 에셋이 없어 풀 빌드 검증 자체가 불가).
    다만 러너 PC 상시 가동 부담과, public 저장소라 외부 fork PR의 코드가 그 PC에서 실행되지 않도록
    fork PR 워크플로를 관리자 승인제로 잠가야 하는 보안 부담이 있어, 컴파일 검사만으로 부족해질 때
    재검토합니다.

## 기획/설계 결정 사항

코드만 봐서는 의도를 알기 어려운, 팀에서 확정한 결정들입니다. 리뷰나 리팩토링에서 "버그처럼 보여도" 고치기 전에 의도를 확인하세요.

- **콤보 공격**: "클릭 구조 유지 + 진행도 게이트" 설계로 확정. 구현은 Animation Event 방식을 유지합니다.
  로코모션 진입 시 `OnComboWindowOpen()`이 호출되는 것은 버그가 아니라 의도된 설계입니다.
- **구르기**: WASD + 대각선 총 8방향, 블렌드 트리 애니메이션 사용.
  무입력(Idle) 상태에서는 구르지 않습니다 (원래는 앞으로 굴렀지만 기획 변경됨).
- **대시**: 대시가 끝날 때 아래로 떨어지는 동작이 의도입니다. 점프 대시 기능은 제거되었습니다.
- **네임스페이스 (2026-07-20 결정 및 일괄 적용 완료)**: 전 스크립트가 `ProjectS.` 루트 네임스페이스를 사용합니다.
  **새 파일은 폴더에 맞는 네임스페이스를 필수로 붙입니다.** 원칙: 폴더 = 네임스페이스, 깊이 최대 3단.
  - 매핑: `Core/` → `ProjectS.Core`(공용 계약: `IState`, `IDamageable`, `SoundID`),
    `Player/` → `ProjectS.Players`, `Enemy/` → `ProjectS.Enemies`, `Events/` → `ProjectS.Events`,
    `Managers/` → `ProjectS.Managers`, `Datas/` → `ProjectS.Data`, `Effect/` → `ProjectS.Effects`,
    `Scene/` → `ProjectS.Scenes`, `Camera/` → `ProjectS.Cameras`, `Debug/` → `ProjectS.Debugging`,
    `UI/Framework/` → `ProjectS.UI.Framework`(기반층), 그 외 `UI/` → `ProjectS.UI`.
  - 이름이 폴더와 다른 곳은 전부 **단순명 가림(shadowing) 회피** 목적입니다. C#은 가까운 네임스페이스 멤버가
    using으로 수입한 타입을 이기므로, 네임스페이스 세그먼트가 `Debug`/`Camera`/`Scene`(UnityEngine 내장 타입)이나
    `Player`/`Enemy`(우리 클래스명)와 같으면 프로젝트 전역에서 `Debug.Log`, `Camera.main`, `Player` 참조가
    컴파일 에러가 됩니다. 새 폴더/네임스페이스를 만들 때도 클래스명·Unity 타입명과 같은 세그먼트는 피하세요.
  - UI 의존 방향은 화면(`ProjectS.UI`) → 기반층(`ProjectS.UI.Framework`) 단방향만 허용합니다.
  - **asmdef(어셈블리 정의)는 도입하지 않기로 결정.** 네임스페이스와 별개 사안이며, 현 규모에서는
    컴파일 시간 이득이 없고 Player↔Enemy 상호 참조 구조상 순환 참조 리팩토링 비용만 발생합니다.
    수백 파일 규모가 되거나 컴파일 시간이 실제로 문제될 때 재검토합니다.
