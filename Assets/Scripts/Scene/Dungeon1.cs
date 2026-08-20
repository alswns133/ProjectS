namespace ProjectS.Scenes
{
    /// <summary>1번 던전 씬. 공통 흐름은 DungeonGather가 처리하고, 이 씬은 던전 번호만 선언한다.</summary>
    public class Dungeon1 : DungeonGather
    {
        protected override int DungeonNumber => 1;
    }
}
