using System;
using System.Collections.Generic;

namespace ProjectS.UI
{
    /// <summary>
    /// 초대 목록이 보여줄 플레이어를 어디서 가져올지에 대한 계약. UI와 네트워크 사이의 유일한 접점이며
    /// (docs/PARTY_WINDOW_UI.md §10), 팝업은 이 인터페이스만 알고 미러를 모른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 덕분에 <b>네트워크가 붙기 전에도 UI를 끝까지 만들 수 있다.</b> 지금은
    /// <see cref="DummyPartyMemberSource"/>가 가짜 목록을 물려주고, 나중에 네트워크 담당자가
    /// 이 인터페이스를 구현한 컴포넌트로 갈아 끼우면 UI는 손대지 않는다.
    /// </para>
    /// <para>
    /// <b>주기적으로 물어보지 않는다.</b> 목록이 바뀌면 <see cref="OnChanged"/>로 알리는 쪽이
    /// 미러의 SyncList와 자연스럽게 맞물린다(폴링은 이 구조에서 오히려 부자연스럽다).
    /// </para>
    /// </remarks>
    public interface IPartyMemberSource
    {
        /// <summary>목록 내용이 바뀌었다(접속·이탈·파티 상태 변화). 팝업이 이 신호로 다시 그린다.</summary>
        event Action OnChanged;

        /// <summary>
        /// 첫 응답을 받았는지. <b>빈 목록과 로딩 중을 가르는 유일한 근거다.</b>
        /// </summary>
        /// <remarks>
        /// 이게 없으면 팝업을 열 때마다 "접속 중인 다른 플레이어가 없습니다"가 한 번 번쩍였다가
        /// 목록이 채워진다 — 응답을 기다리는 동안에도 목록은 비어 있기 때문이다.
        /// </remarks>
        bool IsReady { get; }

        /// <summary>
        /// 지금 접속해 있는 플레이어들. <b>자기 자신은 빼고</b> 준다.
        /// </summary>
        IReadOnlyList<PartyMemberInfo> GetOnlineMembers();

        /// <summary>
        /// 파티로 던전을 클리어한 적 있는 사람들(최근 순, 최대 10명). 지금 접속 중이 아닌 사람도 섞인다.
        /// </summary>
        IReadOnlyList<PartyMemberInfo> GetRecentMembers();

        /// <summary>
        /// 목록을 다시 받아 오라고 요청한다. 새로고침 버튼이 부른다.
        /// 갱신이 끝나면 <see cref="OnChanged"/>가 발행된다.
        /// </summary>
        void Refresh();
    }
}
