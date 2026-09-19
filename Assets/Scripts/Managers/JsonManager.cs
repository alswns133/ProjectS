using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using ProjectS.Data;

namespace ProjectS.Managers
{
    public class JsonManager : MonoBehaviour
    {
        // 싱글톤 인스턴스
        public static JsonManager Instance { get; private set; }

        // 각 데이터 테이블을 Type별로 보관하는 딕셔너리
        private readonly Dictionary<Type, object> tables = new();

        /// <summary>
        /// 데이터 로딩이 끝났는지 외부가 확인할 수 있는 플래그
        /// </summary>
        public bool IsReady { get; private set; }

        public Task ReadyTask { get; private set; }   // ★ 외부가 이걸 await (Bootstrap에서)

        // 로드에 실패한 필수 테이블 이름. 선택 테이블(미등록 허용)은 넣지 않는다.
        private readonly List<string> failedTables = new();

        /// <summary>
        /// 필수 테이블 중 로드에 실패한 것이 있는지. <c>PlayerSaveService</c>가 이 값이 참이면 저장을 막는다.
        /// </summary>
        /// <remarks>
        /// 실패한 테이블은 빈 Dictionary로 남고 <see cref="IsReady"/>는 그대로 true가 된다. 그 상태에서 인벤토리 복원은
        /// "정의 없는 아이템은 건너뜀" 규칙으로 장비를 전부 버리므로, 저장이 돌면 빈 인벤토리가 세이브를 덮어쓴다
        /// (2026-09-18 Addressables가 빠진 개발 빌드에서 실제로 장비가 소실됨). 게임이 이상해지는 건 막지 못해도
        /// 세이브만은 지키기 위한 신호다.
        /// </remarks>
        public bool HasLoadFailures => failedTables.Count > 0;

        /// <summary>로드에 실패한 필수 테이블 이름 목록(진단 로그용).</summary>
        public IReadOnlyList<string> FailedTables => failedTables;

        // 데이터 접근용 프로퍼티 (필요한 테이블마다 추가)
        public IReadOnlyDictionary<int, SoundTable> SoundDict => GetTable<SoundTable>();
        public IReadOnlyDictionary<int, PlayerStatTable> PlayerStatDict => GetTable<PlayerStatTable>();
        public IReadOnlyDictionary<int, PlayerLevelTable> PlayerLevelDict => GetTable<PlayerLevelTable>();
        public IReadOnlyDictionary<int, MonsterStatTable> MonsterStatDict => GetTable<MonsterStatTable>();
        public IReadOnlyDictionary<int, SkillTable> SkillDict => GetTable<SkillTable>();
        public IReadOnlyDictionary<int, SkillGrowthTable> SkillGrowthDict => GetTable<SkillGrowthTable>();

        public IReadOnlyDictionary<int, ItemData> ItemDict => GetTable<ItemData>();
        public IReadOnlyDictionary<int, EquipmentData> EquipmentDict => GetTable<EquipmentData>();
        public IReadOnlyDictionary<int, ConsumableData> ConsumableDict => GetTable<ConsumableData>();
        public IReadOnlyDictionary<int, ItemOptionData> ItemOptionDict => GetTable<ItemOptionData>();
        public IReadOnlyDictionary<int, ItemGradeData> ItemGradeDict => GetTable<ItemGradeData>();
        public IReadOnlyDictionary<int, EnhanceBonusData> EnhanceBonusDict => GetTable<EnhanceBonusData>();
        public IReadOnlyDictionary<int, EnhanceCostData> EnhanceCostDict => GetTable<EnhanceCostData>();
        public IReadOnlyDictionary<int, QuestTable> QuestDict => GetTable<QuestTable>();
        public IReadOnlyDictionary<int, DialogueTable> DialogueDict => GetTable<DialogueTable>();
        public IReadOnlyDictionary<int, ShopTable> ShopDict => GetTable<ShopTable>();
        public IReadOnlyDictionary<int, DungeonRewardTable> DungeonRewardDict => GetTable<DungeonRewardTable>();

