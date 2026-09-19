#if UNITY_EDITOR
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Core;
using ProjectS.Enemies;
using ProjectS.Managers;
using ProjectS.Scenes;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 에디터 전용: 로그인 → 마을 → 레이드 입장을 거치지 않고 <b>보스 연출이 있는 씬을 바로 틀어</b>
    /// 등장·페이즈 전환 연출을 확인하게 해 주는 테스트 하네스.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 씬 흐름(RaidGather)을 안 쓰나</b>: <c>Raid</c> 씬 컴포넌트는 씬 파일에 없고 <see cref="GameSceneManager"/>가
    /// 런타임에 붙인다. 그래서 씬만 틀면 부를 진입 흐름 자체가 없다. 대신 씬에 이미 배치된 것(<see cref="EnemySpawner"/>·
    /// <see cref="EnemyRoom"/>·<see cref="BossIntroDirector"/>)만으로 보스를 스폰한다. 스폰 후 등장 신호 → 연출 재생은
    /// 실제 경로(<c>Boss.Start</c> → <c>BossIntroDirector.SoloWaitThenPlay</c>)를 그대로 탄다.
    /// </para>
    /// <para>
    /// 함께 맞물린 에디터 전용 우회: <c>BossIntroDirector.IsEditorDirectPlay</c>가 캐릭터 대기를 건너뛰고(PlayerManager가 없어
    /// 캐릭터가 영영 안 온다), <see cref="EditorAutoBootstrap"/>이 JsonManager를 만들어 몬스터 스탯이 실제 테이블로 로드된다.
    /// </para>
    /// <para>
    /// 키
    /// <list type="bullet">
    ///   <item><b>F10</b> — 페이즈 전환이 달린 보스의 HP를 전환 임계까지 깎아, 실제 <see cref="BossPhaseTransition"/> 경로로 전환 연출을 재생한다.</item>
    ///   <item><b>F11</b> — 등장 연출을 지금 재생한다. 디렉터의 autoStartWhenSolo가 꺼져 있어 자동으로 안 돌 때 쓴다.</item>
    /// </list>
    /// 연출은 한 판에 한 번만 재생되므로 다시 보려면 플레이를 재시작한다.
    /// </para>
    /// <para>
    /// 싱글(로컬 스폰) 경로만 검증한다. 멀티 동기 재생(RaidIntroSession·서버 시각)은 실제 흐름으로 확인해야 한다.
    /// 파일 전체가 #if UNITY_EDITOR라 빌드에는 클래스도 자동 생성도 포함되지 않는다.
    /// </para>
    /// </remarks>
    public class DebugRaidDirectPlay : MonoBehaviour
    {
        // 레이드 던전 ID(ID_NUMBERING §4의 99). 몬스터 스탯 행을 이 던전 기준으로 읽게 한다 — RaidGather.ResolveDungeon과 같은 값.
        private const int RaidDungeonId = 99;

        // 씬을 바로 튼 경우에만 자기 오브젝트를 만든다. 정상 흐름(부트스트랩에서 시작)이면 GameSceneManager가 이미 있다.
        // 보스 연출 디렉터가 있는 씬만 대상으로 한다(마을·일반 던전을 바로 틀었을 때 끼어들지 않게).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (GameSceneManager.Instance != null) return;
            if (FindAnyObjectByType<BossIntroDirector>(FindObjectsInactive.Include) == null) return;

            new GameObject("[DebugRaidDirectPlay]").AddComponent<DebugRaidDirectPlay>();
        }

        // Start로 한 프레임 미뤄, 같은 시점에 도는 EditorAutoBootstrap(JsonManager 생성)과의 실행 순서에 기대지 않는다.
        private async void Start()
        {
            Debug.Log("[DebugRaidDirectPlay] 씬을 바로 틀었습니다 — 보스를 스폰하고 등장 연출을 재생합니다. " +
                      "F10 = 페이즈 전환 연출, F11 = 등장 연출 즉시 재생.", this);

            DungeonContext.SetDungeon(RaidDungeonId);

            // 보스를 씬에 직접 배치해 둔 구성이면 스폰할 필요가 없다(그 보스의 Start가 등장 신호를 쏜다).
            if (FindAnyObjectByType<Boss>() != null) return;

            EnemySpawner spawner = FindAnyObjectByType<EnemySpawner>();
            EnemyRoom[] rooms = FindObjectsByType<EnemyRoom>(FindObjectsSortMode.None);
            if (spawner == null || rooms.Length == 0)
            {
                Debug.LogWarning($"[DebugRaidDirectPlay] 보스를 스폰할 수 없습니다 — EnemySpawner={(spawner != null ? "있음" : "없음")}, " +
                                 $"EnemyRoom {rooms.Length}개.", this);
                return;
            }

            try
            {
                // SpawnOne은 프리로드된 프리팹만 성공한다(RaidGather.PreloadThenSpawnAsync와 같은 순서).
                await spawner.PreloadAsync(rooms.SelectMany(r => r.EnemyRefs).Distinct());
                if (this == null) return;   // 로딩 중 플레이를 멈췄다

                foreach (EnemyRoom room in rooms)
                    foreach (EnemySpawnPoint point in room.Points)
                        for (int i = 0; i < point.Count; i++)
                            spawner.SpawnOne(point.EnemyRef, point.Position, point.Rotation);
            }
            catch (System.Exception e) { Debug.LogException(e); }   // async void는 예외가 삼켜지니 로깅
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f10Key.wasPressedThisFrame) TriggerPhaseTransition();
            if (keyboard.f11Key.wasPressedThisFrame) PlayIntroNow();
        }

        // 등장 연출 디렉터를 즉시 시작시킨다. 이미 재생했거나 재생 중이면 디렉터가 알아서 무시한다.
        private void PlayIntroNow()
        {
            BossIntroDirector intro = BossIntroDirector.Find(default, BossIntroDirector.DirectorRole.Intro);
            if (intro == null)
            {
                Debug.LogWarning("[DebugRaidDirectPlay] 등장 연출 디렉터(Role=Intro)가 씬에 없습니다.", this);
                return;
            }

            intro.PlayNow();
        }

        // 페이즈 전환이 달린 보스에 큰 피해를 준다. 하한(SetDamageFloorRatio)이 걸려 있어 죽지 않고 임계에 멈추며,
        // 그 HP 변화 이벤트로 BossPhaseTransition이 실제 전환(2페이즈 스폰 → 전환 연출 → 1페이즈 제거)을 시작한다.
        // 전환 컴포넌트가 없는 보스(2페이즈 등)는 하한이 없어 즉사하므로 대상에서 뺀다.
        private void TriggerPhaseTransition()
        {
            foreach (Boss boss in FindObjectsByType<Boss>(FindObjectsSortMode.None))
            {
                if (boss.Stats == null || boss.Stats.IsDead) continue;
                if (!boss.TryGetComponent(out BossPhaseTransition transition) || !transition.enabled) continue;

                bool applied = boss.Stats.TakeDamage(new DamageResult { Amount = boss.Stats.MaxHp });
                Debug.Log(applied
                    ? $"[DebugRaidDirectPlay] '{boss.name}' HP를 전환 임계로 깎아 페이즈 전환을 시작합니다."
                    : $"[DebugRaidDirectPlay] '{boss.name}'이(가) 피해를 받지 않았습니다(연출 중 무적 등). 연출이 끝난 뒤 다시 누르세요.", boss);
                return;
            }

            Debug.LogWarning("[DebugRaidDirectPlay] 페이즈 전환(BossPhaseTransition)이 달린 살아 있는 보스가 없습니다 — " +
                             "보스가 아직 스폰 전이거나 이미 전환이 끝났습니다.", this);
        }
    }
}
#endif
