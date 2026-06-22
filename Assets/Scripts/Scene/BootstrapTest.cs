using UnityEngine;

// 게임 시작 시 가장 먼저 켜지는 씬
// 역할: 데이터 / 시스템 초기화를 전부 끝낸다.로딩 화면을 보여줄 수도 있음.
// 초기화 완료 → 다음 씬으로 전환
public class BootstrapTest : MonoBehaviour
{

    private void RequsetScene()
    {
        GameSceneManager.Instance.RegisterScene<InGame>(false);
        GameSceneManager.Instance.RegisterScene<Tutorial>(false);
    }

    private async void Start()
    {
        // 1) 매니저들이 Awake에서 초기화를 '시작'할 시간을 줌
        //    (JsonManager.Awake가 ReadyTask 발사)

        // 2) 데이터 로딩이 '끝날 때까지' 여기서 기다림
        await JsonManager.Instance.ReadyTask;
        RequsetScene();
        print(Application.persistentDataPath);  // 로컬 장소
        print(Application.dataPath);            // Asset 폴더
        print(Application.streamingAssetsPath);

        // 3) 모든 준비 완료 → 게임 씬으로 전환
        Debug.Log("[BootstrapTest] 초기화 완료, 다음 씬으로 이동");
        GameSceneManager.Instance.RequestSceneChange<InGame>();
    }
}
