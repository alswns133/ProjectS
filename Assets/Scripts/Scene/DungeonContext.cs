using UnityEngine;

namespace ProjectS.Scenes
{
    /// <summary>
    /// "지금 어느 던전에 있는가"를 담는 전역 상태. 던전이 씬별로 나뉘어 있어, 각 던전 씬이 진입/이탈 시
    /// 자기 dungeonId를 여기 세팅/해제한다(마을은 0 = 던전 밖).
    /// 퀘스트 나침반이 "목표 던전에 도착했는가"를 이 값으로 판정한다 — 도착하면 화살표 대신 정적 아이콘으로 바꾸고
    /// 거리를 숨긴다. 이 값이 갱신되지 않으면 던전에 들어와도 계속 게이트를 가리켜 나침반이 어긋난다.
    /// </summary>
    public static class DungeonContext
    {
        /// <summary>현재 던전 ID. 0이면 마을 등 던전 밖.</summary>
        public static int CurrentDungeonId { get; private set; }

        /// <summary>현재 던전의 던전 번호(ID 앞자리). 던전 밖이면 0. 씬이 갈리는 축이다.</summary>
        public static int DungeonNumber => CurrentDungeonId / 10;

        /// <summary>현재 난이도(ID 뒷자리 · 1=노말 · 2=하드 · 3=매니악). 던전 밖이면 0. 몬스터가 갈리는 축이다.</summary>
        public static int Difficulty => CurrentDungeonId % 10;

        /// <summary>
        /// 씬에 배치된 몬스터의 기준 ID에 "지금 입장한 던전·난이도"를 입힌다.
        /// </summary>
        /// <remarks>
        /// 던전은 <b>씬으로</b> 갈리고 난이도는 <b>몬스터로</b> 갈리는 구조라, 같은 씬 하나가 노말·하드·매니악을
        /// 모두 굴린다. 그게 가능한 이유는 ID 체계가 그렇게 짜여 있어서다 —
        /// 몬스터 ID는 <c>[던전1][난이도1][순번2]</c>(docs/ID_NUMBERING.md §3)이고 던전 ID는
        /// <c>[던전1][난이도1]</c>(§4)라, <b>몬스터 ID의 앞 2자리가 곧 던전 ID</b>다.
        /// 그래서 씬의 몬스터는 순번(뒤 2자리)만 자기 것으로 갖고, 앞 2자리는 입장 시점에 갈아끼우면 된다.
        ///
        /// 난이도별로 씬을 세 벌 만들지 않기 위한 이음매이므로, 몬스터 스탯을 읽는 쪽(EnemyStats 등)에서
        /// 테이블 조회 직전에 한 번 통과시키면 된다. 던전 밖(0)이면 원본을 그대로 돌려주므로
        /// 마을이나 직접 씬 테스트에서는 인스펙터 값이 그대로 쓰인다.
        /// </remarks>
        /// <param name="baseMonsterId">씬/프리팹에 박혀 있는 기준 몬스터 ID</param>
        /// <returns>현재 던전·난이도가 반영된 몬스터 ID</returns>
        public static int ResolveMonsterId(int baseMonsterId)
        {
            if (CurrentDungeonId <= 0) return baseMonsterId;

            return CurrentDungeonId * 100 + Mathf.Abs(baseMonsterId) % 100;
        }

        /// <summary>던전 씬 진입 시 그 던전 ID로 세팅한다.</summary>
        /// <param name="dungeonId">진입한 던전 ID</param>
        public static void SetDungeon(int dungeonId) => CurrentDungeonId = dungeonId;

        /// <summary>던전 밖(마을 등)으로 나갈 때 0으로 되돌린다.</summary>
        public static void ClearDungeon() => CurrentDungeonId = 0;

        // 플레이 모드 리로드 후 이전 플레이 값이 남지 않게 초기화한다(static 리셋 방침).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => CurrentDungeonId = 0;
    }
}
