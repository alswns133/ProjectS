# 인계 — 등급 공통 테이블 (옵션 최대 개수 + 표시 색) (2026-08-06, TH)

## 무엇을 했나
등급별 **옵션 최대 개수(상한)** 와 **표시 색**을 JSON 테이블로 추가했습니다. 개수는 기획서 확정값 기준.

| Grade | Label | MaxOptionCount | ColorHex |
|---|---|---|---|
| Normal | 노말 | 0 | `#D9D9D9` |
| Magic | 매직 | 1 | `#5999FF` |
| Rare | 레어 | 2 | `#FFD933` |
| Relic | 유물 | 4 | `#FF8026` |

- `Assets/Scripts/Datas/ItemGradeData.cs` — `IDataRow` 행 클래스
  (`Index`, `Grade`, `Label`, `MaxOptionCount`, `ColorHex`)
  - `Index`는 `ItemGrade` 정수(Normal 0 / Magic 1 / Rare 2 / Relic 3)
  - `Validate`: `Index`↔`Grade` 불일치 시 행 제외. 나머지는 보정 후 행을 살림
    (`MaxOptionCount` 음수면 0 / `Label` 비면 enum 이름 / `ColorHex` 파싱 실패면 흰색, 각각 경고 로그)
    → 행을 버리면 같은 행의 상한까지 날아가 옵션 개수가 조용히 0이 되므로 일부러 살립니다
  - `DisplayColor`(`Color`)는 `ColorHex`를 로딩 때 한 번만 파싱해 둔 파생 값. `[JsonIgnore]`라 JSON에는 안 나감
  - `Label`을 따로 두는 이유: 표기는 기획 결정이라 enum 이름(`Rare`)을 바꾸면 코드가 깨집니다.
    표기만 이 컬럼에서 갈아끼웁니다.
- `Assets/JsonData/ItemGradeData.json` — 4행

> 색 메모: 지금 색 값은 `ItemTooltip`이 인스펙터에 들고 있던 등급 색 4개를 그대로 옮겨 적은 것이라,
> 갈아끼워도 보이는 색은 그대로입니다. 등급 색을 쓰는 UI가 늘어날수록 인스펙터에 4개씩 복제되는 걸
> 막으려고 테이블로 뺐습니다.

> 설계 메모: 실제로 붙는 개수는 `EquipmentData.OptionCount`(아이템별, 유물 0개 예외 있음)에 그대로 둡니다.
> 이 테이블은 **등급 공통 상한**만 담당합니다. 아이템 행마다 값을 복제하지 않으려고 등급 4행으로 분리했습니다.

## JsonManager 등록 (완료)

처음엔 충돌 우려로 미뤘지만, 등급 색을 실제로 쓰려면 등록 없이는 조회 자체가 안 돼서(`GetTable`이 private)
아래 두 줄을 넣었습니다. **`JsonManager.cs`를 같이 작업 중이었다면 머지 때 확인해 주세요.**

```csharp
// InitAllDataAsync() 아이템 블록 (ItemOptionData 다음 줄)
await RegisterAsync<ItemGradeData>();

// 접근 프로퍼티 (ItemOptionDict 다음 줄)
public IReadOnlyDictionary<int, ItemGradeData> ItemGradeDict => GetTable<ItemGradeData>();
```

## Addressables 등록 (완료)

`JsonData Local Group`에 주소 `ItemGradeData`, 라벨 `jsonData`로 등록했습니다
(JsonManager가 `typeof(T).Name`을 주소로 쓰므로 클래스명과 정확히 일치해야 함).
→ 빠지면 로딩 시 `'ItemGradeData' 로드 실패` 로그가 뜨고 **등급 색이 전부 흰색**이 됩니다.
CI(컴파일 검사)로는 안 잡히니 플레이 테스트로 한 번 확인해 주세요.

## 런타임 조회 예시

```csharp
ItemGradeData grade = JsonManager.Instance.ItemGradeDict[(int)itemData.Grade];

int max = grade.MaxOptionCount;
nameText.color = grade.DisplayColor;                    // 텍스트/이미지 색
string rich = $"<color={grade.ColorHex}>{item.Name}</color>";   // TMP 리치 텍스트
```

## ItemTooltip 하드코딩 제거 (완료)

`ItemTooltip`이 들고 있던 등급 색 4개(인스펙터 `normalColor`/`magicColor`/`rareColor`/`relicColor`)와
등급 이름 4개(`GradeLabel()` switch)를 지우고 테이블 조회 하나로 합쳤습니다.
씬(`HUD(TH) 2`)에 직렬화돼 있던 색 값도 같이 제거했습니다.

```csharp
// 표기와 색은 같은 행에서 나오므로 조회는 한 번만
ItemGradeData gradeRow = GradeRow(item.Grade);
Color gradeColor = gradeRow != null ? gradeRow.DisplayColor : Color.white;
gradeText.text   = gradeRow != null ? gradeRow.Label        : item.Grade.ToString();

private static ItemGradeData GradeRow(ItemGrade grade)
    => JsonManager.Instance != null ? JsonManager.Instance.Get<ItemGradeData>((int)grade) : null;
```

로딩 전이거나 행이 없으면 색은 흰색, 표기는 enum 이름으로 떨어집니다
(등급 하나 때문에 툴팁이 안 뜨는 것보다 낫다는 판단).

## 아직 안 한 것
- 이 상한을 실제로 적용하는 **옵션 롤 로직**(등급 상한 안에서 `ItemOptionData` 풀에서 뽑기)은 미구현. 필요하면 이어서 진행.
- 다국어가 들어가면 `Label`은 결국 별도 텍스트 테이블(등급/아이템/퀘스트 문구를 한데 모은)로 옮겨야 합니다.
  지금은 등급 4행뿐이라 여기 두는 게 싸지만, 번역이 붙는 시점에 재검토가 필요합니다.
