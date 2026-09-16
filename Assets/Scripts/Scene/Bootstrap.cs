using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectS.Managers;
using ProjectS.Networking;
using ProjectS.UI;
using ProjectS.Core;

namespace ProjectS.Scenes
{
    // 게임 시작 시 가장 먼저 켜지는 씬
    // 역할: 데이터 / 시스템 초기화를 전부 끝낸다.로딩 화면을 보여줄 수도 있음.
    // 초기화 완료 → 다음 씬으로 전환
    public class Bootstrap : MonoBehaviour
    {
        // 스테이지(씬)별 미니맵 데이터. 씬 오브젝트는 코드로 생성돼 인스펙터가 없으므로,
        // 실제 배치 오브젝트인 Bootstrap에 모아 두고 시작 시 MinimapRegistry에 등록한다.
        // sceneName은 GameSceneManager가 쓰는 씬 클래스 이름(예: "Tutorial1")과 정확히 같아야 한다.
        [Serializable]
        private class StageMinimap
        {
            public string sceneName;
            public MinimapData data;
        }

        [Header("미니맵")]
        [SerializeField] private StageMinimap[] stageMinimaps;

        private TutorialState tutorialState;

        // 아직 튜토리얼을 실행해야 하는지 여부(미완료/진행중이면 튜토리얼 씬으로 보낸다).
        private bool isTutorial => tutorialState == TutorialState.Undone
                                || tutorialState == TutorialState.Ongoing;



        private void RequsetScene() 
        {
            GameSceneManager.Instance.RegisterScene<VillageGather>(false);
            GameSceneManager.Instance.RegisterScene<Dungeon1>(false);
            GameSceneManager.Instance.RegisterScene<Dungeon2>(false);
            GameSceneManager.Instance.RegisterScene<Raid>(false);
            GameSceneManager.Instance.RegisterScene<Tutorial>(false);
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
            // 0) 첫 프레임부터 로딩 화면으로 화면을 덮는다.
            //    이 시점엔 게임 카메라(플레이어 프리팹 안, 부트스트랩에선 비활성)가 아직 없어,
            //    안 가리면 카메라 기본 배경(파란색)이 데이터 로딩~첫 씬 활성화 사이에 노출된다.
            //    씬 전환(GameSceneManager)에서도 ShowLoading을 다시 부르지만, 그 전 구간까지 앞당겨 덮는다.
            //    ★ 전용 서버는 렌더링/클라 UI가 없으므로 로딩 화면을 띄우지 않는다.
            if (!GameNetworkManager.IsServerMode) UIManager.Instance.ShowLoading();

            // 1) 매니저들이 Awake에서 초기화를 '시작'할 시간을 줌
            //    (JsonManager.Awake가 ReadyTask 발사)

            // 2) 데이터 로딩이 '끝날 때까지' 여기서 기다림
            await JsonManager.Instance.ReadyTask;
            RequsetScene();
            SetupMinimap();   // 씬 전환 전에 미니맵 데이터를 등록소에 채워 둔다

            // 전용 서버: 로그인·튜토리얼·클라 UI/씬 흐름을 타지 않는다. 데이터가 준비된 이 시점에
            // 마을 월드만 서버용으로 로드한다(B안 — Mirror ServerChangeScene 아님; 클라는 GameSceneManager로 독립 씬관리).
            // 접속·연결별 플레이어(ChatManager) 스폰은 GameNetworkManager가 담당하고, StartServer는 이미 완료됐다.
            // 씬 파일명은 씬 클래스명과 같아야 한다("VillageGather") — RequestSceneChange<VillageGather>와 동일 규약.
            if (GameNetworkManager.IsServerMode)
            {
                SceneManager.LoadSceneAsync(nameof(VillageGather), LoadSceneMode.Single);
                return;
            }

            print(Application.persistentDataPath);  // 로컬 장소
            print(Application.dataPath);            // Asset 폴더
            print(Application.streamingAssetsPath);

            // 3) 모든 준비 완료 → 게임 씬으로 전환
            Debug.Log("[Bootstrap] 초기화 완료, 다음 씬으로 이동");

            var selected = GameSession.SelectedCharacter;
            tutorialState = selected != null ? selected.tutorialState
                                             : TutorialState.Completed; // 로그인 없이 씬 직접 실행 → 마을 폴백
            if (isTutorial)
            {
                GameSceneManager.Instance.RequestSceneChange<Tutorial>();
            }
            else
            {
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
            }
        }
    }
}
