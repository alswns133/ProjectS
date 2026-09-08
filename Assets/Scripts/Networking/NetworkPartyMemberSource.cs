using System;
using System.Collections.Generic;
using Mirror;
using ProjectS.Managers;
using ProjectS.UI;
using UnityEngine;

namespace ProjectS.Networking
{
    /// <summary>
    /// <see cref="PlayerPresence"/>(SyncVar로 복제되는 접속 명단)를 초대 목록 팝업이 아는 형태
    /// (<see cref="IPartyMemberSource"/>)로 옮기는 다리. <c>DummyPartyMemberSource</c> 자리에 이걸 끼우면
    /// 팝업 코드는 한 줄도 바뀌지 않는다(docs 계약 §10) — UI는 여전히 미러를 모른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 컴포넌트는 네트워크 오브젝트가 아니다.</b> 씬에 놓인 평범한 MonoBehaviour로, 복제돼 온
    /// 프레즌스들을 <b>읽기만</b> 한다. 팝업의 <c>memberSourceBehaviour</c> 슬롯에 끼운다.
    /// 의존 방향은 Networking → UI(타입)뿐이라 팝업 내부는 모른다.
    /// </para>
    /// <para>
    /// <b>push 기반이라 폴링하지 않는다.</b> 프레즌스 SyncVar가 바뀌면 <see cref="PlayerPresence.OnAnyChanged"/>가
    /// 오고, 그때 캐시를 다시 만들어 <see cref="OnChanged"/>를 흘려보낸다.
    /// </para>
    /// </remarks>
    public class NetworkPartyMemberSource : MonoBehaviour, IPartyMemberSource
    {
        /// <inheritdoc/>
        public event Action OnChanged;

        private PartyInvitePopup popup;   // 캐시. GetComponent로 잡거나 [SerializeField]로 꽂는다.

        // 팝업에 그대로 넘길 목록. 프레즌스가 바뀔 때마다 다시 만든다(재사용해 할당을 줄인다).
        private readonly List<PartyMemberInfo> onlineCache = new();
        private readonly List<PartyMemberInfo> recentCache = new();

        /// <inheritdoc/>
        /// <remarks>
        /// TODO(로딩 구분): 지금은 "접속·ready" 만으로 준비됐다고 본다. 엄밀히는 첫 스냅샷(내 프레즌스가
        /// 목록에 들어온 시점)을 준비 신호로 삼아야 팝업이 "없습니다"를 번쩍이지 않는다
        /// (IsReady의 존재 이유 — IPartyMemberSource 주석). 프레즌스가 하나라도 잡히면 true로 좁히는 방법이 있다.
        /// </remarks>
        public bool IsReady => NetworkClient.isConnected && NetworkClient.ready;

        private void Awake()
        {
            popup = GetComponent<PartyInvitePopup>();
        }

        private void OnEnable()
        {
            PlayerPresence.OnAnyChanged += HandlePresenceChanged;
            BindAcceptToggle();
            Rebuild();
        }

        private void OnDisable()
        {
            PlayerPresence.OnAnyChanged -= HandlePresenceChanged;
            UnbindAcceptToggle();
        }

        // ── ⑥ 초대 수신 토글 (틀 — 주석 풀고 채운다) ──────────────────
        // 팝업(같은 GameObject)의 OnAcceptInvitesChanged를 받아 서버 프레즌스에 반영하고 세이브에 저장한다.
        // UI(팝업)는 미러를 모르고, 이 다리가 PlayerPresence.Local로 넘긴다(초대 목록과 같은 분리).
        // 팝업과 같은 GO라 GetComponent로 잡히고, 팝업이 켜질 때 이 소스도 함께 OnEnable 된다.

