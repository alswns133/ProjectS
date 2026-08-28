using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;

namespace ProjectS.UI
{
    /// <summary>
    /// 플레이어 사망(<see cref="PlayerEvents.OnPlayerDied"/>)을 받아 사망 팝업(<see cref="DeathPopup"/>)을 띄운다.
    /// 팝업 GameObject는 평소 비활성이라 스스로 이벤트를 구독할 수 없으므로, 씬을 넘어 상주하는 이 컴포넌트가
    /// 대신 구독한다(<see cref="PopupHotkey"/>와 같은 방침). UIManager 오브젝트에 붙인다.
    /// </summary>
    public class DeathPopupTrigger : MonoBehaviour
    {
        // 구독/해제는 OnEnable↔OnDisable 짝으로. static 이벤트라 짝을 안 맞추면 중복 구독이 쌓인다.
        private void OnEnable() => PlayerEvents.OnPlayerDied += ShowDeathPopup;
        private void OnDisable() => PlayerEvents.OnPlayerDied -= ShowDeathPopup;

        private void ShowDeathPopup()
        {
            UIManager ui = UIManager.Instance;
            if (ui == null) return;

            if (ui.IsPopupOpen<DeathPopup>()) return;   // 이미 떠 있으면 중복 표시 방지

            ui.ShowPopup<DeathPopup>();
        }
    }
}
