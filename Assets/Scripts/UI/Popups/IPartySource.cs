using System;

namespace ProjectS.UI
{
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
    /// 파티 정원이 2명이라 상대는 <see cref="Partner"/> 하나로 충분하다. 정원이 늘면 목록으로 바꾼다.
    /// </para>
    /// </remarks>
    public interface IPartySource
    {
        /// <summary>파티 구성이나 초대 진행 상태가 바뀌었다. 슬롯·결성창이 이 신호로 다시 그린다.</summary>
        event Action OnChanged;

        /// <summary>나. 파티가 없어도 항상 있다(슬롯 왼쪽 칸에 그린다).</summary>
        PartyMemberInfo Self { get; }

        /// <summary>파티원. 파티가 없으면 null.</summary>
        PartyMemberInfo Partner { get; }

        /// <summary>내가 파티장인지. 파티가 없으면 의미 없다.</summary>
        bool IsLeader { get; }

        /// <summary>초대를 보내고 응답을 기다리는 중인지. 슬롯을 대기 표시로 잠그는 데 쓴다.</summary>
        bool IsInviting { get; }

        /// <summary>상대에게 초대를 보내 달라고 요청한다. 실제 발송·검증은 서버가 한다.</summary>
        /// <param name="target">초대할 상대</param>
        void RequestInvite(PartyMemberInfo target);

        /// <summary>파티원을 내보내 달라고 요청한다. 파티장만 의미가 있다.</summary>
        void RequestKick();

        /// <summary>파티에서 나가겠다고 요청한다.</summary>
        void RequestLeave();
    }
}
