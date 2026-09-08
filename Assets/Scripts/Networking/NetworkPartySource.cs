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
    /// <para>
    /// <b>초대 대기(Invited) 국면은 이 다리가 들고 있다.</b> 서버는 초대를
    /// <see cref="PartyEvents.OnInviteReceived"/>로 한 번 배달할 뿐 "지금 초대 대기 중"이라는 상태를
    /// 복제하지 않는다. 그래서 배달된 offer를 여기서 보관하고 20초를 세어 국면으로 바꿔 준다
    /// (2026-09-08 확정 — 받는 쪽 UI를 <c>PartyInviteAcceptPopup</c>으로 일원화).
    /// 응답은 <see cref="PartyManager.AnswerInvite"/>로 넘긴다.
    /// </para>
    /// <para>
    /// <b>★ 출발 카운트다운(Departing)은 아직 서버에 없다.</b> 30초 출발 관련 멤버는 대응하는 서버 로직이
    /// 없어 "모른다"를 정직하게 돌려주는 자리표시로 둔다(아래 <c>── 미구현 ──</c> 구역).
    /// 지어낸 값을 돌려주지 않는 이유는 <see cref="IPartySource"/> 주석의 대기 모델과 같다 —
    /// 서버 판정과 화면이 어긋나는 쪽이 아무것도 안 보이는 쪽보다 나쁘다.
    /// </para>
    /// </remarks>
    public class NetworkPartySource : MonoBehaviour, IPartySource
    {
        /// <inheritdoc/>
        public event Action OnChanged;

        [Tooltip("받은 초대에 답할 수 있는 시간(초). 서버 안전망(PartyManager.InviteTimeoutSeconds=25초)보다 " +
                 "짧게 둬야 화면이 먼저 닫히고 서버와 어긋나지 않는다.")]
        [SerializeField, Min(1f)] private float inviteTimeoutSeconds = 20f;

        // 배달돼 온 초대 한 건과 그 마감 시각(unscaled). hasInvite가 false면 초대 대기 국면이 아니다.
        // ★ 이 셋이 Invited 국면의 유일한 근거다 — 서버가 "대기 중"을 복제해 주지 않기 때문에
        //   (클래스 주석 참고) 배달 시점부터 여기서 직접 센다.
        private PartyInviteOffer pendingOffer;
        private bool hasInvite;
        private float inviteDeadline;

        // 프레즌스 변화(접속·partyId 등)와 파티 상태 변화(성립·해체·대기) 둘 다 슬롯을 다시 그려야 한다.
        // 둘을 모두 구독하고 OnChanged로 합류시킨다(다시 그리기는 멱등이라 중복 신호는 무해).
        private void OnEnable()
        {
            PlayerPresence.OnAnyChanged += Raise;
            PartyEvents.OnChanged += Raise;
            PartyEvents.OnInviteReceived += OnInviteReceived;
        }

        private void OnDisable()
        {
            PlayerPresence.OnAnyChanged -= Raise;
            PartyEvents.OnChanged -= Raise;
            PartyEvents.OnInviteReceived -= OnInviteReceived;
        }

        private void Raise() => OnChanged?.Invoke();

        // 초대가 배달됐다. 국면이 None → Invited로 바뀌므로 한 번 알린다.
        // 겹친 초대는 최신이 이긴다 — 서버가 1인 1건만 보류(PartyManager.pendingByTarget)하므로
        // 이전 것은 이미 무효다. 그것을 계속 들고 있으면 답할 수 없는 창이 뜬다.
        private void OnInviteReceived(PartyInviteOffer offer)
        {
            pendingOffer = offer;
            hasInvite = true;
            inviteDeadline = Time.unscaledTime + inviteTimeoutSeconds;

            Raise();
        }

        // 마감을 넘겼는지만 본다. 남은 시간 자체는 그리는 쪽이 RemainingSeconds로 읽어 가므로
        // 여기서 매 프레임 OnChanged를 쏘지 않는다(IPartySource 주석: 국면이 바뀔 때만 발행).
        private void Update()
        {
            if (!hasInvite || Time.unscaledTime < inviteDeadline) return;

            // 무응답 = 거절. 그냥 창만 닫으면 초대자는 서버 안전망(25초)까지 "초대 중…"에 갇힌다.
            AnswerAndClear(false);
        }

        // 서버로 답을 넘기고 대기 상태를 지운다. 수락/거절/시간초과가 모두 이 한 곳을 지나
        // 국면 종료와 통지가 갈라지지 않게 한다.
        private void AnswerAndClear(bool accept)
        {
            PartyInviteOffer offer = pendingOffer;

            hasInvite = false;
            pendingOffer = default;

            PartyManager.Local?.AnswerInvite(offer.inviterNetId, accept);

            // 성립 여부는 서버가 partyId 복제로 알려 준다. 여기서는 Invited 국면이 끝났다는 것만 알린다
            // (수락이 성사됐다고 지어내지 않는다 — IPartySource 주석의 대기 모델).
            Raise();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <b>파티 소속이 초대 대기를 이긴다.</b> 이미 파티가 있는데 초대가 남아 있으면(중복 배달·
        /// 지연 도착) 결성창을 밀어내고 수락 팝업이 뜨는 것이 더 나쁘다.
        /// <para>
        /// <b><see cref="PartyPhase.Departing"/>은 내지 않는다.</b> 30초 출발 카운트다운은 서버
        /// (<see cref="PartyManager"/>)에 로직이 아직 없어 판정할 근거가 없다.
        /// </para>
        /// </remarks>
        public PartyPhase Phase
        {
            get
            {
                PlayerPresence me = PlayerPresence.Local;
                if (me != null && me.InParty) return PartyPhase.Formed;

                return hasInvite ? PartyPhase.Invited : PartyPhase.None;
            }
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
        /// <remarks>
        /// 이름은 offer에 실려 오지만 레벨·직업은 안 실린다. 그 둘은 초대자 netId로 프레즌스를 찾아
        /// 채운다 — 서버가 이미 전원 복제하고 있어 초대 패킷을 넓힐 이유가 없다.
        /// 프레즌스를 못 찾으면(아직 스폰 전 등) 이름만으로 만든다. 레벨이 비는 것이
        /// 팝업이 아예 안 뜨는 것보다 낫다.
        /// </remarks>
        public PartyMemberInfo Inviter
        {
            get
            {
                if (!hasInvite) return null;

                foreach (PlayerPresence p in PlayerPresence.All)
                {
                    if (p != null && p.netId == pendingOffer.inviterNetId) return ToInfo(p);
                }

                return new PartyMemberInfo(pendingOffer.inviterNetId.ToString(), pendingOffer.inviterName,
                                           level: 0, characterType: 0,
                                           isOnline: true, PartyInviteState.Invitable);
            }
        }

        /// <inheritdoc/>
        public bool IsLeader => PartyManager.Local != null && PartyManager.Local.IsLeader;

        /// <inheritdoc/>
        public bool IsInviting => PartyManager.Local != null && PartyManager.Local.IsInviting;

        // ── 표시 전용 (서버 대기) ───────────────────────────────────
        // 던전·난이도는 "무엇에 동의하는가"를 보여주는 값이라 docs/PARTY_WINDOW_UI.md §7이 필수로 두지만,
        // 지금 초대 패킷(PartyInviteOffer)에 그 필드가 없다. 2026-09-08에 싣기로 정했고, 서버가
        // CmdInvite에서 실어 보내기 시작하면 여기서 pendingOffer로 읽는다.
        // 그때까지 빈 문자열인 이유: 표시하는 쪽이 그대로 text에 넣으므로, 임의의 문구를 지어내면
        // 실제 목적지와 다른 던전이 화면에 뜬다.

        /// <inheritdoc/>
        public string DungeonName => string.Empty;

        /// <inheritdoc/>
        public string DifficultyLabel => string.Empty;

        // ── 카운트다운 ──────────────────────────────────────────────
        // 초대 20초는 이 다리가 세지만(위 Update), 출발 30초는 서버에 로직 자체가 없어 0을 돌려준다.
        // 0을 받으면 CountdownView는 duration <= 0을 "진행 바를 채운 채로 둔다"로 처리하므로
        // 0 나누기 없이 안전하게 멈춘 상태가 된다.

        /// <inheritdoc/>
        public float RemainingSeconds
            => hasInvite ? Mathf.Max(0f, inviteDeadline - Time.unscaledTime) : 0f;

        /// <inheritdoc/>
        public float PhaseDuration => hasInvite ? inviteTimeoutSeconds : 0f;

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

        /// <inheritdoc/>
        /// <remarks>
        /// 수락해도 여기서 파티가 생겼다고 치지 않는다. 성립 판정은 서버가 하고
        /// (<see cref="PartyManager"/>의 재검증), 결과는 partyId 복제로 돌아온다.
        /// </remarks>
        public void AcceptInvite()
        {
            if (hasInvite) AnswerAndClear(true);
        }

        /// <inheritdoc/>
        public void DeclineInvite()
        {
            if (hasInvite) AnswerAndClear(false);
        }

        // ── 미구현 요청 ─────────────────────────────────────────────
        // 아래 셋은 서버(PartyManager)에 출발 카운트다운 Command가 없어 아직 넘길 곳이 없다.
        // ★ 조용한 no-op으로 두지 않고 경고를 남긴다 — 버튼을 눌렀는데 아무 일도 일어나지 않으면
        //   "서버가 응답을 안 준다"와 구분이 안 되고, 원인을 네트워크에서 찾게 된다.
        //   셋 다 PartyRosterPopup의 [던전 입장]·[확인] 버튼에서 실제로 도달한다.

        /// <inheritdoc/>
        /// <remarks>미구현. 서버에 출발 카운트다운(30초) 로직이 없다.</remarks>
        public void RequestDepart() => WarnNotWired(nameof(RequestDepart),
            "서버에 출발 카운트다운이 없다. PartyManager에 Command·복제 상태를 먼저 만든다.");

        /// <inheritdoc/>
        /// <remarks>미구현. <see cref="RequestDepart"/>와 같은 이유다.</remarks>
        public void CancelDepart() => WarnNotWired(nameof(CancelDepart),
            "서버에 출발 카운트다운이 없다. PartyManager에 Command·복제 상태를 먼저 만든다.");

        /// <inheritdoc/>
        /// <remarks>미구현. <see cref="RequestDepart"/>와 같은 이유다.</remarks>
        public void ConfirmDepart() => WarnNotWired(nameof(ConfirmDepart),
            "서버에 출발 카운트다운이 없다. PartyManager에 Command·복제 상태를 먼저 만든다.");

        // 미구현 요청이 불렸음을 한 줄로 알린다. 어느 컴포넌트가 불렀는지 보이도록 this를 넘긴다.
        private void WarnNotWired(string member, string why)
            => Debug.LogWarning($"[NetworkPartySource] {member}는 아직 서버에 연결되지 않았다 — {why}", this);

        // 프레즌스 한 줄을 슬롯이 그릴 PartyMemberInfo로 옮긴다. 슬롯에 그리는 사람은 접속 중이고,
        // 초대 가능 여부는 슬롯에서 의미가 없어 Invitable로 둔다(색은 접속 여부만으로 갈린다).
        private static PartyMemberInfo ToInfo(PlayerPresence p)
            => new PartyMemberInfo(p.netId.ToString(), p.DisplayName, p.Level, p.CharacterType,
                                   isOnline: true, PartyInviteState.Invitable);
    }
}
