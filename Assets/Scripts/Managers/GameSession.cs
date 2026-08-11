using UnityEngine;
using ProjectS.Data;

namespace ProjectS.Managers
{
    /// <summary>
    /// 로그인~접속 사이의 "선택된 캐릭터" 상태를 씬을 넘어 나르는 홀더.
    /// Firebase에 의존하지 않는다 — 로그인/선택 흐름이 세이브를 로드해 여기 담고,
    /// <see cref="PlayerManager"/>·PlayerStats·InventoryManager는 여기만 읽는다.
    /// 이 분리 덕분에 게임플레이 코드는 백엔드(Firebase/Steam/AWS)를 몰라도 된다.
    ///
    /// 값이 비어 있으면(직접 씬 테스트 등 로그인 없이 실행) 각 소비자는 인스펙터 폴백을 그대로 쓴다.
    /// </summary>
    public static class GameSession
    {
        /// <summary>선택창에서 고른(또는 새로 만든) 캐릭터 세이브. 접속 전이면 null.</summary>
        public static CharacterSaveData SelectedCharacter { get; private set; }

        /// <summary>선택된 캐릭터 타입(검사/거너). 세션이 없으면 0 → PlayerManager가 폴백 인덱스로 처리한다.</summary>
        public static int SelectedCharacterType => SelectedCharacter != null ? SelectedCharacter.characterType : 0;

        /// <summary>선택창에서 캐릭터를 고르거나 새로 만든 뒤 접속 직전에 담는다.</summary>
        public static void SetSelectedCharacter(CharacterSaveData data) => SelectedCharacter = data;

        /// <summary>
        /// 진입할(또는 생성 중인) 던전 ID. 던전은 런타임에 자동 생성돼 씬 컨트롤러(DungeonGather)가 인스펙터로 알 수 없으므로,
        /// 던전 선택/생성 코드가 여기 실어두면 던전 씬이 읽어 <see cref="ProjectS.Scenes.DungeonContext"/>에 반영한다
        /// (몬스터/레이아웃 로드 기준과 같은 값). 0이면 미지정 — 직접 씬 테스트로 보고 소비자가 인스펙터 폴백을 쓴다.
        /// </summary>
        public static int SelectedDungeonId { get; private set; }

        /// <summary>던전 선택/생성 시점에 진입할 던전 ID를 담는다.</summary>
        /// <param name="dungeonId">진입할 던전 ID</param>
        public static void SetSelectedDungeon(int dungeonId) => SelectedDungeonId = dungeonId;

        /// <summary>로그아웃·캐릭터 선택창 복귀 시 선택을 비운다.</summary>
        public static void Clear()
        {
            SelectedCharacter = null;
            SelectedDungeonId = 0;
        }

        // 플레이 모드 리로드 후에도 static 값이 남을 수 있어 초기화한다
        // (CLAUDE.md: 남을 수 있는 static 상태는 RuntimeInitializeOnLoadMethod에서 리셋).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SelectedCharacter = null;
            SelectedDungeonId = 0;
        }
    }
}
