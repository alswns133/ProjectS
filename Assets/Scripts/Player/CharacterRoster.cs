using UnityEngine;
using UnityEngine.Serialization;


namespace ProjectS.Players
{
    /// <summary>
    /// 로스터 한 칸. 캐릭터 프리팹과 그 캐릭터의 UI 아트를 한 줄로 묶는다.
    /// 그림을 프리팹 옆에 두는 이유는, 캐릭터가 늘 때 어느 칸을 빠뜨렸는지 한눈에 보이게 하기 위함이다.
    /// </summary>
    [System.Serializable]
    public class PlayerType
    {
        [Tooltip("네트워크 컴포넌트가 붙은 캐릭터 프리팹.")]
        public Player character;

        [Tooltip("캐릭터 일러스트. 장비창 전신 그림과 대화창 초상화가 같이 쓴다.")]
        [FormerlySerializedAs("sprite")]
        public Sprite illust;

        [Tooltip("직업 심볼 아이콘. HUD와 장비창 헤더가 쓴다.")]
        public Sprite symbol;

        public Sprite portrait;
    }

    /// <summary>
    /// 캐릭터 프리팹·UI 아트의 단일 소스.
    /// 마을=PlayerManager 로컬 생성 / 레이드=PartyManager 서버 스폰이 같은 이 에셋을 참조하고,
    /// 장비창·대화창·HUD는 <see cref="PlayerManager.Roster"/>를 거쳐 캐릭터별 그림을 꺼낸다.
    /// ScriptableObject라 서버·클라 어디서나 있어(PlayerManager 의존 X) 서버 코드도 안전.
    /// </summary>
    [CreateAssetMenu(menuName = "ProjectS/Character Roster", fileName = "CharacterRoster")]
    public class CharacterRoster : ScriptableObject
    {
        [Tooltip("캐릭터 한 명당 한 칸. 배열 순서는 상관없다 — 조회는 프리팹의 PlayerStats.CharacterId로 한다.")]
        [SerializeField] private PlayerType[] characters;

        /// <summary>등록된 캐릭터 칸 수.</summary>
        public int Count => characters?.Length ?? 0;

        /// <summary>characterType(=PlayerStats.CharacterId)으로 찾는다. 배열 순서 무관. 없으면 null.</summary>
        /// <param name="characterType">캐릭터 타입(1=검사, 2=거너 …)</param>
        public Player GetByType(int characterType) => Find(characterType)?.character;

        /// <summary>characterType에 맞는 캐릭터 일러스트(장비창 전신·대화창 초상화 공용). 없으면 null.</summary>
        /// <param name="characterType">캐릭터 타입(1=검사, 2=거너 …)</param>
        public Sprite GetIllust(int characterType) => Find(characterType)?.illust;

        /// <summary>characterType에 맞는 직업 심볼(HUD·장비창 헤더). 없으면 null.</summary>
        /// <param name="characterType">캐릭터 타입(1=검사, 2=거너 …)</param>
        public Sprite GetSymbol(int characterType) => Find(characterType)?.symbol;

        public Sprite GetPortrait(int characterType) => Find(characterType)?.portrait;

        /// <summary>배열 순서 그대로 index번째 프리팹. 부트스트랩 임시 선택용이라 characterType과 무관하다.</summary>
        /// <param name="index">배열 인덱스(0부터)</param>
        public Player GetByIndex(int index)
            => (characters == null || index < 0 || index >= characters.Length) ? null : characters[index]?.character;

        /// <summary>등록된 프리팹 전체(빈 칸은 null). Mirror 스폰 목록 등록처럼 목록이 통째로 필요할 때 쓴다.</summary>
        public Player[] GetByList()
        {
            if (characters == null) return null;

            Player[] players = new Player[Count];

            for (int i = 0; i < players.Length; i++)
            {
                players[i] = GetByIndex(i);
            }

            return players;
        }

        // characterType으로 칸을 찾는 유일한 규칙. 프리팹·일러스트·심볼 조회가 모두 이 함수를 지난다.
        // 배열 인덱스로 직접 찾지 않는 이유는, 0-base와 1-base를 오가다 아이콘과 얼굴이 어긋나는 사고를
        // 막기 위함이다(실제로 UI 세 곳에서 같은 실수가 났다). 등록 순서가 바뀌어도 결과가 변하지 않는다.
        private PlayerType Find(int characterType)
        {
            if (characters == null) return null;

            foreach (PlayerType t in characters)
            {
                if (t == null || t.character == null) continue;

                PlayerStats stats = t.character.GetComponent<PlayerStats>();
                if (stats != null && stats.CharacterId == characterType) return t;
            }

            return null;
        }
    }
}
