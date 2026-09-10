using System;
using System.Collections.Generic;
using Mirror;
using ProjectS.Data;
using ProjectS.Events;
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

        // 파티가 향하는 던전(표시·입장용). 소속이 생길 때 서버가 채우고 해체 시 비운다. 파티는 2인이라
        // 양쪽 프레즌스에 같은 값을 심는다(소속처럼 파티 상태를 멤버마다 복제해 두는 방식).
        [SyncVar(hook = nameof(OnPartyDungeonIdChanged))] private int partyDungeonId;
        [SyncVar(hook = nameof(OnPartyDungeonTextChanged))] private string partyDungeonName = string.Empty;
        [SyncVar(hook = nameof(OnPartyDungeonTextChanged))] private string partyDifficultyLabel = string.Empty;

        // ── 파티원 상태 HUD용 생명/자원 비율 ─────────────────────────
        // 파티원 상태 HUD(PartyStatusView)가 그릴 "원격 플레이어의 HP/SG"다. 비율(0~1)만 복제한다 —
        // 슬롯은 게이지와 % 표기만 그려 cur/max 원본이 필요 없고, 비율이면 트래픽도 최소다.
        // ★ 훅을 달지 않는다. HP/SG는 전투 중 매우 자주 바뀌는데, 훅에서 OnAnyChanged를 쏘면
        //   로스터·결성창이 갱신마다 통째로 다시 그려진다(IPartySource가 "남은 시간은 이벤트로
        //   알리지 않는다"고 경계한 것과 같은 이유). 파티원 슬롯 드라이버가 자기 Update에서 값만
        //   읽어 가므로, 복제만 되면 충분하고 훅은 필요 없다.
        [SyncVar] private float hpRatio = 1f;
        [SyncVar] private float sgRatio = 1f;

        // 로컬에서 마지막으로 계산한 내 비율. PlayerEvents는 cur/max로 오므로 여기서 비율로 접어 둔다.
        private float localHpRatio = 1f;
        private float localSgRatio = 1f;

        // 마지막으로 서버에 올린 값. 전송 문턱(SendThreshold)의 기준점.
        private float lastSentHpRatio = -1f;
        private float lastSentSgRatio = -1f;

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

        /// <summary>파티가 향하는 던전 ID(2자리). 무소속이면 0. 실제 입장에 쓴다.</summary>
        public int PartyDungeonId => partyDungeonId;

        /// <summary>파티가 향하는 던전 표시 이름. 결성창이 그린다.</summary>
        public string PartyDungeonName => partyDungeonName;

        /// <summary>파티가 향하는 난이도 라벨.</summary>
        public string PartyDifficultyLabel => partyDifficultyLabel;

        /// <summary>남은 HP 비율(0~1). 파티원 상태 HUD가 원격 파티원 게이지를 그릴 때 읽는다.</summary>
        public float HpRatio => hpRatio;

        /// <summary>SG(자원) 비율(0~1). <see cref="HpRatio"/>와 같은 취지.</summary>
        public float SgRatio => sgRatio;

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
            if (Local == this)
            {
                Local = null;

                // 로컬에서만 걸었던 HP/SG 구독을 짝 맞춰 푼다. 안 풀면 파괴된 프레즌스가 static 이벤트에 남는다.
                PlayerEvents.OnHpChanged -= OnLocalHpChanged;
                PlayerEvents.OnSGChanged -= OnLocalSgChanged;
            }
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

            // 내 HP/SG 변화를 파티원에게 흘리려 로컬 플레이어에서만 구독한다(원격 복제본은 구독하지 않는다 —
            // 남의 프레즌스가 내 화면 이벤트를 주우면 안 된다). 짝은 OnStopClient에서 푼다.
            // PlayerEvents는 static이라 마을↔던전 씬 전환에도 구독이 유지된다.
            PlayerEvents.OnHpChanged += OnLocalHpChanged;
            PlayerEvents.OnSGChanged += OnLocalSgChanged;

            // 구독 직전에 이미 발행됐을 현재 스탯을 다시 받아 첫 값을 밀어 올린다(HudPresenter와 같은 통로).
            PlayerEvents.FireStatsRefreshRequested();
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

        // ── 로컬 → 서버: 내 HP/SG 밀어 올리기 ──────────────────────────
        // 로컬 플레이어일 때만 PlayerEvents를 구독해(OnStartLocalPlayer) 내 HP/SG 변화를 서버로 올린다.
        // 서버가 SyncVar에 담아 파티원 클라로 복제하면 그쪽 PartyStatusView가 내 게이지를 그린다.

        // TODO(전송 게이트 튜닝): 값이 이만큼 바뀌어야 올린다. 전투 중 잦은 미세 변화를 다 올리면 Command가
        // 폭주하므로 문턱을 둔다. 밸런스가 아니라 시스템 나사라 인스펙터로 빼지 않고 상수로 둔다(수치 조정 대상).
        private const float SendThreshold = 0.01f;

        // PlayerEvents는 cur/max로 온다. 비율로 접어 두고 문턱을 넘으면 서버로 올린다.
        private void OnLocalHpChanged(float cur, float max)
        {
            localHpRatio = Ratio(cur, max);
            PushVitals();
        }

        private void OnLocalSgChanged(float cur, float max)
        {
            localSgRatio = Ratio(cur, max);
            PushVitals();
        }

        // 문턱을 넘게 바뀌었을 때만 올린다.
        // TODO(경계값): 죽음(0)·풀피(1)처럼 놓치면 안 되는 경계는 문턱과 별개로 항상 통과시키는 편이
        //   안전하다. 지금은 문턱만 두었으니, 사망 표시가 한 박자 늦으면 여기에 경계 조건을 더한다.
        private void PushVitals()
        {
            if (Mathf.Abs(localHpRatio - lastSentHpRatio) < SendThreshold &&
                Mathf.Abs(localSgRatio - lastSentSgRatio) < SendThreshold)
                return;

            lastSentHpRatio = localHpRatio;
            lastSentSgRatio = localSgRatio;
            CmdSetVitals(localHpRatio, localSgRatio);
        }

        /// <summary>
        /// 내 HP/SG 비율을 서버에 반영한다(→ 파티원 클라로 복제). 등록 Command와 같은 trust-on-first-use라,
        /// 값의 권위는 나중에 서버가 스탯을 직접 들고 판정하도록 바꿀 때 생긴다.
        /// </summary>
        /// <param name="hp">HP 비율(0~1)</param>
        /// <param name="sg">SG 비율(0~1)</param>
        [Command]
        private void CmdSetVitals(float hp, float sg)
        {
            hpRatio = Mathf.Clamp01(hp);
            sgRatio = Mathf.Clamp01(sg);
        }

        // cur/max를 0~1로. max가 0이면(스폰 직전 등) 0으로 눕혀 0 나눗셈을 피한다.
        private static float Ratio(float cur, float max) => max > 0f ? Mathf.Clamp01(cur / max) : 0f;

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

            // 무소속으로 돌아가면 파티 던전도 비운다(해체·추방·나가기). 안 비우면 다음 창에 옛 던전이 남는다.
            if (newPartyId == 0)
            {
                partyDungeonId = 0;
                partyDungeonName = string.Empty;
                partyDifficultyLabel = string.Empty;
            }
        }

        /// <summary>
        /// 파티가 향하는 던전을 서버에서 갱신한다(PartyManager 전용). 파티 성립 시 초대에 실려 온 값을 심는다.
        /// </summary>
        [Server]
        public void ServerSetPartyDungeon(int id, string dungeonName, string difficultyLabel)
        {
            partyDungeonId = id;
            partyDungeonName = dungeonName ?? string.Empty;
            partyDifficultyLabel = difficultyLabel ?? string.Empty;
        }

        // ── 훅 ───────────────────────────────────────────────────────

        // SyncVar 훅 시그니처(old, new). 어느 필드가 바뀌든 로스터를 통째로 다시 그리므로 값은 쓰지 않는다.
        // (Mirror Weaver가 오버로드된 훅을 확실히 물지 못하는 경우가 있어 필드마다 이름을 따로 둔다.)
        private void OnNameChanged(string _, string __)    => OnAnyChanged?.Invoke();
        private void OnLevelChanged(int _, int __)         => OnAnyChanged?.Invoke();
        private void OnTypeChanged(int _, int __)          => OnAnyChanged?.Invoke();
        private void OnAcceptsChanged(bool _, bool __)     => OnAnyChanged?.Invoke();
        private void OnPartyIdChanged(uint _, uint __)     => OnAnyChanged?.Invoke();
        private void OnPartyDungeonIdChanged(int _, int __)        => OnAnyChanged?.Invoke();   // partyDungeonId
        private void OnPartyDungeonTextChanged(string _, string __) => OnAnyChanged?.Invoke();  // partyDungeonName·partyDifficultyLabel 공용(같은 시그니처라 오버로드 아님)

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
