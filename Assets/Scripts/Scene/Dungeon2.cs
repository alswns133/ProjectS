namespace ProjectS.Scenes
{
    /// <summary>2번 던전 씬. 고유 연출이 필요해지면 OnDungeonEnter/Exit이나 단계 메서드를 재정의한다.</summary>
    public class Dungeon2 : DungeonGather
    {
        protected override int DungeonNumber => 2;
    }
}
