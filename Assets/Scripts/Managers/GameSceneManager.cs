using UnityEngine;
using System.Collections.Generic; // Dictinary를 사용
using System.Collections; // IEnumerator를 사용
using UnityEngine.SceneManagement; //유니티 엔진의 신매니저를 사용하기 위해 추가
using System.Threading.Tasks;
using System;

/// <summary>
/// 씬 등록/전환을 관리하는 싱글톤
/// </summary>
public class GameSceneManager : MonoBehaviour
{
    // 1단계(프리로드)가 전체 로딩 바에서 차지하는 비중. 나머지(1 - 이 값)는 2단계(씬 로드) 몫.
    // 프리로드가 가벼우면 낮추고, 무거우면 높여 체감 시간 비율에 맞춘다.
    private const float PRELOAD_WEIGHT = 0.4f;

    public static GameSceneManager Instance { get; private set; }

    // 로드되고 있는 상태를 가리키는 변수
    [SerializeField] private bool loading = false;
    // 신을 등록하기 위한 변수
    private Dictionary<string, BaseScene> sceneDic = new Dictionary<string, BaseScene>();
    // 현재 신을 가리키는 변수
    private string current = string.Empty;

    /// <summary>
    /// 어플리케이션이 종료될 때 호출될 이벤트 메서드가 있다면 등록해서 사용하면 됩니다.
    /// </summary>
    public static event Action OnExited;

