using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI;

namespace ProjectS.Networking
{
    /// <summary>
    /// 초대 신호를 상시 듣고 있다가 받는 쪽 팝업(<see cref="PartyInviteRequestPopup"/>)을 띄우는 연결자.
    /// 팝업은 닫혀 있는 동안 비활성이라 스스로 신호를 들을 수 없어, 항상 켜져 있는 이 컴포넌트가 대신 듣는다
    /// (<see cref="Debugging"/>의 디버그 키가 팝업을 여는 것과 같은 방식).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>계층 경계.</b> 팝업(UI)은 네트워크를 모르고 답을 <see cref="PartyInviteRequestPopup.OnAnswer"/>로만
    /// 흘린다. 이 연결자가 그 답을 <see cref="PartyManager.Local"/>의 Command로 넘긴다 —
    /// 슬롯이 초대 팝업의 요청을 받아 소스로 넘기는 것(<c>PartySlotBar</c>)과 같은 분리다.
    /// 그래서 이 파일만 UI·Networking 양쪽을 알고, 팝업/매니저는 서로를 모른다.
    /// </para>
    /// <para>
    /// 마을·던전 씬에 항상 켜진 오브젝트로 하나 둔다(UIManager 근처가 자연스럽다).
    /// </para>
    /// </remarks>
    public class PartyInvitePrompter : MonoBehaviour
    {
        private void OnEnable()  => PartyEvents.OnInviteReceived += HandleInvite;
        private void OnDisable() => PartyEvents.OnInviteReceived -= HandleInvite;

        private void HandleInvite(PartyInviteOffer offer)
        {
            if (UIManager.Instance == null)
            {
                Debug.LogWarning("[PartyInvitePrompter] UIManager가 없어 초대 팝업을 열 수 없다.", this);
                return;
            }

            PartyInviteRequestPopup popup = UIManager.Instance.GetPopup<PartyInviteRequestPopup>();
            if (popup == null)
            {
                Debug.LogWarning("[PartyInvitePrompter] PartyInviteRequestPopup이 UIManager에 등록돼 있지 않다.", this);
                return;
            }

            // TODO(겹침): 이미 다른 초대 팝업이 떠 있을 때의 정책(뒤엣것 무시 / 최신으로 갈아치우기)을 정한다.
            //             지금은 최신 초대로 내용만 갈아끼운다(마지막이 이긴다).
            popup.SetOffer(offer);        // 열기 전에 내용을 먹인다(OnShow가 읽으므로 순서 중요).

            // 구독을 매번 대칭으로 갈아 끼운다 — 팝업은 이 연결자보다 오래 살아 구독이 겹치면 답이 두 번 나간다.
            popup.OnAnswer -= OnPopupAnswer;
            popup.OnAnswer += OnPopupAnswer;

            UIManager.Instance.ShowPopup<PartyInviteRequestPopup>();
        }

        // 팝업이 정한 답(수락/거절)을 서버로 넘긴다. 실제 판정·성립은 서버가 한다.
        private void OnPopupAnswer(PartyInviteOffer offer, bool accept)
        {
            if (PartyManager.Local != null)
                PartyManager.Local.AnswerInvite(offer.inviterNetId, accept);
        }
    }
}
