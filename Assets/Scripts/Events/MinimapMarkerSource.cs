using UnityEngine;
using ProjectS.Core;
using ProjectS.Enemies;

namespace ProjectS.Events
{
    /// <summary>
    /// 이 오브젝트를 미니맵에 표시하겠다고 등록하는 브릿지 컴포넌트.
    /// 미니맵에 띄우고 싶은 것(플레이어·몬스터·NPC)에 붙이고 type만 고르면 된다.
    /// <para>
    /// Enemy/Player 같은 게임플레이 클래스에 등록 코드를 넣지 않고 이 컴포넌트로 분리한 이유:
    /// 전투·이동 로직이 미니맵(UI)의 존재를 몰라야 경계가 유지되기 때문이다.
    /// 붙이고 떼는 것만으로 미니맵 표시 여부가 정해지고, 클래스는 아무것도 알 필요가 없다.
    /// </para>
    /// <para>
    /// OnEnable/OnDisable에 짝지어 등록/해제하므로, 풀링으로 켜졌다 꺼지는 몬스터도
    /// 활성 상태와 미니맵 마커가 자동으로 일치한다.
    /// </para>
    /// <para>
    /// <b>사망 즉시 해제.</b> 사망한 몬스터는 사망 연출 동안 오브젝트가 활성 상태로 남아(OnDisable이 아직 안 옴)
    /// 마커가 시체 위에 계속 떠 있었다. 대상에 <see cref="IDamageable"/>이 있으면 <c>IsDead</c>를 보고 죽는 즉시
    /// 마커를 해제하고, 되살아나면(플레이어 부활) 다시 등록한다. IDamageable이 없으면(NPC 등) 기존처럼
    /// 활성 상태로만 판단한다 — 게임플레이 클래스는 여전히 미니맵을 몰라도 된다.
    /// </para>
    /// </summary>
    public class MinimapMarkerSource : MonoBehaviour
    {
        // 이 대상이 미니맵에서 어떤 마커로 보일지. 마커 아이콘 자체는 MinimapView가 type별로 들고 있다.
        [SerializeField] private MinimapMarkerType type = MinimapMarkerType.Enemy;

        // 사망 감지용(없으면 null → 사망 게이트 없이 활성 상태로만 판단). 같은 오브젝트에서 캐싱.
        private IDamageable damageable;
        private bool registered;

        private void Awake()
        {
            damageable = GetComponent<IDamageable>();

            // 보스는 일반 적과 다른 마커로 구분한다. Boss 컴포넌트(Boss:Enemy)가 붙어 있으면 인스펙터 type과
            // 무관하게 Boss 마커로 승격한다 — 보스 프리팹마다 type을 손으로 바꾸지 않아도 되게 하기 위함.
            // 판정을 동기적으로 확실한 '컴포넌트 유무'로 하는 이유: EnemyStats.IsBoss는 MonsterStatTable을
            // async로 읽은 뒤에야 채워져, 마커를 등록하는 OnEnable 시점에는 아직 못 믿기 때문이다.
            if (GetComponent<Boss>() != null)
                type = MinimapMarkerType.Boss;
        }

        private void OnEnable()
        {
            MinimapEvents.Register(transform, type);
            registered = true;
        }

        private void OnDisable()
        {
            MinimapEvents.Unregister(transform);
            registered = false;
        }

        // 사망/부활에 맞춰 마커를 즉시 해제/재등록한다(OnDisable을 기다리지 않는다).
        private void Update()
        {
            if (damageable == null) return;

            bool dead = damageable.IsDead;

            if (dead && registered)
            {
                MinimapEvents.Unregister(transform);
                registered = false;
            }
            else if (!dead && !registered)
            {
                MinimapEvents.Register(transform, type);
                registered = true;
            }
        }
    }
}
