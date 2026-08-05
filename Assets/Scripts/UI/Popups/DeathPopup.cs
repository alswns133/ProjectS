using UnityEngine;
using UnityEngine.UI;
using ProjectS.Managers;
using ProjectS.Scenes;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 사망 시 뜨는 임시 팝업. 버튼 2개(부활·마을 복귀)만 있는 최소 구현이다 —
    /// 나중에 사망 연출·부활 비용·리스폰 지점 선택 같은 UI가 더 붙을 자리다.
    /// 표시는 <see cref="DeathPopupTrigger"/>가 <see cref="ProjectS.Events.PlayerEvents.OnPlayerDied"/>를 받아 띄운다
    /// (팝업 자신은 비활성이라 이벤트를 못 받으므로).
    ///
    /// - 부활: 죽은 자리에서 즉시 부활(HP 회복). "1회성"이라 한 번 쓰면 이후 사망부턴 이 버튼이 비활성이다.
    /// - 마을 복귀: 부활 후 마을 씬으로 전환한다(HP 회복은 부활과 씬 진입이 함께 처리).
    ///
    /// 다른 창과 공존하지 않고 사망 시에만 단독으로 뜨지만, 스택형 패널 흐름(HUD Pause/Resume)에 끼어들지 않도록
    /// 리스트형 <see cref="BasePopup"/>으로 둔다(DungeonSelectPopup과 같은 방침).
    /// </summary>
    public class DeathPopup : BasePopup
    {
        [Header("버튼")]
        [SerializeField] private Button reviveButton;
        [SerializeField] private Button returnToVillageButton;

        // 부활 1회성: 한 번 쓰면 이후 사망부터는 부활 버튼을 막는다(마을 복귀만 가능).
        // 임시 규칙이라 이 플래그 하나로 끝낸다 — 정식화되면 부활 아이템/코인 소모 등으로 대체한다.
        private bool reviveConsumed;

        protected override void OnInit()
        {
            // 버튼 배선은 최초 1회만(BasePopup이 OnInit을 1회만 호출 → 중복 구독이 쌓이지 않는다).
            if (reviveButton != null) reviveButton.onClick.AddListener(OnReviveClicked);
            if (returnToVillageButton != null) returnToVillageButton.onClick.AddListener(OnReturnToVillageClicked);
        }

        protected override void OnShow()
        {
            // 마우스로 버튼을 누르려면 커서를 풀어야 한다(플레이 중엔 커서가 잠겨 숨겨져 있음).
            SetCursorFree(true);

            // 부활을 이미 소모했으면 부활 버튼을 비활성화한다(누를 수 없게).
            if (reviveButton != null) reviveButton.interactable = !reviveConsumed;
        }

        // 닫힐 때(부활/마을 복귀 모두) 커서를 다시 잠가 플레이 조작으로 복귀시킨다.
        protected override void OnHide() => SetCursorFree(false);

        // 죽은 자리에서 즉시 부활. 1회성이라 소모 처리 후 팝업을 닫는다.
        private void OnReviveClicked()
        {
            if (reviveConsumed) return;
            reviveConsumed = true;

            PlayerManager.Instance?.Player?.Revive();
            RequestClose();
        }

        // 부활로 사망 상태를 풀고 마을 씬으로 전환한다. 팝업을 먼저 닫아 커서를 원복(OnHide)한 뒤 전환을 요청한다.
        private void OnReturnToVillageClicked()
        {
            PlayerManager.Instance?.Player?.Revive();
            RequestClose();

            if (GameSceneManager.Instance != null)
                GameSceneManager.Instance.RequestSceneChange<VillageGather>();
        }

        private void SetCursorFree(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
