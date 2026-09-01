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
    /// 모든 던전 씬의 공통 진입/이탈 흐름을 쥔 베이스. 개별 던전(Dungeon1/Dungeon2…)은 이걸 상속해
    /// <see cref="DungeonNumber"/>만 선언하고, 고유 연출이 필요할 때만 훅을 재정의한다.
    ///
    /// <para>
    /// <b>던전 번호는 개발자 영역, 난이도는 유저 영역.</b> 씬 = 던전 번호가 1:1이라 씬 스크립트가 자기
    /// 번호(앞자리)를 쥐고, 난이도(뒷자리)는 입장 화면에서 유저가 골라 세션에 실어 온다.
    /// 그래서 씬은 "1?"에서 <c>1</c>만 알고 <c>?</c>는 세션에서 받는다.
    /// </para>
    /// </summary>
    public class DungeonGather : BaseScene
    {
        /// <summary>
        /// 이 씬이 몇 번 던전인가(ID 앞자리). 개발자가 박는 값 — 씬이 곧 던전의 증거다.
        /// 서브클래스가 재정의한다. 베이스 기본값은 1.
        /// </summary>
        protected virtual int DungeonNumber => 1;

        [Tooltip("직접 씬 테스트용 폴백 난이도(뒷자리). 세션 없이 씬을 바로 열었을 때만 쓴다. " +
                 "1=노말 · 2=하드 · 3=매니악. 실제 진입은 유저가 입장 화면에서 고른 값을 세션에서 받는다.")]
        [SerializeField] protected int fallbackDifficulty = 1;

        protected EnemySpawner enemySpawner;
        protected EnemyRoom[] rooms;
        protected readonly List<Enemy> alive = new();

        /// <summary>
        /// 던전 공통 진입. 오케스트레이션만 하고 세부는 단계 메서드로 위임한다.
        /// virtual이라 서브클래스가 통째로 갈아끼울 수도, 단계·훅만 바꿀 수도 있다.
        /// </summary>
        public override void Enter()
        {
            ResolveDungeon();
            UIManager.Instance.ShowPanel<HUDPanel>();
            SetupPlayer();
            SetupSpawning();
            OnDungeonEnter();   // 던전별 훅. 베이스는 비어 있음.
        }

        /// <summary>던전 공통 이탈. 씬을 떠나며 상태·몬스터·플레이어·스포너를 정리한다.</summary>
        public override void Exit()
        {
            OnDungeonExit();    // 던전별 훅(정리 전에 먼저).

            // 이 던전을 떠나므로 현재 던전 표식을 지운다(다음 씬이 마을이면 안내가 게이트로 돌아간다).
            DungeonContext.ClearDungeon();

            // 로딩이 도는 동안(던전 씬은 아직 언로드 전) 몬스터가 숨겨진 플레이어 마지막 위치로
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
        /// 이번에 입장한 던전·난이도를 확정해 <see cref="DungeonContext"/>에 싣는다.
        /// 난이도만 세션(유저 선택)에서 뽑고, 던전 번호는 이 씬이 쥔 <see cref="DungeonNumber"/>를 쓴다.
        /// </summary>
        protected virtual void ResolveDungeon()
        {
            // ★ 난이도만 유저가 고른 값(세션)에서 뽑는다. 던전 번호는 씬이 증거라 세션 앞자리를 믿지 않는다.
            int difficulty = GameSession.SelectedDungeonId != 0
                ? DungeonRouter.DifficultyOf(GameSession.SelectedDungeonId)
                : fallbackDifficulty;

            int currentDungeonId = DungeonNumber * 10 + difficulty;   // "1" + "?" = 1?
            DungeonContext.SetDungeon(currentDungeonId);
        }

        /// <summary>지속 플레이어를 스폰 지점으로 옮겨 던전 모드로 켜고, 진입 회복·부활 기회·스탯 갱신을 처리한다.</summary>
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

            // 기획: 던전 한 판마다 부활 기회 1회. 여기서 안 채우면 첫 죽음에 바로 마을로 쫓겨난다.
            ReviveBudget.GrantOnDungeonEnter();

            // JSON에서 로드된 실제 스탯을 HUD에 다시 반영(마을과 동일 방침).
            PlayerEvents.FireStatsRefreshRequested();
        }

        /// <summary>씬에 배치된 스포너·방을 찾아 스폰 권위를 연결하고, 몬스터 프리로드 후 트리거를 켠다.</summary>
        protected virtual void SetupSpawning()
        {
            enemySpawner = FindAnyObjectByType<EnemySpawner>();
            rooms = FindObjectsByType<EnemyRoom>(FindObjectsSortMode.None);
            foreach (var r in rooms)
                r.Bind(RequestRoomSpawn);   // 각 방에 스폰 권위 연결

            _ = PreloadThenEnableAsync();
        }

        /// <summary>던전별 진입 연출·기믹 훅. 베이스는 아무것도 하지 않는다.</summary>
        protected virtual void OnDungeonEnter() { }

        /// <summary>던전별 이탈 정리 훅. 베이스는 아무것도 하지 않는다.</summary>
        protected virtual void OnDungeonExit() { }

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
    }
}
