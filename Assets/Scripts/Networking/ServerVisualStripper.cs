using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 전용(headless) 서버에서 <b>순수 연출용 컴포넌트</b>를 꺼 CPU 낭비를 줄인다.
    /// 헤드리스 서버는 화면에 "그리지"는 않지만 파티클 시뮬레이션·오디오 같은 CPU 연산은 그대로 돌기 때문에,
    /// 로직에 무관한 연출을 씬 로드마다 정지시킨다(서버 안정성/경량화).
    ///
    /// <para><b>대상(안전한 것만):</b> ParticleSystem(정지+비활성), AudioListener(비활성).
    /// 카메라·NPC Animator는 로직이 참조할 수 있어(<c>Camera.main</c>·AI 상태) 여기서 건드리지 않는다.</para>
    ///
    /// <para>클라(일반 빌드/에디터)는 <see cref="GameNetworkManager.IsServerMode"/>가 false라 구독조차 하지 않는다.</para>
    /// </summary>
    public static class ServerVisualStripper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!GameNetworkManager.IsServerMode) return;

            // 씬 로드(마을·레이드 인스턴스 등)마다 새로 올라온 연출을 정리한다.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Strip();   // 이 훅 시점의 첫 씬도 처리
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Strip();

        private static void Strip()
        {
            // 파티클: 서버는 렌더가 없어 시뮬 자체가 낭비. 정지·클리어로 시뮬을 멈추고 렌더러만 끈다.
            // ★ GameObject는 건드리지 않는다 — 파티클이 게임플레이 스크립트와 같은 GO에 붙어 있을 때
            //   SetActive(false)로 그 스크립트까지 꺼지는 사고를 피하기 위함. 렌더러를 끄면 "No mesh data /
            //   Read-Write" 경고(렌더러가 메시 접근)도 사라진다.
            ParticleSystem[] systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            foreach (ParticleSystem ps in systems)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps.TryGetComponent(out ParticleSystemRenderer psr)) psr.enabled = false;
            }

            // 오디오리스너: 서버는 오디오 출력이 없다. 컴포넌트만 끈다(오브젝트=대개 카메라라 유지).
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (AudioListener al in listeners)
                al.enabled = false;
        }
    }
}
