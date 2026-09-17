using ProjectS.Events;
using ProjectS.UI.Framework;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// <see cref="RaidIntroEvents"/>를 받아 <see cref="RaidWaitView"/>에 옮기는 Presenter.
    /// 네트워크 코드(PartyManager)와 화면이 서로를 모르게 한다.
    /// </summary>
    /// <remarks>
    /// View와 같은 오브젝트(항상 켜 둔 루트)에 붙인다. 루트가 꺼지면 구독도 풀리므로 View는 알파로만 숨는다.
    /// </remarks>
    public class RaidWaitPresenter : BasePresenter
    {
        [Tooltip("대기 화면 View. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private RaidWaitView view;

        private void Awake()
        {
            if (view == null) view = GetComponent<RaidWaitView>();
        }

        /// <inheritdoc/>
        protected override void Subscribe()
        {
            RaidIntroEvents.OnWaitStarted += OnWaitStarted;
            RaidIntroEvents.OnReadyCountChanged += OnReadyCountChanged;
            RaidIntroEvents.OnWaitEnded += OnWaitEnded;
        }

        /// <inheritdoc/>
        protected override void Unsubscribe()
        {
            RaidIntroEvents.OnWaitStarted -= OnWaitStarted;
            RaidIntroEvents.OnReadyCountChanged -= OnReadyCountChanged;
            RaidIntroEvents.OnWaitEnded -= OnWaitEnded;
        }

        private void OnWaitStarted(float showDelay)
        {
            if (view != null) view.BeginWait(showDelay);
        }

        private void OnReadyCountChanged(int ready, int total)
        {
            if (view != null) view.SetCount(ready, total);
        }

        private void OnWaitEnded()
        {
            if (view != null) view.EndWait();
        }
    }
}
