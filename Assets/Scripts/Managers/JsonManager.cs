using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class JsonManager : MonoBehaviour
{
    // 싱글톤 인스턴스
    public static JsonManager Instance { get; private set; }

    // 각 데이터 테이블을 Type별로 보관하는 딕셔너리
    private readonly Dictionary<Type, object> _tables = new();

    /// <summary>
    /// 데이터 로딩이 끝났는지 외부가 확인할 수 있는 플래그
    /// </summary>
    public bool IsReady { get; private set; }

    public Task ReadyTask { get; private set; }   // ★ 외부가 이걸 await (Bootstrap에서)

    // 데이터 접근용 프로퍼티 (필요한 테이블마다 추가)
    public IReadOnlyDictionary<int, SoundTable> SoundDict => GetTable<SoundTable>();

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

        IsReady = true;   // ★ 모든 로딩이 끝난 뒤에야 true

        // 에디터에서 로드된 데이터를 확인하기 위한 디버그 리스트 초기화
#if UNITY_EDITOR
        soundDebugList = new List<SoundTable>(SoundDict.Values);
        Debug.Log("[JsonManager] 모든 데이터 로딩 완료");
#endif
    }

    // 제네릭을 활용한 데이터 등록 메서드
    private async Task RegisterAsync<T>() where T : class, IDataRow
    {
        var dict = await LoadDataDictionaryAsync<T>(typeof(T).Name);
        _tables[typeof(T)] = dict;
    }

    // 제네릭을 활용한 데이터 접근 메서드
    private Dictionary<int, T> GetTable<T>() where T : class, IDataRow
    {
        if(!IsReady)
        {
            Debug.LogWarning($"[JsonManager] 아직 데이터 로딩 중입니다. IsReady 확인 후 접근하세요.");
            return new Dictionary<int, T>();
        }

        if (_tables.TryGetValue(typeof(T), out var table)) 
            return (Dictionary<int, T>)table;

        Debug.LogError($"[JsonManager] {typeof(T).Name} 테이블 미로드. RegisterAsync<{typeof(T).Name}>() 추가했나요? (또는 IsReady 확인 전 접근?)");
        return new Dictionary<int, T>();
    }

    // Addressables에서 JSON 텍스트를 로드하고, 제네릭 리스트로 파싱한 뒤, 딕셔너리로 변환하는 메서드
    private async Task<Dictionary<int, T>> LoadDataDictionaryAsync<T>(string address) where T : class, IDataRow
    {
        var handle = Addressables.LoadAssetAsync<TextAsset>(address);
        TextAsset asset = await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
        {
            Debug.LogError($"[JsonManager] '{address}' 로드 실패");
            Addressables.Release(handle);              // 실패해도 핸들은 반드시 해제
            return new Dictionary<int, T>();
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
