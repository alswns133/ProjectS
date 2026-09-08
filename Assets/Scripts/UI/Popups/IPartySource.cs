using System;

namespace ProjectS.UI
{
    /// <summary>
    /// 파티가 지금 어느 국면에 있는지. 결성창은 이 값 하나로 어떤 화면을 보일지 정한다
    /// (docs/PARTY_WINDOW_UI.md §7·§8).
    /// </summary>
    public enum PartyPhase
    {
        /// <summary>파티 없음. 결성창은 열리되 모든 기능이 비활성이다.</summary>
        None = 0,

        /// <summary>초대를 받아 응답을 기다리는 중(20초). 수락/거절을 고른다.</summary>
        Invited = 1,

        /// <summary>파티가 맺어졌고 아직 출발 전.</summary>
        Formed = 2,

        /// <summary>파티장이 출발을 걸어 30초 카운트다운이 도는 중.</summary>
        Departing = 3,
    }

    /// <summary>
    /// 지금 내 파티가 어떤 상태인지, 그리고 파티에 무엇을 요청할지에 대한 계약.
    /// 파티 슬롯·결성창이 이것만 알고 미러를 모른다(docs/PARTY_WINDOW_UI.md §10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>요청 메서드가 즉시 성공을 뜻하지 않는다.</b> <see cref="RequestInvite"/>를 부른다고 파티가
    /// 생기는 게 아니라, 서버가 검증하고 상대가 수락해야 <see cref="OnChanged"/>가 온다.
    /// UI는 요청을 보낸 뒤 대기 상태로 잠그고 결과를 기다린다 — 여기서 결과를 지어내면
    /// 서버 판정과 화면이 어긋난다.
    /// </para>
    /// <para>
    /// <b>남은 시간은 이벤트로 알리지 않는다.</b> 매 프레임 <see cref="OnChanged"/>를 쏘면 목록·슬롯이
    /// 통째로 다시 그려진다. 카운트다운을 그리는 쪽이 <see cref="RemainingSeconds"/>를 자기 Update에서
    /// 읽어 가고, <see cref="OnChanged"/>는 <b>국면이 바뀔 때만</b> 발행한다.
    /// </para>
    /// <para>
    /// 파티 정원이 2명이라 상대는 <see cref="Partner"/> 하나로 충분하다. 정원이 늘면 목록으로 바꾼다.
    /// </para>
    /// <para>
    /// <b>이 구현체는 던전 입장 창 안에 두면 안 된다.</b> 창이 닫히면 오브젝트가 꺼져 타이머가 끊기고
    /// 파티 상태가 사라진다. 마을·던전을 넘어 살아 있는 오브젝트(UIManager 아래 등)에 두고,
    /// 슬롯 바와 결성창이 같은 것을 참조하게 한다.
    /// </para>
    /// </remarks>
    public interface IPartySource
    {
        /// <summary>파티 구성이나 국면이 바뀌었다. 슬롯·결성창이 이 신호로 다시 그린다.</summary>
        event Action OnChanged;

        /// <summary>지금 어느 국면인지.</summary>
        PartyPhase Phase { get; }

        /// <summary>나. 파티가 없어도 항상 있다(슬롯 왼쪽 칸에 그린다).</summary>
        PartyMemberInfo Self { get; }

        /// <summary>파티원. 파티가 없으면 null.</summary>
        PartyMemberInfo Partner { get; }

        /// <summary><see cref="PartyPhase.Invited"/>일 때 나를 부른 사람. 그 외에는 null.</summary>
        PartyMemberInfo Inviter { get; }

        /// <summary>내가 파티장인지. 파티가 없으면 의미 없다.</summary>
        bool IsLeader { get; }

        /// <summary>초대를 보내고 응답을 기다리는 중인지. 보낸 쪽 슬롯을 잠그는 데 쓴다.</summary>
        /// <remarks><see cref="PartyPhase.Invited"/>는 <b>받은 쪽</b>이라 서로 다른 상태다.</remarks>
        bool IsInviting { get; }

        /// <summary>가려는 던전 이름. 표시 전용이며 초대 시점에 확정된다.</summary>
        string DungeonName { get; }

        /// <summary>가려는 난이도 이름. 표시 전용.</summary>
        string DifficultyLabel { get; }

        /// <summary>지금 국면의 남은 시간(초). 타이머가 없는 국면이면 0.</summary>
        float RemainingSeconds { get; }

        /// <summary>지금 국면의 전체 시간(초). 진행 바 비율을 내는 데 쓴다(초대 20 · 출발 30).</summary>
        float PhaseDuration { get; }

        /// <summary>상대에게 초대를 보내 달라고 요청한다. 실제 발송·검증은 서버가 한다.</summary>
        /// <param name="target">초대할 상대</param>
        /// <param name="dungeonId">향하는 던전 ID(2자리, 실제 입장용). 미지정이면 0</param>
        /// <param name="dungeonName">표시용 던전 이름</param>
        /// <param name="difficultyLabel">표시용 난이도 라벨</param>
        void RequestInvite(PartyMemberInfo target, int dungeonId, string dungeonName, string difficultyLabel);

        /// <summary>받은 초대를 수락한다.</summary>
        void AcceptInvite();

        /// <summary>받은 초대를 거절한다.</summary>
        void DeclineInvite();

        /// <summary>파티원을 내보내 달라고 요청한다. 파티장만 의미가 있다.</summary>
        void RequestKick();

        /// <summary>파티에서 나가겠다고 요청한다.</summary>
        void RequestLeave();

        /// <summary>출발을 걸어 30초 카운트다운을 시작한다. 파티장만 의미가 있다.</summary>
        void RequestDepart();

        /// <summary>도는 카운트다운을 멈춘다. 파티장만 의미가 있으며 <b>파티는 유지된다</b>.</summary>
        void CancelDepart();

        /// <summary>
        /// 파티원이 출발을 확인해 즉시 입장한다. 카운트다운이 남아 있어도 바로 들어간다.
        /// </summary>
        void ConfirmDepart();
    }
}
