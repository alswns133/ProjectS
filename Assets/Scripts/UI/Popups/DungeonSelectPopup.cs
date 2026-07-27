using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 마을 던전 게이트에 근접하면 뜨는 던전 선택 팝업. 지금은 전용 UI가 없어 <b>버튼 하나</b>로 시작하고,
    /// 누르면 던전(<see cref="DungeonGather"/>) 씬으로 전환한다. 전환 이후 플레이어 이동·활성화·던전 모드는
    /// PlayerManager/DungeonGather.Enter가 처리하므로 여기는 "닫고 → 씬 전환 요청"만 한다.
    ///
    /// 던전이 늘면 이 버튼 하나를 목록(엔트리 프리팹)으로 확장하고, 선택한 dungeonId를 세션에 실어보내
    /// 던전 씬에서 그 id로 몬스터/레이아웃을 로드하는 방식으로 바꾼다(캐릭터 선택과 같은 패턴).
    /// </summary>
    public class DungeonSelectPopup : BasePopup
    {
        [SerializeField] private Button enterButton;

        // 버튼 연결은 최초 1회만. BasePopup이 OnInit을 1회만 호출해 주므로 중복 구독이 쌓이지 않는다.
        protected override void OnInit()
        {
            if (enterButton != null) enterButton.onClick.AddListener(EnterDungeon);
        }

        // 마우스로 버튼을 누르려면 커서가 풀려 있어야 한다(플레이 중엔 커서가 잠겨 숨겨져 있음).
        protected override void OnShow() => SetCursorFree(true);

        // 닫힐 때(입장/이탈 모두) 커서를 다시 잠가 플레이 조작으로 복귀시킨다.
        protected override void OnHide() => SetCursorFree(false);

        private void EnterDungeon()
        {
            // 먼저 팝업을 닫아 커서를 원복(OnHide)하고, 그 다음 씬 전환을 요청한다.
            RequestClose();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<DungeonGather>();
        }

        private void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
