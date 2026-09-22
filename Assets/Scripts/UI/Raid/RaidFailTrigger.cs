using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 레이드 실패(<see cref="RaidFailEvents.OnFailed"/>)를 받아 <see cref="RaidFailPopup"/>을 띄운다.
    /// 팝업 GameObject는 평소 비활성이라 스스로 이벤트를 구독할 수 없으므로, 씬을 넘어 상주하는 이 컴포넌트가
    /// 대신 구독한다(<see cref="DeathPopupTrigger"/>와 같은 방침). UIManager 오브젝트에 붙인다.
    /// </summary>
    public class RaidFailTrigger : MonoBehaviour
    {
        // 구독/해제는 OnEnable↔OnDisable 짝으로. static 이벤트라 짝을 안 맞추면 중복 구독이 쌓인다.
        private void OnEnable() => RaidFailEvents.OnFailed += ShowFailPopup;
        private void OnDisable() => RaidFailEvents.OnFailed -= ShowFailPopup;

        private void ShowFailPopup(int total, float voteSeconds)
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<RaidFailPopup>()) return;   // 이미 떠 있으면 중복 표시 방지

            // 사망 팝업(부활 기회를 다 쓰고 파티 결과를 기다리던 창)은 역할이 끝났다. 남겨 두면
            // 실패 팝업 뒤에 겹쳐 남아 두 창이 동시에 커서를 만진다.
            ui.ClosePopup<DeathPopup>();

            RaidFailPopup popup = ui.GetPopup<RaidFailPopup>();
            if (popup == null)
            {
                Debug.LogWarning("[RaidFailTrigger] RaidFailPopup을 찾지 못했습니다 — UIManager 아래에 있는지 확인하세요. " +
                                 "생성: Tools ▸ ProjectS ▸ Create Raid Fail Popup");
                return;
            }

            // 투표 정보는 ShowPopup이 인자를 받을 수 없어 미리 넘긴다(OnShow가 이 값으로 화면을 잡는다).
            popup.Prepare(total, voteSeconds);
            ui.ShowPopup<RaidFailPopup>();
        }
    }
}
