using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.Managers;
using ProjectS.UI;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 초대 목록 팝업(<see cref="PartyInvitePopup"/>)을 키 하나로 열고 닫는 테스트용 컴포넌트.
    /// 씬 아무 GameObject에 붙이고 플레이한 뒤 <see cref="openKey"/>(기본 P)를 누른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이게 필요한 이유: 팝업은 <b>던전 입장 창의 빈 파티 슬롯</b>이 열어 주는데 그 슬롯을 아직 안 만들었다.
    /// 그래서 지금은 팝업을 띄울 길이 전혀 없어, 더미 데이터를 넣어도 화면에 아무것도 안 나온다.
    /// </para>
    /// <para>
    /// <b>하이어라키에서 팝업 오브젝트를 직접 켜는 것으로는 안 된다.</b> 카드는 <c>OnShow</c>에서 만들어지고
    /// 그 호출은 <c>UIManager.ShowPopup</c>을 거쳐야 일어난다. 오브젝트만 켜면 그 경로를 건너뛰어
    /// 켜졌는데도 목록이 비어 있는, 원인 찾기 어려운 상태가 된다.
    /// </para>
    /// <para>
    /// 파티 슬롯이 붙고 나면 이 파일은 지운다.
    /// </para>
    /// </remarks>
    public class DebugPartyInviteKey : MonoBehaviour
    {
        [Tooltip("팝업을 열고 닫을 키.")]
        [SerializeField] private Key openKey = Key.P;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[openKey].wasPressedThisFrame) return;

            if (UIManager.Instance == null)
            {
                Debug.LogWarning("[DebugPartyInviteKey] 씬에 UIManager가 없다. 팝업을 열 수 없다.");
                return;
            }

            if (UIManager.Instance.IsPopupOpen<PartyInvitePopup>())
            {
                UIManager.Instance.ClosePopup<PartyInvitePopup>();
                return;
            }

            UIManager.Instance.ShowPopup<PartyInvitePopup>();
        }
    }
}
