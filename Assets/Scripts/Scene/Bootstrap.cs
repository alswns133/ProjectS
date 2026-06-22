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

    // Start is called once before the first execution of Update after the MonoBehaviour is created
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
