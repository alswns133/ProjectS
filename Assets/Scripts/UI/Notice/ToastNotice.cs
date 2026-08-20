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
    /// <b>씬에서 활성(켜 둔) 상태로 배치할 것.</b> 이벤트 구독은 <see cref="OnEnable"/>에서 일어나므로,
    /// 오브젝트가 꺼져 있으면 토스트 요청을 받지 못한다. <see cref="TimedNoticeView.Awake"/>가 알파를 0으로
    /// 내려 켜 둬도 보이지 않는 상태로 시작한다(요청이 오면 그때 페이드 인).
    /// </remarks>
    public class ToastNotice : TimedNoticeView
    {
        [SerializeField] private TMP_Text messageText;

        private void OnEnable()
        {
            UIEvents.OnToast += Show;
        }

        // 구독/해제를 대칭으로 맞춘다. 베이스가 OnDisable을 virtual로 열어 두었으므로 override해 base를 부른다.
        protected override void OnDisable()
        {
            UIEvents.OnToast -= Show;
            base.OnDisable();
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
