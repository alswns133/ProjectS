using System.Text;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 전용 서버 콘솔에 일정 주기로 <b>서버 프레임·네트워크 송수신량·접속별 핑</b>을 찍는 진단 로그.
    /// 헤드리스 서버는 화면이 없어 프레임을 볼 방법이 없으므로, "끊김이 서버 과부하인지 / 특정 클라 회선인지"를
    /// 숫자로 가르기 위해 둔다(2026-10-07 "파티 2개 레이드 시 3명만 끊김" 진단용).
    /// </summary>
    /// <remarks>
    /// <para><b>읽는 법.</b> 프레임 예산은 서버 <c>targetFrameRate</c>에서 잡는다(30fps=33ms, 60fps=16.7ms).
    /// <c>fps</c>가 목표보다 떨어지거나 <c>지연프레임</c>(예산의 1.5배 초과)이 많으면 서버가 틱을 못 맞추는 것(전원이 끊겨야 정상).
    /// 서버 프레임이 멀쩡한데 특정 접속만 <c>rtt</c>가 튀면 그 클라 회선/전송 문제다.</para>
    /// <para>씬 배치가 필요 없다 — 서버 모드일 때만 스스로 생성된다(<see cref="ServerVisualStripper"/>와 같은 방식).
    /// 클라·에디터에서는 아무것도 하지 않는다.</para>
    /// <para>송수신량 집계는 <see cref="NetworkDiagnostics"/> 이벤트 구독이라 메시지마다 박싱 할당이 생긴다.
    /// 진단이 끝나면 <see cref="EnabledOnServer"/>를 false로 두거나 파일째 빼도 게임 로직에는 영향이 없다.</para>
    /// </remarks>
    public class ServerPerfLog : MonoBehaviour
    {
        // 진단을 끄고 싶을 때 여기만 false로 바꾼다(코드 상수 — 인스펙터에 둘 오브젝트가 없다).
        private const bool EnabledOnServer = true;

        // 로그 주기(초). 너무 짧으면 콘솔이 로그로 덮여 다른 진단 로그를 읽기 어렵다.
        private const float ReportInterval = 5f;

        // 서버 프레임 예산(ms). GameNetworkManager가 고정한 targetFrameRate를 따른다(값을 바꿔도 로그가 맞게).
        private static float FrameBudgetMs => 1000f / Mathf.Max(1, Application.targetFrameRate);

        private float windowStart;
        private int frames;
        private float sumMs;
        private float worstMs;
        private int overBudget;

        private long outBytes;
        private long inBytes;

        private readonly StringBuilder sb = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!EnabledOnServer || !GameNetworkManager.IsServerMode) return;

            GameObject go = new GameObject(nameof(ServerPerfLog));
            DontDestroyOnLoad(go);
            go.AddComponent<ServerPerfLog>();
        }

        private void OnEnable()
        {
            NetworkDiagnostics.OutMessageEvent += OnOut;
            NetworkDiagnostics.InMessageEvent += OnIn;
            windowStart = Time.unscaledTime;
        }

        private void OnDisable()
        {
            NetworkDiagnostics.OutMessageEvent -= OnOut;
            NetworkDiagnostics.InMessageEvent -= OnIn;
        }

        // 브로드캐스트는 count(받는 접속 수)만큼 실제로 나간다.
        private void OnOut(NetworkDiagnostics.MessageInfo info) => outBytes += (long)info.bytes * info.count;

        private void OnIn(NetworkDiagnostics.MessageInfo info) => inBytes += info.bytes;

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            frames++;
            sumMs += ms;
            if (ms > worstMs) worstMs = ms;
            if (ms > FrameBudgetMs * 1.5f) overBudget++;   // 약간의 지터는 빼고 확실히 밀린 프레임만 센다

            float elapsed = Time.unscaledTime - windowStart;
            if (elapsed < ReportInterval) return;

            Report(elapsed);

            windowStart = Time.unscaledTime;
            frames = 0;
            sumMs = 0f;
            worstMs = 0f;
            overBudget = 0;
            outBytes = 0;
            inBytes = 0;
        }

        private void Report(float elapsed)
        {
            int instances = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (PartyInstanceInterestManagement.IsInstanceScene(SceneManager.GetSceneAt(i))) instances++;

            sb.Clear();
            sb.Append($"[진단][ServerPerf] fps={frames / elapsed:0.0} 평균={sumMs / Mathf.Max(1, frames):0.0}ms 최악={worstMs:0.0}ms ")
              .Append($"목표={Application.targetFrameRate} 지연프레임(>{FrameBudgetMs * 1.5f:0}ms)={overBudget}회 | 인스턴스={instances} spawned={NetworkServer.spawned.Count} ")
              .Append($"접속={NetworkServer.connections.Count} | 송신={outBytes / elapsed / 1024f:0.0}KB/s 수신={inBytes / elapsed / 1024f:0.0}KB/s");

            // 접속별: 누가 어느 인스턴스에서 핑이 튀는지. "3명만 끊김"이 어느 3명인지 여기서 가른다.
            foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
            {
                if (conn == null) continue;

                string who = "?";
                uint party = 0;
                string scene = "-";
                if (conn.identity != null)
                {
                    scene = conn.identity.gameObject.scene.name;
                    if (conn.identity.TryGetComponent(out PlayerPresence p))
                    {
                        who = p.DisplayName;
                        party = p.PartyId;
                    }
                }

                sb.Append($"\n    conn{conn.connectionId} '{who}' 파티={party} 씬={scene} ip={conn.address} rtt={conn.rtt * 1000:0}ms");
            }

            Debug.Log(sb.ToString());
        }
    }
}