        private void BindAcceptToggle()
        {
            popup = GetComponent<PartyInvitePopup>();
            if (popup == null) return;

            // 열릴 때 드롭다운을 현재 값으로 맞춘다(내 프레즌스가 진실, 접속 전이면 세이브 폴백).
            // SetAcceptInvites는 SetValueWithoutNotify라 이 초기화가 OnAcceptInvitesChanged를 다시 쏘지 않는다.
            bool accepting = PlayerPresence.Local != null
                ? PlayerPresence.Local.AcceptsInvites
                : (GameSession.SelectedCharacter?.acceptsPartyInvites ?? true);
            popup.SetAcceptInvites(accepting);

            popup.OnAcceptInvitesChanged += OnAcceptInvitesChanged;
        }

        private void UnbindAcceptToggle()
        {
             if (popup != null) popup.OnAcceptInvitesChanged -= OnAcceptInvitesChanged;
        }

        // 드롭다운이 바뀌면: 서버 프레즌스에 반영(전 클라에서 내 카드가 회색 처리됨) + 세이브에 저장(다음 접속 유지).
        private void OnAcceptInvitesChanged(bool accepting)
        {
             PlayerPresence.Local?.CmdSetAcceptsInvites(accepting);
             if (GameSession.SelectedCharacter != null)
                 GameSession.SelectedCharacter.acceptsPartyInvites = accepting;
        }

        private void HandlePresenceChanged()
        {
            Rebuild();
            OnChanged?.Invoke();
        }

        /// <inheritdoc/>
        public IReadOnlyList<PartyMemberInfo> GetOnlineMembers() => onlineCache;

        /// <inheritdoc/>
        /// <remarks>
        /// TODO(최근 파티): 함께 던전을 클리어한 기록(최근 순, 최대 10명)이 필요하다. 접속 로스터가 아니라
        /// 세이브/서버 히스토리에서 와야 하므로 프레즌스와 출처가 다르다 — 지금은 빈 목록을 돌려
        /// "기록이 없습니다" 빈 상태로 둔다. 히스토리 저장이 붙으면 여기서 채운다.
        /// </remarks>
        public IReadOnlyList<PartyMemberInfo> GetRecentMembers() => recentCache;

        /// <inheritdoc/>
        /// <remarks>
        /// 프레즌스는 push(SyncVar)라 서버에 다시 물어볼 것이 없다. 새로고침은 지금 명단으로 캐시를
        /// 다시 만들어 흘려보내는 것으로 충분하다(연결이 끊겼다 붙는 사이에 놓친 것을 다시 반영).
        /// </remarks>
        public void Refresh()
        {
            Rebuild();
            OnChanged?.Invoke();
        }

        // 복제돼 온 프레즌스들을 초대 목록 한 줄(PartyMemberInfo)로 옮긴다. 자기 자신은 뺀다(계약: 자신 제외).
        private void Rebuild()
        {
            onlineCache.Clear();

            IReadOnlyList<PlayerPresence> presences = PlayerPresence.All;
            for (int i = 0; i < presences.Count; i++)
            {
                PlayerPresence p = presences[i];
                if (p == null || p.isLocalPlayer) continue;   // 내 프레즌스는 목록에서 제외

                // Id는 netId를 쓴다. 세션 안에서 유일하고 서버가 커넥션으로 바로 되짚을 수 있어(초대 라우팅)
                // 계정 uniqueId보다 이 용도엔 곧다. UI는 이 값을 해석하지 않고 그대로 돌려준다(PartyMemberInfo 주석).
                onlineCache.Add(new PartyMemberInfo(
                    p.netId.ToString(),
                    p.DisplayName,
                    p.Level,
                    p.CharacterType,
                    isOnline: true,
                    MapState(p)));
            }

            // recentCache는 위 TODO(최근 파티)가 붙기 전까지 비워 둔다.
        }

        // 초대 가능 여부를 프레즌스 상태에서 판정한다. 우선순위: 이미 파티중 > 초대 거부 > 초대 가능.
        private static PartyInviteState MapState(PlayerPresence p)
        {
            if (p.InParty) return PartyInviteState.InParty;
            if (!p.AcceptsInvites) return PartyInviteState.NotAccepting;

            return PartyInviteState.Invitable;
        }
    }
}
