using System;
using Mirror;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 커넥션이 소유한 네트워크 오브젝트(playerPrefab = NetworkPlayer.prefab)에 붙어, <b>서버가 도출한 권위 전투 스탯</b>
    /// (<see cref="CombatStatBlock"/>)을 실어 나른다. 접속 인증에서 서버가 세이브로 도출한 값
    /// (<see cref="FirebaseServerAuthenticator.PendingConnectionAuth.stats"/>)을 여기 SyncVar에 담아:
    /// <list type="bullet">
    ///   <item>소유 클라로 복제한다(HUD·데미지 예측이 서버 권위값을 읽게).</item>
    ///   <item>서버 게임플레이(보스 데미지 계산)가 커넥션으로 조회한다(<see cref="TryGetForConnection"/>).</item>
    /// </list>
    ///
    /// <para><b>왜 이 오브젝트인가.</b> 스탯은 "이 플레이어(커넥션)"의 것이지 특정 아바타의 것이 아니다. 마을·던전을
    /// 넘나들며 아바타는 새로 스폰돼도 커넥션 소유 오브젝트는 유지되므로, 커넥션 단위 사실인 <see cref="PlayerPresence"/>와
    /// 같은 자리에 둔다. 서버 데미지 계산은 공격자 아바타의 <c>connectionToClient.identity</c>(=이 오브젝트)에서 스탯을 찾는다.</para>
    ///
    /// <para><b>권위 여부.</b> 로그인 세이브를 서버가 읽었으면 <see cref="HasAuthoritativeStats"/>=true. 개발/오프라인
    /// (토큰 없음 → save=null)이면 false이고 <see cref="Stats"/>는 0 — 이때 서버 데미지 계산은 클라 제공값으로 폴백한다(S5).</para>
    /// </summary>
    public class NetworkCombatStats : NetworkBehaviour
    {
        /// <summary>소유 클라(로컬 플레이어) 인스턴스. HUD·예측이 자기 권위 스탯을 읽을 때 쓴다. 접속 전 null.</summary>
        public static NetworkCombatStats Local { get; private set; }

        /// <summary>소유 클라의 권위 스탯이 도착/변경됐을 때 발행(HUD·예측 바인딩용). Local이 유효할 때만 의미.</summary>
        public static event Action OnLocalStatsChanged;

        // 서버가 도출해 담는 권위 전투 스탯. 훅으로 소유 클라에 변경을 알린다.
        [SyncVar(hook = nameof(OnStatsChanged))] private CombatStatBlock stats;

        // 서버가 로그인 세이브를 실제로 읽어 도출했는지. false면 stats는 0(폴백 대상).
        [SyncVar] private bool hasAuthoritativeStats;

        /// <summary>서버가 도출한 권위 전투 스탯(서버·클라 모두 읽기).</summary>
        public CombatStatBlock Stats => stats;

        /// <summary>서버가 로그인 세이브로 실제 도출했는지(=stats를 신뢰해도 되는지).</summary>
        public bool HasAuthoritativeStats => hasAuthoritativeStats;

        public override void OnStartServer()
        {
            // 인증 단계에서 이 커넥션에 대해 도출·보관해 둔 스탯을 SyncVar로 옮겨 담는다(→ 소유 클라로 복제).
            if (FirebaseServerAuthenticator.TryGet(connectionToClient, out FirebaseServerAuthenticator.PendingConnectionAuth auth))
            {
                stats = auth.stats;
                hasAuthoritativeStats = auth.save != null;
            }
            else
            {
                // 인증 정보가 없다(예외적). 폴백 상태로 둔다.
                hasAuthoritativeStats = false;
                Debug.LogWarning($"[CombatStats] conn={connectionToClient?.connectionId} 인증 정보 없음 → 권위 스탯 없음(폴백).");
            }
        }

        public override void OnStartLocalPlayer()
        {
            Local = this;
            OnLocalStatsChanged?.Invoke();   // 스폰 시 이미 도착해 있을 초기값을 밀어 올린다.
        }

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        // 서버가 stats를 갱신하면 소유 클라에서 훅이 돈다. 재도출(장비 변경 등)로 값이 바뀌면 HUD가 따라오게 한다.
        private void OnStatsChanged(CombatStatBlock _, CombatStatBlock __)
        {
            if (isLocalPlayer) OnLocalStatsChanged?.Invoke();
        }

        /// <summary>
        /// 서버 게임플레이용: 이 커넥션의 권위 전투 스탯을 조회한다. 공격자 아바타의 커넥션으로 찾아
        /// 보스 데미지를 서버가 계산할 때 쓴다(S5). 커넥션의 identity(=이 오브젝트가 붙은 playerPrefab)에서 읽는다.
        /// </summary>
        /// <param name="conn">조회할 커넥션(공격자).</param>
        /// <param name="block">도출된 전투 스탯(없으면 default).</param>
        /// <param name="authoritative">서버가 세이브로 실제 도출한 값인지(false면 폴백해야 함).</param>
        /// <returns>컴포넌트를 찾았으면 true.</returns>
        public static bool TryGetForConnection(NetworkConnectionToClient conn, out CombatStatBlock block, out bool authoritative)
        {
            block = default;
            authoritative = false;

            if (conn != null && conn.identity != null && conn.identity.TryGetComponent(out NetworkCombatStats cs))
            {
                block = cs.stats;
                authoritative = cs.hasAuthoritativeStats;
                return true;
            }
            return false;
        }
    }
}
