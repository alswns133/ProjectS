using Mirror;
using ProjectS.Events;
using ProjectS.Players;
using ProjectS.Scenes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 파티 초대 핸드셰이크와 파티 상태의 서버 권위. <see cref="ChatManager"/>·<see cref="PlayerPresence"/>와
    /// 같은 커넥션 소유 오브젝트(playerPrefab)에 붙어, 소유권이 있어 로컬 클라의 Command가 서버에서 허용된다.
    ///
    /// <para>
    /// 흐름(docs 계약 §10, IPartySource 주석):
    /// <code>
    /// [초대자 클라] RequestInvite → CmdInvite(targetNetId)
    /// [서버] 검증(온라인·초대수신·무소속·자기 자신 아님) → 대상 커넥션에 TargetInviteReceived
    /// [대상 클라] 수락/거절 → CmdAnswerInvite(inviterNetId, accept)
    /// [서버] 수락이면 파티 성립(양쪽 partyId·파티장 설정), 초대자에게 TargetInviteEnded(true)
    ///        거절/실패/타임아웃이면 초대자에게 TargetInviteEnded(false)
    /// </code>
    /// </para>
    /// <para>
    /// <b>파티 소속의 진실은 <see cref="PlayerPresence.PartyId"/> 하나다.</b> 여기서는 소속 값을 정하는
    /// 권위(서버 로직)와 "내가 파티장인지"만 들고, 파티원이 누구인지는 다리가 partyId가 같은 프레즌스로 찾는다
    /// — 상태를 두 곳에 두지 않기 위함이다.
    /// </para>
    /// <para>
    /// <b>UI는 이 클래스를 직접 모른다.</b> 받는 쪽 팝업·슬롯은 <see cref="PartyEvents"/>로만 이야기하고,
    /// 파티 다리(<see cref="NetworkPartySource"/>)가 로컬 인스턴스(<see cref="Local"/>)로 요청을 넘긴다
    /// (ChatManager↔ChatEvents와 같은 분리).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class PartyManager : NetworkBehaviour
    {
        /// <summary>로컬 플레이어 소유 인스턴스. 다리·팝업이 이걸 통해 요청 Command를 부른다. 접속 전 null.</summary>
        public static PartyManager Local { get; private set; }

        // ── 서버 전용 상태 ──────────────────────────────────────────
        // 보류 초대 한 건: 초대자 netId + 향하는 던전(성립 시 파티 상태로 심는다).
        private struct PendingInvite
        {
            public uint inviter;
            public int dungeonId;
            public string dungeonName;
            public string difficultyLabel;
        }

        [SerializeField] private CharacterRoster roster;

        // 보류 초대: 대상 netId → 보류 초대. "이 사람은 지금 누구의(어느 던전으로의) 초대를 받고 있나"를 서버가 기억한다.
        // 1인당 1건만 보류(2인 파티라 동시에 여러 초대를 받을 이유가 적다).
        private static readonly Dictionary<uint, PendingInvite> pendingByTarget = new();

        // 파티 id 발급기. 0은 "무소속" 예약값이라 1부터 센다. 서버에서만 증가한다.
        private static uint nextPartyId = 1;

        // 초대 서버측 타임아웃(안전망). 받는 팝업의 클라 타임아웃(기본 20초)보다 살짝 길게 둬,
        // 보통은 클라가 먼저 자동 거절하고 이 타이머는 팝업이 없거나 이상한 상황에서만 발동한다.
        // 밸런스가 아니라 네트워크 안전장치라 상수로 둔다(ChatManager.MaxChatLength와 같은 취지).
        // 받는 쪽 카운트다운의 PhaseDuration(총 시간)으로도 쓰여 public.
        public const float InviteTimeoutSeconds = 25f;

        /// <summary>
        /// 출발 카운트다운 길이(초). 밸런스가 아니라 기획 확정값이라 상수(2026-09-07: 30초, 파티원이 확인할 시간).
        /// 다리가 진행 바 비율(남은 시간/전체)을 내는 데 총 길이가 필요해 공개한다.
        /// </summary>
        public const float DepartCountdownSeconds = 30f;

        // ── 복제/로컬 상태 ──────────────────────────────────────────
        /// <summary>내가 파티장인지. 서버가 파티 성립 시 설정한다(초대한 쪽=파티장).</summary>
        [SyncVar(hook = nameof(OnLeaderChanged))] private bool isLeader;

        // 출발 카운트다운이 끝나는 서버 시각(NetworkTime.time 기준). 0 = 출발 안 함.
        // ★ 남은 시간(값)을 매 프레임 쏘지 않는다 — 종료 "시각" 하나만 복제하고, 남은 시간은 각 클라가
        //   자기 Local에서 (departEndTime - NetworkTime.time)으로 매 프레임 계산한다(IPartySource 주석
        //   "남은 시간은 이벤트로 알리지 않는다"). 서버 동기 시계라 두 파티원의 카운트다운이 안 어긋난다.
        //   isLeader와 마찬가지로 인스턴스별 SyncVar라, 서버가 이 파티원 둘의 인스턴스에만 심으면 다른 파티와 안 섞인다.
        [SyncVar(hook = nameof(OnDepartChanged))] private double departEndTime;

        // 초대를 보내고 응답을 기다리는 중인지. 순전히 로컬 UI 대기 플래그라 SyncVar가 아니다
        // (남에게 복제할 필요가 없다). RequestInvite에서 켜고 TargetInviteEnded에서 끈다.
        private bool isInviting;

        // 이 초대자가 방금 보낸 초대의 대상 netId(서버 타임아웃 판정용). isInviting이 한 번에 하나만
        // 허용하므로 단일 값으로 충분하다. 서버에서만 의미가 있다(타이머가 서버에서 돈다).
        private uint serverPendingTarget;

        /// <summary>내가 파티장인지(다리가 IsLeader로 노출).</summary>
        public bool IsLeader => isLeader;

        /// <summary>초대 응답을 기다리는 중인지(다리가 IsInviting으로 노출, 슬롯 대기 표시).</summary>
        public bool IsInviting => isInviting;

        /// <summary>출발 카운트다운이 도는 중인지(다리가 Phase를 Departing으로 가르는 근거).</summary>
        public bool IsDeparting => departEndTime > 0d;

        /// <summary>출발 카운트다운이 끝나는 서버 시각(NetworkTime.time 기준). 다리가 남은 시간을 여기서 뺀다.</summary>
        public double DepartEndTime => departEndTime;

        // ── 생명주기 ────────────────────────────────────────────────

        public override void OnStartLocalPlayer() => Local = this;

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        // 이 커넥션의 오브젝트가 서버에서 사라질 때(=접속 끊김/강제종료/언스폰) 뒤처리.
        public override void OnStopServer()
        {
            // 1. 이 사람이 낀 보류 초대 청소. 안 하면 가드 7(pendingByTarget.ContainsKey)이 영영 참이 돼
            //    상대가 초대 불가로 잠긴다.
            ServerCleanupPendingFor(netId);

            // 2. ★ 파티에 속해 있었다면 파티를 해체한다(2인이라 한 명이 빠지면 곧 해체).
            //    안 하면 남은 파티원의 partyId가 그대로 남아, 파티원 프레즌스는 파괴됐는데도
            //    "파티는 있는데 파티원이 안 보이는" 상태가 되고, 남은 사람이 나갈 수도 없다(강제종료 버그).
            //    ServerDisbandMyParty는 같은 partyId를 가진 모두를 NetworkServer.spawned에서 찾아 0으로 되돌린다
            //    — 파괴 직전이라도 이 오브젝트의 PlayerPresence.PartyId는 아직 읽히고, 남은 파티원 인스턴스는
            //    살아 있어 정상 해체된다.
            ServerDisbandMyParty();
        }

        // ── 초대 보내기 (초대자 클라 → 서버) ──────────────────────────

        /// <summary>
        /// 상대에게 초대를 보내 달라고 요청한다(다리의 <c>RequestInvite</c>가 부른다). 실제 발송·검증은 서버가 한다.
        /// 낙관적으로 대기 상태(<see cref="IsInviting"/>)로 잠그고, 결과는 <see cref="TargetInviteEnded"/>가 푼다.
        /// </summary>
        /// <param name="targetNetId">초대할 상대의 netId(로스터의 PartyMemberInfo.Id를 파싱한 값)</param>
        /// <param name="dungeonId">향하는 던전 ID(2자리, 실제 입장용). 0=미지정</param>
        /// <param name="dungeonName">표시용 던전 이름</param>
        /// <param name="difficultyLabel">표시용 난이도 라벨</param>
        public void RequestInvite(uint targetNetId, int dungeonId, string dungeonName, string difficultyLabel)
        {
            Debug.Log($"[진단][PartyManager] RequestInvite(클라): target={targetNetId}, dungeon={dungeonId}, isInviting={isInviting}, isLocalPlayer={isLocalPlayer}, Local={(Local == this)}", this);
            if (isInviting) return;

            isInviting = true;
            PartyEvents.FireChanged();      // 슬롯을 "초대 중…"으로
            CmdInvite(targetNetId, dungeonId, dungeonName, difficultyLabel);
        }

        [Command]
        private void CmdInvite(uint targetNetId, int dungeonId, string dungeonName, string difficultyLabel)
        {
            Debug.Log($"[진단][PartyManager] CmdInvite 수신(서버): 초대자={netId}, 대상={targetNetId}, 던전={dungeonId}");

            // 초대 가능 판정은 ServerCanInvite로 모았다(아래 도우미 구역). 실패면 초대자 대기만 풀고 끝낸다.
            // this=초대자 오브젝트라 connectionToClient=초대자 본인 → 이 경로는 그대로 맞다.
            if (!ServerCanInvite(targetNetId, out NetworkConnectionToClient targetConn))
            {
                // [진단] 어느 조건에서 막혔는지 상태를 덤프한다(원인 파악 후 이 블록 삭제).
                bool spawned = NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity ti);
                PlayerPresence tp = spawned && ti != null ? ti.GetComponent<PlayerPresence>() : null;
                PlayerPresence mp = GetComponent<PlayerPresence>();
                Debug.LogWarning($"[진단][PartyManager] ServerCanInvite 거부 — self={targetNetId == netId}, spawned={spawned}, " +
                    $"targetPresence={tp != null}, targetPartyId={(tp != null ? tp.PartyId : 0)}, targetAccepts={tp != null && tp.AcceptsInvites}, " +
                    $"myPresence={mp != null}, myPartyId={(mp != null ? mp.PartyId : 0)}, pendingHasTarget={pendingByTarget.ContainsKey(targetNetId)}");

                TargetInviteEnded(connectionToClient, false);
                return;
            }

            Debug.Log($"[진단][PartyManager] ServerCanInvite 통과 → TargetInviteReceived 발송(대상 conn={targetConn.connectionId})");

            // 이 대상에 대한 보류 초대를 기록한다(대상의 수락이 이 초대자와 맞는지 대조 + 성립 시 던전을 심을 근거).
            pendingByTarget[targetNetId] = new PendingInvite
            {
                inviter = netId,
                dungeonId = dungeonId,
                dungeonName = dungeonName,
                difficultyLabel = difficultyLabel,
            };
            serverPendingTarget = targetNetId;

            // 서버측 타임아웃 안전망을 (재)예약한다. 이전 초대의 잔여 타이머가 새 초대를 잘못 취소하지 않게
            // 먼저 취소하고 건다(한 사람은 한 번에 하나만 초대하므로 타이머도 하나).
            CancelInvoke(nameof(ServerInviteTimeout));
            Invoke(nameof(ServerInviteTimeout), InviteTimeoutSeconds);

            // 대상에게 "초대 왔음"을 배달한다. 서버가 보관한 내 표시 이름을 쓴다(위조 차단, ChatManager와 같은 취지).
            // 만료 시각은 서버 시각 + 타임아웃 — 받는 쪽 카운트다운이 이 값으로 남은 시간을 로컬 계산한다.
            TargetInviteReceived(targetConn, netId, ServerDisplayName(),
                                 NetworkTime.time + InviteTimeoutSeconds, dungeonId, dungeonName, difficultyLabel);
        }

        // ── 받는 쪽 (서버 → 대상 클라) ───────────────────────────────

        /// <summary>대상 클라에게만 초대를 배달한다. 프롬프터가 <see cref="PartyEvents.OnInviteReceived"/>로 받아 팝업을 띄운다.</summary>
        [TargetRpc]
        private void TargetInviteReceived(NetworkConnectionToClient target, uint inviterNetId, string inviterName,
                                          double expireTime, int dungeonId, string dungeonName, string difficultyLabel)
        {
            PartyEvents.FireInviteReceived(new PartyInviteOffer
            {
                inviterNetId = inviterNetId,
                inviterName = inviterName,
                expireTime = expireTime,
                dungeonId = dungeonId,
                dungeonName = dungeonName,
                difficultyLabel = difficultyLabel,
            });
        }

        /// <summary>초대에 답한다(받는 쪽 팝업이 부른다). 수락/거절 모두 서버가 최종 판정한다.</summary>
        /// <param name="inviterNetId">초대한 사람의 netId(offer에서 그대로 돌려준다)</param>
        /// <param name="accept">수락이면 true</param>
        public void AnswerInvite(uint inviterNetId, bool accept) => CmdAnswerInvite(inviterNetId, accept);

        [Command]
        private void CmdAnswerInvite(uint inviterNetId, bool accept)
        {
            // 이 답이 실제 보류 초대와 맞는지 대조한다(엉뚱한/지난 초대에 대한 답 차단).
            // recorded는 struct 복사본이라 아래 Remove 후에도 던전 정보가 남아 성립 시 그대로 쓴다.
            bool matches = pendingByTarget.TryGetValue(netId, out PendingInvite recorded) && recorded.inviter == inviterNetId;
            if (matches) pendingByTarget.Remove(netId);

            // 초대자 오브젝트/커넥션/매니저를 찾는다. 그 사이 초대자가 나갔으면 성립할 수 없다.
            // ★ 대기를 푸는 TargetInviteEnded는 반드시 "초대자의 PartyManager"에서 불러야 한다 —
            //   TargetRpc는 불린 오브젝트의 대상 클라 복제본에서 실행되므로, 여기서 this(수락자)로 부르면
            //   초대자 클라에서 '수락자 오브젝트 복제본'의 isInviting만 꺼져 정작 초대자 Local은 안 풀린다.
            NetworkConnectionToClient inviterConn = null;
            PartyManager inviterParty = null;
            if (NetworkServer.spawned.TryGetValue(inviterNetId, out NetworkIdentity inviterIdentity))
            {
                inviterConn = inviterIdentity.connectionToClient;
                inviterIdentity.TryGetComponent(out inviterParty);
            }

            if (!accept || !matches || inviterConn == null || inviterParty == null)
            {
                // 거절·불일치·초대자 이탈 → 초대자 대기만 풀어 준다(성립 없음).
                if (inviterConn != null && inviterParty != null) inviterParty.TargetInviteEnded(inviterConn, false);
                return;
            }

            // 재검증(틀 — 주석 풀고 채운다): 초대 발송~수락 사이에 한쪽이 다른 파티를 맺었을 수 있다.
            // 성립 직전 양쪽이 여전히 무소속(PartyId == 0)인지 다시 본다.
            // (this=수락자(대상). 초대자 프레즌스는 inviterIdentity에서 꺼낸다.)
            bool targetFree = TryGetComponent(out PlayerPresence myPresence) && myPresence.PartyId == 0;
            bool inviterFree = inviterIdentity.TryGetComponent(out PlayerPresence inviterPresence) && inviterPresence.PartyId == 0;
            if (!targetFree || !inviterFree)
            {
                inviterParty.TargetInviteEnded(inviterConn, false);   // 초대자 대기 풀기(대상 팝업은 이미 닫힘)
                return;
            }

            ServerFormParty(inviterIdentity, inviterConn, inviterParty, targetIdentity: netIdentity, dungeon: recorded);
        }

        // ── 성립 (서버) ─────────────────────────────────────────────

        // 양쪽을 한 파티로 묶는다. 소속(partyId)·파티 던전은 PlayerPresence에 심고, 파티장 여부는 각자 PartyManager에 심는다.
        [Server]
        private void ServerFormParty(NetworkIdentity inviterIdentity, NetworkConnectionToClient inviterConn,
                                     PartyManager inviterParty, NetworkIdentity targetIdentity, PendingInvite dungeon)
        {
            uint pid = nextPartyId++;

            SetPresenceParty(inviterIdentity, pid);
            SetPresenceParty(targetIdentity, pid);

            // 초대에 실려 온 던전을 파티 상태로 양쪽에 심는다(결성창이 표시, 나중에 입장에도 사용).
            SetPresenceDungeon(inviterIdentity, dungeon);
            SetPresenceDungeon(targetIdentity, dungeon);

            // 초대한 쪽이 파티장. (파티장 위임은 넣지 않기로 결정 — PartySlotBar 주석 2026-08-31.)
            inviterParty.isLeader = true;
            isLeader = false;   // 이 인스턴스=대상(수락한 쪽)

            // 초대자 대기를 성립으로 풀어 준다. 반드시 초대자의 PartyManager에서 불러야 그의 Local이 풀린다(위 CmdAnswerInvite 주석).
            // 대상 쪽 슬롯은 partyId 복제(PlayerPresence.OnAnyChanged)가 다시 그리게 하므로 별도 통지 불필요.
            inviterParty.TargetInviteEnded(inviterConn, true);
        }

        [Server]
        private static void SetPresenceParty(NetworkIdentity identity, uint partyId)
        {
            if (identity != null && identity.TryGetComponent(out PlayerPresence presence))
                presence.ServerSetPartyId(partyId);
        }

        [Server]
        private static void SetPresenceDungeon(NetworkIdentity identity, PendingInvite dungeon)
        {
            if (identity != null && identity.TryGetComponent(out PlayerPresence presence))
                presence.ServerSetPartyDungeon(dungeon.dungeonId, dungeon.dungeonName, dungeon.difficultyLabel);
        }

        // ── 성립/종료 통지 (서버 → 초대자 클라) ───────────────────────

        /// <summary>초대자에게 대기 종료를 알린다. 성립이든 실패든 대기 플래그를 푼다.</summary>
        [TargetRpc]
        private void TargetInviteEnded(NetworkConnectionToClient target, bool accepted)
        {
            isInviting = false;
            PartyEvents.FireChanged();

            // 성립 시 슬롯 갱신은 partyId 복제(PlayerPresence.OnAnyChanged)가 이미 일으킨다.
            // 실패 시에는 여기 FireChanged 하나로 "초대 중…"만 원복된다.
        }

        // ── 나가기 / 내보내기 ────────────────────────────────────────

        /// <summary>파티에서 나가겠다고 요청한다(다리의 RequestLeave).</summary>
        public void RequestLeave() => CmdLeave();

        /// <summary>파티원을 내보내 달라고 요청한다(다리의 RequestKick). 파티장만 의미가 있다.</summary>
        public void RequestKick() => CmdKick();

        [Command]
        private void CmdLeave()
        {
            // TODO: 내 partyId를 읽어 같은 파티원을 찾아 양쪽 모두 0으로 되돌린다(2인이라 나가면 해체).
            //       파티장 플래그(isLeader)도 양쪽 false로. 아래 ServerDisbandMyParty 참고.
            ServerDisbandMyParty();
        }

        [Command]
        private void CmdKick()
        {
            // TODO(검증): isLeader가 아닌 커넥션의 CmdKick은 무시한다(파티장만 내보낼 수 있다).
            //             2인 파티라 내보내기 결과는 나가기와 같다(둘 다 해체).
            if (!isLeader) return;

            ServerDisbandMyParty();
        }

        // 내가 속한 파티를 서버에서 해체한다(양쪽 partyId=0, isLeader=false).
        [Server]
        private void ServerDisbandMyParty()
        {
            if (!TryGetComponent(out PlayerPresence myPresence) || myPresence.PartyId == 0) return;

            uint pid = myPresence.PartyId;

            // 같은 파티 id를 가진 모든 프레즌스를 무소속으로 되돌린다(2인이라 최대 둘).
            // ★ 서버 권위 코드는 클라 목록(PlayerPresence.All — OnStartClient로 채워져 전용 서버에선 빈다)이
            //   아니라 NetworkServer.spawned를 훑는다. 그래야 host·전용서버 양쪽에서 동작한다.
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out PlayerPresence p) || p.PartyId != pid) continue;

                p.ServerSetPartyId(0);
                if (identity.TryGetComponent(out PartyManager party))
                {
                    party.departEndTime = 0d;   // ← 추가: 출발 중 해체돼도 Departing에 안 갇히게
                    party.isLeader = false;
                }
            }
        }

        // ── 출발 (파티장 클라 → 서버 → 파티원 둘) ─────────────────────

        /// <summary>출발을 걸어 카운트다운을 시작한다(다리의 RequestDepart). 파티장만 의미가 있다.</summary>
        public void RequestDepart() => CmdDepart();

        /// <summary>도는 카운트다운을 멈춘다(다리의 CancelDepart). 파티는 유지된다.</summary>
        public void CancelDepart() => CmdCancelDepart();

        /// <summary>파티원이 출발을 확인해 즉시 입장한다(다리의 ConfirmDepart).</summary>
        public void ConfirmDepart() => CmdConfirmDepart();

        [Command]
        private void CmdDepart()
        {
            // 1. 파티장만 출발을 건다(파티장 위임 없음 — 초대한 쪽이 계속 파티장).
            if (!isLeader) return;
            // 2. 무소속이 아닌가.
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) return;
            // 3. 이미 출발 중이면 무시(중복 시작 방지).
            if (departEndTime > 0d) return;

            // 종료 시각을 파티원 둘에게 심고(서버 동기 시계), 만료 타이머를 (재)예약한다.
            // 이전 잔여 타이머부터 취소한다(타이머는 이 인스턴스=파티장에서 돈다).
            ServerSetDepartForMyParty(NetworkTime.time + DepartCountdownSeconds);
            CancelInvoke(nameof(ServerDepartTimeout));
            Invoke(nameof(ServerDepartTimeout), DepartCountdownSeconds);
        }

        [Command]
        private void CmdCancelDepart()
        {
            // 1. 파티장만 취소한다.
            if (!isLeader) return;
            // 2. 출발 중이 아니면 할 일 없음.
            if (departEndTime == 0d) return;

            // 카운트다운만 끄고 파티는 유지한다(파티원 둘의 departEndTime=0).
            // 타이머는 CancelInvoke 하지 않아도 된다 — ServerDepartTimeout이 departEndTime==0을 보고
            // 스스로 아무 일도 안 한다(ServerInviteTimeout과 같은 자기-무력화).
            ServerSetDepartForMyParty(0d);
        }

        [Command]
        private void CmdConfirmDepart()
        {
            // [진단] 도착 여부와 가드 상태를 남긴다(어디서 멈추는지 보이게). 원인 파악 후 삭제.
            Debug.Log($"[진단][PartyManager] CmdConfirmDepart 수신(서버): departEndTime={departEndTime}, isLeader={isLeader}, netId={netId}");

            // 1. 출발 중인가.
            if (departEndTime == 0d)
            {
                Debug.Log("[진단][PartyManager] ConfirmDepart 무시 — 출발 중 아님(departEndTime=0). 파티장이 먼저 '출발'을 걸어야 한다.");
                return;
            }
            // 2. 무소속이 아닌가.
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0)
            {
                Debug.Log("[진단][PartyManager] ConfirmDepart 무시 — 무소속(PartyId=0).");
                return;
            }

            // 3. 파티장이 아니라 멤버인가(확인은 멤버만).
            if (isLeader)
            {
                Debug.Log("[진단][PartyManager] ConfirmDepart 무시 — 파티장은 확인 대상이 아니다(멤버만 입장 확인).");
                return;
            }

            // ★ 실제 던전 입장(additive Stage 1: 서버가 파티 전용 던전 인스턴스를 열고 파티 오브젝트를 옮긴다).
            ServerEnterDungeonForMyParty();

            // 입장 처리를 건 뒤 카운트다운을 정리한다.
            ServerSetDepartForMyParty(0d);
        }

        // ── 파티 동시입장 (additive Stage 1 — 서버) ──────────────────────
        // 목표: "파티마다 자기 던전 사본". ServerChangeScene(전원 이동)이 아니라 additive로 새 인스턴스를 열고
        // 그 파티(partyId) 오브젝트만 그 씬으로 옮긴다. 지금은 서버 측 로드·이동까지만 — 클라 로드(Stage 2)·
        // 관심관리(Stage 3)·씬별 물리(Stage 4)·GameSceneManager 통합(Stage 5)·언로드(Stage 6)는 뒤에 붙인다.

        /// <summary>이 파티를 파티 전용 던전 인스턴스로 입장시킨다(서버 권위). 멤버의 ConfirmDepart가 부른다.</summary>
        [Server]
        private void ServerEnterDungeonForMyParty()
        {
            if (!TryGetComponent(out PlayerPresence me) || me.PartyId == 0) return;

            string sceneName = DungeonRouter.SceneNameOf(me.PartyDungeonId);
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning($"[PartyManager] 파티 던전 {me.PartyDungeonId}에 연결된 씬이 없어 입장 못 함.", this);
                return;
            }

            StartCoroutine(ServerLoadInstanceAndMove(me.PartyId, sceneName));
        }

        // 던전 씬을 additive로 한 벌 더 로드하고, 같은 partyId 오브젝트를 그 인스턴스로 옮긴다.
        [Server]
        private IEnumerator ServerLoadInstanceAndMove(uint pid, string sceneName)
        {
            // additive 로드: 마을(기존 씬)은 그대로 두고 던전 사본을 새로 연다(전원 이동이 아니다).
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            yield return op;

            // 방금 로드된 인스턴스를 잡는다.
            // ★ TODO(Stage 1 함정 — 인스턴스 식별): 파티가 둘 이상 동시에 같은 던전에 들어가면 이름이 같아
            //   "마지막에 로드된 씬"으로 잡는 이 방식은 레이스에 취약하다. 로드 요청과 완료된 Scene 핸들을
            //   1:1로 매칭해 partyId→Scene 맵으로 들고 있어야 한다. 지금은 단일 파티 검증용 스톱갭.
            Scene instance = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);

            // 같은 파티(partyId)의 네트워크 오브젝트를 이 인스턴스로 옮긴다.
            // ★ TODO(무엇을 옮기나): 지금 네트워크 오브젝트는 경량 프레즌스/채팅뿐이다(A안 — 보이는 캐릭터는
            //   로컬 PlayerManager.Player라 네트워크 밖). 던전 협동에서 서로의 캐릭터를 보려면 캐릭터를
            //   네트워크로 올리는 결정이 먼저다. 그게 정해지면 그 캐릭터 오브젝트도 여기서 함께 옮긴다.
            foreach (NetworkIdentity id in NetworkServer.spawned.Values)
            {
                if (id == null || !id.TryGetComponent(out PlayerPresence p) || p.PartyId != pid) continue;

                SceneManager.MoveGameObjectToScene(id.gameObject, instance);
            }

            Debug.Log($"[PartyManager] (Stage1) 파티 {pid} → '{sceneName}' 인스턴스 로드+이동 완료. " +
                      $"다음: Stage2(그 파티 2명 커넥션에만 SceneMessage(LoadAdditive) 전송).", this);

            // TODO(Stage 2): 이 파티 두 커넥션에만 클라 additive 로드를 지시한다.
            //   foreach 파티원 conn: conn.Send(new SceneMessage { sceneName = sceneName, sceneOperation = SceneOperation.LoadAdditive });
            //   그 뒤 클라의 로컬 캐릭터/DungeonContext 세팅(PartyDungeonId SyncVar 이용) + GameSceneManager 통합(Stage 5).

            foreach (NetworkIdentity id in NetworkServer.spawned.Values)
            {
                if (id == null || !id.TryGetComponent(out PlayerPresence p) || p.PartyId != pid) continue;

                NetworkConnectionToClient conn = id.connectionToClient;

                if (conn == null) continue;

                if (conn == NetworkServer.localConnection) continue;  // 호스트는 서버 씬을 공유 → 다시 로드 금지

                conn.Send(new SceneMessage
                {
                    sceneName = sceneName,
                    sceneOperation = SceneOperation.LoadAdditive,
                    customHandling = true
                });  // ← 미러 자동로드 끔, 클라가 직접 로드
            }

            // 인스턴스 씬 안의 PlayerSpawnPoint를 찾는다(FindAnyObjectByType는 다른 인스턴스 걸 잡을 수 있어 X).
            Vector3 basePos = Vector3.zero;
            Quaternion baseRot = Quaternion.identity;
            foreach (GameObject root in instance.GetRootGameObjects())
            {
                PlayerSpawnPoint sp = root.GetComponentInChildren<PlayerSpawnPoint>();
                if (sp != null)
                {
                    basePos = sp.transform.position;
                    baseRot = sp.transform.rotation; break;
                }
            }

            List<PlayerPresence> members = new();
            foreach(var id in NetworkServer.spawned.Values)
            {
                if (id == null || !id.TryGetComponent(out PlayerPresence p) || p.PartyId != pid) continue;
                members.Add(p);
            }


            int i = 0;
            foreach (var p in members)
            {
                NetworkConnectionToClient conn = p.connectionToClient;
                if (conn == null) continue;

                Vector3 spawnPos = basePos + new Vector3(i * 1.5f, 0, 0);
                Player prefabs = roster.GetByType(p.CharacterType);
                if (prefabs == null) continue;

                GameObject avatar = Instantiate(prefabs.gameObject, spawnPos, baseRot);
                SceneManager.MoveGameObjectToScene(avatar, instance);
                NetworkServer.Spawn(avatar, conn);
                i++;
            }
        }

        // 카운트다운 만료(서버 안전망). CmdDepart가 Invoke로 예약해 파티장 인스턴스에서 돈다.
        // 그 사이 취소·확인으로 departEndTime이 0이 됐으면 아래 조건이 거짓이라 아무 일도 안 한다
        // (별도 CancelInvoke 불필요 — ServerInviteTimeout과 같은 자기-무력화).
        // 만료 동작: 30초가 지나도 파티는 유지되고 출발만 취소된다(2026-09-07 확정, DummyPartySource와 동일).
        [Server]
        private void ServerDepartTimeout()
        {
            if (departEndTime == 0d) return;   // 그 사이 취소·확인됐으면 무시
            ServerSetDepartForMyParty(0d);     // 출발만 취소, 파티는 유지
        }

        // 내가 속한 파티(같은 partyId) 모두의 departEndTime을 심는다. end=0이면 출발 해제.
        // ★ 서버 권위 코드라 클라 목록이 아니라 NetworkServer.spawned를 훑는다(host·전용서버 양쪽 동작).
        //   ServerDisbandMyParty의 순회와 판박이 — 소속 판정은 언제나 partyId로 스코프해 다른 파티에 안 샌다.
        [Server]
        private void ServerSetDepartForMyParty(double end)
        {
            if (!TryGetComponent(out PlayerPresence myPresence) || myPresence.PartyId == 0) return;

            uint pid = myPresence.PartyId;
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null || !identity.TryGetComponent(out PlayerPresence p) || p.PartyId != pid) continue;
                if (identity.TryGetComponent(out PartyManager party)) party.departEndTime = end;
            }
        }

        // ── 도우미 ──────────────────────────────────────────────────

        // 초대 가능 판정(서버 권위). CmdInvite가 쓰고, CmdAnswerInvite 재검증에서도 재사용할 수 있다.
        // 통과하면 대상 커넥션을 out으로 돌려준다(발송·성립에 필요). 구조 해석(1~3)은 채워 뒀고,
        // ★ 게임 규칙 가드(4~7)는 직접 채운다 — 주석을 풀고 조건을 쓰면 된다. return true 자리는 그대로 둔다.
        [Server]
        private bool ServerCanInvite(uint targetNetId, out NetworkConnectionToClient targetConn)
        {
            targetConn = null;

            // 1. 자기 자신 초대 방지
            if (targetNetId == netId) return false;

            // 2. 대상이 스폰돼 있는가
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
                return false;

            // 3. 대상 PlayerPresence·커넥션 확보(여기서 targetConn을 채운다)
            if (!targetIdentity.TryGetComponent(out PlayerPresence targetPresence))
                return false;
            targetConn = targetIdentity.connectionToClient;
            if (targetConn == null)
                return false;

            // 4. 내가 무소속인가 (★ 초대자 본인 상태 — 빼먹기 쉽다)
            if (!TryGetComponent(out PlayerPresence myPresence) || myPresence.PartyId != 0) return false;

            // 5. 대상이 무소속인가
            if (targetPresence.PartyId != 0) return false;

            // 6. 대상이 초대 수신 허용인가
            if (!targetPresence.AcceptsInvites) return false;

            // 7. (권장) 대상이 이미 다른 보류 초대 중인가 — "먼저 온 것 우선"이면 거부
            if (pendingByTarget.ContainsKey(targetNetId)) return false;

            return true;
        }

        // goneNetId가 대상(키) 또는 초대자(값)로 낀 보류 초대를 지운다. 접속 끊김 청소가 부른다.
        // ★ 구조만 잡아 둔 틀 — 아래 두 단계의 주석을 풀고 채운다.
        [Server]
        private static void ServerCleanupPendingFor(uint goneNetId)
        {
            // 1. goneNetId가 '대상'인 항목(키 == goneNetId).
            //    이 경우 초대자는 아직 "초대 중…"이라, 지우기만 하면 그쪽이 대기에 갇힌다 → 대기도 풀어 준다.
            if (pendingByTarget.TryGetValue(goneNetId, out PendingInvite p))
            {
                pendingByTarget.Remove(goneNetId);
                NotifyInviteEnded(p.inviter, accepted: false);   // 초대자 대기 해제
            }

            // 2. goneNetId가 '초대자'인 항목들(값.inviter == goneNetId). 대상 잠금만 풀면 되므로 제거만 하면 된다.
            //    값으로 찾는 검색이라 순회 중 삭제가 안전하지 않다 → 지울 키를 먼저 모은 뒤 지운다.
            List<uint> staleTargets = new();
            foreach (var pair in pendingByTarget)
                if (pair.Value.inviter == goneNetId) staleTargets.Add(pair.Key);
            foreach (uint t in staleTargets) pendingByTarget.Remove(t);
        }

        // 초대 타임아웃(서버 안전망). CmdInvite가 Invoke로 예약해 InviteTimeoutSeconds 뒤에 이 오브젝트(초대자)에서 돈다.
        // 그 사이 답이 와서 pending이 지워졌으면 아래 조건이 거짓이라 아무 일도 안 한다(별도 취소 불필요).
        // ★ 본문 틀 — 주석을 풀고 채운다. this=초대자라 TargetInviteEnded(connectionToClient, ...)로 바로 내 대기를 푼다.
        [Server]
        private void ServerInviteTimeout()
        {
            // 아직 내 초대가 그대로 걸려 있으면 → 취소하고 내 대기를 푼다.
            if (pendingByTarget.TryGetValue(serverPendingTarget, out PendingInvite p) && p.inviter == netId)
            {
                pendingByTarget.Remove(serverPendingTarget);
                TargetInviteEnded(connectionToClient, false);
            }
        }

        // 특정 초대자에게 대기 종료를 통지한다. ★ TargetInviteEnded는 반드시 초대자 오브젝트에서 불러야
        // 그의 Local 대기가 풀린다(CmdAnswerInvite·ServerFormParty와 같은 규칙). 채우면 위 청소·재검증이 공유한다.
        [Server]
        private static void NotifyInviteEnded(uint inviterNetId, bool accepted)
        {
            if (NetworkServer.spawned.TryGetValue(inviterNetId, out NetworkIdentity id)
                && id.connectionToClient != null
                && id.TryGetComponent(out PartyManager party))
            {
                party.TargetInviteEnded(id.connectionToClient, accepted);
            }
        }

        // 서버가 보관한 이 커넥션의 표시 이름. 프레즌스에 등록된 값을 재사용한다(클라가 보낸 값 안 믿음).
        [Server]
        private string ServerDisplayName()
        {
            return TryGetComponent(out PlayerPresence presence) ? presence.DisplayName : "Player";
        }

        private void OnLeaderChanged(bool _, bool __) => PartyEvents.FireChanged();

        // 출발 상태(departEndTime)가 복제돼 바뀌면 결성창을 다시 그린다(Formed↔Departing 전환·카운트다운 표시).
        private void OnDepartChanged(double _, double __) => PartyEvents.FireChanged();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            pendingByTarget.Clear();
            nextPartyId = 1;
        }
    }
}
