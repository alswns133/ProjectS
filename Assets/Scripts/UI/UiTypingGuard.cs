using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace ProjectS.UI
{
    /// <summary>
    /// 텍스트 입력창(채팅 등)에 포커스가 있는지 판정하는 공용 게이트.
    /// <para>
    /// Keyboard를 직접 읽는 핫키 컴포넌트들(<see cref="PopupHotkey"/>·포션 퀵슬롯·디버그 키 등)은
    /// PlayerInputHandler의 SetInputSuspended가 닿지 않는다(그건 InputAction만 끈다). 그래서 채팅 타이핑 중
    /// 눌린 글자가 창을 여닫거나 스킬/복귀 등을 오발동시킨다. 그런 컴포넌트가 이 게이트를 함께 확인해
    /// "입력창에 포커스가 있으면 무시"하게 만들기 위한 단일 판정 지점이다(같은 로직을 여러 곳에 복붙하지 않기 위함).
    /// </para>
    /// </summary>
    public static class UiTypingGuard
    {
        /// <summary>지금 EventSystem이 선택한 오브젝트가 TMP_InputField면 true(=텍스트 입력 중).</summary>
        public static bool IsTypingInInputField()
        {
            EventSystem es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }
    }
}
