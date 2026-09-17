using System.Collections.Generic;
using Mirror;
using ProjectS.Enemies;
using ProjectS.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Networking
{
    /// <summary>
    /// 파티 레이드 인스턴스 하나의 <b>등장 연출 진행을 서버가 쥐는</b> 세션. 파티원 준비 현황을 세고,
    /// 전원 준비(또는 시간 초과) 시 연출 시작 시각을 정해 모두에게 알리고, 끝 시각에 보스를 깨운다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 서버가 쥐나(2026-09-17 확정 구조).</b> 누가 스폰됐는지는 서버만 알고, 각자의 화면이 아직 로딩 중인지는
    /// 그 클라만 안다. 그래서 준비 여부는 클라가 보고하고(<c>PartyManager.CmdRaidIntroReady</c>), 시작 판단과
    /// 시작 시각(<see cref="NetworkTime.time"/> 기준)은 서버가 정한다. 모두가 같은 시각에 맞춰 재생하므로
    /// 신호 도착이 늦은 클라도 같은 순간에 끝나고, 서버가 보스 AI를 깨우는 시점과 어긋나지 않는다.
    /// </para>
    /// <para>
    /// <b>수명.</b> 인스턴스 씬 안의 빈 오브젝트에 붙여, 인스턴스가 내려가면 함께 사라진다. 세션이 파티장
    /// 오브젝트에 붙어 있으면 파티장이 나갈 때 진행이 통째로 끊기기 때문이다.
    /// </para>
    /// <para>
    /// <b>인스턴스 단위.</b> 공유 서버라 여러 파티가 동시에 레이드를 돌 수 있어, 준비 현황을 partyId별로 따로 센다.
    /// </para>
    /// </remarks>
    public class RaidIntroSession : MonoBehaviour
    {
        // 한 사람의 로딩이 멈춰도 나머지가 영원히 기다리지 않게 하는 상한. 넘으면 준비된 사람 기준으로 시작하고,
        // 늦게 준비된 사람은 서버 시각 기준으로 연출 중간부터 본다.
        private const float WaitTimeout = 30f;

        // 시작 지시가 클라에 도착할 여유. 시작 시각을 "지금"이 아니라 조금 뒤로 잡아, 대부분의 클라가
        // 연출을 처음부터(0초 지점) 보게 한다. 더 늦게 도착한 클라는 늦은 만큼 건너뛰어 끝 시각을 맞춘다.
        private const double StartLead = 0.25;

        private static readonly Dictionary<uint, RaidIntroSession> sessions = new();

        private uint partyId;
        private Scene instance;

        // 이 인스턴스로 들어온 파티원(플레이어 오브젝트 netId). 접속이 끊긴 사람은 매 프레임 걸러 분모에서 뺀다
        // — 안 빼면 나간 사람을 영원히 기다린다.
        private readonly HashSet<uint> expected = new();
        private readonly HashSet<uint> ready = new();

        private Boss boss;
        private BossIntroDirector director;

        private float createdAt;
        private double startTime = -1;   // -1 = 아직 시작 전
        private double endTime;
        private bool ended;

        // 같은 값을 매 프레임 다시 보내지 않기 위한 마지막 전송값.
        private int sentReady = -1;
        private int sentTotal = -1;

        /// <summary>
        /// 인스턴스의 대기 세션을 연다. 인스턴스 씬 로드 직후, 클라에 씬 로드를 지시하기 <b>전에</b> 부른다
        /// — 그래야 대기 화면 표시 지시가 로딩 화면보다 먼저 깔려 로딩→씬 사이 빈 프레임이 안 보인다.
        /// </summary>
        /// <param name="party">파티 ID.</param>
        /// <param name="instanceScene">이 파티의 인스턴스 씬.</param>
        /// <param name="members">인스턴스로 들어온 파티원.</param>
        /// <returns>열린 세션.</returns>
        public static RaidIntroSession Open(uint party, Scene instanceScene, IEnumerable<PlayerPresence> members)
        {
            // ([Server] 속성은 NetworkBehaviour에서만 보장되므로 MonoBehaviour인 여기선 직접 막는다.)
            if (!NetworkServer.active) return null;

            if (sessions.TryGetValue(party, out RaidIntroSession old) && old != null)
                Destroy(old.gameObject);

            GameObject host = new GameObject($"RaidIntroSession{party}");
            SceneManager.MoveGameObjectToScene(host, instanceScene);

            RaidIntroSession session = host.AddComponent<RaidIntroSession>();
            session.partyId = party;
            session.instance = instanceScene;
            session.createdAt = Time.time;

            foreach (PlayerPresence member in members)
                if (member != null) session.expected.Add(member.netId);

            sessions[party] = session;

            foreach (uint id in session.expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidWaitStarted(conn);

            Debug.Log($"[진단][RaidIntro] 세션 시작 — 파티 {party}, 파티원 {session.expected.Count}명", host);
            return session;
        }

        /// <summary>파티의 진행 중인 세션을 찾는다.</summary>
        public static bool TryGet(uint party, out RaidIntroSession session)
            => sessions.TryGetValue(party, out session) && session != null;

        /// <summary>
        /// 스폰된 보스를 세션에 묶고 <b>즉시 AI를 재운다.</b>
        /// </summary>
        /// <remarks>
        /// 연출 시작이 아니라 스폰 순간에 재우는 이유: 대기 화면 뒤에서도 게임은 돌고 있어, 안 재우면 먼저
        /// 준비된 플레이어를 보스가 대기 중에 공격한다. 깨우는 것은 연출 종료(<see cref="End"/>)가 맡는다.
        /// </remarks>
        public void AttachBoss(Boss spawnedBoss)
        {
            if (!NetworkServer.active) return;

            boss = spawnedBoss;
            if (boss != null) boss.SuspendAI();

            // 한 씬에 등장·페이즈 전환 디렉터가 함께 있어 역할로 가린다.
            director = BossIntroDirector.Find(instance, BossIntroDirector.DirectorRole.Intro);

            if (director == null)
                Debug.LogWarning("[RaidIntro] 인스턴스에 BossIntroDirector가 없습니다 — 연출 없이 전원 준비 시 곧바로 보스를 깨웁니다.", this);
        }

        /// <summary>
        /// 파티원 한 명의 준비 완료를 기록한다. 이미 시작했으면 그 사람에게만 시작을 알려 중간부터 합류시킨다.
        /// </summary>
        /// <param name="memberNetId">준비된 파티원의 플레이어 오브젝트 netId.</param>
        public void MarkReady(uint memberNetId)
        {
            if (!NetworkServer.active || ended || !expected.Contains(memberNetId) || !ready.Add(memberNetId)) return;

            Debug.Log($"[진단][RaidIntro] 준비 — netId={memberNetId} ({ready.Count}/{expected.Count})", this);

            // 시간 초과로 먼저 시작한 뒤 늦게 준비된 사람: 서버 시각 기준으로 지금 지점부터 보게 한다.
            if (startTime >= 0) SendStart(memberNetId);
        }

        private void Update()
        {
            if (ended) return;

            // 접속이 끊겼거나 인스턴스에서 나간(본체가 마을로 돌아간) 파티원은 분모·분자에서 뺀다.
            expected.RemoveWhere(id => !NetworkServer.spawned.TryGetValue(id, out NetworkIdentity member)
                                       || member == null
                                       || member.gameObject.scene != instance);
            ready.IntersectWith(expected);

            BroadcastCountIfChanged();

            if (expected.Count == 0)
            {
                End();   // 전원 이탈 — 기다릴 사람이 없다
                return;
            }

            if (startTime < 0)
            {
                bool timedOut = Time.time - createdAt >= WaitTimeout;

                // 보스가 아직 안 떴으면 시작할 수 없다. 끝내 안 뜨면 대기만 정리한다.
                if (boss == null)
                {
                    if (timedOut) End();
                    return;
                }

                if (ready.Count >= expected.Count) Begin("전원 준비");
                else if (timedOut) Begin($"대기 {WaitTimeout:0}초 초과");
                return;
            }

            if (NetworkTime.time >= endTime) End();
        }

        private void Begin(string reason)
        {
            double duration = director != null ? director.Duration : 0.0;
            startTime = NetworkTime.time + StartLead;
            endTime = startTime + duration;

            // 서버도 같은 시각에 연출을 재생해 보스를 실제로 연출대로 옮긴다. 관찰자는 이 위치를 동기화로 받는다.
            // 서버 쪽 재생에서는 UI·입력·카메라 같은 "화면 연출"을 하지 않는다(presentLocally=false) — 호스트가
            // 이 파티원이면 자기 클라 지시(TargetRaidIntroStart)로 화면 연출이 따로 붙는다.
            if (director != null) director.PlaySynced(boss, startTime, false);

            foreach (uint id in ready) SendStart(id);

            Debug.Log($"[진단][RaidIntro] 연출 시작({reason}) — {ready.Count}/{expected.Count}, 길이 {duration:0.00}초", this);
        }

        private void End()
        {
            if (ended) return;
            ended = true;

            // 서버 연출을 마무리한다. 디렉터 종료 처리가 보스 AI를 깨운다. 연출이 아예 없었으면 여기서 직접 깨운다.
            if (director != null) director.ForceEnd();
            else if (boss != null) boss.ResumeAI();

            foreach (uint id in expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidIntroEnd(conn);

            Debug.Log($"[진단][RaidIntro] 세션 종료 — 파티 {partyId}", this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (sessions.TryGetValue(partyId, out RaidIntroSession current) && current == this)
                sessions.Remove(partyId);
        }

        private void SendStart(uint memberNetId)
        {
            if (TryGetMember(memberNetId, out PartyManager pm, out NetworkConnectionToClient conn))
                pm.TargetRaidIntroStart(conn, startTime);
        }

        private void BroadcastCountIfChanged()
        {
            if (ready.Count == sentReady && expected.Count == sentTotal) return;

            sentReady = ready.Count;
            sentTotal = expected.Count;

            foreach (uint id in expected)
                if (TryGetMember(id, out PartyManager pm, out NetworkConnectionToClient conn))
                    pm.TargetRaidReadyCount(conn, sentReady, sentTotal);
        }

        // ★ TargetRpc는 "불린 오브젝트"의 대상 클라 복제본에서 실행된다. 반드시 그 파티원 자신의 PartyManager를 찾아 부른다.
        private static bool TryGetMember(uint netId, out PartyManager partyManager, out NetworkConnectionToClient conn)
        {
            partyManager = null;
            conn = null;

            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null) return false;
            if (!identity.TryGetComponent(out partyManager)) return false;

            conn = identity.connectionToClient;
            return conn != null;
        }

        // 플레이 모드 리로드 후 이전 판의 세션 표가 남지 않게 한다(static 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => sessions.Clear();
    }
}
