namespace ProjectS.Scenes
{
    /// <summary>
    /// 레이드 씬(Assets/Scenes/GC/Raid.unity). 공통 흐름은 <see cref="RaidGather"/>가 처리하고,
    /// 이 씬은 던전 번호(=9, ID_NUMBERING §4의 레이드 <c>99</c> 앞자리)만 선언한다.
    ///
    /// <para>클래스 이름은 씬 파일 이름과 같아야 한다 — <c>DungeonRouter</c>가 <c>RequestSceneChange&lt;Raid&gt;()</c>로
    /// <c>typeof(T).Name</c>("Raid")을 씬 이름으로 쓰기 때문. 보스 등장 컷신·HP바 등 고유 연출이 필요해지면
    /// <c>OnRaidEnter</c>/<c>OnRaidExit</c>이나 단계 메서드를 재정의한다.</para>
    /// </summary>
    public class Raid : RaidGather
    {
        protected override int DungeonNumber => 9;
    }
}
