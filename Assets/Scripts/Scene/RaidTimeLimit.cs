using System.Collections.Generic;
using Mirror;
using ProjectS.Data;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 레이드 한 판의 <b>플레이 제한 시간</b>. 판정 권한이 있는 쪽(파티=서버, 싱글=내 컴퓨터)에만 존재하며,
    /// 시간이 다 되면 레이드 실패로 넘긴다(전멸과 같은 재시도 투표 흐름).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>흐르는 구간(2026-09-28 사용자 확정).</b> 입장 시 가득 찬 채 멈춰 있다가 <b>등장 연출이 끝나면</b> 흐르기 시작하고,
    /// <b>페이즈 전환 연출 동안은 멈춘다</b> — 조작할 수 없는 컷신 시간을 빼고 실제 전투 시간만 센다.
    /// 최종 보스가 죽거나(클리어) 전멸로 실패하면 그 자리에서 멈춘다.
    /// </para>
    /// <para>
    /// <b>시간 초과 = 레이드 실패</b>(2026-09-28 사용자 확정). 새 화면을 두지 않고 전멸과 같은 실패·재시도 투표를 탄다.
    /// 살아 있는 플레이어가 투표하는 동안 보스가 계속 싸우거나 뒤늦게 잡혀 "실패 뒤 클리어"가 되지 않도록,
    /// 이 인스턴스의 보스를 재우고 피해 면역을 건다.
    /// </para>
    /// <para>
    /// <b>수명.</b> <see cref="RaidFailSession"/>과 같이 레이드 씬(파티는 인스턴스 씬) 안의 빈 오브젝트에 붙어,
    /// 씬이 내려가면(재시도 재로드·마을 복귀) 함께 사라진다. 그래서 재시도는 새 타이머가 가득 찬 채 다시 시작한다.
    /// </para>
    /// <para>
    /// <b>표시.</b> 상태가 바뀔 때만 끝 시각을 알리고(<see cref="RaidTimerEvents"/>), 화면이 스스로 줄여 그린다.
    /// 파티는 서버가 파티원에게 TargetRpc로, 싱글은 여기서 직접 발행한다.
    /// </para>
    /// <para>
    /// 시작·정지 신호를 받는 곳(단일 진입점은 씬 기준 static 메서드):
    /// 열기 = <c>PartyManager.ServerLoadInstanceAndMove</c>(파티) / <see cref="RaidGather"/>.Enter(싱글),
    /// 시작 = <see cref="RaidIntroSession"/>.End(파티) / <see cref="BossIntroDirector"/> 등장 연출 종료(싱글·서버),
    /// 멈춤·재개 = <see cref="BossPhaseTransition"/>, 정지 = <see cref="Boss.OnDied"/>(클리어)·실패 확정.
    /// </para>
    /// </remarks>
    public class RaidTimeLimit : MonoBehaviour
    {
        // 시간 초과 뒤 보스에 거는 피해 면역 길이(초). 투표가 끝나면 씬이 내려가므로 투표 시간보다 넉넉하면 된다.
        private const float TimeUpImmuneSeconds = 3600f;

        private static readonly Dictionary<Scene, RaidTimeLimit> timers = new();

        private enum Phase
        {
            Waiting,   // 등장 연출 대기 — 가득 찬 채 멈춰 있다
            Running,   // 전투 중 — 흐른다
            Paused,    // 페이즈 전환 연출 — 멈춰 있다
            Stopped    // 클리어·실패·시간 초과 — 더 이상 아무것도 하지 않는다
        }

        private Phase phase = Phase.Waiting;

        private Scene instance;
        private uint partyId;   // 0 = 싱글

        private float remaining;   // 멈춰 있을 때의 남은 시간(초)
        private double endTime;    // 흐를 때의 끝 시각(Now 기준)

        /// <summary>
        /// 제한 시간이 쓰는 시계. 네트워크 중이면 서버 시계(<see cref="NetworkTime.time"/>), 아니면 로컬 시계.
        /// 끝 시각을 정하는 쪽(서버)과 그리는 쪽(각 클라)이 같은 시계를 봐야 모든 화면에서 같은 순간에 0이 된다.
        /// </summary>
        /// <remarks><see cref="BossIntroDirector"/>가 연출 시작 시각을 맞추는 시계와 같다.</remarks>
        public static double Now => NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.timeAsDouble;

        /// <summary>
        /// 레이드 씬(파티는 인스턴스)의 제한 시간을 연다. 가득 찬 채 멈춰 있고, 등장 연출이 끝나야 흐른다.
        /// </summary>
        /// <param name="scene">레이드 씬. 타이머가 이 씬 안에 붙어 씬과 수명을 같이한다.</param>
        /// <param name="dungeonId">던전 ID. <see cref="RaidTable"/> 행을 찾는 키.</param>
        /// <param name="party">파티 ID. 0이면 싱글 제한 시간을 쓴다.</param>
        /// <returns>열린 타이머. 이 레이드에 제한 시간이 없으면(행 없음·0 이하) null.</returns>
        public static RaidTimeLimit Open(Scene scene, int dungeonId, uint party)
        {
            if (!scene.IsValid()) return null;

            if (timers.TryGetValue(scene, out RaidTimeLimit old) && old != null)
                Destroy(old.gameObject);

            float limit = ResolveLimit(dungeonId, party != 0);
            if (limit <= 0f)
            {
                Debug.Log($"[RaidTimeLimit] 던전 {dungeonId}({(party != 0 ? "파티" : "싱글")})은 제한 시간이 없습니다(RaidTable 행 없음 또는 0).");
                return null;
            }

            GameObject host = new GameObject(party != 0 ? $"RaidTimeLimit{party}" : "RaidTimeLimit");
            SceneManager.MoveGameObjectToScene(host, scene);

            RaidTimeLimit timer = host.AddComponent<RaidTimeLimit>();
            timer.instance = scene;
            timer.partyId = party;
            timer.remaining = limit;

            timers[scene] = timer;
            timer.Broadcast();

            Debug.Log($"[진단][RaidTimeLimit] 열림 — 던전 {dungeonId}, {(party != 0 ? $"파티 {party}" : "싱글")}, 제한 {limit:0}초", host);
            return timer;
        }

        /// <summary>등장 연출이 끝났다 — 시간을 흐르게 한다. 이미 시작했으면 무시한다(여러 경로에서 불려도 안전).</summary>
        /// <param name="scene">레이드 씬.</param>
        public static void BeginIn(Scene scene)
        {
            if (TryGet(scene, out RaidTimeLimit timer)) timer.Begin();
        }

        /// <summary>페이즈 전환 연출 시작 — 시간을 멈춘다.</summary>
        /// <param name="scene">레이드 씬.</param>
        public static void PauseIn(Scene scene)
        {
            if (TryGet(scene, out RaidTimeLimit timer)) timer.Pause();
        }

        /// <summary>페이즈 전환 완료 — 멈췄던 시간을 다시 흐르게 한다.</summary>
        /// <param name="scene">레이드 씬.</param>
        public static void ResumeIn(Scene scene)
        {
            if (TryGet(scene, out RaidTimeLimit timer)) timer.Resume();
        }

        /// <summary>클리어·실패로 판이 끝났다 — 남은 시간을 그 자리에 고정한다.</summary>
        /// <param name="scene">레이드 씬.</param>
        public static void StopIn(Scene scene)
        {
            if (TryGet(scene, out RaidTimeLimit timer)) timer.Stop();
        }

        private static bool TryGet(Scene scene, out RaidTimeLimit timer)
            => timers.TryGetValue(scene, out timer) && timer != null;

        // 테이블에서 이 판의 제한 시간을 읽는다. 테이블이 아직 없거나 행이 없으면 0(제한 없음).
        private static float ResolveLimit(int dungeonId, bool isParty)
        {
            JsonManager json = JsonManager.Instance;
            if (json == null || !json.IsReady) return 0f;

            RaidTable row = json.Get<RaidTable>(dungeonId);
            if (row == null) return 0f;

            return isParty ? row.PartyTimeLimit : row.SoloTimeLimit;
        }

        private void Begin()
        {
            if (phase != Phase.Waiting) return;

            phase = Phase.Running;
            endTime = Now + remaining;
            Broadcast();

            Debug.Log($"[진단][RaidTimeLimit] 시작 — 남은 {remaining:0}초", this);
        }

        private void Pause()
        {
            if (phase != Phase.Running) return;

            remaining = RemainingNow();
            phase = Phase.Paused;
            Broadcast();
        }

        private void Resume()
        {
            if (phase != Phase.Paused) return;

            phase = Phase.Running;
            endTime = Now + remaining;
            Broadcast();
        }

        private void Stop()
        {
            if (phase == Phase.Stopped) return;

            if (phase == Phase.Running) remaining = RemainingNow();
            phase = Phase.Stopped;
            Broadcast();

            Debug.Log($"[진단][RaidTimeLimit] 정지 — 남은 {remaining:0.0}초", this);
        }

        private void Update()
        {
            if (phase == Phase.Running && Now >= endTime) TimeUp();
        }

        // 시간 초과 → 레이드 실패. 전멸과 같은 실패·재시도 투표로 넘긴다.
        private void TimeUp()
        {
            remaining = 0f;
            phase = Phase.Stopped;
            Broadcast();

            // 살아 있는 플레이어가 투표하는 동안 보스가 계속 공격하거나, 뒤늦게 잡혀 결과창이 뜨지 않게 한다.
            FreezeBosses();

            Debug.Log($"[진단][RaidTimeLimit] 시간 초과 — {(partyId != 0 ? $"파티 {partyId}" : "싱글")} 레이드 실패", this);

            if (partyId != 0)
            {
                if (RaidFailSession.TryGet(partyId, out RaidFailSession session)) session.FailByTimeout();
                else Debug.LogWarning($"[RaidTimeLimit] 파티 {partyId}의 RaidFailSession이 없어 시간 초과 실패를 알리지 못했습니다.", this);
                return;
            }

            // 싱글: 서버 세션이 없으므로 RaidFailFlow의 싱글 실패와 같이 1/1로 직접 알린다(투표도 혼자라 제한 시간 없음).
            RaidFailEvents.FireFailed(1, 0f);
        }

        private void FreezeBosses()
        {
            foreach (Boss boss in FindObjectsByType<Boss>(FindObjectsSortMode.None))
            {
                if (boss == null || boss.gameObject.scene != instance) continue;

                boss.SuspendAI();
                if (boss.Stats != null) boss.Stats.SetDamageImmune(TimeUpImmuneSeconds);
            }
        }

        private float RemainingNow() => Mathf.Max(0f, (float)(endTime - Now));

        // 지금 상태를 화면에 알린다. 파티는 이 인스턴스에 있는 파티원 각자에게, 싱글은 이 컴퓨터에 직접.
        private void Broadcast()
        {
            bool running = phase == Phase.Running;

            if (partyId == 0)
            {
                RaidTimerEvents.FireChanged(remaining, endTime, running);
                return;
            }

            // ★ TargetRpc는 "불린 오브젝트"의 대상 클라 복제본에서 실행된다. 반드시 그 파티원 자신의 PartyManager로 부른다.
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (identity == null || identity.gameObject.scene != instance) continue;
                if (!identity.TryGetComponent(out PlayerPresence member) || member.PartyId != partyId) continue;
                if (!identity.TryGetComponent(out PartyManager pm) || identity.connectionToClient == null) continue;

                pm.TargetRaidTimer(identity.connectionToClient, remaining, endTime, running);
            }
        }

        private void OnDestroy()
        {
            if (timers.TryGetValue(instance, out RaidTimeLimit current) && current == this)
                timers.Remove(instance);
        }

        // 플레이 모드 리로드 후 이전 판의 타이머 표가 남지 않게 한다(static 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => timers.Clear();
    }
}
