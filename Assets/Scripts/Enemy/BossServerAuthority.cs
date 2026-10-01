using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectS.Enemies
{
    /// <summary>
    /// 레이드 보스의 <b>서버 권위</b> 게이트. 보스는 서버가 AI를 돌리고, 관찰자 클라는 트랜스폼/애니를
    /// 동기화로 "받기만" 한다. <see cref="OwnerGate"/>(플레이어 아바타 = 클라 권한)의 서버판이며,
    /// 판정 기준만 <c>isOwned</c> → <c>isServer</c>로 바뀐다.
    ///
    /// <para><b>왜 필요한가.</b> 서버가 스폰한 보스는 Mirror가 모든 클라에 복제한다. 게이트가 없으면
    /// 관찰자 클라에서도 보스 AI(상태머신·NavMeshAgent·공격 판정)가 로컬로 돌아, 서버 AI와 이중으로
    /// 굴러 위치·행동이 어긋난다(desync). 또 NavMeshAgent가 동기화로 들어오는 트랜스폼과 싸운다
    /// ("agent not on navmesh" 등). 관찰자에선 AI를 꺼 두고 <see cref="NetworkTransform"/>이 준 위치만
    /// 따라가게 한다.</para>
    ///
    /// <para><b>누가 끄고 누가 켜나.</b> 전용 서버는 <see cref="NetworkBehaviour.OnStartClient"/>가 아예
    /// 불리지 않아 AI가 그대로 돈다. 호스트(서버+클라)는 <c>isServer==true</c>라 끄지 않는다
    /// (서버로서 AI를 돌려야 하므로). 순수 관찰자 클라(<c>isServer==false</c>)에서만 끈다.</para>
    /// </summary>
    public class BossServerAuthority : NetworkBehaviour
    {
        // 서버에서만 도는 AI 컴포넌트. 관찰자 클라에선 전부 비활성.
        private readonly List<Behaviour> serverOnly = new();

        private void Awake()
        {
            Add<Enemy>();          // Update가 상태머신을 구동 → 끄면 AI 정지
            Add<EnemyMovement>();  // NavMeshAgent 기반 이동
            Add<EnemyCombat>();    // 공격 판정
            Add<NavMeshAgent>();   // 경로/속도(클라는 NetworkTransform이 준 위치만 따라감)

            // ★ 페이즈 전환은 반드시 서버만. 관찰자도 BossNetSync가 동기화한 HP로 CombatEvents.OnEnemyHealthChanged를
            //   받기 때문에, 이걸 안 끄면 관찰자가 로컬로 2페이즈를 스폰하고 1페이즈를 파괴해 화면이 갈라진다.
            Add<BossPhaseTransition>();
        }

        /// <summary>순수 관찰자 클라에서만 서버 전용 AI 컴포넌트를 끈다. 호스트는 서버로서 AI를 돌려야 하므로 건드리지 않는다.</summary>
        public override void OnStartClient()
        {
            base.OnStartClient();

            // ★ 호스트(서버+클라)는 isServer=true → 여기서 끄면 서버 AI까지 죽는다. 순수 관찰자만 끈다.
            //   (전용 서버는 OnStartClient 자체가 안 불려 AI가 그대로 유지된다.)
            if (isServer) return;

            foreach (Behaviour b in serverOnly)
                if (b) b.enabled = false;
        }

        /// <summary>
        /// 보스를 지정 위치·방향으로 즉시 옮긴다. 네트워크 스폰된 서버 인스턴스면 클라에도 "순간이동"으로 확정 전달한다.
        /// 연출 시작·종료 지점(startPoint/endPoint)처럼 <b>한 번 놓고 끝나는</b> 배치에 쓴다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>transform 대입만으로는 클라 방향이 어긋날 수 있다(2026-10-01 "페이즈 전환 연출에서 2페이즈만 보는 방향이 다름").</b>
        /// NetworkTransform(onlySyncOnChange)은 바뀐 값만 한 번 보내고, 클라는 빠진 값을 마지막 값으로 채운다.
        /// 그런데 연출 중 서버의 보스 회전은 다시 바뀌지 않는다(EnemyMovement.OnAnimatorMove가 회전 루트모션을 버림).
        /// 2페이즈는 "1페이즈가 보던 방향으로 스폰 → 같은 프레임에 시작 지점으로 회전 → 곧바로 활성화 트랙이 껐다 켬"을 겪는데,
        /// NetworkTransform은 껐다 켤 때 받아 둔 값을 비우므로(ResetState) 그 한 번의 회전이 적용 전에 사라지면 다시 오지 않는다.
        /// 위치는 루트모션으로 계속 바뀌어 계속 오니, 위치는 맞고 방향만 스폰 때 방향으로 굳는다.
        /// </para>
        /// <para>
        /// ServerTeleport는 신뢰 채널 RPC로 클라 transform에 직접 쓰고(오브젝트가 꺼져 있어도 적용) 받아 둔 값을 비운다.
        /// 이후 위치만 오는 동기화도 회전을 현재 transform(= 순간이동한 방향)으로 채우므로 방향이 유지된다.
        /// 싱글(netId 0)·순수 클라에서는 transform 대입만 한다.
        /// </para>
        /// </remarks>
        /// <param name="boss">옮길 보스(루트에 NetworkTransform이 있는 레이드 보스. 없으면 대입만 한다).</param>
        /// <param name="position">월드 위치.</param>
        /// <param name="rotation">월드 회전.</param>
        public static void PlaceAt(Component boss, Vector3 position, Quaternion rotation)
        {
            if (boss == null) return;

            boss.transform.SetPositionAndRotation(position, rotation);

            if (!NetworkServer.active) return;
            if (!boss.TryGetComponent(out NetworkIdentity identity) || identity.netId == 0) return;

            if (boss.TryGetComponent(out NetworkTransformBase sync)) sync.ServerTeleport(position, rotation);
        }

        // 자기(또는 자식)에 붙은 컴포넌트를 찾아 서버 전용 목록에 담는다. 없으면 조용히 건너뛴다.
        private void Add<T>() where T : Behaviour
        {
            T component = GetComponentInChildren<T>();
            if (component != null) serverOnly.Add(component);
        }
    }
}
