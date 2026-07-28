using System;
using UnityEngine;
using ProjectS.Managers;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    // 게임 시작 시 가장 먼저 켜지는 씬
    // 역할: 데이터 / 시스템 초기화를 전부 끝낸다.로딩 화면을 보여줄 수도 있음.
    // 초기화 완료 → 다음 씬으로 전환
    public class BootstrapTest : MonoBehaviour
    {
        // 스테이지(씬)별 미니맵 데이터. 씬 오브젝트는 코드로 생성돼 인스펙터가 없으므로,
        // 실제 배치 오브젝트인 BootstrapTest에 모아 두고 시작 시 MinimapRegistry에 등록한다.
        // sceneName은 GameSceneManager가 쓰는 씬 클래스 이름(예: "Tutorial1")과 정확히 같아야 한다.
        [Serializable]
        private class StageMinimap
        {
            public string sceneName;
            public MinimapData data;
        }

        [Header("미니맵")]
        [SerializeField] private StageMinimap[] stageMinimaps;

        private void RequsetScene()
        {
            GameSceneManager.Instance.RegisterScene<Tutorial1>(false);
            GameSceneManager.Instance.RegisterScene<VillageGather>(false);
            GameSceneManager.Instance.RegisterScene<DungeonGather>(false);
            GameSceneManager.Instance.RegisterScene<Animator_Effect>(false);
        }

        // 스테이지별 미니맵 데이터를 조회 등록소에 넣는다. 씬 전환(RequestSceneChange)보다 먼저 호출해야
        // 전환 시점의 ApplyStage가 등록된 데이터를 찾을 수 있다. 여기는 로드만, 적용은 GameSceneManager가 한다.
        private void SetupMinimap()
        {
            if (stageMinimaps == null) return;

            foreach (StageMinimap stage in stageMinimaps)
            {
                if (stage == null) continue;
                MinimapRegistry.RegisterStage(stage.sceneName, stage.data);
            }
        }

        private async void Start()
        {
            // 1) 매니저들이 Awake에서 초기화를 '시작'할 시간을 줌
            //    (JsonManager.Awake가 ReadyTask 발사)

            // 2) 데이터 로딩이 '끝날 때까지' 여기서 기다림
            await JsonManager.Instance.ReadyTask;
            RequsetScene();
            SetupMinimap();   // 씬 전환 전에 미니맵 데이터를 등록소에 채워 둔다
            print(Application.persistentDataPath);  // 로컬 장소
            print(Application.dataPath);            // Asset 폴더
            print(Application.streamingAssetsPath);

            // 3) 모든 준비 완료 → 게임 씬으로 전환
            Debug.Log("[BootstrapTest] 초기화 완료, 다음 씬으로 이동");
            //GameSceneManager.Instance.RequestSceneChange<InGame>();
            //GameSceneManager.Instance.RequestSceneChange<Tutorial1>();
            GameSceneManager.Instance.RequestSceneChange<VillageGather>();
            //GameSceneManager.Instance.RequestSceneChange<DungeonGather>();
            //GameSceneManager.Instance.RequestSceneChange<Animator_Effect>();
        }
    }
}
