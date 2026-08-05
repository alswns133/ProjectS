using UnityEngine;
using ProjectS.Events;
using ProjectS.Managers;
using ProjectS.UI.Framework;

namespace ProjectS.UI
{
    /// <summary>
    /// 플레이어 사망을 듣고 <see cref="DeathPopupPrototype"/>을 여는 프리젠터.
    /// </summary>
    /// <remarks>
    /// 팝업이 직접 구독하지 못하는 이유가 이 클래스의 존재 이유다. <see cref="BasePopup"/>은 닫힐 때
    /// GameObject를 꺼버려서, 닫혀 있는 동안에는 사망 이벤트를 받을 수 없다. 그래서 <b>항상 켜져 있는</b>
    /// 이 오브젝트가 대신 듣는다 — HUD 루트처럼 꺼지지 않는 곳에 붙여야 한다.
    ///
    /// 등록까지 여기서 하는 것도 같은 이유다. UIManager는 <b>자기 자식에서만</b> 팝업을 수집하는데
    /// 이 팝업은 HUD 씬에 있으므로, 누군가 <c>RegisterPopup</c>으로 넣어 주지 않으면 ShowPopup이
    /// "팝업이 없음" 경고만 남기고 끝난다. (QuestTrackerHud ↔ QuestDetailPopup과 같은 구조)
    ///
    /// 붙이는 것을 잊으면 죽어도 아무 창이 뜨지 않고, 플레이어는 사망 모션 상태로 조작이 막힌 채 갇힌다
    /// (<c>PlayerDeadState</c>가 스스로 빠져나오지 않는 막다른 상태이기 때문).
    /// </remarks>
    public class DeathPresenter : BasePresenter
    {
        [Tooltip("사망 시 열 팝업. 꺼져 있어도 되며, 이 프리젠터가 UIManager에 등록시킨다.")]
        [SerializeField] private DeathPopupPrototype deathPopup;

        protected override void Subscribe()
        {
            PlayerEvents.OnPlayerDied += OnPlayerDied;
            WarnIfInsidePanel();
        }

        /// <remarks>
        /// 이 프리젠터가 BasePanel 아래에 있으면 <b>패널이 닫힌 동안 사망을 듣지 못한다</b>.
        /// BasePanel.Hide는 GameObject를 통째로 끄고, 그러면 OnDisable → 구독 해제가 되기 때문이다.
        /// 증상이 "죽어도 아무 창이 안 뜨고 경고도 없음"이라 원인을 찾기 매우 어려워, 켜지는 시점에 미리 알린다.
        /// (팝업 자체도 같은 이유로 패널 밖에 있어야 한다 — 부모가 꺼져 있으면 Show해도 화면에 안 나온다.)
        /// </remarks>
        private void WarnIfInsidePanel()
        {
            BasePanel owner = GetComponentInParent<BasePanel>(true);
            if (owner == null) return;

            Debug.LogWarning(
                $"{name}: 이 프리젠터가 패널('{owner.name}') 아래에 있습니다. " +
                "패널이 닫히면 구독이 끊겨 사망 팝업이 뜨지 않습니다. " +
                "UIManager 바로 아래처럼 항상 켜져 있는 곳으로 옮기세요(팝업도 함께).", this);
        }

        protected override void Unsubscribe()
        {
            PlayerEvents.OnPlayerDied -= OnPlayerDied;

            // 씬이 내려갈 때 등록을 풀지 않으면, 씬을 넘어 살아남는 UIManager가 파괴된 팝업을 계속 들고 있게 된다.
            UIManager.Instance?.UnregisterPopup(deathPopup);
        }

        private void OnPlayerDied()
        {
            if (deathPopup == null)
            {
                Debug.LogWarning($"{name}: deathPopup이 비어 있어 사망 팝업을 열 수 없다.", this);
                return;
            }

            UIManager manager = UIManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning($"{name}: UIManager가 없어 사망 팝업을 열 수 없다. " +
                                 "Bootstrap에서 시작하거나 이 씬에 UIManager를 추가하세요.", this);
                return;
            }

            // 등록은 멱등이라 죽을 때마다 불러도 안전하다. Awake 순서에 기대지 않기 위해 여는 시점에 확인한다.
            manager.RegisterPopup(deathPopup);

            // 이미 떠 있는데 다시 부르면 UIManager의 활성 목록에 중복으로 쌓인다.
            if (!deathPopup.IsVisible) manager.ShowPopup<DeathPopupPrototype>();
        }
    }
}
