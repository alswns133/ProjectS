using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 전용(headless) 서버 진입점. 빌드의 첫 씬은 <c>Login</c>(로그인·캐릭터 선택 = 클라 전용 흐름)이라,
    /// 서버가 그대로 두면 로그인 UI에서 멈춘다(헤드리스라 로그인 불가). 서버는 로그인·캐릭터 선택·Firebase가
    /// 필요 없으므로(그건 "누가/어느 캐릭터"를 정하는 클라 관심사, 서버는 연결 단위로만 안다),
    /// 시작 직후 그 씬들을 건너뛰고 곧장 <c>Bootstrap</c>으로 보낸다.
    ///
    /// <para><b>왜 여기서(RuntimeInitialize)인가.</b> 서버 분기를 Bootstrap 안에 두면 이미 늦다 — 서버는
    /// Bootstrap에 도달하기도 전에 Login에서 멈추기 때문. 첫 씬 로드 직후 한 번 돌아 Bootstrap으로 갈아탄다.
    /// Bootstrap이 <see cref="GameNetworkManager"/>를 만들고(StartServer), 서버 분기에서 마을을 로드한다.</para>
    ///
    /// <para>클라(일반 빌드/에디터)에서는 <see cref="GameNetworkManager.IsServerMode"/>가 false라 아무 일도
    /// 하지 않는다 — 평소 Login → CharacterSelect → Bootstrap 흐름 그대로.</para>
    /// </summary>
    public static class ServerBoot
    {
        // 서버가 직행할 부팅 씬. Bootstrap이 데이터 로딩 + 네트워크 매니저 생성 + (서버면)마을 로드를 담당한다.
        private const string BootstrapScene = "Bootstrap";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RedirectServerPastLogin()
        {
            if (!GameNetworkManager.IsServerMode) return;

            // 이미 Bootstrap(또는 그 이후)이면 다시 로드하지 않는다(무한 리다이렉트 방지).
            if (SceneManager.GetActiveScene().name == BootstrapScene) return;

            Debug.Log("[ServerBoot] 전용 서버 — 로그인/캐릭터 선택 건너뛰고 Bootstrap으로 직행.");
            SceneManager.LoadScene(BootstrapScene);
        }
    }
}
