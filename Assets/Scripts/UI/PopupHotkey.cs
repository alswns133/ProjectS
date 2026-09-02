using UnityEngine;
using UnityEngine.InputSystem;
using ProjectS.NPCs;

namespace ProjectS.UI
{
    /// <summary>
    /// 키 하나로 팝업 창을 여닫는 공용 핫키. 인벤(I)·장비(P)·스킬(K)이 키·팝업 타입만 다른
    /// 똑같은 코드였어서 셋을 하나로 합쳤다 — 오브젝트마다 <see cref="key"/>와 <see cref="target"/>만 지정한다.
    /// <para>
    /// 여닫기 판정은 <see cref="PopupToggle"/>(아이콘 클릭 <see cref="HudMenuButton"/>과 공용)에 있고,
    /// 이 컴포넌트는 <b>키 입력</b>과 <b>NPC 상호작용 시작 시 창 닫기</b>만 담당한다.
    /// UI 열기는 게임플레이 입력이 아니므로 PlayerInputHandler에 넣지 않고(입력 경계 규칙) 이 전용 컴포넌트가 맡는다.
    /// 씬을 넘어 유지되는 오브젝트(예: UIManager)에 붙인다.
    /// </para>
    /// </summary>
    public class PopupHotkey : MonoBehaviour
    {
        [Tooltip("이 핫키가 여닫을 창.")]
        [SerializeField] private PopupToggle.PopupKind target = PopupToggle.PopupKind.Inventory;

        [Tooltip("토글 키. 인벤=I, 장비=P, 스킬=K.")]
        [SerializeField] private Key key = Key.I;

        // NPC 상호작용이 시작되면(대화·허브·보상) 열려 있던 창을 닫는다.
        private void OnEnable() => NpcInteractionController.ActiveChanged += OnNpcInteractionChanged;
        private void OnDisable() => NpcInteractionController.ActiveChanged -= OnNpcInteractionChanged;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 텍스트 입력창(채팅 등)에 포커스가 있으면 핫키를 무시한다 — 타이핑하는 글자가 창을 여닫지 않게 하기 위함.
            // 특히 채팅은 Enter가 '열기'와 '전송'에 겹치는데, PopupHotkey는 Keyboard를 직접 읽어(SetInputSuspended 무관)
            // 이 가드가 없으면 전송·닫기와 같은 프레임에 다시 열려 깜빡인다.
            if (UiTypingGuard.IsTypingInInputField()) return;

            if (keyboard[key].wasPressedThisFrame) PopupToggle.Toggle(target);
        }

        // 상호작용 시작(active != null) 시 해당 창이 떠 있으면 닫는다. 종료(null)엔 아무것도 하지 않는다.
        private void OnNpcInteractionChanged(NpcInteractionController active)
        {
            if (active != null) PopupToggle.Close(target);
        }
    }
}
