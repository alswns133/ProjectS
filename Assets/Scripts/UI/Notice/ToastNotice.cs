using TMPro;
using UnityEngine;
using ProjectS.Events;

namespace ProjectS.UI
{
    /// <summary>
    /// 짧게 떴다 사라지는 안내 토스트. <see cref="UIEvents.OnToast"/>를 구독해 어떤 시스템이든
    /// 문구만 던지면 이 뷰가 표시한다(예: 강화 재료 부족). 버튼 없이 <see cref="TimedNoticeView.HoldSeconds"/>초
    /// 뒤 스스로 사라지므로 패널 스택·포커스 관리 대상이 아니다(LevelUpNotice와 같은 결).
    /// </summary>
    /// <remarks>
    /// <b>씬에서 활성(켜 둔) 상태로 배치할 것.</b> 구독은 <see cref="Awake"/>에서 한 번 일어나므로 오브젝트가
    /// 처음부터 켜져 있어야 한다(<see cref="TimedNoticeView.Awake"/>가 알파를 0으로 내려 켜 둬도 안 보인다).
    /// <para>
    /// <b>★구독을 OnEnable/OnDisable에 두지 않는 이유(버그 방지):</b> 이 알림은 재생이 끝나면 베이스가
    /// <c>SetActive(false)</c>로 스스로 꺼진다. 구독을 <c>OnDisable</c>에서 해제하면 <b>첫 토스트가 사라지는
    /// 순간 구독이 끊겨 이후 토스트를 하나도 못 받는다.</b> 그래서 GameObject 활성 상태와 무관하게 살아 있도록
    /// <see cref="Awake"/>에서 구독하고 <see cref="OnDestroy"/>에서만 해제한다. 꺼진 동안 요청이 와도
    /// <see cref="Show"/>가 <see cref="TimedNoticeView.Play"/>로 오브젝트를 다시 켜므로 안전하다.
    /// </para>
    /// </remarks>
    public class ToastNotice : TimedNoticeView
    {
        [SerializeField] private TMP_Text messageText;

        protected override void Awake()
        {
            base.Awake();
            UIEvents.OnToast += Show;
        }

        private void OnDestroy()
        {
            UIEvents.OnToast -= Show;
        }

        /// <summary>
        /// 토스트 문구를 채우고 재생한다. 재생 중 새 요청이 오면 최신 문구로 갈아끼워 다시 재생한다.
        /// </summary>
        /// <param name="message">표시할 문구</param>
        public void Show(string message)
        {
            if (messageText != null) messageText.text = message;
            Play();
        }
    }
}