        // 에디터에서 로드된 데이터를 확인하기 위한 디버그 리스트
#if UNITY_EDITOR
        [SerializeField] private List<SoundTable> soundDebugList;
#endif

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                // async void는 Awake 같은 진입점에서만 예외적으로 허용. (이유는 아래 설명)
                ReadyTask = InitAllDataAsync();   // Task를 보관해둠
            }
            else { Destroy(gameObject); }
        }

        // 모든 데이터 로딩을 담당하는 비동기 메서드
        private async Task InitAllDataAsync()
        {
            // 데이터 로딩이 필요한 테이블마다 RegisterAsync 호출
            await RegisterAsync<SoundTable>();
            await RegisterAsync<PlayerStatTable>();
            await RegisterAsync<PlayerLevelTable>();
            await RegisterAsync<MonsterStatTable>();
            await RegisterAsync<SkillTable>();

            // 스킬 성장 테이블(스킬창 K)은 2026-08-26 신설이라 어드레서블 "SkillGrowthTable"이 아직
            // 없을 수 있다. 키가 없으면 LoadAssetAsync가 throw해 이후 등록과 IsReady까지 막으므로,
            // 이 한 줄만 감싸 부팅이 멈추지 않게 한다(어드레서블 등록 후엔 정상 로드).
            try { await RegisterAsync<SkillGrowthTable>(critical: false); }
            catch (Exception e) { Debug.LogWarning($"[JsonManager] SkillGrowthTable 로드 건너뜀(어드레서블 미등록?): {e.Message}"); }

            await RegisterAsync<ItemData>();
            await RegisterAsync<EquipmentData>();
            await RegisterAsync<ConsumableData>();
            await RegisterAsync<ItemOptionData>();
            await RegisterAsync<ItemGradeData>();
            await RegisterAsync<EnhanceBonusData>();
            await RegisterAsync<EnhanceCostData>();
            await RegisterAsync<QuestTable>();
            await RegisterAsync<DialogueTable>();
            await RegisterAsync<ShopTable>();

            // 던전 보상 테이블(결과 화면)은 신설이라 어드레서블 "DungeonRewardTable"이 아직 없을 수 있다.
            // 키가 없으면 LoadAssetAsync가 throw해 부팅이 멈추므로, SkillGrowthTable과 같은 방식으로 감싼다.
            try { await RegisterAsync<DungeonRewardTable>(critical: false); }
            catch (Exception e) { Debug.LogWarning($"[JsonManager] DungeonRewardTable 로드 건너뜀(어드레서블 미등록?): {e.Message}"); }

            IsReady = true;   // ★ 모든 로딩이 끝난 뒤에야 true

            if (HasLoadFailures)
                Debug.LogError($"[JsonManager] 필수 테이블 {failedTables.Count}개 로드 실패({string.Join(", ", failedTables)}) — " +
                               "세이브 덮어쓰기를 막기 위해 이번 실행에서는 저장이 차단됩니다. 빌드의 Addressables 콘텐츠(StreamingAssets/aa)를 확인하세요.");

            // 에디터에서 로드된 데이터를 확인하기 위한 디버그 리스트 초기화
#if UNITY_EDITOR
            soundDebugList = new List<SoundTable>(SoundDict.Values);
            Debug.Log("[JsonManager] 모든 데이터 로딩 완료");
#endif
        }

        // 제네릭을 활용한 데이터 등록 메서드.
        // critical=false는 "아직 어드레서블 미등록일 수 있는" 선택 테이블 — 실패해도 저장 차단(HasLoadFailures)에 넣지 않는다.
        private async Task RegisterAsync<T>(bool critical = true) where T : class, IDataRow
        {
            var dict = await LoadDataDictionaryAsync<T>(typeof(T).Name);
            if (dict == null)
            {
                if (critical) failedTables.Add(typeof(T).Name);
                dict = new Dictionary<int, T>();   // 실패해도 조회가 예외 없이 null을 돌려주게 빈 테이블로 둔다
            }
            tables[typeof(T)] = dict;
        }

        // 제네릭을 활용한 데이터 접근 메서드
        private Dictionary<int, T> GetTable<T>() where T : class, IDataRow
        {
            if (!IsReady)
            {
                Debug.LogWarning($"[JsonManager] 아직 데이터 로딩 중입니다. IsReady 확인 후 접근하세요.");
                return new Dictionary<int, T>();
            }

            if (tables.TryGetValue(typeof(T), out var table))
                return (Dictionary<int, T>)table;

            Debug.LogError($"[JsonManager] {typeof(T).Name} 테이블 미로드. RegisterAsync<{typeof(T).Name}>() 추가했나요? (또는 IsReady 확인 전 접근?)");
            return new Dictionary<int, T>();
        }

        /// <summary>
        /// 테이블에서 행 하나를 조회한다. 없는 키면 null을 돌려주므로 호출측이 폴백을 정할 수 있다.
        /// 로딩이 끝난 뒤(IsReady 또는 ReadyTask await 이후)에 호출해야 한다.
        /// </summary>
        /// <typeparam name="T">조회할 테이블의 행 타입</typeparam>
        /// <param name="index">행의 Index(캐릭터/몬스터/스킬 ID)</param>
        /// <returns>찾은 행. 없으면 null</returns>
        public T Get<T>(int index) where T : class, IDataRow
            => GetTable<T>().TryGetValue(index, out T row) ? row : null;

        // Addressables에서 JSON 텍스트를 로드하고, 제네릭 리스트로 파싱한 뒤, 딕셔너리로 변환하는 메서드.
        // 로드 실패면 null — 호출측(RegisterAsync)이 실패를 기록하고 빈 테이블로 바꾼다.
        private async Task<Dictionary<int, T>> LoadDataDictionaryAsync<T>(string address) where T : class, IDataRow
        {
            var handle = Addressables.LoadAssetAsync<TextAsset>(address);
            TextAsset asset = await handle.Task;

            if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
            {
                Debug.LogError($"[JsonManager] '{address}' 로드 실패");
                Addressables.Release(handle);              // 실패해도 핸들은 반드시 해제
                return null;
            }

            List<T> list = JsonConvert.DeserializeObject<List<T>>(asset.text);

            var dict = new Dictionary<int, T>(list?.Count ?? 0);
            if (list != null)
            {
                foreach (var item in list)
                {
                    // 검증: false면 치명적 결함이라 제외, true면(보정 포함) 통과
                    if (!item.Validate(out string error))
                    {
                        Debug.LogWarning($"[JsonManager] {error} ({address})");
                        continue;   // ★ 딕셔너리에 안 넣고 건너뜀
                    }

                    if (!dict.TryAdd(item.Index, item))
                        Debug.LogWarning($"[JsonManager] 중복 키: {item.Index} ({address})");
                }
            }

            Addressables.Release(handle);   // ★ 파싱 끝났으니 원본 TextAsset 즉시 해제
            return dict;
        }

        /// JSON 데이터를 파일로 저장하는 유틸리티 메서드
        public void SaveDataToJson<T>(T data, string fileName)
        {
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            string path = Path.Combine(Application.persistentDataPath, fileName + ".json");
            File.WriteAllText(path, json);
        }
    }
}
