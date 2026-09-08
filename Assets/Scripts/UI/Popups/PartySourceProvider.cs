using System;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 지금 살아 있는 <see cref="IPartySource"/> 하나를 가리키는 UI 계층의 중개소.
    /// 파티 창들이 서로 다른 오브젝트에 흩어져 있어도, 인스펙터로 소스를 일일이 끌어다 꽂지 않고
    /// 여기서 받아가게 한다(슬롯을 비워 두면 자동 연결).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가.</b> 실 파티 소스(<c>NetworkPartySource</c>)는 항상 켜진 오브젝트 하나에만 있어야 하는데,
    /// 그것을 쓰는 창(초대 수락·결성창·슬롯 바·창 오프너)은 UIManager 아래 제각각 흩어져 있다. 창마다
    /// <c>partySourceBehaviour</c> 슬롯에 그 하나를 크로스 오브젝트로 끌어다 꽂는 것은 번거롭고 자주 어긋난다.
    /// 소스가 스스로 <see cref="Set"/>로 등록하고 창들이 <see cref="Current"/>를 읽으면 배선이 사라진다
    /// (<see cref="ProjectS.Networking.PlayerPresence"/>.Local·PartyManager.Local과 같은 방식).
    /// </para>
    /// <para>
    /// <b>계층 경계는 지킨다.</b> 이 중개소는 UI 계층에 있고 <see cref="IPartySource"/>(UI 타입)만 다룬다.
    /// 네트워크 소스가 여기에 자기를 등록할 뿐이라(Networking → UI 방향), UI는 여전히 미러를 모른다.
    /// </para>
    /// <para>
    /// <b>소스는 하나여야 한다.</b> 두 개가 등록되면 나중 것이 이겨 상태가 갈린다. 그래서 서로 다른 인스턴스가
    /// 덮어쓰면 경고를 낸다(팝업 안에 남은 중복 소스를 잡기 위함).
    /// </para>
    /// </remarks>
    public static class PartySourceProvider
    {
        /// <summary>지금 등록된 파티 소스. 없으면 null. 창들은 슬롯이 비었을 때 이걸 폴백으로 쓴다.</summary>
        public static IPartySource Current { get; private set; }

        /// <summary>
        /// 등록된 소스가 바뀌었다(등록·해제). <b>Awake에서 소스를 못 잡는 쪽</b>(예: PartyWindowOpener는
        /// 씬 시작 Awake에 도는데 소스는 그 뒤 OnEnable에 등록된다)이 이 신호로 뒤늦게 붙을 수 있게 한다.
        /// </summary>
        public static event Action Changed;

        /// <summary>파티 소스를 등록한다(소스가 활성화될 때 스스로 호출).</summary>
        public static void Set(IPartySource source)
        {
            if (source == null || ReferenceEquals(Current, source)) return;

            if (Current != null)
                Debug.LogWarning("[PartySourceProvider] 이미 다른 파티 소스가 등록돼 있는데 새 소스가 덮어쓴다 — 소스는 하나여야 한다(팝업 안에 남은 중복을 확인).");

            Current = source;
            Changed?.Invoke();
        }

        /// <summary>등록을 해제한다(소스가 비활성화될 때 호출). 지금 등록된 그 소스일 때만 지운다.</summary>
        public static void Clear(IPartySource source)
        {
            if (!ReferenceEquals(Current, source)) return;

            Current = null;
            Changed?.Invoke();
        }

        // 플레이 모드 리로드(도메인 리로드 off) 후 죽은 참조/구독이 남지 않게 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            Changed = null;
        }
    }
}
