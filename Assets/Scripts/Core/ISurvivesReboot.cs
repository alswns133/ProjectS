namespace ProjectS.Core
{
    /// <summary>
    /// 캐릭터 선택 복귀(<c>ProjectS.Managers.SessionReboot</c>)가 DontDestroyOnLoad 오브젝트를
    /// 정리할 때 <b>살아남을</b> 오브젝트를 표시하는 마커. 이 컴포넌트가 하나라도 붙어 있는
    /// 루트 오브젝트는 파괴하지 않는다.
    ///
    /// 대상은 "캐릭터와 무관한 인프라"이면서 <b>앱 시작(AfterSceneLoad)에만 자동 생성되는</b> 것들이다.
    /// 그런 오브젝트는 한 번 파괴하면 다시 만들어 주는 경로가 없어 남은 세션 내내 기능이 죽는다
    /// (특히 <c>AutoSaveTicker</c>가 사라지면 오토세이브가 영구히 멈춘다 — 유실은 조용히 일어난다).
    ///
    /// 반대로 캐릭터·세션 상태를 들고 있는 것(PlayerManager · InventoryManager · QuestManager ·
    /// UIManager · 네트워크 매니저 등)에는 <b>절대 붙이지 않는다.</b> 살려 두면 이전 캐릭터의
    /// 인벤토리·퀘스트가 다음 접속으로 새고, 그 상태가 Firebase 세이브를 덮어쓴다.
    /// </summary>
    public interface ISurvivesReboot
    {
    }
}
