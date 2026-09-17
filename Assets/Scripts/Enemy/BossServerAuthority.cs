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

        public override void OnStartClient()
        {
            base.OnStartClient();

            // ★ 호스트(서버+클라)는 isServer=true → 여기서 끄면 서버 AI까지 죽는다. 순수 관찰자만 끈다.
            //   (전용 서버는 OnStartClient 자체가 안 불려 AI가 그대로 유지된다.)
            if (isServer) return;

            foreach (Behaviour b in serverOnly)
                if (b) b.enabled = false;
        }

        // 자기(또는 자식)에 붙은 컴포넌트를 찾아 서버 전용 목록에 담는다. 없으면 조용히 건너뛴다.
        private void Add<T>() where T : Behaviour
        {
            T component = GetComponentInChildren<T>();
            if (component != null) serverOnly.Add(component);
        }
    }
}
