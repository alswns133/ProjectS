using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets;
using System.Threading.Tasks;

/// <summary>
/// 게임 전체의 사운드를 관리하는 싱글톤 매니저.
/// BGM/SFX 재생, 볼륨 조절, Addressables 기반 로드/해제를 담당한다.
/// </summary>
/// <remarks>
/// - 다른 스크립트에서 SoundManager.Instance.PlaySFX(id) 형태로 호출
/// - 클립은 씬 단위로 로드되며, 씬 전환 시 ReleaseAllClips() 호출 필요
/// - 3D 사운드는 PlaySFX3D(id, position) 사용
/// </remarks>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("믹서")]
    [SerializeField] private AudioMixer audioMixer; // 에디터 확인용 (빌드 전 정리 가능)

    [Header("SFX Pool 설정")]
    [SerializeField] private int initialPoolSize = 10; // 시작 시 미리 만들어둘 개수
    [SerializeField] private int maxPoolSize = 30;     // 무한 증가 방지용 상한선

    private const string BGM_VOLUME_PARAM = "BGMVolume";
    private const string SFX_VOLUME_PARAM = "SFXVolume";

    // AudioMixer에서 사실상 무음으로 취급되는 dB (SetXxxVolume(0)과 동일한 값)
    private const float MUTE_DB = -80f;

    // 뮤트 해제 시 복원할 dB. 뮤트 직전 볼륨을 보관해두는 용도 (2026-07-20 TH 추가)
    private float savedBgmDb;
    private float savedSfxDb;

    /// <summary>
    /// BGM이 뮤트 상태인지 여부. UI 토글 초기 상태 동기화에 사용.
    /// </summary>
    public bool IsBgmMuted { get; private set; }

    /// <summary>
    /// SFX가 뮤트 상태인지 여부. UI 토글 초기 상태 동기화에 사용.
    /// </summary>
    public bool IsSfxMuted { get; private set; }

    private AudioMixerGroup bgmGroup;
    private AudioMixerGroup sfxGroup;

    // BGM은 보통 1개만 재생되므로 전용 소스 하나만 둠
    private AudioSource bgmSource;

    // 디버깅용 SerializeField. 빌드 전 주석 해제 → readonly 복구
    // SFX는 Pool로 관리 (동적 확장)
    // FormerlySerializedAs: 언더바 제거 리네임(_sfxPool→sfxPool) 전에 저장된
    // 인스펙터 연결이 끊기지 않게 유지한다. 씬 재저장 후에는 지워도 된다.
    [FormerlySerializedAs("_sfxPool")]
    [SerializeField] private /*readonly*/ List<AudioSource> sfxPool = new List<AudioSource>();

    // Addressables로 로드한 클립 캐시 (씬 단위로 로드/해제)
    private readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
    // 해제를 위해 핸들도 같이 보관해둠
    private readonly Dictionary<string, AsyncOperationHandle<AudioClip>> handles =
        new Dictionary<string, AsyncOperationHandle<AudioClip>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        audioMixer = Resources.Load<AudioMixer>("AudioMixer");

        // 믹서 그룹은 한 번만 찾아서 캐싱 (FindMatchingGroups는 비용이 있음)
        bgmGroup = audioMixer.FindMatchingGroups(BGM_VOLUME_PARAM)[0];
        sfxGroup = audioMixer.FindMatchingGroups(SFX_VOLUME_PARAM)[0];

        // BGM 소스 생성 (2D 사운드)
        bgmSource = CreateAudioSource("BGM_Source", bgmGroup);
        bgmSource.spatialBlend = 0f; // 0 = 완전 2D

        // SFX Pool 미리 채워두기
        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateSfxSource();
        }
    }

    // AudioSource 생성 헬퍼
    private AudioSource CreateAudioSource(string name, AudioMixerGroup group)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.outputAudioMixerGroup = group;
        return source;
    }

    private AudioSource CreateSfxSource()
    {
        var source = CreateAudioSource($"SFX_Source_{sfxPool.Count}", sfxGroup);
        sfxPool.Add(source);
        return source;
    }

    // Pool에서 사용 가능한 SFX 소스 가져오기 (없으면 동적 확장)
    private AudioSource GetAvailableSfxSource()
    {
        // 1순위: 재생 안 하고 노는 소스 찾기
        for (int i = 0; i < sfxPool.Count; i++)
        {
            if (!sfxPool[i].isPlaying) return sfxPool[i];
        }

        // 2순위: 아직 상한선 안 넘었으면 새로 만들기
        if (sfxPool.Count < maxPoolSize)
        {
            return CreateSfxSource();
        }

        // 3순위: 상한선 도달 → 가장 오래된(0번) 소스를 강제로 재활용
        // RPG에서 동시 사운드가 폭발해도 안정적으로 동작하게 하는 fallback
        return sfxPool[0];
    }

    /// <summary>
    /// Addressables로 클립 하나를 미리 로드
    /// </summary>
    /// <param name="fileName">사운드 파일의 이름</param>
    /// <returns>로드 완료를 기다릴 수 있는 Task</returns>
    public Task PreloadClip(string fileName)
    {
         return LoadClipAsync(fileName);
    }

    /// <summary>
    /// 씬 단위로 클립을 미리 로드
    /// </summary>
    /// <typeparam name="T">Scene를 상속받은 씬 클래스</typeparam>
    /// <returns>해당 씬 사운드 전체의 프리로드 완료 Task</returns>
    public async Task PreloadSceneSounds<T>() where T : BaseScene
    {
        string sceneName  = typeof(T).Name;

        var tasks = new List<Task>();
        foreach (var data in JsonManager.Instance.SoundDict.Values)
        {
            if (data.Scene == sceneName)
                tasks.Add(PreloadClip(data.FileName));   // 기다리지 말고 일단 다 발사
        }
        await Task.WhenAll(tasks);   // 전부 끝날 때까지 한 번에 기다림
    }

    /// <summary>
    /// AudioClip을 캐시 우선으로 로드. 캐시에 없으면 Addressables로 로드 후 캐싱하고,
    /// 해제용 핸들도 함께 보관.
    /// </summary>
    /// <param name="fileName">사운드 파일의 이름</param>
    /// <returns>오디오 클립을 반환</returns>
    private async Task<AudioClip> LoadClipAsync(string fileName)
    {
        // 이미 캐시에 있으면 그대로 반환
        if (clipCache.TryGetValue(fileName, out var cached)) return cached;

        var handle = Addressables.LoadAssetAsync<AudioClip>(fileName);
        await handle.Task;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            clipCache[fileName] = handle.Result;
            handles[fileName] = handle; // 나중에 해제하려고 핸들 보관
            return handle.Result;
        }

        Debug.LogError($"[SoundManager] '{fileName}' 로드 실패");
        Addressables.Release(handle);
        return null;
    }

    /// <summary>
    /// 씬 전환 시 호출: 로드했던 사운드 전부 해제
    /// </summary>
    public void ReleaseAllClips()
    {
        StopBgm();

        // 재생 중인 SFX도 정지
        foreach (var src in sfxPool) src.Stop();

        foreach (var handle in handles.Values)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        handles.Clear();
        clipCache.Clear();
    }


    /// <summary>
    /// BGM 재생 (2D). 
    /// 정상 경로에선 PreloadSceneSounds로 미리 로드된 클립을 즉시 재생.
    /// 프리로드 안 된 경우엔 폴백으로 비동기 로드 후 재생.
    /// </summary>
    /// <param name="soundIndex">사운드 테이블의 인덱스</param>
    public async void PlayBgm(int soundIndex)
    {
        if (!JsonManager.Instance.SoundDict.TryGetValue(soundIndex, out var table))
        {
            Debug.LogError($"[SoundManager] Index {soundIndex}를 찾을 수 없습니다.");
            return;
        }

        // 프리로드됐으면 즉시 완료, 아니면 여기서 로드 (폴백)
        AudioClip clip = await LoadClipAsync(table.FileName);
        if (clip == null) return;

        // 이미 같은 곡 재생 중이면 무시
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.volume = table.Volume;
        bgmSource.loop = table.Loop;
        bgmSource.Play();
    }

    /// <summary>
    /// bgm 정지
    /// </summary>
    public void StopBgm()
    {
        bgmSource.Stop();
        bgmSource.clip = null;
    }

    /// <summary>
    /// SFX 재생 - 2D (UI, 시스템 사운드 등)  
    /// 정상 경로에선 PreloadSceneSounds로 미리 로드된 클립을 즉시 재생.
    /// 프리로드 안 된 경우엔 폴백으로 비동기 로드 후 재생.
    /// </summary>
    /// <param name="soundIndex">사운드 테이블의 인덱스</param>
    public async void PlaySFX(int soundIndex)
    {
        if (!JsonManager.Instance.SoundDict.TryGetValue(soundIndex, out var table))
        {
            Debug.LogError($"[SoundManager] Index {soundIndex}를 찾을 수 없습니다.");
            return;
        }

        // 프리로드됐으면 즉시 완료, 아니면 여기서 로드 (폴백)
        AudioClip clip = await LoadClipAsync(table.FileName);
        if (clip == null) return;

        AudioSource source = GetAvailableSfxSource();
        source.transform.position = Vector3.zero;
        source.spatialBlend = 0f;        // 2D
        source.clip = clip;
        source.loop = table.Loop;
        source.volume = table.Volume;
        source.Play();
    }

    /// <summary>
    /// SFX 재생 - 3D (스킬, 몬스터, 발소리 등 위치 기반)
    /// 정상 경로에선 PreloadSceneSounds로 미리 로드된 클립을 즉시 재생.
    /// 프리로드 안 된 경우엔 폴백으로 비동기 로드 후 재생.
    /// </summary>
    /// <param name="soundIndex">사운드 테이블의 인덱스</param>
    /// <param name="position">재생 될 오브젝트의 위치</param>
    public async void PlaySFX3D(int soundIndex, Vector3 position)
    {
        if (!JsonManager.Instance.SoundDict.TryGetValue(soundIndex, out var table))
        {
            Debug.LogError($"[SoundManager] Index {soundIndex}를 찾을 수 없습니다.");
            return;
        }

        AudioClip clip = await LoadClipAsync(table.FileName);
        if (clip == null) return;

        AudioSource source = GetAvailableSfxSource();
        source.transform.position = position;
        source.spatialBlend = 1f;        // 1 = 완전 3D
        source.clip = clip;
        source.loop = table.Loop;
        source.volume = table.Volume;
        source.Play();
    }

    /// <summary>
    /// Transform을 넘기면 그 위치에서 재생 (편의 오버로드)
    /// </summary>
    /// <param name="soundIndex">사운드 테이블의 인덱스</param>
    /// <param name="target">소리를 재생할 대상 Transform (위치를 따라감)</param>
    public void PlaySFX3D(int soundIndex, Transform target)
    {
        if (target != null) PlaySFX3D(soundIndex, target.position);
    }

    /// <summary>
    /// bgm 볼륨 조절 (Mixer dB 변환)
    /// </summary>
    /// <param name="volume">볼륨 (0~100, 슬라이더 값 기준)</param>
    public void SetBgmVolume(int volume)
    {
        float linear = Mathf.Max(0.0001f, volume / 100f);
        float db = 20f * Mathf.Log10(linear);

        // 뮤트 중 볼륨 변경은 복원값에만 반영. 믹서에 바로 쓰면 뮤트가 풀려버림 (2026-07-20 TH 수정)
        if (IsBgmMuted)
        {
            savedBgmDb = db;
            return;
        }

        audioMixer.SetFloat(BGM_VOLUME_PARAM, db);
    }

    /// <summary>
    /// sfx 볼륨 조절 (Mixer dB 변환)
    /// </summary>
    /// <param name="volume">볼륨 (0~100, 슬라이더 값 기준)</param>
    public void SetSfxVolume(int volume)
    {
        float linear = Mathf.Max(0.0001f, volume / 100f);
        float db = 20f * Mathf.Log10(linear);

        // 뮤트 중 볼륨 변경은 복원값에만 반영. 믹서에 바로 쓰면 뮤트가 풀려버림 (2026-07-20 TH 수정)
        if (IsSfxMuted)
        {
            savedSfxDb = db;
            return;
        }

        audioMixer.SetFloat(SFX_VOLUME_PARAM, db);
    }

    /// <summary>
    /// BGM 뮤트 on/off. 뮤트 시 현재 볼륨(dB)을 보관했다가 해제 시 그대로 복원한다.
    /// UI 뮤트 토글(CyberIconToggleView.OnToggled)에서 호출하는 용도. (2026-07-20 TH 추가)
    /// </summary>
    /// <param name="mute">true면 무음, false면 뮤트 직전 볼륨으로 복원</param>
    public void SetBgmMute(bool mute)
    {
        // 같은 상태로 중복 호출 시 savedBgmDb가 -80으로 덮여 복원이 망가지는 것 방지
        if (mute == IsBgmMuted) return;

        if (mute)
        {
            audioMixer.GetFloat(BGM_VOLUME_PARAM, out savedBgmDb);
            audioMixer.SetFloat(BGM_VOLUME_PARAM, MUTE_DB);
        }
        else
        {
            audioMixer.SetFloat(BGM_VOLUME_PARAM, savedBgmDb);
        }

        IsBgmMuted = mute;
    }

    /// <summary>
    /// SFX 뮤트 on/off. 뮤트 시 현재 볼륨(dB)을 보관했다가 해제 시 그대로 복원한다.
    /// UI 뮤트 토글(CyberIconToggleView.OnToggled)에서 호출하는 용도. (2026-07-20 TH 추가)
    /// </summary>
    /// <param name="mute">true면 무음, false면 뮤트 직전 볼륨으로 복원</param>
    public void SetSfxMute(bool mute)
    {
        // 같은 상태로 중복 호출 시 savedSfxDb가 -80으로 덮여 복원이 망가지는 것 방지
        if (mute == IsSfxMuted) return;

        if (mute)
        {
            audioMixer.GetFloat(SFX_VOLUME_PARAM, out savedSfxDb);
            audioMixer.SetFloat(SFX_VOLUME_PARAM, MUTE_DB);
        }
        else
        {
            audioMixer.SetFloat(SFX_VOLUME_PARAM, savedSfxDb);
        }

        IsSfxMuted = mute;
    }
}
