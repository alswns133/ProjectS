using System;
using System.Collections.Generic;
using Mirror;
using ProjectS.Data;
using ProjectS.Managers;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// 커넥션별 네트워크 플레이어 오브젝트에 붙는 "프레즌스"(접속 명단 한 줄). 초대 목록이 보여줄
    /// 닉네임·레벨·직업·초대수신·파티소속을 담아 전 클라로 복제한다.
    ///
    /// <para>
    /// <b><see cref="ChatManager"/>와 같은 오브젝트(playerPrefab)에 함께 붙인다.</b> 둘 다 커넥션 소유권
    /// (NetworkIdentity)이 있어야 Command가 성립하고, 접속=스폰 시점에 커넥션당 하나씩 만들어지기 때문이다.
    /// 관심사만 갈라 둔다(채팅 vs 명단) — 컴포넌트 분리 원칙(CLAUDE.md).
    /// </para>
    /// <para>
    /// <b>왜 SyncVar인가.</b> 초대 목록은 "지금 접속한 모두"를 봐야 하고, Mirror의 SyncVar는 스폰된
    /// 오브젝트 상태를 전 클라로 자동 복제한다(<see cref="ChatManager"/>의 <c>ownerName</c>과 같은 방식).
    /// 그래서 클라의 <see cref="NetworkPartyMemberSource"/>는 살아 있는 <see cref="PlayerPresence"/>들을
    /// 모으기만 하면 로스터가 완성된다 — 폴링이 필요 없고, 값이 바뀌면 훅이 <see cref="OnAnyChanged"/>를 쏴
    /// UI가 다시 그린다(<c>IPartyMemberSource.OnChanged</c>와 자연스럽게 맞물림).
    /// </para>
    /// <para>
    /// 등록 값의 신뢰 수준은 <see cref="ChatManager"/>와 같은 trust-on-first-use다. 로그인이 네트워크와
    /// 이어지는 단계에서 서버가 계정에서 직접 값을 조회하도록 바꾸면 완전한 권위가 된다.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class PlayerPresence : NetworkBehaviour
    {
        // ── 클라 전역: 살아 있는 프레즌스들 ──────────────────────────
        // 각 클라가 자기 화면의 로스터를 만들 때 훑는 대상. OnStartClient/OnStopClient에서 등록/해제한다.
        // (전용 서버 자신은 화면이 없어 이 목록을 쓰지 않는다 — 채우는 것은 무해하다.)
        private static readonly List<PlayerPresence> all = new();

        /// <summary>지금 스폰돼 있는 모든 프레즌스. 로스터 소스가 이걸 훑어 초대 목록을 만든다.</summary>
        public static IReadOnlyList<PlayerPresence> All => all;

        /// <summary>
        /// 내(로컬 플레이어) 프레즌스. 파티 다리(<see cref="NetworkPartySource"/>)가 "나"와 파티원을
        /// 뽑을 때 기준으로 쓴다. 접속 전에는 null이다.
        /// </summary>
        public static PlayerPresence Local { get; private set; }

        /// <summary>
        /// 누군가의 프레즌스가 생기거나·사라지거나·값이 바뀌었다. 로스터 소스가 이 신호로 다시 그린다.
        /// </summary>
        /// <remarks>
        /// static이라 씬을 넘어 살아남는다 — 마을↔던전 전환 후에도 구독이 유효하도록
        /// 소스는 OnEnable/OnDisable에서 대칭으로 구독/해제한다(플레이 모드 리로드 리셋은 아래 훅 참조).
        /// </remarks>
        public static event Action OnAnyChanged;

        // ── 복제되는 한 줄 ──────────────────────────────────────────
        // 서버가 채우고 전 클라로 복제된다. 값이 바뀌면 각 훅이 OnAnyChanged를 쏜다.
        // 훅 하나로 묶는 이유: 어느 필드가 바뀌든 로스터를 통째로 다시 그리면 되므로 값별 분기가 필요 없다.
        [SyncVar(hook = nameof(OnNameChanged))] private string displayName = "Player";
        [SyncVar(hook = nameof(OnLevelChanged))] private int level = 1;
        [SyncVar(hook = nameof(OnTypeChanged))] private int characterType;

        /// <summary>초대 수신 허용 여부(⑥ 드롭다운). 거부면 목록에서 회색(NotAccepting)으로 뜬다.</summary>
        [SyncVar(hook = nameof(OnAcceptsChanged))] private bool acceptsInvites = true;

        /// <summary>
        /// 소속 파티 식별자. 0이면 무소속. <b>PartyManager가 파티 성립/해체 시 서버에서만 갱신한다.</b>
        /// 여기서는 복제만 하고, 값을 정하는 권위는 파티 매니저에 둔다(초대 핸드셰이크 단계에서 채운다).
        /// </summary>
        [SyncVar(hook = nameof(OnPartyIdChanged))] private uint partyId;

        /// <summary>목록에 그릴 닉네임.</summary>
        public string DisplayName => displayName;

        /// <summary>목록에 그릴 레벨.</summary>
        public int Level => level;

        /// <summary>직업 아이콘 인덱스(<c>CharacterSaveData.characterType</c>).</summary>
        public int CharacterType => characterType;

        /// <summary>초대를 받는 상태인지. 거부면 초대 불가(회색).</summary>
        public bool AcceptsInvites => acceptsInvites;

        /// <summary>이미 파티에 속해 있는지. 정원 2인이라 소속=만석=초대 불가.</summary>
        public bool InParty => partyId != 0;

        /// <summary>소속 파티 id(0=무소속). PartyManager가 대상 파티를 찾을 때 쓴다.</summary>
        public uint PartyId => partyId;

        // ── 생명주기: 목록 등록/해제 ──────────────────────────────────

        /// <summary>클라(복제본 포함)에 등장할 때 전역 목록에 넣고 로스터를 다시 그리게 한다.</summary>
        public override void OnStartClient()
        {
            if (!all.Contains(this)) all.Add(this);
            OnAnyChanged?.Invoke();
        }

        /// <summary>클라에서 사라질 때(이탈·씬 정리) 목록에서 빼고 다시 그리게 한다.</summary>
        public override void OnStopClient()
        {
            all.Remove(this);
            if (Local == this) Local = null;
            OnAnyChanged?.Invoke();
        }

        /// <summary>
        /// 로컬 플레이어일 때만 서버에 내 프레즌스를 1회 등록한다(원격 복제본은 등록하지 않는다 —
        /// 자기 것을 여러 번 올리게 된다). <see cref="ChatManager.OnStartLocalPlayer"/>와 같은 진입점이다.
        /// </summary>
        public override void OnStartLocalPlayer()
        {
            Local = this;

            CharacterSaveData save = GameSession.SelectedCharacter;

            // 초대 수신 허용 초기값은 캐릭터 세이브에서 읽는다(세이브 없으면=오프라인 테스트는 기본 허용).
            CmdRegisterPresence(
                save != null ? save.name : "Player",
                save != null ? save.level : 1,
                save != null ? save.characterType : 0,
                accepts: save == null || save.acceptsPartyInvites);
        }

        // ── 클라 → 서버: 등록/변경 ────────────────────────────────────

        /// <summary>
        /// 접속 직후 내 프레즌스를 서버에 1회 등록한다(trust-on-first-use). 서버가 보관·복제하므로
        /// 이후 다른 클라가 위조할 수 없다. 빈 이름이면 커넥션 id 기반 대체 이름을 쓴다.
        /// </summary>
        [Command]
        private void CmdRegisterPresence(string name, int lv, int type, bool accepts)
        {
            name = name?.Trim();
            displayName = string.IsNullOrWhiteSpace(name)
                ? $"Player {connectionToClient.connectionId}"
                : name;
            level = Mathf.Max(1, lv);
            characterType = Mathf.Max(0, type);
            acceptsInvites = accepts;
            // partyId는 여기서 건드리지 않는다 — 무소속(0)으로 시작해 PartyManager가 성립 시 채운다.
        }

        /// <summary>
        /// ⑥ 초대 수신 허용/거부를 서버에 반영한다. 팝업 드롭다운(OnAcceptInvitesChanged)이 넘긴 값을
        /// <see cref="NetworkPartyMemberSource"/>가 이 Command로 올린다.
        /// </summary>
        /// <param name="accepts">초대를 받는 상태로 둘지</param>
        [Command]
        public void CmdSetAcceptsInvites(bool accepts)
        {
            acceptsInvites = accepts;
        }

        // ── 서버 전용: 파티 매니저가 쓰는 소속 갱신 ─────────────────────

        /// <summary>
        /// 소속 파티를 서버에서 갱신한다. <b>PartyManager 전용 진입점</b>이라 클라가 못 부르게 <see cref="ServerAttribute"/>로 막는다.
        /// 파티 성립 시 파티 id를, 해체·추방·나가기 시 0을 넘긴다.
        /// </summary>
        /// <param name="newPartyId">새 소속 파티 id(0=무소속)</param>
        [Server]
        public void ServerSetPartyId(uint newPartyId)
        {
            partyId = newPartyId;
        }

        // ── 훅 ───────────────────────────────────────────────────────

        // SyncVar 훅 시그니처(old, new). 어느 필드가 바뀌든 로스터를 통째로 다시 그리므로 값은 쓰지 않는다.
        // (Mirror Weaver가 오버로드된 훅을 확실히 물지 못하는 경우가 있어 필드마다 이름을 따로 둔다.)
        private void OnNameChanged(string _, string __)    => OnAnyChanged?.Invoke();
        private void OnLevelChanged(int _, int __)         => OnAnyChanged?.Invoke();
        private void OnTypeChanged(int _, int __)          => OnAnyChanged?.Invoke();
        private void OnAcceptsChanged(bool _, bool __)     => OnAnyChanged?.Invoke();
        private void OnPartyIdChanged(uint _, uint __)     => OnAnyChanged?.Invoke();

        // 플레이 모드 리로드(도메인 리로드 off) 후에도 static이 남아 죽은 구독/항목이 끼는 것을 막는다.
        // static 이벤트·목록 초기화 규칙(CLAUDE.md 이벤트 시스템).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            all.Clear();
            Local = null;
            OnAnyChanged = null;
        }
    }
}
