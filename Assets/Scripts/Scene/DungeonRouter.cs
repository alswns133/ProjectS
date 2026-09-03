using UnityEngine;
using ProjectS.Managers;
using ProjectS.UI;

namespace ProjectS.Scenes
{
    /// <summary>
    /// "이 던전 ID로 입장한다"를 실제 씬 전환으로 옮기는 단 한 곳. 입장 UI는 무엇을 골랐는지만 알고,
    /// 어느 씬으로 가는지는 여기서만 정한다.
    ///
    /// <para>
    /// <b>던전 = 씬 / 난이도 = 몬스터</b> 로 축이 갈린다.
    /// 던전 ID 2자리(<c>[던전1][난이도1]</c>) 중 <b>앞자리만 씬을 고르고</b>, 뒷자리(난이도)는 씬을 바꾸지 않는다.
    /// 난이도는 같은 씬 안에서 <see cref="DungeonContext.ResolveMonsterId"/>가 몬스터 ID로 옮겨 준다.
    /// 그래서 던전이 늘면 여기 <c>case</c> 하나만 늘고, 난이도가 늘면 여기는 손대지 않는다.
    /// </para>
    /// <para>
    /// 씬 로드 스텝을 이 함수 하나로 모아 둔 것은 멀티 대비이기도 하다
    /// (docs/DUNGEON_AND_MULTIPLAYER.md §6 — 로컬 <c>SceneManager</c> → 미러 <c>ServerChangeScene</c>으로
    /// 갈아끼울 지점을 함수 경계로 분리). 여러 곳에서 직접 <c>RequestSceneChange</c>를 부르기 시작하면
    /// 그 전환 비용이 호출부 수만큼 늘어난다.
    /// </para>
    /// </summary>
    public static class DungeonRouter
    {
        /// <summary>
        /// 던전 ID를 세션에 싣고 해당 던전 씬으로 전환을 요청한다.
        /// </summary>
        /// <remarks>
        /// 세션을 먼저 채우는 순서가 중요하다 — 던전 씬의 <c>Enter</c>가 로드 직후
        /// <c>GameSession.SelectedDungeonId</c>를 읽어 <see cref="DungeonContext"/>에 반영하기 때문이다.
        /// 순서가 뒤집히면 던전 씬이 인스펙터 폴백 ID로 시작한다.
        /// </remarks>
        /// <param name="mode">던전인지 레이드인지</param>
        /// <param name="dungeonId">2자리 던전 ID(docs/ID_NUMBERING.md §4)</param>
        /// <param name="dungeonName">결과 화면에 찍을 던전 표시 이름. 입장 화면만 알고 있어 여기로 넘긴다.
        /// null이면 세션의 기존 이름을 유지한다(재도전은 ID만 들고 다시 들어오므로 이름을 지우지 않기 위함).</param>
        /// <returns>전환을 요청했으면 true. 갈 씬이 없으면 false</returns>
        public static bool Enter(EntryMode mode, int dungeonId, string dungeonName = null)
        {
            GameSession.SetSelectedDungeon(dungeonId, dungeonName);

            // ★ 씬을 로드하기 전에 컨텍스트를 먼저 채운다. 순서 때문이다.
            //   GameSceneManager는 "새 씬 오브젝트의 Start까지 완료된 뒤" 씬의 Enter를 부른다.
            //   즉 몬스터의 EnemyStats.Start(→ MonsterStatTable 조회)가 DungeonGather.Enter보다 먼저 돈다.
            //   Enter에서만 채우면 몬스터가 스탯을 읽는 순간엔 CurrentDungeonId가 아직 0(마을)이라
            //   ResolveMonsterId가 난이도를 못 입힌다. static이라 씬을 넘어 살아남으므로 여기서 채워두면
            //   몬스터가 깨어날 때 이미 올바른 값이 있다.
            //   (DungeonGather.Enter의 SetDungeon은 그대로 둔다 — 직접 씬 테스트 진입 경로의 폴백이다.)
            DungeonContext.SetDungeon(dungeonId);

            if (GameSceneManager.Instance == null)
            {
                Debug.LogWarning("[DungeonRouter] GameSceneManager가 없어 전환하지 못함");
                return false;
            }

            if (mode == EntryMode.Raid)
            {
                // 레이드는 최종 단일 컨텐츠(ID_NUMBERING §4 = 99)라 지금은 씬 하나로 고정 분기한다.
                // 레이드가 여러 개가 되면 던전처럼 DungeonNumberOf로 switch를 넓힌다.
                GameSceneManager.Instance.RequestSceneChange<Raid>();
                return true;
            }

            // ★ 던전이 늘면 여기 case 하나만 추가한다. 난이도는 여기서 보지 않는다.
            //   씬 클래스 이름 = 씬 파일 이름 규약(docs/DUNGEON_AND_MULTIPLAYER.md §1)이라,
            //   타입을 적는 것만으로 씬이 지정된다(오타는 컴파일러가 잡는다).
            switch (DungeonNumberOf(dungeonId))
            {
                case 1:
                    GameSceneManager.Instance.RequestSceneChange<Dungeon1>();
                    return true;
                case 2:
                    GameSceneManager.Instance.RequestSceneChange<Dungeon2>();
                    return true;

                default:
                    Debug.LogWarning($"[DungeonRouter] 던전 번호 {DungeonNumberOf(dungeonId)}에 연결된 씬이 없다 (id {dungeonId})");
                    return false;
            }
        }

        /// <summary>던전 ID에서 던전 번호(앞자리)를 뽑는다.</summary>
        /// <param name="dungeonId">2자리 던전 ID</param>
        /// <returns>던전 번호</returns>
        public static int DungeonNumberOf(int dungeonId) => dungeonId / 10;

        /// <summary>던전 ID에서 난이도(뒷자리)를 뽑는다.</summary>
        /// <param name="dungeonId">2자리 던전 ID</param>
        /// <returns>난이도 값(1=노말 · 2=하드 · 3=매니악)</returns>
        public static int DifficultyOf(int dungeonId) => dungeonId % 10;
    }
}
