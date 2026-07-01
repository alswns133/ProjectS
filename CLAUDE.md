# ProjectS — Code 작업 가이드

Unity 3D 액션 RPG 프로젝트. 모든 스크립트는 `Assets/Scripts/` 아래에 있다.
이 문서는 코드를 작성/수정할 때 지켜야 할 아키텍처 패턴과 코딩 컨벤션을 정의한다.

---

## 아키텍처 핵심 패턴

### 플레이어 — 중앙 컨텍스트 + 상태 머신
- `Player`가 중앙 컨텍스트("뇌"). 기능은 컴포넌트로 분리한다:
  `PlayerInputHandler`(입력) / `PlayerMovement`(이동) / `PlayerAnimation`(애니) /
  `PlayerCombat`(전투) / `PlayerStats`(스탯).
- 컴포넌트 참조는 `{ get; private set; }`로 노출(읽기 허용, 교체 금지). `Awake`에서 1회 캐싱.
- 상태는 `IState` / `BaseState` / `PlayerStateMachine` 패턴. 전환은 반드시 `ChangeState`를 거친다(Exit→Enter 보장).
- **기능을 넣을 위치**: 입력→`PlayerInputHandler`, 이동→`PlayerMovement`, 전투/쿨타임/판정→`PlayerCombat`,
  Animator 제어→`PlayerAnimation`. `Player`는 이들을 중재만 한다.

### 이벤트 — static 이벤트 + Fire 메서드
- `PlayerEvents` 같은 전역 이벤트는 `static event` + `FireXxx()` 발행 메서드 쌍으로 구성.
- ★ **새 static 이벤트를 추가하면 `ResetStatics()`에 반드시 `= null`을 추가**한다
  (도메인 리로드를 꺼도 이전 플레이 세션의 죽은 구독자가 남지 않도록).
- 구독/해제는 `OnEnable` ↔ `OnDisable` 짝으로 맞춘다.

### 매니저 — 싱글톤
- `public static XxxManager Instance { get; private set; }`, `Awake`에서 중복 인스턴스 `Destroy`.
- 씬을 넘겨 유지할 것은 `DontDestroyOnLoad`.

### 리소스 — Addressables
- 에셋 로드는 Addressables 사용. 핸들을 보관했다가 씬 전환 시 `Release`로 해제.

---

## 코딩 컨벤션

### 네이밍
- 클래스 / 메서드 / 프로퍼티 / 상수: `PascalCase`
- 지역변수 / 매개변수 / private 필드: `camelCase` (예: `moveSpeed`, `controller`)
  - 일부 매니저 파일은 `_camelCase`(예: `_bgmSource`)를 쓴다 → **그 파일 안에서는 기존 스타일 유지**.
- 상수: `PascalCase`. 단 믹서/셰이더 문자열 키는 `ALL_CAPS` 허용(예: `BGM_VOLUME_PARAM`).
- 이벤트: `OnXxx`, 발행 메서드: `FireXxx`.
- bool: `IsXxx` / `HasXxx` / `CanXxx` (예: `IsGrounded`, `CanUseSkill`).

### XML 문서 주석 (public 필수)
- **모든 public 멤버**(클래스/메서드/프로퍼티/이벤트)에 `/// <summary>`를 단다. 형식:
  ```csharp
  /// <summary>
  /// 레벨업 이벤트 발행. 구독자에게 도달한 레벨을 알림.
  /// </summary>
  /// <param name="level">새로 도달한 레벨</param>
  public static void FireLevelUp(int level) => OnLevelUp?.Invoke(level);
  ```
- 매개변수가 있으면 `<param>`, 반환값이 있으면 `<returns>`를 함께 적는다.
- 사용법/주의가 필요하면 `<remarks>`로 보강(예: `SoundManager`).

### 일반 주석
- 한국어로 작성. **"코드가 무엇을 하는지"를 반복하지 말 것** — 그런 주석은 금방 낡고 소음이 된다.
  나쁜 예: `// 이동 잠금 해제 메서드` (메서드 이름만 봐도 아는 내용)
- 대신 **왜 / 언제 / 누가 호출하는지 / 빠지면 어떤 문제가 생기는지**를 쓴다. 초보자일수록 이게 도움이 크다.
  좋은 예:
  ```csharp
  /// <summary>
  /// 공격/스킬 애니메이션이 끝나 로코모션 상태로 돌아왔을 때 이동 제한을 해제한다.
  /// 주로 ComboResetBehaviour가 호출하며, 놓치면 안전장치 타이머가 대신 푼다.
  /// </summary>
  public void UnlockMovement() => IsMovementLocked = false;
  ```
- 치명적이거나 놓치기 쉬운 부분은 `★`로 강조한다.
- private/지역 로직 설명은 `//` 인라인으로.

### 포맷팅
- 들여쓰기 4칸(스페이스).
- 중괄호는 Allman 스타일(여는 `{`를 새 줄에).
- 멤버 사이에는 빈 줄 1개.
- 키워드 뒤 공백: `if (cond)`, `for (int i = 0; ...)`.
- **가드절은 한 줄 허용**: `if (IsDead) return;`
  단, 본문이 2줄 이상이면 반드시 중괄호 블록으로 감싼다.
- 우변에서 타입이 자명하면 `var` 사용(예: `var go = new GameObject();`).

### 인스펙터 필드
- `[SerializeField] private` + 뒤에 `//` 로 역할 설명.
- 관련 필드는 `[Header("...")]`로 그룹화.

---

## Unity 특성상 주의
- `GetComponent`는 `Awake`에서 1회 캐싱. 매 프레임 호출 금지.
- Animator 파라미터는 `Animator.StringToHash`로 캐싱해서 int로 사용(`PlayerAnimation` 참고).
- 코드만으로 끝나지 않는 작업(애니메이터 상태/태그 설정, 인스펙터 값 세팅, StateMachineBehaviour 부착 등)은
  구현 후 **사용자에게 해당 에디터 작업을 명확히 안내**한다.

## 작업 원칙
- 기존 파일을 수정할 때는 위 규칙보다 **그 파일의 기존 스타일을 우선** 따른다(국소적 일관성).
- 새 파일/기능은 이 문서의 패턴과 컨벤션에 맞춘다.
