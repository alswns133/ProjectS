using UnityEngine;
using ProjectS.Enemies;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.Players;
using ProjectS.UI;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace ProjectS.Scenes
{
    /// <summary>
    /// 모든 레이드 씬의 공통 진입/이탈 흐름을 쥔 베이스. 개별 레이드(<see cref="Raid"/>…)는 이걸 상속해
    /// <see cref="DungeonNumber"/>만 선언하고, 고유 연출이 필요할 때만 훅을 재정의한다.
    ///
    /// <para>
    /// <b>던전과 왜 별도 베이스인가.</b> 플레이어 진입·HUD·회복·부활 오케스트레이션은 <see cref="DungeonGather"/>와
    /// 같은 시스템을 부르지만(같은 매니저 호출), 레이드는 <b>미로형 방 진행이 아니라 단일 보스 아레나</b>라
    /// 스폰·클리어·연출의 뼈대가 갈린다(보스 HP바·그로기 잠금 등 — docs/BOSS 계열, boss-hpbar 메모).
    /// 그래서 던전 흐름에 조건문을 늘리기보다 흐름을 처음부터 분리한다.
    /// </para>
    /// <para>
    /// <b>ID.</b> 레이드는 던전 ID 규칙(docs/ID_NUMBERING.md §4)에서 <c>99</c>(최종 단일 컨텐츠) 한 칸을 쓴다.
    /// 앞자리(던전 번호)=<c>9</c>, 뒷자리(난이도)=<c>9</c>. 던전과 같은 축 분리를 따라 <see cref="DungeonNumber"/>는
    /// 씬이 쥐고, 난이도는 입장 화면이 세션에 실어 온다(현재 레이드는 난이도 한 칸뿐이라 사실상 9 고정).
    /// </para>
    /// </summary>
    public class RaidGather : BaseScene
    {
        /// <summary>
        /// 이 씬이 몇 번 레이드인가(던전 ID 앞자리). 개발자가 박는 값 — 씬이 곧 레이드의 증거다.
        /// 서브클래스가 재정의한다. 베이스 기본값은 9(레이드 = 던전 ID <c>99</c>의 앞자리, ID_NUMBERING §4).
        /// </summary>
        protected virtual int DungeonNumber => 9;

        [Tooltip("직접 씬 테스트용 폴백 난이도(뒷자리). 세션 없이 씬을 바로 열었을 때만 쓴다. " +
                 "레이드는 난이도 한 칸(9)뿐이라 기본 9. 실제 진입은 유저가 입장 화면에서 고른 값을 세션에서 받는다.")]
        [SerializeField] protected int fallbackDifficulty = 9;

        [Tooltip("켜면 씬 진입 즉시 보스를 스폰한다(레이드 관례 — 방 트리거를 밟을 필요 없음). " +
                 "끄면 던전처럼 방 트리거를 밟을 때 스폰한다. 보스를 씬에 직접 배치했다면(스포너/방 없음) 이 값과 무관하게 그대로 있는다.")]
        [SerializeField] protected bool spawnBossOnEnter = true;

        protected EnemySpawner enemySpawner;
        protected EnemyRoom[] rooms;
        protected readonly List<Enemy> alive = new();

        /// <summary>
        /// 레이드 공통 진입. 오케스트레이션만 하고 세부는 단계 메서드로 위임한다.
        /// virtual이라 서브클래스가 통째로 갈아끼울 수도, 단계·훅만 바꿀 수도 있다.
        /// </summary>
        public override void Enter()
        {
            ResolveDungeon();
            UIManager.Instance.ShowPanel<HUDPanel>();
            SetupPlayer();
            SetupSpawning();
            OnRaidEnter();   // 레이드별 훅. 베이스는 비어 있음.
        }

        /// <summary>레이드 공통 이탈. 씬을 떠나며 상태·몬스터·플레이어·스포너를 정리한다.</summary>
        public override void Exit()
        {
            OnRaidExit();    // 레이드별 훅(정리 전에 먼저).

            // 이 레이드를 떠나므로 현재 던전 표식을 지운다(다음 씬이 마을이면 안내가 게이트로 돌아간다).
            DungeonContext.ClearDungeon();

            // 로딩이 도는 동안(레이드 씬은 아직 언로드 전) 몬스터가 숨겨진 플레이어 마지막 위치로
            // 계속 이동하는 잔상을 막는다.
            foreach (Enemy enemy in Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None))
                enemy.HaltForSceneExit();

            // 전환 동안 지속 플레이어를 잠시 끈다(월드에 방치돼 떨어지지 않게).
            if (PlayerManager.Instance != null) PlayerManager.Instance.Hide();

            enemySpawner?.ClearAndRelease();
            alive.Clear();
        }

        public override void Initialize() { }

        public override void Progress(float progress) { }

        // ── 공통 단계: 서브클래스가 필요한 것만 재정의 ────────────────────

        /// <summary>
        /// 이번에 입장한 레이드·난이도를 확정해 <see cref="DungeonContext"/>에 싣는다.
        /// 난이도만 세션(유저 선택)에서 뽑고, 던전 번호는 이 씬이 쥔 <see cref="DungeonNumber"/>를 쓴다.
        /// </summary>
        protected virtual void ResolveDungeon()
        {
            // ★ 난이도만 유저가 고른 값(세션)에서 뽑는다. 던전 번호는 씬이 증거라 세션 앞자리를 믿지 않는다.
            int difficulty = GameSession.SelectedDungeonId != 0
                ? DungeonRouter.DifficultyOf(GameSession.SelectedDungeonId)
                : fallbackDifficulty;

            int currentDungeonId = DungeonNumber * 10 + difficulty;   // "9" + "9" = 99
            DungeonContext.SetDungeon(currentDungeonId);
        }

        /// <summary>지속 플레이어를 스폰 지점으로 옮겨 던전(전투) 모드로 켜고, 진입 회복·부활 기회·스탯 갱신을 처리한다.</summary>
        protected virtual void SetupPlayer()
        {
            Player player;
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.WarpToSpawn();
                player = PlayerManager.Instance.Player;
                player?.EnterDungeon();
            }
            else
            {
                // 부트스트랩 없이 직접 씬 테스트: 씬에 배치된 플레이어를 그대로 사용(워프 없음).
                player = Object.FindAnyObjectByType<Player>();
                player?.EnterDungeon();
            }

            // 기획: 씬 진입마다 HP·SG 최대 회복. 발행 전에 값을 먼저 세팅한다.
            player?.Stats.RefillOnSceneEnter();

            // 기획: 레이드 한 판마다 부활 기회 1회. 여기서 안 채우면 첫 죽음에 바로 마을로 쫓겨난다.
            ReviveBudget.GrantOnDungeonEnter();

            // JSON에서 로드된 실제 스탯을 HUD에 다시 반영(마을·던전과 동일 방침).
            PlayerEvents.FireStatsRefreshRequested();
        }

        /// <summary>
        /// 씬에 스포너·방이 있으면 스폰 권위를 연결하고 몬스터를 프리로드한다.
        /// <b>레이드 보스를 씬에 직접 배치한 경우엔 스포너·방이 없어도 되며(null 허용) 아무것도 하지 않는다.</b>
        /// </summary>
        protected virtual void SetupSpawning()
        {
            enemySpawner = FindAnyObjectByType<EnemySpawner>();
            rooms = FindObjectsByType<EnemyRoom>(FindObjectsSortMode.None);

            // 보스를 씬에 미리 깔아 두는 아레나면 스포너/방이 없다 — 그 구성도 정상으로 본다.
            // 다만 "즉시 스폰을 켰는데 안 나온다"의 대부분이 여기라, 왜 스폰 경로를 안 타는지 콘솔에 남긴다.
            if (enemySpawner == null || rooms == null || rooms.Length == 0)
            {
                if (spawnBossOnEnter)
                    Debug.LogWarning($"[RaidGather] 즉시 스폰이 켜져 있지만 스폰을 건너뛴다 — " +
                        $"EnemySpawner={(enemySpawner != null ? "있음" : "없음")}, EnemyRoom {rooms?.Length ?? 0}개. " +
                        $"씬에 EnemySpawner와 스폰 포인트를 담은 EnemyRoom을 두거나, 보스를 씬에 직접 배치했다면 이 토글을 꺼라.", this);
                return;
            }

            Debug.Log($"[RaidGather] 스폰 준비: EnemyRoom {rooms.Length}개, " +
                      $"스폰 포인트 {rooms.Sum(r => r.Points?.Length ?? 0)}개, 즉시 스폰={spawnBossOnEnter}", this);

            foreach (var r in rooms)
                r.Bind(RequestRoomSpawn);   // 각 방에 스폰 권위 연결

            // 즉시 스폰이면 프리로드 후 곧바로 스폰하고 트리거는 켜지 않는다(방 진입 대기 없음).
            // 던전 방식이면 프리로드 후 트리거만 켜 두고 플레이어가 방에 들어올 때 스폰한다.
            _ = spawnBossOnEnter ? PreloadThenSpawnAsync() : PreloadThenEnableAsync();
        }

        /// <summary>레이드별 진입 연출·기믹 훅(보스 등장 컷신, HP바 활성 등). 베이스는 아무것도 하지 않는다.</summary>
        protected virtual void OnRaidEnter() { }

        /// <summary>레이드별 이탈 정리 훅. 베이스는 아무것도 하지 않는다.</summary>
        protected virtual void OnRaidExit() { }

        /// <summary>★ 권위: 방이 요청 → 여기서만 스폰. 나중에 [Server] 가드.</summary>
        public void RequestRoomSpawn(EnemyRoom room)
        {
            // TODO(멀티): [Server]
            foreach (var p in room.Points)
            {
                p.PlaySpawnEffects();

                for (int i = 0; i < p.Count; i++)
                {
                    alive.Add(enemySpawner.SpawnOne(p.EnemyRef, p.Position, p.Rotation));
                    room.RegisterSpawned(alive[^1]);    // 방의 클리어 감시(문 여닫이)용 등록. null은 방이 걸러낸다.
                    if (p.IsEndBoss)
                        room.SetEndBoss(alive[^1]);
                }
            }
        }

        protected async Task PreloadThenEnableAsync()
        {
            try
            {
                await enemySpawner.PreloadAsync(rooms.SelectMany(r => r.EnemyRefs).Distinct());
                foreach (var r in rooms) r.EnableTrigger();   // 프리로드 끝난 뒤에야 트리거 켬
            }
            catch (System.Exception e) { Debug.LogException(e); }  // fire-and-forget은 예외가 삼켜지니 로깅
        }

        /// <summary>
        /// 프리로드가 끝난 뒤 방 트리거를 밟지 않고 곧바로 스폰한다(레이드 즉시 스폰).
        /// <see cref="EnemySpawner.SpawnOne"/>은 프리로드된 프리팹만 스폰하므로 반드시 프리로드 완료 후에 부른다.
        /// </summary>
        /// <remarks>
        /// 트리거를 켜지 않으므로(<see cref="EnemyRoom.EnableTrigger"/> 미호출) 방의 <c>isStart</c>가 false로 남아
        /// 플레이어가 방에 들어와도 <c>OnTriggerEnter</c>가 스폰을 다시 걸지 않는다(이중 스폰 방지).
        /// 스폰 권위(<see cref="RequestRoomSpawn"/>)를 직접 부르므로 IsEndBoss 등록·결과 감시는 그대로 돌지만,
        /// 방 문 잠금·클리어 감시(<c>BeginEncounter</c>)는 트리거 경로 전용이라 건너뛴다 — 단일 아레나 레이드엔 문이 없어 무방하다.
        /// </remarks>
        protected async Task PreloadThenSpawnAsync()
        {
            try
            {
                await enemySpawner.PreloadAsync(rooms.SelectMany(r => r.EnemyRefs).Distinct());
                foreach (var r in rooms) RequestRoomSpawn(r);   // 프리로드 끝난 뒤 즉시 스폰
            }
            catch (System.Exception e) { Debug.LogException(e); }  // fire-and-forget은 예외가 삼켜지니 로깅
        }
    }
}
