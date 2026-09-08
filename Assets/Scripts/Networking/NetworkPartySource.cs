using System;
using Mirror;
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

        // 방금 받은 초대. 받는 쪽은 서버가 복제하는 파티 상태가 아직 없어(성립 전이라 partyId=0),
        // 이 offer 하나가 "지금 초대를 받아 응답을 기다리는 중"의 유일한 근거다. 수락/거절할 때
        // 초대자 netId도 여기서 꺼내 PartyManager로 넘긴다. 성립·거절·이탈로 국면이 바뀌면 지운다.
        // 값 타입이 아니라 nullable로 둬 "초대 없음"과 명확히 구분한다.
        private PartyInviteOffer? incomingInvite;

        // 프레즌스 변화(접속·partyId 등)와 파티 상태 변화(성립·해체·대기) 둘 다 슬롯을 다시 그려야 한다.
        // 초대 수신도 결성창을 '초대 받음' 화면으로 바꿔야 해 함께 듣는다. 모두 OnChanged로 합류시킨다
        // (다시 그리기는 멱등이라 중복 신호는 무해).
        private void OnEnable()
        {
            Debug.Log($"[진단][NetworkPartySource] OnEnable #{GetInstanceID()} (GO='{name}') — 파티 상태 소스 활성. " +
                      $"소비자(AcceptPopup·RosterPopup·SlotBar·WindowOpener)가 이 #ID를 물어야 한다.", this);

            // 창들이 슬롯 배선 없이 받아가게 스스로 등록한다(PartySourceProvider). 소스는 하나여야 한다.
            PartySourceProvider.Set(this);

            PlayerPresence.OnAnyChanged += OnPresenceChanged;
            PartyEvents.OnChanged += Raise;
            PartyEvents.OnInviteReceived += OnInviteReceived;
        }

        private void OnDisable()
        {
            PartySourceProvider.Clear(this);

            PlayerPresence.OnAnyChanged -= OnPresenceChanged;
            PartyEvents.OnChanged -= Raise;
            PartyEvents.OnInviteReceived -= OnInviteReceived;
        }

        private void Raise() => OnChanged?.Invoke();

        // 초대가 오면 보관하고 결성창을 다시 그린다(Phase가 Invited로 바뀐다).
        private void OnInviteReceived(PartyInviteOffer offer)
        {
            Debug.Log($"[진단][NetworkPartySource] 초대 수신: '{offer.inviterName}'(netId {offer.inviterNetId}) → Phase=Invited", this);
            incomingInvite = offer;
            Raise();
        }

        // 프레즌스가 바뀔 때(=파티가 성립돼 내 partyId가 채워졌을 때 등) 대기 중이던 초대는 소멸시킨다.
        // 성립하면 Formed로 넘어가야 하는데 보관된 offer가 남아 있으면 Invited가 이겨 화면이 어긋난다.
        private void OnPresenceChanged()
        {
            if (incomingInvite.HasValue && PlayerPresence.Local != null && PlayerPresence.Local.InParty)
                incomingInvite = null;

            Raise();
        }

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
        /// <remarks>
        /// 우선순위가 중요하다. 출발 중이면 Departing, 파티가 있으면 Formed, 초대를 받아 응답 대기 중이면
        /// Invited, 아무것도 아니면 None. 출발(Departing)은 파티가 있는 상태의 위이고, 성립(Formed)은
        /// 초대(Invited)의 위다 — 뒤 국면이 남은 앞 국면에 가려지지 않게 한다.
        /// </remarks>
        public PartyPhase Phase
        {
            get
            {
                if (PartyManager.Local != null && PartyManager.Local.IsDeparting) return PartyPhase.Departing;

                PlayerPresence me = PlayerPresence.Local;
                if (me != null && me.InParty) return PartyPhase.Formed;
                if (incomingInvite.HasValue) return PartyPhase.Invited;

                return PartyPhase.None;
            }
        }

        /// <inheritdoc/>
        public PartyMemberInfo Inviter
        {
            get
            {
                if (!incomingInvite.HasValue) return null;

                PartyInviteOffer offer = incomingInvite.Value;

                // offer는 이름만 싣고 온다. 초대자 프레즌스가 클라에 복제돼 있으면 레벨·캐릭터까지 채워 그린다.
                foreach (PlayerPresence p in PlayerPresence.All)
                {
                    if (p != null && p.netId == offer.inviterNetId) return ToInfo(p);
                }

                // 프레즌스를 못 찾으면(멀어졌거나 아직 스폰 전) 이름만으로 최소 정보를 만든다.
                return new PartyMemberInfo(offer.inviterNetId.ToString(), offer.inviterName,
                                           level: 0, characterType: 0,
                                           isOnline: true, PartyInviteState.Invitable);
            }
        }

        // 던전/난이도 출처가 국면마다 다르다. 초대 대기(Invited)엔 아직 파티가 없어 offer에 실려 온 값을,
        // 파티가 맺어진 뒤(Formed/Departing)엔 서버가 프레즌스에 심은 파티 던전을 읽는다.

        /// <inheritdoc/>
        public string DungeonName
        {
            get
            {
                if (Phase == PartyPhase.Invited && incomingInvite.HasValue)
                    return incomingInvite.Value.dungeonName ?? string.Empty;

                return PlayerPresence.Local != null ? PlayerPresence.Local.PartyDungeonName : string.Empty;
            }
        }

        /// <inheritdoc/>
        public string DifficultyLabel
        {
            get
            {
                if (Phase == PartyPhase.Invited && incomingInvite.HasValue)
                    return incomingInvite.Value.difficultyLabel ?? string.Empty;

                return PlayerPresence.Local != null ? PlayerPresence.Local.PartyDifficultyLabel : string.Empty;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 남은 시간은 서버가 복제한 종료 "시각"(<see cref="PartyManager.DepartEndTime"/>)에서 지금
        /// 서버 시각(<see cref="NetworkTime.time"/>)을 빼 매 프레임 로컬 계산한다 — 값을 네트워크로
        /// 흘리지 않기 위함이다(IPartySource 주석). 두 파티원이 같은 서버 시계를 봐 카운트다운이 안 어긋난다.
        /// </remarks>
        public float RemainingSeconds
        {
            get
            {
                // 초대 대기: 초대 만료 시각(offer.expireTime)까지 남은 시간을 서버시각 기준으로 계산.
                if (Phase == PartyPhase.Invited && incomingInvite.HasValue)
                    return Mathf.Max(0f, (float)(incomingInvite.Value.expireTime - NetworkTime.time));

                // 출발: 출발 종료 시각까지.
                PartyManager local = PartyManager.Local;
                if (local != null && local.IsDeparting)
                    return Mathf.Max(0f, (float)(local.DepartEndTime - NetworkTime.time));

                return 0f;
            }
        }

        /// <inheritdoc/>
        public float PhaseDuration => Phase switch
        {
            PartyPhase.Invited   => PartyManager.InviteTimeoutSeconds,
            PartyPhase.Departing => PartyManager.DepartCountdownSeconds,
            _                    => 0f,
        };

        /// <inheritdoc/>
        public void RequestInvite(PartyMemberInfo target, int dungeonId, string dungeonName, string difficultyLabel)
        {
            if (target == null || PartyManager.Local == null) return;

            // 로스터가 넣어 준 Id는 netId 문자열이다(NetworkPartyMemberSource 참조). 파싱해 던전과 함께 서버로 넘긴다.
            if (uint.TryParse(target.Id, out uint targetNetId))
                PartyManager.Local.RequestInvite(targetNetId, dungeonId, dungeonName, difficultyLabel);
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

        /// <inheritdoc/>
        public void AcceptInvite() => AnswerIncoming(accept: true);

        /// <inheritdoc/>
        public void DeclineInvite() => AnswerIncoming(accept: false);

        // 받은 초대에 답한다. 초대자 netId는 보관한 offer에서 꺼내 서버로 넘기고, 답한 초대는 지운다
        // (성립은 partyId 복제가, 거절은 여기 Raise가 화면을 원복한다).
        private void AnswerIncoming(bool accept)
        {
            if (!incomingInvite.HasValue || PartyManager.Local == null) return;

            PartyManager.Local.AnswerInvite(incomingInvite.Value.inviterNetId, accept);
            incomingInvite = null;
            Raise();
        }

        // 출발 3종은 로컬 PartyManager로 요청만 넘긴다(검증·타이머·복제는 서버). 초대·나가기와 같은 대기 모델.
        /// <inheritdoc/>
        public void RequestDepart() => PartyManager.Local?.RequestDepart();

        /// <inheritdoc/>
        public void CancelDepart() => PartyManager.Local?.CancelDepart();

        /// <inheritdoc/>
        public void ConfirmDepart() => PartyManager.Local?.ConfirmDepart();
    }
}
