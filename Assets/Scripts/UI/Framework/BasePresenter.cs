using UnityEngine;

namespace ProjectS.UI.Framework
{
    public abstract class BasePresenter : MonoBehaviour
    {
        /// <summary>
        /// 패널이 열릴 때 이벤트 구독
        /// </summary>
        protected abstract void Subscribe();

        /// <summary>
        /// 패널이 닫힐 때 이벤트 해제
        /// </summary>
        protected abstract void Unsubscribe();

        /// <summary>
        /// true면 꺼져 있는 동안에도 구독을 유지한다 — OnEnable/OnDisable에서 구독·해제하지 않으므로,
        /// 하위 클래스가 <c>Awake</c>에서 <see cref="Subscribe"/>, <c>OnDestroy</c>에서 <see cref="Unsubscribe"/>를 직접 짝지어 부른다.
        /// </summary>
        /// <remarks>
        /// 연출이 UI 루트를 잠깐 끄는 동안 온 이벤트를 놓치면 안 되는 Presenter용이다(예: 보스 HP 바 —
        /// 페이즈 전환 연출 중 다음 페이즈 등장을 놓치면 바가 이전 페이즈에 붙은 채 안 깎인다).
        /// 기본은 false(기존 동작: 켜질 때 구독, 꺼질 때 해제).
        /// </remarks>
        protected virtual bool KeepSubscribedWhileDisabled => false;

        private void OnEnable()
        {
            if (!KeepSubscribedWhileDisabled) Subscribe();
        }

        private void OnDisable()
        {
            if (!KeepSubscribedWhileDisabled) Unsubscribe();
        }
    }
}
