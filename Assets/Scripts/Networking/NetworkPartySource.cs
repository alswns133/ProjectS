using System;
using ProjectS.Events;
using ProjectS.UI;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 파티 상태(<see cref="PlayerPresence"/>·<see cref="PartyManager"/>)를 파티 슬롯이 아는 형태
    /// (<see cref="IPartySource"/>)로 옮기는 다리. <c>DummyPartySource</c> 자리에 이걸 끼우면 슬롯 코드는
    /// 한 줄도 바뀌지 않는다(docs 계약 §10). <see cref="NetworkPartyMemberSource"/>와 같은 성격의 씬 컴포넌트다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>읽기는 복제 상태에서, 쓰기는 로컬 PartyManager Command로.</b> Self·Partner·IsLeader는
    /// 프레즌스/매니저에서 읽고, RequestInvite/Kick/Leave는 <see cref="PartyManager.Local"/>로 넘긴다.
    /// UI는 미러를 모른 채 요청만 하고 결과를 기다린다(<see cref="IPartySource"/> 주석의 대기 모델).
    /// </para>
    /// <para>
    /// <b>파티원은 partyId가 같은 프레즌스로 찾는다.</b> 소속의 진실을 한 곳(PlayerPresence.PartyId)에만
    /// 두기 위함이다(2인 파티라 나 말고 같은 id를 가진 프레즌스가 곧 파티원).
    /// </para>
    /// </remarks>
    public class NetworkPartySource : MonoBehaviour, IPartySource
    {
        /// <inheritdoc/>
        public event Action OnChanged;

        // 프레즌스 변화(접속·partyId 등)와 파티 상태 변화(성립·해체·대기) 둘 다 슬롯을 다시 그려야 한다.
        // 둘을 모두 구독하고 OnChanged로 합류시킨다(다시 그리기는 멱등이라 중복 신호는 무해).
        private void OnEnable()
        {
            PlayerPresence.OnAnyChanged += Raise;
            PartyEvents.OnChanged += Raise;
        }

        private void OnDisable()
        {
            PlayerPresence.OnAnyChanged -= Raise;
            PartyEvents.OnChanged -= Raise;
        }

        private void Raise() => OnChanged?.Invoke();

        /// <inheritdoc/>
        public PartyMemberInfo Self
        {
            get
            {
                PlayerPresence me = PlayerPresence.Local;
                if (me == null) return null;

                return ToInfo(me);
            }
        }

        /// <inheritdoc/>
        public PartyMemberInfo Partner
        {
            get
            {
                PlayerPresence me = PlayerPresence.Local;
                if (me == null || !me.InParty) return null;

                // 나와 같은 파티 id를 가진, 내가 아닌 프레즌스가 파티원(2인 파티).
                foreach (PlayerPresence p in PlayerPresence.All)
                {
                    if (p == null || p.isLocalPlayer) continue;
                    if (p.PartyId == me.PartyId) return ToInfo(p);
                }

                return null;
            }
        }

        /// <inheritdoc/>
        public bool IsLeader => PartyManager.Local != null && PartyManager.Local.IsLeader;

        /// <inheritdoc/>
        public bool IsInviting => PartyManager.Local != null && PartyManager.Local.IsInviting;

        /// <inheritdoc/>
        public void RequestInvite(PartyMemberInfo target)
        {
            if (target == null || PartyManager.Local == null) return;

            // 로스터가 넣어 준 Id는 netId 문자열이다(NetworkPartyMemberSource 참조). 파싱해 서버로 넘긴다.
            if (uint.TryParse(target.Id, out uint targetNetId))
                PartyManager.Local.RequestInvite(targetNetId);
            else
                Debug.LogWarning($"[NetworkPartySource] target.Id를 netId로 파싱하지 못했다: '{target.Id}'", this);
        }

        /// <inheritdoc/>
        public void RequestKick() => PartyManager.Local?.RequestKick();

        /// <inheritdoc/>
        public void RequestLeave() => PartyManager.Local?.RequestLeave();

        // 프레즌스 한 줄을 슬롯이 그릴 PartyMemberInfo로 옮긴다. 슬롯에 그리는 사람은 접속 중이고,
        // 초대 가능 여부는 슬롯에서 의미가 없어 Invitable로 둔다(색은 접속 여부만으로 갈린다).
        private static PartyMemberInfo ToInfo(PlayerPresence p)
            => new PartyMemberInfo(p.netId.ToString(), p.DisplayName, p.Level, p.CharacterType,
                                   isOnline: true, PartyInviteState.Invitable);
    }
}
