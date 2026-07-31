using UnityEngine;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// NPC 상호작용 화면 뷰의 공통 뼈대(허브/퀘스트리스트가 상속). 씬에 하나만 두는 공유 뷰라,
    /// 특정 NPC에 직접 붙지 않고 <see cref="NpcInteractionController.ActiveChanged"/>로 지금 상호작용 중인
    /// 컨트롤러에 붙었다 뗀다. 그 컨트롤러의 화면이 자기 화면(<see cref="Screen"/>)이 될 때만 켜진다.
    ///
    /// 배치: 이 스크립트는 <b>항상 켜진 오브젝트</b>에 두고, 껐다 켜는 실제 패널은 <see cref="root"/>에 따로 연결한다
    /// (스크립트가 붙은 오브젝트가 꺼지면 이벤트 구독이 풀려 화면이 다시 안 뜬다).
    /// </summary>
    public abstract class NpcScreenViewBase : MonoBehaviour
    {
        [Tooltip("껐다 켜는 화면 패널. 이 스크립트는 항상 켜진 오브젝트에 두고 root만 토글한다.")]
        [SerializeField] protected GameObject root;

        private NpcInteractionController controller;

        /// <summary>지금 붙어 있는 컨트롤러(상호작용 중이 아니면 null).</summary>
        protected NpcInteractionController Controller => controller;

        /// <summary>이 뷰가 담당하는 화면. 컨트롤러가 이 화면이 되면 켜지고 아니면 꺼진다.</summary>
        protected abstract NpcScreen Screen { get; }

        protected virtual void Awake()
        {
            if (root != null) root.SetActive(false);
        }

        protected virtual void OnEnable()
        {
            NpcInteractionController.ActiveChanged += OnActiveChanged;
            OnActiveChanged(NpcInteractionController.Active);   // 이미 상호작용 중이면 즉시 붙는다
        }

        protected virtual void OnDisable()
        {
            NpcInteractionController.ActiveChanged -= OnActiveChanged;
            Unbind();
            Hide();
        }

        private void OnActiveChanged(NpcInteractionController active)
        {
            Unbind();
            controller = active;

            if (controller != null)
                controller.ScreenChanged += OnScreenChanged;
            else
                Hide();
        }

        private void Unbind()
        {
            if (controller != null) controller.ScreenChanged -= OnScreenChanged;
            controller = null;
        }

        private void OnScreenChanged(NpcScreen screen)
        {
            if (screen == Screen) Show();
            else Hide();
        }

        // 켠다: 데이터를 채운 뒤(OnShow) 패널을 켜고 입력을 연다.
        private void Show()
        {
            if (controller == null) return;
            OnShow();
            if (root != null) root.SetActive(true);
            EnableInput(true);
        }

        // 끈다: 입력을 닫고 패널을 끈 뒤 뒷정리(OnHide).
        private void Hide()
        {
            EnableInput(false);
            if (root != null) root.SetActive(false);
            OnHide();
        }

        /// <summary>패널을 켜기 직전 데이터 채우기(이름·인사말·목록 등). 서브클래스가 구현.</summary>
        protected virtual void OnShow() { }

        /// <summary>패널을 끈 직후 뒷정리. 필요 없으면 비워 둔다.</summary>
        protected virtual void OnHide() { }

        /// <summary>이 화면의 단축키 입력을 켜고 끈다(짝 맞춰 구독/해제). 서브클래스가 구현.</summary>
        /// <param name="enable">켜면 true</param>
        protected abstract void EnableInput(bool enable);
    }
}
