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
    /// <b>★ 미완성 — 국면(Phase)과 출발 카운트다운은 아직 서버에 없다.</b> 이 다리는 2026-09-07에
    /// <see cref="IPartySource"/>가 결성창·수락 팝업까지 먹이도록 넓어지기 전(8멤버 시절)에 작성됐다.
    /// 늘어난 11개 중 <b>Invited·Departing 국면에 해당하는 것들은 <see cref="PartyManager"/>에 대응하는
    /// 서버 로직 자체가 없어</b>, 여기서는 "모른다"를 정직하게 돌려주는 자리표시로 채워 뒀다
    /// (아래 <c>── 미구현 ──</c> 구역). 서버에 국면·타이머가 생기면 그때 채운다.
    /// 지어낸 값을 돌려주지 않는 이유는 <see cref="IPartySource"/> 주석의 대기 모델과 같다 —
    /// 서버 판정과 화면이 어긋나는 쪽이 아무것도 안 보이는 쪽보다 나쁘다.
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
        /// <remarks>
        /// 서버가 복제하는 파티 상태는 <see cref="PlayerPresence.PartyId"/>와
        /// <see cref="PartyManager.IsLeader"/> 둘뿐이라, 여기서 낼 수 있는 국면은
        /// <see cref="PartyPhase.None"/>과 <see cref="PartyPhase.Formed"/>까지다.
        /// <para>
        /// <b><see cref="PartyPhase.Invited"/>를 내지 않는다.</b> 받은 초대는 이 다리를 거치지 않고
        /// <see cref="PartyEvents.OnInviteReceived"/> → <see cref="PartyInvitePrompter"/> →
        /// <c>PartyInviteRequestPopup</c>으로 따로 흐른다. 여기서 Invited를 흉내 내면 그 팝업과
        /// <c>PartyInviteAcceptPopup</c>이 <b>같은 초대에 동시에 뜬다</b>.
        /// </para>
        /// <para>
        /// <b><see cref="PartyPhase.Departing"/>도 내지 않는다.</b> 30초 출발 카운트다운은 서버
        /// (<see cref="PartyManager"/>)에 로직이 아직 없어 판정할 근거가 없다.
        /// </para>
        /// </remarks>
        public PartyPhase Phase
        {
            get
            {
                PlayerPresence me = PlayerPresence.Local;
                return me != null && me.InParty ? PartyPhase.Formed : PartyPhase.None;
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
        /// 항상 null이다. <see cref="Phase"/>가 <see cref="PartyPhase.Invited"/>를 내지 않으므로
        /// "나를 부른 사람"을 물을 국면 자체가 없다(위 <see cref="Phase"/> 주석 참고).
        /// 초대자 이름이 필요한 쪽은 <see cref="PartyInviteOffer.inviterName"/>을 쓴다.
        /// </remarks>
        public PartyMemberInfo Inviter => null;

        /// <inheritdoc/>
        public bool IsLeader => PartyManager.Local != null && PartyManager.Local.IsLeader;

        /// <inheritdoc/>
        public bool IsInviting => PartyManager.Local != null && PartyManager.Local.IsInviting;

        // ── 표시 전용 (미구현) ──────────────────────────────────────
        // 던전·난이도는 "무엇에 동의하는가"를 보여주는 값인데, 지금 초대 패킷(PartyInviteOffer)에
        // 그 필드가 아예 없다(PartyEvents.cs의 TODO(선택)). 서버가 실어 보내기 시작하면 여기서 읽는다.
        // 빈 문자열을 돌려주는 이유: 표시하는 쪽(PartyRosterPopup·PartyInviteAcceptPopup)이 그대로
        // text에 넣으므로, 임의의 문구를 지어내면 실제 목적지와 다른 던전이 화면에 뜬다.

        /// <inheritdoc/>
        public string DungeonName => string.Empty;

        /// <inheritdoc/>
        public string DifficultyLabel => string.Empty;

        // ── 카운트다운 (미구현) ─────────────────────────────────────
        // 초대 20초·출발 30초 모두 서버에 타이머가 없다(PartyManager의 InviteTimeoutSeconds는
        // 서버 안전망이라 클라로 복제되지 않는다). 0을 돌려주면 CountdownView는 duration <= 0을
        // "진행 바를 채운 채로 둔다"로 처리하므로 0 나누기 없이 안전하게 멈춘 상태가 된다.

        /// <inheritdoc/>
        public float RemainingSeconds => 0f;

        /// <inheritdoc/>
        public float PhaseDuration => 0f;

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

        // ── 미구현 요청 ─────────────────────────────────────────────
        // 아래 다섯은 서버(PartyManager)에 대응 Command가 없어 아직 넘길 곳이 없다.
        // ★ 조용한 no-op으로 두지 않고 경고를 남긴다 — 버튼을 눌렀는데 아무 일도 일어나지 않으면
        //   "서버가 응답을 안 준다"와 구분이 안 되고, 원인을 네트워크에서 찾게 된다.
        //   RequestDepart/CancelDepart/ConfirmDepart는 PartyRosterPopup의 [던전 입장]·[확인]
        //   버튼에서 실제로 도달하므로 특히 그렇다(수락/거절 둘은 현재 도달 경로가 없다).

        /// <inheritdoc/>
        /// <remarks>
        /// 미구현. 받은 초대의 응답은 이 다리가 아니라 <see cref="PartyInvitePrompter"/>가
        /// <see cref="PartyManager.AnswerInvite"/>로 넘긴다 — 서버 쪽 경로는 이미 살아 있다.
        /// </remarks>
        public void AcceptInvite() => WarnNotWired(nameof(AcceptInvite),
            "받은 초대 응답은 PartyInvitePrompter → PartyManager.AnswerInvite 경로가 담당한다.");

        /// <inheritdoc/>
        /// <remarks>미구현. <see cref="AcceptInvite"/>와 같은 이유다.</remarks>
        public void DeclineInvite() => WarnNotWired(nameof(DeclineInvite),
            "받은 초대 응답은 PartyInvitePrompter → PartyManager.AnswerInvite 경로가 담당한다.");

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
