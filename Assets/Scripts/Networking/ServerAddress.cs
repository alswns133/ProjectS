using System;
using Mirror;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 접속할 <b>서버 주소·포트를 런타임에 정하는</b> 단일 진입점.
    /// 우선순위는 <c>실행 인자 → PlayerPrefs → 인스펙터 기본값</c>이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가(2026-09-22).</b> 씬에 저장된 <c>networkAddress</c>가 <c>localhost</c>여서, 다른 PC에서
    /// 띄운 클라가 <b>자기 자신에게</b> 접속을 시도했다. 서버에는 아무 기록도 남지 않아(연결 자체가 성립하지
    /// 않으므로 인증 요청도 오지 않는다) 원인을 찾기 어려웠다. 주소를 바꾸려고 매번 다시 빌드하지 않도록
    /// 실행 시점에 지정할 수 있게 한다.
    /// </para>
    /// <para>
    /// <b>쓰는 법.</b> <c>ProjectS.exe -server 192.168.0.5</c> 또는 <c>-server=192.168.0.5</c>.
    /// 포트도 같은 방식으로 <c>-port 7777</c>. 인자 없이 띄우는 빌드를 위해 <see cref="Save"/>로
    /// PlayerPrefs에 저장해 둘 수도 있다(설정 화면이 생기면 거기서 부르면 된다).
    /// </para>
    /// <para>
    /// 에디터에서도 인자를 읽지만, 에디터는 보통 <c>hostInEditor</c>로 Host가 되므로 영향이 없다.
    /// </para>
    /// </remarks>
    public static class ServerAddress
    {
        /// <summary>서버 주소를 지정하는 실행 인자 이름.</summary>
        public const string AddressArg = "-server";

        /// <summary>포트를 지정하는 실행 인자 이름.</summary>
        public const string PortArg = "-port";

        /// <summary>주소를 기억해 두는 PlayerPrefs 키.</summary>
        public const string AddressPrefsKey = "net.serverAddress";

        /// <summary>포트를 기억해 두는 PlayerPrefs 키.</summary>
        public const string PortPrefsKey = "net.serverPort";

        /// <summary>
        /// 이번 실행에서 쓸 서버 주소를 고른다.
        /// </summary>
        /// <param name="fallback">아무 지정도 없을 때 쓸 값(보통 인스펙터의 networkAddress).</param>
        /// <returns>실행 인자 → 저장값 → fallback 순으로 고른 주소.</returns>
        public static string Resolve(string fallback)
        {
            if (TryGetArg(AddressArg, out string fromArgs) && !string.IsNullOrWhiteSpace(fromArgs))
            {
                Debug.Log($"[Net] 서버 주소를 실행 인자에서 읽었습니다: {fromArgs}");
                return fromArgs.Trim();
            }

            string saved = PlayerPrefs.GetString(AddressPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                Debug.Log($"[Net] 서버 주소를 저장값에서 읽었습니다: {saved}");
                return saved.Trim();
            }

            return fallback;
        }

        /// <summary>
        /// 포트 지정이 있으면 현재 트랜스포트에 적용한다. 서버·클라 양쪽에서 부른다(같은 포트를 써야 하므로).
        /// </summary>
        /// <remarks>
        /// <see cref="Transport.active"/>는 <c>NetworkManager.Awake</c>가 채우므로 그 뒤에 불러야 한다.
        /// 포트를 노출하지 않는 트랜스포트면 경고만 남기고 무시한다.
        /// </remarks>
        public static void ApplyPortOverride()
        {
            if (!TryResolvePort(out ushort port)) return;

            if (Transport.active is PortTransport portTransport)
            {
                portTransport.Port = port;
                Debug.Log($"[Net] 포트를 {port}로 지정했습니다.");
                return;
            }

            Debug.LogWarning($"[Net] 포트 지정({port})이 무시됐습니다 — 현재 트랜스포트가 포트를 노출하지 않습니다.");
        }

        /// <summary>
        /// 주소(와 포트)를 다음 실행에도 쓰도록 저장한다. 접속 설정 UI가 생기면 여기로 넘기면 된다.
        /// </summary>
        /// <param name="address">저장할 주소. 비우면 저장값을 지운다.</param>
        /// <param name="port">저장할 포트. 0이면 포트는 건드리지 않는다.</param>
        public static void Save(string address, ushort port = 0)
        {
            if (string.IsNullOrWhiteSpace(address)) PlayerPrefs.DeleteKey(AddressPrefsKey);
            else PlayerPrefs.SetString(AddressPrefsKey, address.Trim());

            if (port != 0) PlayerPrefs.SetInt(PortPrefsKey, port);

            PlayerPrefs.Save();
        }

        /// <summary>저장해 둔 주소·포트를 지운다(인스펙터 기본값으로 되돌리기).</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(AddressPrefsKey);
            PlayerPrefs.DeleteKey(PortPrefsKey);
            PlayerPrefs.Save();
        }

        private static bool TryResolvePort(out ushort port)
        {
            port = 0;

            if (TryGetArg(PortArg, out string fromArgs) && ushort.TryParse(fromArgs, out port)) return true;

            int saved = PlayerPrefs.GetInt(PortPrefsKey, 0);
            if (saved > 0 && saved <= ushort.MaxValue)
            {
                port = (ushort)saved;
                return true;
            }

            return false;
        }

        // "-server 1.2.3.4"와 "-server=1.2.3.4"를 모두 받는다. 둘 중 어느 쪽으로 적었는지로 막히면
        // 원인이 "접속이 안 된다"로만 보여서 시간을 크게 버린다.
        private static bool TryGetArg(string name, out string value)
        {
            value = null;

            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                return false;   // 일부 플랫폼은 인자를 주지 않는다
            }

            if (args == null) return false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg)) continue;

                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = arg.Substring(name.Length + 1);
                    return true;
                }

                if (!arg.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Length) return false;

                value = args[i + 1];
                return true;
            }

            return false;
        }
    }
}
