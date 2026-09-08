using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ProjectS.Events;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 초대를 <b>받는</b> 쪽 팝업. "OO님이 파티에 초대했습니다 [수락][거절]"과 남은 시간을 보여준다.
    /// 보내는 쪽(<see cref="PartyInvitePopup"/>)과 짝을 이루는 창이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>이 팝업은 네트워크를 모른다.</b> 초대 정보(<see cref="PartyInviteOffer"/>)는 <see cref="SetOffer"/>로
    /// 받아 두고, 답은 <see cref="OnAnswer"/>로 밖(프롬프터)에 알린다 — 실제 수락/거절 Command는 프롬프터가
    /// <c>PartyManager.Local</c>로 보낸다. UI가 결과를 지어내지 않고 서버 판정을 기다리는 원칙(§10)과 같다.
    /// </para>
    /// <para>
    /// <b>답 없이 닫히면(타임아웃·ESC) 거절로 친다.</b> 대답을 미룬 채 창만 사라지면 초대자는 영영
    /// "초대 중…"에 갇힌다. <see cref="answered"/>로 한 번만 답하도록 막는다.
    /// </para>
    /// </remarks>
    public class PartyInviteRequestPopup : BasePopup
    {
        /// <summary>답을 정했다(수락/거절). 인자는 수락이면 true. 프롬프터가 받아 서버로 넘긴다.</summary>
        public event System.Action<PartyInviteOffer, bool> OnAnswer;

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button declineButton;

        [Tooltip("답이 없으면 자동 거절되는 시간(초). 서버 타임아웃보다 짧게 둬야 화면과 서버가 어긋나지 않는다.")]
        [SerializeField, Min(1f)] private float timeoutSeconds = 20f;

        private PartyInviteOffer offer;
        private float remaining;
        private bool answered;

        /// <summary>
        /// 띄우기 전에 초대 내용을 넣는다. <c>ShowPopup</c> <b>전에</b> 부른다(안 그러면 OnShow가 빈 내용으로 그린다).
        /// </summary>
        /// <param name="inviteOffer">배달돼 온 초대 한 건</param>
        public void SetOffer(PartyInviteOffer inviteOffer)
        {
            offer = inviteOffer;
        }

        protected override void OnInit()
        {
            if (acceptButton != null) acceptButton.onClick.AddListener(() => Answer(true));
            if (declineButton != null) declineButton.onClick.AddListener(() => Answer(false));
        }

        protected override void OnShow()
        {
            answered = false;
            remaining = timeoutSeconds;

            if (messageText != null)
            {
                string who = string.IsNullOrWhiteSpace(offer.inviterName) ? "상대" : offer.inviterName;
                messageText.text = $"{who}님이 파티에 초대했습니다";
            }

            UpdateTimerLabel();
        }

        protected override void OnHide()
        {
            // 버튼/타임아웃으로 이미 답했으면 여기선 아무것도 안 한다(닫기는 그쪽이 이미 시작함 — 재진입 방지).
            // 답 없이 닫혔다면(ESC 등) 거절로만 알린다. 닫기를 다시 부르지 않는다(지금 닫히는 중).
            if (answered) return;

            answered = true;
            OnAnswer?.Invoke(offer, false);
        }

        private void Update()
        {
            if (!IsVisible || answered) return;

            remaining -= Time.unscaledDeltaTime;   // UI 대기라 timeScale 영향을 받지 않게 unscaled
            if (remaining <= 0f)
            {
                Answer(false);   // 타임아웃 = 자동 거절
                return;
            }

            UpdateTimerLabel();
        }

        // 버튼·타임아웃 경로. 답을 알리고 창을 닫는다(닫히면 OnHide가 돌지만 answered라 다시 답하지 않는다).
        private void Answer(bool accept)
        {
            if (answered) return;
            answered = true;

            OnAnswer?.Invoke(offer, accept);

            // 내 화면에서는 먼저 닫는다. 성립 여부는 서버가 partyId 복제로 슬롯에 반영한다.
            RequestClose();
        }

        private void UpdateTimerLabel()
        {
            if (timerText != null) timerText.text = $"{Mathf.CeilToInt(remaining)}초";
        }
    }
}
