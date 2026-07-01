using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.AddressableAssets;

public class AddressableManager : MonoBehaviour
{
    [SerializeField] private AssetReferenceGameObject charecterObj;
    [SerializeField] private AssetReferenceGameObject[] buildingObj;

    [SerializeField] private List<GameObject> gameObjects;

    // 다른 매니저가 에셋을 요청하기 전에 어드레서블 시스템을 먼저 초기화해 둔다.
    void Awake()
    {
        StartCoroutine(InitAddressable());
    }

    IEnumerator InitAddressable()
    {
        // 어드레서블 초기화
        var init = Addressables.InitializeAsync();

        // 완료될 때까지 대기
        yield return init;
    }

    public void Button_SpawnObject()
    {
        // 비동기 생성(어드레서블)
        charecterObj.InstantiateAsync().Completed += (obj) =>
        {
            gameObjects.Add(obj.Result);
        };

        // 여러 개의 오브젝트를 생성하는 경우
        for (int i = 0; i < buildingObj.Length; i++)
        {
            for (int j = 0; j < 1000; j++)
                buildingObj[i].InstantiateAsync().Completed += (obj) =>
                {
                    gameObjects.Add(obj.Result);
                };
        }

        SoundManager.Instance.PlayBgm(101);
    }

    public void Button_Releas()
    {
        if (gameObjects.Count == 0) return;

        // 가장 마지막에 생성된 오브젝트를 해제하는 경우
        //var index = gameObjects.Count - 1;
        //Addressables.ReleaseInstance(gameObjects[index]);
        //gameObjects.RemoveAt(index);

        // 모든 오브젝트를 해제하는 경우
        foreach (var obj in gameObjects)
        {
            bool released = Addressables.ReleaseInstance(obj);
            if (!released)
                Debug.LogWarning($"{obj.name}은 Addressables 인스턴스가 아님! 일반 Destroy 필요");
        }
        gameObjects.Clear();
        SoundManager.Instance.ReleaseAllClips();
    }

    /// <summary>
    /// GC 강제 실행 버튼 (메모리 누수 방지 및 최적화)
    /// </summary>
    public void Button_ForceGC()
    {
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        Debug.Log("강제 GC 실행 완료");
    }

    public void NextScene()
    {
        GameSceneManager.Instance.RequestSceneChange<Tutorial>();
    }

}
