namespace ProjectS.UI
{
    /// <summary>
    /// 초대 목록에서 이 플레이어를 지금 부를 수 있는지. 카드 ③ 구역 표기와 선택 가능 여부를 함께 결정한다.
    /// (docs/PARTY_WINDOW_UI.md §5)
    /// </summary>
    /// <remarks>
    /// <b>"비접속"이 여기 없는 것은 의도다.</b> 접속 여부는 <see cref="PartyMemberInfo.IsOnline"/> 하나만
    /// 진실로 두고, ③에 찍을 문구는 뷰가 그 둘을 합쳐 만든다. 여기에 Offline을 또 두면
    /// "IsOnline=true인데 상태는 Offline" 같은 모순 조합이 만들어질 수 있다.
    /// </remarks>
    public enum PartyInviteState
    {
        /// <summary>초대할 수 있다. 카드가 파란색이고 선택 가능한 유일한 상태.</summary>
        Invitable = 0,

        /// <summary>이미 다른 파티에 속해 있다. 정원이 2인이라 파티 소속 = 항상 만석이다.</summary>
        InParty = 1,

        /// <summary>본인이 파티 초대를 거부로 설정해 두었다.</summary>
        NotAccepting = 2,
    }

    /// <summary>
    /// 초대 목록·파티 슬롯에 한 줄로 그려지는 플레이어 정보. 네트워크 담당자와의 접점이며
    /// (docs/PARTY_WINDOW_UI.md §10), UI는 이 형태만 받으면 더미 데이터로도 그대로 돌아간다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Id"/>는 UI가 해석하지 않는다.</b> 클릭한 대상을 서버로 되돌려 보내기 위한 손잡이일 뿐이라,
    /// 네트워크 쪽이 netId를 쓰든 계정 ID를 쓰든 UI는 받은 값을 그대로 들고 있다가 돌려준다.
    /// 목록 갱신 사이에 같은 사람을 알아보는 기준도 이 값이다(닉네임이 아니라).
    /// </para>
    /// <para>
    /// <b>인스펙터가 아니라 런타임에 만들어진다.</b> 그래서 EpisodeInfo와 달리 SerializeField가 아니라
    /// 생성자로 채우는 읽기 전용 객체다.
    /// </para>
    /// </remarks>
    public class PartyMemberInfo
    {
        /// <summary>서버로 되돌려 줄 식별자. 목록 갱신 사이의 동일인 판정 기준이기도 하다.</summary>
        public string Id { get; }

        /// <summary>표시·검색·정렬의 기준이 되는 닉네임. 전 서버 유니크라 사실상 키 역할도 겸한다.</summary>
        public string Nickname { get; }

        /// <summary>카드 ② 구역에 찍을 레벨.</summary>
        public int Level { get; }

        /// <summary>직업 아이콘을 고르는 값(검사/거너). <c>CharacterSaveData.characterType</c>과 같은 값이다.</summary>
        public int CharacterType { get; }

        /// <summary>지금 접속해 있는지. 카드 ① 구체를 채울지 비울지를 결정한다.</summary>
        public bool IsOnline { get; }

        /// <summary>초대 가능 여부. 접속 중일 때만 의미가 있다.</summary>
        public PartyInviteState InviteState { get; }

        /// <summary>
        /// 지금 이 사람을 초대할 수 있는지. 카드 색(파랑/회색)과 선택 가능 여부가 모두 이 값 하나로 갈린다.
        /// </summary>
        public bool CanInvite => IsOnline && InviteState == PartyInviteState.Invitable;

        /// <summary>목록 한 줄을 만든다. 서버 응답 또는 더미 데이터에서 채운다.</summary>
        /// <param name="id">서버로 되돌려 줄 식별자</param>
        /// <param name="nickname">표시할 닉네임</param>
        /// <param name="level">표시할 레벨</param>
        /// <param name="characterType">직업 아이콘 인덱스가 되는 캐릭터 타입</param>
        /// <param name="isOnline">접속 중인지</param>
        /// <param name="inviteState">초대 가능 여부</param>
        public PartyMemberInfo(string id, string nickname, int level, int characterType,
                               bool isOnline, PartyInviteState inviteState)
        {
            Id = id;
            Nickname = nickname;
            Level = level;
            CharacterType = characterType;
            IsOnline = isOnline;
            InviteState = inviteState;
        }
    }
}
