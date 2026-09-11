using UnityEngine;


namespace ProjectS.Players
{
    /// <summary>
    /// 캐릭터 프리팹 단일 소스. 인덱스 = characterType.
    /// 마을=PlayerManager 로컬 생성 / 레이드=PartyManager 서버 스폰이 같은 이 에셋을 참조.
    /// ScriptableObject라 서버·클라 어디서나 있어(PlayerManager 의존 X) 서버 코드도 안전.
    /// </summary>
    [CreateAssetMenu(menuName = "ProjectS/Character Roster", fileName = "CharacterRoster")]
    public class CharacterRoster : ScriptableObject
    {
        [Tooltip("인덱스 = characterType(0=검사, 1=거너 …). 네트워크 컴포넌트 붙은 캐릭터 프리팹.")]
        [SerializeField] private Player[] characters;

        public int Count => characters?.Length ?? 0;

        /// <summary>characterType(=PlayerStats.CharacterId)으로 찾는다. 배열 순서 무관. 없으면 null.</summary>
        public Player GetByType(int characterType)
        {
            if (characters == null) return null;
            foreach (Player c in characters)
            {
                if (c == null) continue;
                PlayerStats stats = c.GetComponent<PlayerStats>();
                if (stats != null && stats.CharacterId == characterType) return c;
            }
            return null;
        }

        public Player GetByIndex(int index)
            => (characters == null || index < 0 || index >= characters.Length) ? null : characters[index];

        public Player[] GetByList()
        {
            if(characters == null) return null;

            Player[] players = new Player[Count];
            
            for(int i = 0; i < players.Length; i++)
            {
                players[i] = GetByIndex(i);
            }

            return players;
        }
    }
}
