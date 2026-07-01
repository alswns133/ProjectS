using UnityEngine;

public class Bootstrap : MonoBehaviour
{
    public void RegisterScene()
    {
        //GameSceneManager.Instance.RegisterScene<InGame>(false);
    }
    public void LoadLocalFile()
    {
        
    }
    private void LoadGameTable()
    {
        
    }

    private void SceneSettings()
    {
        
    }

    // 게임 진입점. 씬 등록·로컬 파일·테이블 로드·씬 세팅을 순서대로 끝낸 뒤 첫 게임 씬으로 전환한다.
    void Start()
    {
        RegisterScene();
        LoadLocalFile();
        LoadGameTable();
        SceneSettings();

        print(Application.persistentDataPath);  // 로컬 장소
        print(Application.dataPath);            // Asset 폴더
        print(Application.streamingAssetsPath);

        GameSceneManager.Instance.RequestSceneChange<InGame>();
    }
}