    private void Awake()
    {
        if (Instance != null)
        {
            Release();
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 사용자가 지정한 클래스만 기능을 켜두는 메서드입니다.
    private void EnableOnlyScene<T>() where T : BaseScene
    {
        string sceneName = typeof(T).Name;
        foreach (var scene in sceneDic)
        {
            if (scene.Key == sceneName)
                scene.Value.enabled = true;
            else
                scene.Value.enabled = false;

        }
    }

    /// <summary>
    /// 씬 변경을 요청합니다. (외부에서 호출하는 진입점)
    /// </summary>
    /// <typeparam name="T">Scene을 상속받은 씬 클래스</typeparam>
    public void RequestSceneChange<T>() where T : BaseScene
    {
        // 이미 로딩 중이면 중복 요청 무시
        if (loading) return;

        string sceneName = typeof(T).Name;
        if (sceneDic.ContainsKey(sceneName))
        {
            // 현재 지정한 데이터 타입만 기능을 활성화 합니다.
            EnableOnlyScene<T>();
            // 신을 비동기로 로드합니다.
            BeginSceneLoad<T>();
        }
    }

    /// <summary>
    /// 비동기로 씬을 변경한다.
    /// 1단계(프리로드)를 씬 막기 전에 모두 끝낸 뒤 2단계(씬 로드/활성화)로 넘어간다.
    /// 로드할 씬은 Build Settings에 등록되어 있어야 한다.
    /// </summary>
    private IEnumerator LoadSceneAsyncRoutine<T>() where T : BaseScene
    {
        loading = true;
        string next = typeof(T).Name;   // T의 타입 이름 = 씬 이름 = 어드레서블 키로 사용

        UIManager.Instance.ShowLoading();

        float displayProgress = 0f;   // 화면에 보여줄 보정 진행도(실제값을 부드럽게 따라감)

        // ── 1단계: 큐가 자유로운 상태에서 이전 씬 정리 + 새 씬 프리로드 ──
        // 어드레서블 비동기 프리로드는 반드시 씬을 막기 전(아래 allowSceneActivation = false 전)에
        // 끝내야 한다. 막은 뒤에 하면 비동기 큐가 통째로 멈춰 데드락이 난다.
        if (sceneDic.ContainsKey(current))
        {
            sceneDic[current].Exit();                // 이전 씬의 Exit 호출
            SoundManager.Instance.ReleaseAllClips(); // 이전 씬 사운드 메모리 해제
            UIManager.Instance.ClearPanelStack();    // 이전 씬 패널 스택 클리어
        }

        // 이 씬에 필요한 비동기 프리로드 작업들을 한 곳에 모은다.
        // 앞으로 프리팹/이펙트/데이터가 늘어도 이 리스트에 Add만 하면 되고,
        // 아래 진행도 루프는 건드릴 필요가 없다. (작업 추가를 깜빡해 바가 거짓말하는 일을 구조로 차단)
        var preloadTasks = new List<Task>
        {
            //SoundManager.Instance.PreloadSceneSounds<T>(),

            // 예: PrefabManager.Instance.PreloadSceneObjects<T>(),
            // 예: EffectManager.Instance.PreloadSceneEffects<T>(),
        };

        int total = preloadTasks.Count;

        // 끝난 작업 개수 비율로 1단계 바(0 ~ PRELOAD_WEIGHT)를 채운다.
        // 사운드 등 개별 작업의 내부 진행도는 알 수 없으므로 "완료 개수 / 전체 개수"로 근사한다.
        while (true)
        {
            int done = 0;
            for (int i = 0; i < preloadTasks.Count; i++)
                if (preloadTasks[i].IsCompleted) done++;

            float preloadRatio = (total == 0) ? 1f : (float)done / total;
            float target = preloadRatio * PRELOAD_WEIGHT;

            displayProgress = Mathf.MoveTowards(displayProgress, target, Time.deltaTime * 0.5f);
            UIManager.Instance.SetLoadingProgress(displayProgress);

            // 씬별 로딩 화면 연출 갱신(진행도에 반응하는 일러스트/영상 등). 진행도 바는 위 공통 로직이 담당.
            if (sceneDic.ContainsKey(next))
                sceneDic[next].Progress(displayProgress);

            if (done == total && displayProgress >= PRELOAD_WEIGHT - 0.001f)
                break;

            yield return null;
        }
        // ── 1단계 종료

        // ── 2단계: 프리로드가 끝난 뒤에야 씬 로드를 시작하고 막는다 ──
        // allowSceneActivation = false는 비동기 큐 전체를 막으므로,
        // 어드레서블 프리로드(1단계)가 전부 끝난 뒤에 와야 데드락이 안 난다.
        AsyncOperation operation = SceneManager.LoadSceneAsync(next);
        // false로 두면 로드가 progress 0.9에서 멈추고, 자동으로 다음 씬으로 넘어가지 않는다.
        // 활성화 타이밍을 우리가 직접 제어하기 위함.
        operation.allowSceneActivation = false;

        bool activated = false;
        while (!activated)
        {
            // 씬 진행도(0~0.9)를 뒤쪽 구간(PRELOAD_WEIGHT ~ 1.0)에 매핑해 1단계와 하나의 바로 잇는다
            float sceneRatio = Mathf.Clamp01(operation.progress / 0.9f);
            float target = PRELOAD_WEIGHT + sceneRatio * (1f - PRELOAD_WEIGHT);

            // 목표값으로 서서히 따라가기(바가 뚝뚝 끊기지 않도록) + 매 프레임 갱신
            displayProgress = Mathf.MoveTowards(displayProgress, target, Time.deltaTime * 0.5f);
            UIManager.Instance.SetLoadingProgress(displayProgress);

            // 씬별 로딩 화면 연출 갱신(진행도에 반응하는 일러스트/영상 등). 진행도 바는 위 공통 로직이 담당.
            if (sceneDic.ContainsKey(next))
                sceneDic[next].Progress(displayProgress);

            // 실제 로드 완료 + 보정 바도 끝까지 찼을 때 활성화
            if (operation.progress >= 0.9f && displayProgress >= 1f)
            {
                activated = true;

                if (sceneDic.ContainsKey(next)) sceneDic[next].Enter();  // 새 씬의 Enter 호출
                current = next;

                yield return null;                       // 씬 활성화가 반영되도록 한 프레임 대기
                operation.allowSceneActivation = true;  // 막아뒀던 활성화 허용 → 씬 전환 실행
            }

            yield return null;
        }

        UIManager.Instance.HideLoading();        // 활성화 후 로딩 닫기(한 프레임 깜빡임 방지)
        loading = false;
    }

    private IEnumerator SceneChangeDelayRoutine<T>(float delayTime) where T : BaseScene
    {
        yield return new WaitForSeconds(delayTime);
        RequestSceneChange<T>();
    }

    /// <summary>
    /// delayTime후 씬 변경을 요청합니다. (외부에서 호출하는 진입점)
    /// </summary>
    /// <typeparam name="T">Scene을 상속받은 씬 클래스</typeparam>
    /// <param name="delayTime">다음 씬으로 넘어가기전 시간</param>
    public void RequestSceneChangeWithDelay<T>(float delayTime) where T : BaseScene
    {
        if (loading) return;

        StartCoroutine(SceneChangeDelayRoutine<T>(delayTime));
    }

    /// <summary>
    /// 씬을 상속받은 클래스들을 생성하고 활성/비활성 하는 메서드
    /// </summary>
    /// <typeparam name="T">Scene를 상속받은 씬 클래스</typeparam>
    /// <param name="state">씬 클래스의 활성/비활성 여부 </param>
    /// <returns>생성되거나 이미 등록된 씬 인스턴스</returns>
    public T RegisterScene<T>(bool state) where T : BaseScene
    {
        string sceneName = typeof(T).Name;
        if (!sceneDic.ContainsKey(sceneName))
        {
            // 게임오브젝트를 생성하고, 사용자가 지정한 스크립트를 연결합니다.
            // 게임 신 매니저를 부모 계층으로 설정하고, 초기화 메서드(Initialize)를 호출합니다.
            //T t = Utility.CreateObject<T>(typeof(T).Name, transform, true);
            GameObject newObject = new GameObject(sceneName, typeof(T));
            newObject.transform.SetParent(transform);
            T t = newObject.GetComponent<T>();
            t.Initialize();
            t.enabled = state;
            sceneDic.Add(sceneName, t);
            return t;
        }

        sceneDic[sceneName].enabled = state;
        return sceneDic[sceneName] as T;
    }

    private void BeginSceneLoad<T>() where T : BaseScene
    {
        // 게임 신 매니저가 MonoBehaviour를 상속받은 이유는 코루틴을 자체적으로 실행하기 위함입니다.
        StartCoroutine(LoadSceneAsyncRoutine<T>());
    }
    private void Release()
    {
        Destroy(gameObject);
    }

    // 어플리케이션이 종료될 때 호출되는 메서드입니다.
    private void OnApplicationQuit()
    {
        OnExited?.Invoke();
    }
    // 어플리케이션이 중지되거나 다시 활성화될 때 호출되는 메서드입니다.
    private void OnApplicationPause(bool pause)
    {

    }
    // 현재 앱에 포커스가 맞춰질 때 호출되는 메서드입니다.
    private void OnApplicationFocus(bool focus)
    {

    }
}
