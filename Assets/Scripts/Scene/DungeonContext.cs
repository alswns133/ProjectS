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
